/**
 * Base message structure for all WebSocket communications
 */
export interface BaseMessage {
    version: string;
    id: string;
    type: MessageType;
    timestamp: string;
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
    duration: number;
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
    position: {
        x: number;
        y: number;
    };
    alive: boolean;
    items: string[];
    afkStatus: 'idle' | 'warning' | 'critical' | 'none';
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
    survivalRate: number;
    averageDuration: number;
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
    minUpdateInterval?: number;
}
/**
 * RPC method names (for autocomplete and validation)
 */
export declare enum RPCMethod {
    AUTH_CONNECT = "auth.connect",
    GAME_ROUND_START = "game.roundStart",
    GAME_ROUND_END = "game.roundEnd",
    GAME_PLAYER_UPDATE = "game.playerUpdate",
    INSTANCE_JOIN = "instance.join",
    INSTANCE_LEAVE = "instance.leave",
    INSTANCE_ALERT = "instance.alert",
    SUBSCRIBE = "subscribe",
    UNSUBSCRIBE = "unsubscribe",
    STATS_QUERY = "stats.query",
    STATS_SUBSCRIBE = "stats.subscribe"
}
/**
 * Stream channel names
 */
export declare enum StreamChannel {
    GAME_PLAYER_UPDATE = "game.playerUpdate",
    INSTANCE_MEMBERS = "instance.members",
    INSTANCE_ALERTS = "instance.alerts",
    STATS_REALTIME = "stats.realtime"
}
/**
 * Error codes
 */
export declare enum ErrorCode {
    INVALID_PARAMS = "INVALID_PARAMS",
    NOT_FOUND = "NOT_FOUND",
    UNAUTHORIZED = "UNAUTHORIZED",
    FORBIDDEN = "FORBIDDEN",
    CONFLICT = "CONFLICT",
    RATE_LIMIT = "RATE_LIMIT",
    INTERNAL_ERROR = "INTERNAL_ERROR",
    TIMEOUT = "TIMEOUT"
}
/**
 * Common request/response patterns
 */
export declare namespace RPC {
    interface AuthConnectParams {
        clientId: string;
        clientVersion: string;
        capabilities?: string[];
    }
    interface AuthConnectResult {
        sessionId: string;
        userId: string;
        serverVersion: string;
    }
    interface GameRoundStartParams {
        playerName: string;
        roundType: string;
        mapName?: string;
    }
    interface GameRoundStartResult {
        roundId: string;
        startTime: string;
        terrorType?: string;
    }
    interface GameRoundEndParams {
        roundId: string;
        survived: boolean;
        damageDealt?: number;
        itemsObtained?: string[];
        terrorName?: string;
        duration: number;
    }
    interface GameRoundEndResult {
        recordId: string;
        saved: boolean;
        stats: {
            totalRounds: number;
            survivalRate: number;
            averageDuration: number;
        };
    }
    interface InstanceJoinParams {
        instanceId: string;
        playerName: string;
        playerId: string;
    }
    interface InstanceJoinResult {
        subscriptionId: string;
        members: InstanceMember[];
    }
    interface InstanceAlertParams {
        instanceId: string;
        alertType: 'danger' | 'warning' | 'info';
        alertValue: number;
        isLocal: boolean;
        message?: string;
    }
    interface InstanceAlertResult {
        delivered: boolean;
        recipients: number;
    }
    interface InstanceLeaveParams {
        instanceId: string;
    }
    interface SubscribeParams {
        channel: string;
        filters?: Record<string, unknown>;
        minUpdateInterval?: number;
    }
    interface SubscribeResult {
        subscriptionId: string;
    }
    interface UnsubscribeParams {
        subscriptionId: string;
    }
    interface StatsQueryParams {
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
export declare function isRequestMessage(msg: any): msg is RequestMessage;
export declare function isResponseMessage(msg: any): msg is ResponseMessage;
export declare function isStreamMessage(msg: any): msg is StreamMessage;
/**
 * Message ID generator
 */
export declare function generateMessageId(): string;
/**
 * Create a request message
 */
export declare function createRequest(method: string, params?: Record<string, unknown>, id?: string): RequestMessage;
/**
 * Create a response message
 */
export declare function createResponse(requestId: string, result: unknown): ResponseMessage;
/**
 * Create an error response message
 */
export declare function createErrorResponse(requestId: string, code: string, message: string, details?: Record<string, unknown>): ResponseMessage;
/**
 * Create a stream message
 */
export declare function createStreamMessage(event: string, data: unknown, id?: string): StreamMessage;
//# sourceMappingURL=index.d.ts.map