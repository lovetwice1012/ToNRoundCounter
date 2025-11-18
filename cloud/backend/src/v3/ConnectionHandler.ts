/**
 * Protocol v3 Connection Handler
 * Ultra-lightweight, high-performance binary protocol
 */

import { WebSocket } from 'ws';
import { encode, decode } from '@msgpack/msgpack';
import { logger } from '../logger';
import {
  Message,
  MessageType,
  RequestMessage,
  ErrorCode,
  createResponse,
  createError,
  createPong,
  isValidMessage,
} from '../../shared/src/protocol-v3';

// ============================================================================
// Connection State
// ============================================================================

interface ConnectionState {
  sessionId?: string;
  userId?: string;
  playerId?: string;
  instanceId?: string;
  lastActivity: number;
  messageCount: number;
}

// ============================================================================
// Route Handler Type
// ============================================================================

export type RouteHandler<TReq = unknown, TRes = unknown> = (
  data: TReq,
  ctx: ConnectionState
) => Promise<TRes> | TRes;

// ============================================================================
// Connection Handler
// ============================================================================

export class ConnectionHandler {
  private ws: WebSocket;
  private state: ConnectionState;
  private routes = new Map<string, RouteHandler>();
  private rateLimiter: TokenBucket;

  constructor(ws: WebSocket) {
    this.ws = ws;
    this.state = {
      lastActivity: Date.now(),
      messageCount: 0,
    };
    this.rateLimiter = new TokenBucket(100, 100); // 100 tokens/sec, burst 100

    this.setupHandlers();
  }

  /**
   * Register a route
   */
  route<TReq = unknown, TRes = unknown>(
    method: string,
    handler: RouteHandler<TReq, TRes>
  ): void {
    this.routes.set(method, handler as RouteHandler);
  }

  /**
   * Setup WebSocket handlers
   */
  private setupHandlers(): void {
    this.ws.on('message', (data: Buffer) => {
      this.handleMessage(data).catch((error) => {
        logger.error({ error }, 'Message handling error');
      });
    });

    this.ws.on('error', (error) => {
      logger.error({ error, sessionId: this.state.sessionId }, 'WebSocket error');
    });

    this.ws.on('close', () => {
      logger.info({ sessionId: this.state.sessionId }, 'Connection closed');
    });
  }

  /**
   * Handle incoming binary message
   */
  private async handleMessage(data: Buffer): Promise<void> {
    this.state.lastActivity = Date.now();
    this.state.messageCount++;

    // Decode MessagePack
    let msg: any;
    try {
      msg = decode(data) as Message;
    } catch (error) {
      logger.error({ error }, 'MessagePack decode error');
      return; // Can't send error without message ID
    }

    // Validate message structure
    if (!isValidMessage(msg)) {
      this.sendError("invalid", ErrorCode.INV_MSG, "Invalid message format");
      return;
    }

    // Handle by type
    switch (msg.t) {
      case MessageType.REQ:
        await this.handleRequest(msg as RequestMessage);
        break;

      case MessageType.ACK:
        await this.handleAck(msg as RequestMessage);
        break;

      case MessageType.PING:
        this.sendPong();
        break;

      default:
        // Ignore other types (RES, STR, ERR sent by server only)
        break;
    }
  }

  /**
   * Handle request (expects response)
   */
  private async handleRequest(msg: RequestMessage): Promise<void> {
    // Rate limiting
    if (!this.rateLimiter.consume(1)) {
      this.sendError(msg.i, ErrorCode.RATE_LIM, "Rate limit exceeded");
      return;
    }

    // Find route
    const handler = this.routes.get(msg.m);
    if (!handler) {
      this.sendError(msg.i, ErrorCode.N_FOUND, `Unknown method: ${msg.m}`);
      return;
    }

    // Execute handler
    try {
      const result = await handler(msg.d, this.state);
      this.sendResponse(msg.i, result);
    } catch (error: any) {
      logger.error({ error, method: msg.m }, 'Handler error');
      this.sendError(msg.i, ErrorCode.INTERNAL, error.message || "Internal error");
    }
  }

  /**
   * Handle ACK (fire-and-forget, no response)
   */
  private async handleAck(msg: RequestMessage): Promise<void> {
    // Rate limiting (ACK costs 0.5 tokens)
    if (!this.rateLimiter.consume(0.5)) {
      // Silently drop (no response for ACK)
      return;
    }

    const handler = this.routes.get(msg.m);
    if (!handler) {
      return; // Silently ignore
    }

    try {
      await handler(msg.d, this.state);
      // No response sent for ACK
    } catch (error: any) {
      logger.error({ error, method: msg.m }, 'ACK handler error');
      // No error sent for ACK
    }
  }

  /**
   * Send response
   */
  private sendResponse(id: string, data?: unknown): void {
    if (this.ws.readyState !== WebSocket.OPEN) return;

    const msg = createResponse(id, data);
    const bytes = encode(msg);
    this.ws.send(bytes);
  }

  /**
   * Send error
   */
  private sendError(id: string, code: string, message: string, details?: Record<string, unknown>): void {
    if (this.ws.readyState !== WebSocket.OPEN) return;

    const msg = createError(id, code, message, details);
    const bytes = encode(msg);
    this.ws.send(bytes);
  }

  /**
   * Send pong
   */
  private sendPong(): void {
    if (this.ws.readyState !== WebSocket.OPEN) return;

    const msg = createPong();
    const bytes = encode(msg);
    this.ws.send(bytes);
  }

  /**
   * Send stream event (to this connection)
   */
  sendEvent(method: string, data?: unknown): void {
    if (this.ws.readyState !== WebSocket.OPEN) return;

    const msg = {
      v: 3,
      t: MessageType.STR,
      i: Math.random().toString(36).substring(2, 10),
      m: method,
      d: data,
      s: Date.now(),
    };

    const bytes = encode(msg);
    this.ws.send(bytes);
  }

  /**
   * Get connection state
   */
  getState(): ConnectionState {
    return this.state;
  }

  /**
   * Update connection state
   */
  updateState(updates: Partial<ConnectionState>): void {
    Object.assign(this.state, updates);
  }

  /**
   * Close connection
   */
  close(): void {
    this.ws.close();
  }
}

// ============================================================================
// Token Bucket Rate Limiter
// ============================================================================

class TokenBucket {
  private tokens: number;
  private lastRefill: number;
  private readonly capacity: number;
  private readonly refillRate: number; // tokens per second

  constructor(refillRate: number, capacity: number) {
    this.refillRate = refillRate;
    this.capacity = capacity;
    this.tokens = capacity;
    this.lastRefill = Date.now();
  }

  /**
   * Try to consume tokens
   */
  consume(amount: number): boolean {
    this.refill();

    if (this.tokens >= amount) {
      this.tokens -= amount;
      return true;
    }

    return false;
  }

  /**
   * Refill tokens based on time elapsed
   */
  private refill(): void {
    const now = Date.now();
    const elapsed = (now - this.lastRefill) / 1000; // seconds
    const tokensToAdd = elapsed * this.refillRate;

    this.tokens = Math.min(this.capacity, this.tokens + tokensToAdd);
    this.lastRefill = now;
  }
}
