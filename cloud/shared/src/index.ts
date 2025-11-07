/**
 * Base message structure for all WebSocket communications
 */
export interface BaseMessage {
  version: string;
  id: string;
  type: MessageType;
  timestamp: string; // ISO8601
}

export type MessageType = 'request' | 'response' | 'stream' | 'error';

/**
 * Request message (client -> server)
 */
export interface RequestMessage extends BaseMessage {
  type: 'request';
  method: string;
  params?: Record<string, unknown>;
}

/**
 * Response message (server -> client)
 */
export interface ResponseMessage extends BaseMessage {
  type: 'response';
  status: 'success' | 'error';
  result?: unknown;
  error?: ErrorDetails;
}

/**
 * Stream message (server -> client, unidirectional)
 */
export interface StreamMessage extends BaseMessage {
  type: 'stream';
  event: string;
  data: unknown;
}

/**
 * Error details in response
 */
export interface ErrorDetails {
  code: string;
  message: string;
  details?: Record<string, unknown>;
}

/**
 * Session information after auth.connect
 */
export interface SessionInfo {
  sessionId: string;
  userId: string;
  serverVersion: string;
  connectedAt: string;
}

/**
 * Game round information
 */
export interface RoundInfo {
  roundId: string;
  playerId: string;
  playerName: string;
  roundType: string;
  mapName?: string;
  terrorType?: string;
  startTime: string;
  endTime?: string;
  survived: boolean;
  duration: number; // seconds
  damageDealt?: number;
  itemsObtained?: string[];
}

/**
 * Player state in shared instance
 */
export interface PlayerState {
  playerId: string;
  playerName: string;
  velocity: number;
  velocityX: number;
  velocityZ: number;
  alive: boolean;
  items: string[];
  afkStatus: 'idle' | 'warning' | 'critical' | 'none';
  afkDuration: number;
  lastUpdate: string;
}

/**
 * Alert event
 */
export interface AlertEvent {
  alertType: 'danger' | 'warning' | 'info';
  alertValue: number;
  isLocal: boolean;
  message?: string;
  sourcePlayerId: string;
  timestamp: string;
}

/**
 * Instance information
 */
export interface InstanceInfo {
  instanceId: string;
  members: InstanceMember[];
  createdAt: string;
  memberCount: number;
}

export interface InstanceMember {
  playerId: string;
  playerName: string;
  joined: string;
  lastSeen: string;
  alive: boolean;
}

/**
 * Statistics data
 */
export interface StatsSnapshot {
  userId: string;
  totalRounds: number;
  survivalRate: number; // 0.0 - 1.0
  averageDuration: number; // seconds
  terrorDistribution: Record<string, number>;
  itemDistribution: Record<string, number>;
  timeRange?: {
    from: string;
    to: string;
  };
}

/**
 * Round statistics update (realtime stream)
 */
export interface StatsUpdate {
  roundsCompleted: number;
  survivalRate: number;
  lastRoundResult: {
    survived: boolean;
    duration: number;
    terrorType?: string;
    mapName?: string;
  };
  timestamp: string;
}

/**
 * Subscription configuration
 */
export interface SubscriptionConfig {
  channel: string;
  filters?: Record<string, unknown>;
  minUpdateInterval?: number; // milliseconds
}

/**
 * RPC method names (for autocomplete and validation)
 */
export enum RPCMethod {
  // Auth
  AUTH_CONNECT = 'auth.connect',

  // Game management
  GAME_ROUND_START = 'game.roundStart',
  GAME_ROUND_END = 'game.roundEnd',
  GAME_PLAYER_UPDATE = 'game.playerUpdate',

  // Instance management
  INSTANCE_JOIN = 'instance.join',
  INSTANCE_LEAVE = 'instance.leave',
  INSTANCE_ALERT = 'instance.alert',

  // Subscription management
  SUBSCRIBE = 'subscribe',
  UNSUBSCRIBE = 'unsubscribe',

  // Stats
  STATS_QUERY = 'stats.query',
  STATS_SUBSCRIBE = 'stats.subscribe',
}

/**
 * Stream channel names
 */
export enum StreamChannel {
  GAME_PLAYER_UPDATE = 'game.playerUpdate',
  INSTANCE_MEMBERS = 'instance.members',
  INSTANCE_ALERTS = 'instance.alerts',
  STATS_REALTIME = 'stats.realtime',
}

/**
 * Error codes
 */
export enum ErrorCode {
  INVALID_PARAMS = 'INVALID_PARAMS',
  NOT_FOUND = 'NOT_FOUND',
  UNAUTHORIZED = 'UNAUTHORIZED',
  FORBIDDEN = 'FORBIDDEN',
  CONFLICT = 'CONFLICT',
  RATE_LIMIT = 'RATE_LIMIT',
  INTERNAL_ERROR = 'INTERNAL_ERROR',
  TIMEOUT = 'TIMEOUT',
}

/**
 * Common request/response patterns
 */
export namespace RPC {
  export interface AuthConnectParams {
    clientId: string;
    clientVersion: string;
    capabilities?: string[];
  }

  export interface AuthConnectResult {
    sessionId: string;
    userId: string;
    serverVersion: string;
  }

  export interface GameRoundStartParams {
    playerName: string;
    roundType: string;
    mapName?: string;
  }

  export interface GameRoundStartResult {
    roundId: string;
    startTime: string;
    terrorType?: string;
  }

  export interface GameRoundEndParams {
    roundId: string;
    survived: boolean;
    damageDealt?: number;
    itemsObtained?: string[];
    terrorName?: string;
    duration: number;
  }

  export interface GameRoundEndResult {
    recordId: string;
    saved: boolean;
    stats: {
      totalRounds: number;
      survivalRate: number;
      averageDuration: number;
    };
  }

  export interface InstanceJoinParams {
    instanceId: string;
    playerName: string;
    playerId: string;
  }

  export interface InstanceJoinResult {
    subscriptionId: string;
    members: InstanceMember[];
  }

  export interface InstanceAlertParams {
    instanceId: string;
    alertType: 'danger' | 'warning' | 'info';
    alertValue: number;
    isLocal: boolean;
    message?: string;
  }

  export interface InstanceAlertResult {
    delivered: boolean;
    recipients: number;
  }

  export interface InstanceLeaveParams {
    instanceId: string;
  }

  export interface SubscribeParams {
    channel: string;
    filters?: Record<string, unknown>;
    minUpdateInterval?: number;
  }

  export interface SubscribeResult {
    subscriptionId: string;
  }

  export interface UnsubscribeParams {
    subscriptionId: string;
  }

  export interface StatsQueryParams {
    userId: string;
    timeRange?: {
      from?: string;
      to?: string;
    };
    groupBy?: 'day' | 'week' | 'month' | 'all';
    includeBreakdown?: boolean;
  }
}

/**
 * Type guard functions
 */
export function isRequestMessage(msg: any): msg is RequestMessage {
  return msg && msg.type === 'request' && msg.method;
}

export function isResponseMessage(msg: any): msg is ResponseMessage {
  return msg && msg.type === 'response' && (msg.status === 'success' || msg.status === 'error');
}

export function isStreamMessage(msg: any): msg is StreamMessage {
  return msg && msg.type === 'stream' && msg.event;
}

/**
 * Message ID generator
 */
export function generateMessageId(): string {
  return `msg-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
}

/**
 * Create a request message
 */
export function createRequest(
  method: string,
  params?: Record<string, unknown>,
  id?: string
): RequestMessage {
  return {
    version: '1.0',
    id: id || generateMessageId(),
    type: 'request',
    method,
    params,
    timestamp: new Date().toISOString(),
  };
}

/**
 * Create a response message
 */
export function createResponse(
  requestId: string,
  result: unknown
): ResponseMessage {
  return {
    version: '1.0',
    id: requestId,
    type: 'response',
    status: 'success',
    result,
    timestamp: new Date().toISOString(),
  };
}

/**
 * Create an error response message
 */
export function createErrorResponse(
  requestId: string,
  code: string,
  message: string,
  details?: Record<string, unknown>
): ResponseMessage {
  return {
    version: '1.0',
    id: requestId,
    type: 'response',
    status: 'error',
    error: {
      code,
      message,
      details,
    },
    timestamp: new Date().toISOString(),
  };
}

/**
 * Create a stream message
 */
export function createStreamMessage(
  event: string,
  data: unknown,
  id?: string
): StreamMessage {
  return {
    version: '1.0',
    id: id || generateMessageId(),
    type: 'stream',
    event,
    data,
    timestamp: new Date().toISOString(),
  };
}
