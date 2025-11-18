/**
 * Type Definitions for ToNRoundCounter Cloud API v2
 * Ultra-lightweight, performance-focused types
 */

// ============================================================================
// Core Message Types
// ============================================================================

export type MessageType = "req" | "res" | "stream" | "err";

export interface BaseMessage {
  v: 2;                  // Protocol version
  id: string;            // Message ID (UUID v4)
  type: MessageType;
}

export interface RequestMessage extends BaseMessage {
  type: "req";
  method: string;        // e.g., "auth.login", "instance.create"
  data?: unknown;        // Request payload
}

export interface ResponseMessage extends BaseMessage {
  type: "res";
  data?: unknown;        // Response payload
}

export interface StreamMessage extends BaseMessage {
  type: "stream";
  method: string;        // Event name
  data?: unknown;        // Event payload
  ts: number;            // Unix timestamp (ms)
}

export interface ErrorMessage extends BaseMessage {
  type: "err";
  error: ErrorInfo;
}

export type Message = RequestMessage | ResponseMessage | StreamMessage | ErrorMessage;

export interface ErrorInfo {
  code: ErrorCode;
  message: string;
  details?: Record<string, unknown>;
}

// ============================================================================
// Error Codes
// ============================================================================

export enum ErrorCode {
  // Client errors (4xx)
  INVALID_MESSAGE = "INVALID_MESSAGE",
  AUTH_REQUIRED = "AUTH_REQUIRED",
  AUTH_FAILED = "AUTH_FAILED",
  FORBIDDEN = "FORBIDDEN",
  NOT_FOUND = "NOT_FOUND",
  VALIDATION_ERROR = "VALIDATION_ERROR",
  RATE_LIMIT_EXCEEDED = "RATE_LIMIT_EXCEEDED",

  // Server errors (5xx)
  INTERNAL_ERROR = "INTERNAL_ERROR",
  SERVICE_UNAVAILABLE = "SERVICE_UNAVAILABLE",
  TIMEOUT = "TIMEOUT",
}

// ============================================================================
// Request/Response Payloads
// ============================================================================

// Authentication
export interface AuthLoginRequest {
  playerId: string;
  clientVersion: string;
}

export interface AuthLoginResponse {
  sessionId: string;
  expiresAt: number;
}

export interface AuthRefreshResponse {
  sessionId: string;
  expiresAt: number;
}

// Instance Management
export interface InstanceCreateRequest {
  maxPlayers?: number;
  settings?: InstanceSettings;
}

export interface InstanceSettings {
  autoSuicideMode?: "Individual" | "Coordinated";
  votingTimeout?: number;
}

export interface InstanceCreateResponse {
  instanceId: string;
  createdAt: number;
}

export interface InstanceListRequest {
  filter?: "all" | "available" | "full";
  limit?: number;
  offset?: number;
}

export interface InstanceInfo {
  instanceId: string;
  currentPlayers: number;
  maxPlayers: number;
  createdAt: number;
  owner: string;
}

export interface InstanceListResponse {
  instances: InstanceInfo[];
  total: number;
}

export interface InstanceGetRequest {
  instanceId: string;
}

export interface InstanceGetResponse {
  instanceId: string;
  currentPlayers: number;
  maxPlayers: number;
  settings: Record<string, unknown>;
  createdAt: number;
  updatedAt: number;
}

export interface InstanceUpdateRequest {
  instanceId: string;
  updates: {
    maxPlayers?: number;
    settings?: Record<string, unknown>;
  };
}

export interface InstanceUpdateResponse {
  instanceId: string;
  updatedAt: number;
}

export interface InstanceDeleteRequest {
  instanceId: string;
}

// Player State
export interface PlayerState {
  playerId: string;
  velocity?: number;
  afkDuration?: number;
  items?: string[];
  damage?: number;
  isAlive?: boolean;
}

export interface PlayerUpdateStateRequest {
  instanceId: string;
  state: PlayerState;
}

export interface PlayerUpdateStateResponse {
  success: true;
  updatedAt: number;
}

export interface PlayerGetStatesRequest {
  instanceId: string;
}

export interface PlayerStateInfo extends PlayerState {
  lastUpdate: number;
}

export interface PlayerGetStatesResponse {
  states: PlayerStateInfo[];
}

// Game Rounds
export interface RoundStartRequest {
  instanceId: string;
  roundType: string;
  mapName?: string;
}

export interface RoundStartResponse {
  roundId: string;
  startedAt: number;
}

export interface RoundEndRequest {
  roundId: string;
  survived: boolean;
  duration: number;
  damageDealt?: number;
  itemsObtained?: string[];
  terrorName?: string;
}

export interface RoundEndResponse {
  success: true;
  endedAt: number;
}

// Voting
export interface VotingStartRequest {
  instanceId: string;
  terrorName: string;
  expiresAt: number;
}

export interface VotingStartResponse {
  campaignId: string;
  expiresAt: number;
}

export interface VotingSubmitRequest {
  campaignId: string;
  decision: "proceed" | "cancel";
}

export interface VotingSubmitResponse {
  success: true;
}

// Profile
export interface ProfileGetRequest {
  playerId: string;
}

export interface TerrorStats {
  played: number;
  survived: number;
  avgDamage: number;
}

export interface ProfileGetResponse {
  playerId: string;
  playerName: string;
  skillLevel: number;
  totalRounds: number;
  totalSurvived: number;
  terrorStats: Record<string, TerrorStats>;
}

export interface ProfileUpdateRequest {
  playerId: string;
  updates: {
    playerName?: string;
    [key: string]: unknown;
  };
}

export interface ProfileUpdateResponse {
  success: true;
  updatedAt: number;
}

// Settings
export interface SettingsGetResponse {
  settings: Record<string, unknown>;
  version: number;
  updatedAt: number;
}

export interface SettingsUpdateRequest {
  settings: Record<string, unknown>;
}

export interface SettingsUpdateResponse {
  version: number;
  updatedAt: number;
}

export interface SettingsSyncRequest {
  localSettings: Record<string, unknown>;
  localVersion: number;
}

export type SyncAction = "local_newer" | "remote_newer" | "conflict" | "synced";

export interface SettingsSyncResponse {
  action: SyncAction;
  settings: Record<string, unknown>;
  version: number;
}

// ============================================================================
// Stream Events
// ============================================================================

export interface PlayerStateUpdateEvent {
  instanceId: string;
  state: PlayerStateInfo;
}

export interface RoundStartedEvent {
  instanceId: string;
  roundId: string;
  roundType: string;
  startedAt: number;
}

export interface RoundEndedEvent {
  instanceId: string;
  roundId: string;
  survived: boolean;
  endedAt: number;
}

export interface VotingStartedEvent {
  instanceId: string;
  campaignId: string;
  terrorName: string;
  expiresAt: number;
}

export interface VotingUpdatedEvent {
  campaignId: string;
  votes: {
    proceed: number;
    cancel: number;
  };
}

export interface VotingResolvedEvent {
  campaignId: string;
  decision: "proceed" | "cancel";
  votes: {
    proceed: number;
    cancel: number;
  };
}

export interface SettingsUpdatedEvent {
  version: number;
  updatedAt: number;
}

// ============================================================================
// Internal Server Types
// ============================================================================

export interface Session {
  sessionId: string;
  userId: string;
  playerId: string;
  createdAt: Date;
  expiresAt: Date;
}

export interface Instance {
  instanceId: string;
  ownerId: string;
  maxPlayers: number;
  currentPlayers: number;
  settings: InstanceSettings;
  createdAt: Date;
  updatedAt: Date;
}

// ============================================================================
// Route Handler Types
// ============================================================================

export interface RouteContext {
  sessionId?: string;
  userId?: string;
  playerId?: string;
}

export type RouteHandler<TReq = unknown, TRes = unknown> = (
  data: TReq,
  ctx: RouteContext
) => Promise<TRes>;

export interface RouteDefinition {
  method: string;
  requiresAuth: boolean;
  handler: RouteHandler;
}
