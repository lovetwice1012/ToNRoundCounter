/**
 * Ultra-Lightweight Message Router for Cloud API v2
 * Performance-optimized with O(1) lookups
 */

import { WebSocket } from 'ws';
import { logger } from '../logger';
import {
  Message,
  RequestMessage,
  ResponseMessage,
  ErrorMessage,
  ErrorCode,
  RouteHandler,
  RouteContext,
  RouteDefinition,
} from '../../shared/src/types-v2';

// ============================================================================
// Router Core
// ============================================================================

export class MessageRouter {
  private routes = new Map<string, RouteDefinition>();
  private middleware: MiddlewareFunction[] = [];

  /**
   * Register a route handler
   */
  route<TReq = unknown, TRes = unknown>(
    method: string,
    handler: RouteHandler<TReq, TRes>,
    options: { requiresAuth?: boolean } = {}
  ): void {
    this.routes.set(method, {
      method,
      requiresAuth: options.requiresAuth ?? false,
      handler,
    });
  }

  /**
   * Add middleware
   */
  use(fn: MiddlewareFunction): void {
    this.middleware.push(fn);
  }

  /**
   * Handle incoming message
   */
  async handle(ws: WebSocket, message: Message, context: RouteContext): Promise<void> {
    if (message.type !== 'req') {
      return; // Ignore non-request messages
    }

    const req = message as RequestMessage;
    const route = this.routes.get(req.method);

    if (!route) {
      this.sendError(ws, req.id, ErrorCode.NOT_FOUND, `Unknown method: ${req.method}`);
      return;
    }

    try {
      // Run middleware
      for (const mw of this.middleware) {
        const result = await mw(req, context);
        if (!result.ok) {
          this.sendError(ws, req.id, result.code!, result.message!);
          return;
        }
      }

      // Check auth requirement
      if (route.requiresAuth && !context.sessionId) {
        this.sendError(ws, req.id, ErrorCode.AUTH_REQUIRED, 'Authentication required');
        return;
      }

      // Execute handler
      const data = await route.handler(req.data, context);

      // Send response
      this.sendResponse(ws, req.id, data);
    } catch (error: any) {
      logger.error({ error, method: req.method }, 'Route handler error');
      this.sendError(
        ws,
        req.id,
        ErrorCode.INTERNAL_ERROR,
        error.message || 'Internal server error'
      );
    }
  }

  /**
   * Send success response
   */
  private sendResponse(ws: WebSocket, id: string, data: unknown): void {
    if (ws.readyState !== WebSocket.OPEN) return;

    const response: ResponseMessage = {
      v: 2,
      id,
      type: 'res',
      data,
    };

    ws.send(JSON.stringify(response));
  }

  /**
   * Send error response
   */
  private sendError(
    ws: WebSocket,
    id: string,
    code: ErrorCode,
    message: string,
    details?: Record<string, unknown>
  ): void {
    if (ws.readyState !== WebSocket.OPEN) return;

    const error: ErrorMessage = {
      v: 2,
      id,
      type: 'err',
      error: {
        code,
        message,
        details,
      },
    };

    ws.send(JSON.stringify(error));
  }
}

// ============================================================================
// Middleware
// ============================================================================

export interface MiddlewareResult {
  ok: boolean;
  code?: ErrorCode;
  message?: string;
}

export type MiddlewareFunction = (
  req: RequestMessage,
  ctx: RouteContext
) => Promise<MiddlewareResult> | MiddlewareResult;

/**
 * Validation middleware - validates request data
 */
export function validationMiddleware(
  schema: (data: unknown) => boolean
): MiddlewareFunction {
  return (req) => {
    try {
      const valid = schema(req.data);
      if (!valid) {
        return {
          ok: false,
          code: ErrorCode.VALIDATION_ERROR,
          message: 'Invalid request data',
        };
      }
      return { ok: true };
    } catch {
      return {
        ok: false,
        code: ErrorCode.VALIDATION_ERROR,
        message: 'Invalid request data format',
      };
    }
  };
}

/**
 * Rate limiting middleware
 */
export class RateLimiter {
  private requests = new Map<string, number[]>();
  private readonly windowMs: number;
  private readonly maxRequests: number;

  constructor(windowMs: number = 60000, maxRequests: number = 100) {
    this.windowMs = windowMs;
    this.maxRequests = maxRequests;

    // Cleanup old entries every minute
    setInterval(() => this.cleanup(), 60000);
  }

  middleware(): MiddlewareFunction {
    return (req, ctx) => {
      const key = ctx.sessionId || 'anonymous';
      const now = Date.now();
      const requests = this.requests.get(key) || [];

      // Remove old requests outside window
      const validRequests = requests.filter((t) => now - t < this.windowMs);

      if (validRequests.length >= this.maxRequests) {
        return {
          ok: false,
          code: ErrorCode.RATE_LIMIT_EXCEEDED,
          message: 'Rate limit exceeded',
        };
      }

      validRequests.push(now);
      this.requests.set(key, validRequests);

      return { ok: true };
    };
  }

  private cleanup(): void {
    const now = Date.now();
    for (const [key, requests] of this.requests.entries()) {
      const validRequests = requests.filter((t) => now - t < this.windowMs);
      if (validRequests.length === 0) {
        this.requests.delete(key);
      } else {
        this.requests.set(key, validRequests);
      }
    }
  }
}

/**
 * Logging middleware
 */
export function loggingMiddleware(): MiddlewareFunction {
  return (req, ctx) => {
    logger.debug(
      {
        method: req.method,
        sessionId: ctx.sessionId,
        hasData: !!req.data,
      },
      'Request'
    );
    return { ok: true };
  };
}
