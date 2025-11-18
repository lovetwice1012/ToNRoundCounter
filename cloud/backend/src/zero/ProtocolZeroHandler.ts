/**
 * Protocol ZERO Backend Handler
 * Ultra-minimal binary protocol - 4 APIs only
 */

import { WebSocket } from 'ws';
import { logger } from '../logger';

// ============================================================================
// Opcodes
// ============================================================================

enum Opcode {
  Login = 0x01,
  RoundStart = 0x03,
  RoundEnd = 0x04,
  UpdatePlayerState = 0x05,
  Success = 0x80,
  Error = 0xFF,
  Ping = 0xfe,
  Pong = 0xfd,
  PlayerStateEvent = 0x10,
}

// ============================================================================
// Connection State
// ============================================================================

interface ConnectionState {
  sessionId?: string;
  playerId?: string;
  instanceId?: string;
  lastActivity: number;
}

// ============================================================================
// Protocol ZERO Handler
// ============================================================================

export class ProtocolZeroHandler {
  private ws: WebSocket;
  private state: ConnectionState;
  private sessions = new Map<string, { playerId: string; expiresAt: number }>();
  private instances = new Map<string, Set<WebSocket>>();

  constructor(ws: WebSocket) {
    this.ws = ws;
    this.state = { lastActivity: Date.now() };
    this.setupHandlers();
  }

  private setupHandlers(): void {
    this.ws.on('message', (data: Buffer) => {
      this.handleMessage(data).catch((error) => {
        logger.error({ error }, 'Protocol ZERO message handling error');
      });
    });

    this.ws.on('close', () => {
      this.cleanup();
    });
  }

  private async handleMessage(data: Buffer): Promise<void> {
    this.state.lastActivity = Date.now();

    if (data.length < 4) {
      logger.warn('Message too short (< 4 bytes)');
      return;
    }

    const opcode = data[0] as Opcode;
    const reqId = data[1];
    const payloadLen = (data[2] << 8) | data[3];
    const payload = data.subarray(4, 4 + payloadLen);

    if (payload.length !== payloadLen) {
      logger.warn({ expected: payloadLen, actual: payload.length }, 'Payload length mismatch');
      return;
    }

    try {
      switch (opcode) {
        case Opcode.Login:
          await this.handleLogin(reqId, payload);
          break;

        case Opcode.RoundStart:
          await this.handleRoundStart(reqId, payload);
          break;

        case Opcode.RoundEnd:
          await this.handleRoundEnd(reqId, payload);
          break;

        case Opcode.UpdatePlayerState:
          await this.handlePlayerState(payload);
          break;

        case Opcode.Ping:
          this.sendPong();
          break;

        default:
          logger.warn({ opcode }, 'Unknown opcode');
          this.sendError(reqId, 0x01, 'Unknown opcode');
      }
    } catch (error: any) {
      logger.error({ error, opcode }, 'Handler error');
      this.sendError(reqId, 0xFF, error.message || 'Internal error');
    }
  }

  // ==========================================================================
  // API Handlers
  // ==========================================================================

  private async handleLogin(reqId: number, payload: Buffer): Promise<void> {
    if (payload.length !== 32) {
      this.sendError(reqId, 0x02, 'Invalid login payload');
      return;
    }

    // Parse: [0-15]=playerId, [16-31]=version
    const playerId = payload.subarray(0, 16).toString('utf8').replace(/\0/g, '');
    const version = payload.subarray(16, 32).toString('utf8').replace(/\0/g, '');

    // Generate session
    const sessionId = this.generateId(16);
    const expiresAt = Date.now() + 24 * 60 * 60 * 1000; // 24 hours

    this.sessions.set(sessionId, { playerId, expiresAt });
    this.state.sessionId = sessionId;
    this.state.playerId = playerId;

    logger.info({ playerId, version, sessionId }, 'Login successful');

    // Response: [0-15]=sessionId, [16-23]=expiresAt
    const response = Buffer.alloc(24);
    Buffer.from(sessionId.padEnd(16, '\0'), 'utf8').copy(response, 0);
    response.writeBigInt64BE(BigInt(expiresAt), 16);

    this.sendSuccess(reqId, response);
  }

  private async handleRoundStart(reqId: number, payload: Buffer): Promise<void> {
    if (payload.length !== 80) {
      this.sendError(reqId, 0x02, 'Invalid round start payload');
      return;
    }

    // Parse: [0-15]=instanceId, [16-47]=roundType, [48-79]=mapName
    const instanceId = payload.subarray(0, 16).toString('utf8').replace(/\0/g, '');
    const roundType = payload.subarray(16, 48).toString('utf8').replace(/\0/g, '');
    const mapName = payload.subarray(48, 80).toString('utf8').replace(/\0/g, '');

    this.state.instanceId = instanceId;

    // Join instance broadcast group
    if (!this.instances.has(instanceId)) {
      this.instances.set(instanceId, new Set());
    }
    this.instances.get(instanceId)!.add(this.ws);

    // Generate round ID
    const roundId = this.generateId(16);
    const startedAt = Date.now();

    logger.info({ instanceId, roundType, mapName, roundId }, 'Round started');

    // Response: [0-15]=roundId, [16-23]=startedAt
    const response = Buffer.alloc(24);
    Buffer.from(roundId.padEnd(16, '\0'), 'utf8').copy(response, 0);
    response.writeBigInt64BE(BigInt(startedAt), 16);

    this.sendSuccess(reqId, response);
  }

  private async handleRoundEnd(reqId: number, payload: Buffer): Promise<void> {
    if (payload.length !== 57) {
      this.sendError(reqId, 0x02, 'Invalid round end payload');
      return;
    }

    // Parse: [0-15]=roundId, [16]=survived, [17-20]=duration, [21-24]=damage, [25-56]=terrorName
    const roundId = payload.subarray(0, 16).toString('utf8').replace(/\0/g, '');
    const survived = payload[16] === 1;
    const duration = payload.readUInt32BE(17);
    const damageDealt = payload.readFloatBE(21);
    const terrorName = payload.subarray(25, 57).toString('utf8').replace(/\0/g, '');

    const endedAt = Date.now();

    logger.info({ roundId, survived, duration, damageDealt, terrorName }, 'Round ended');

    // Response: [0-7]=endedAt
    const response = Buffer.alloc(8);
    response.writeBigInt64BE(BigInt(endedAt), 0);

    this.sendSuccess(reqId, response);
  }

  private async handlePlayerState(payload: Buffer): Promise<void> {
    if (payload.length !== 45) {
      logger.warn('Invalid player state payload');
      return;
    }

    // Parse: [0-15]=instanceId, [16-31]=playerId, [32-35]=velocity, [36-39]=afk, [40-43]=damage, [44]=isAlive
    const instanceId = payload.subarray(0, 16).toString('utf8').replace(/\0/g, '');
    const playerId = payload.subarray(16, 32).toString('utf8').replace(/\0/g, '');
    const velocity = payload.readFloatBE(32);
    const afkDuration = payload.readFloatBE(36);
    const damage = payload.readFloatBE(40);
    const isAlive = payload[44] === 1;

    // Broadcast to instance members
    this.broadcastPlayerState(instanceId, playerId, velocity, afkDuration, damage, isAlive);

    // No response sent (fire-and-forget)
  }

  // ==========================================================================
  // Response Helpers
  // ==========================================================================

  private sendSuccess(reqId: number, data: Buffer): void {
    if (this.ws.readyState !== WebSocket.OPEN) return;

    const message = Buffer.alloc(4 + data.length);
    message[0] = Opcode.Success;
    message[1] = reqId;
    message[2] = data.length >> 8;
    message[3] = data.length & 0xff;
    data.copy(message, 4);

    this.ws.send(message);
  }

  private sendError(reqId: number, errorCode: number, errorMsg: string): void {
    if (this.ws.readyState !== WebSocket.OPEN) return;

    const msgBytes = Buffer.from(errorMsg.substring(0, 255), 'utf8');
    const payload = Buffer.alloc(1 + msgBytes.length);
    payload[0] = errorCode;
    msgBytes.copy(payload, 1);

    const message = Buffer.alloc(4 + payload.length);
    message[0] = Opcode.Error;
    message[1] = reqId;
    message[2] = payload.length >> 8;
    message[3] = payload.length & 0xff;
    payload.copy(message, 4);

    this.ws.send(message);
  }

  private sendPong(): void {
    if (this.ws.readyState !== WebSocket.OPEN) return;

    const message = Buffer.from([Opcode.Pong, 0, 0, 0]);
    this.ws.send(message);
  }

  private broadcastPlayerState(
    instanceId: string,
    playerId: string,
    velocity: number,
    afkDuration: number,
    damage: number,
    isAlive: boolean
  ): void {
    const clients = this.instances.get(instanceId);
    if (!clients) return;

    // Event payload: [0-15]=playerId, [16-19]=velocity, [20-23]=afk, [24-27]=damage, [28]=isAlive
    const payload = Buffer.alloc(29);
    Buffer.from(playerId.padEnd(16, '\0'), 'utf8').copy(payload, 0);
    payload.writeFloatBE(velocity, 16);
    payload.writeFloatBE(afkDuration, 20);
    payload.writeFloatBE(damage, 24);
    payload[28] = isAlive ? 1 : 0;

    const message = Buffer.alloc(4 + payload.length);
    message[0] = Opcode.PlayerStateEvent;
    message[1] = 0; // No reqId for events
    message[2] = payload.length >> 8;
    message[3] = payload.length & 0xff;
    payload.copy(message, 4);

    clients.forEach((client) => {
      if (client.readyState === WebSocket.OPEN) {
        client.send(message);
      }
    });
  }

  // ==========================================================================
  // Utilities
  // ==========================================================================

  private generateId(length: number): string {
    const chars = 'abcdefghijklmnopqrstuvwxyz0123456789';
    let result = '';
    for (let i = 0; i < length; i++) {
      result += chars[Math.floor(Math.random() * chars.length)];
    }
    return result;
  }

  private cleanup(): void {
    // Remove from all instances
    this.instances.forEach((clients) => {
      clients.delete(this.ws);
    });

    logger.info({ sessionId: this.state.sessionId }, 'Connection closed');
  }
}
