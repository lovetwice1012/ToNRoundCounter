# ToNRoundCounter Cloud API Specification v2.0

## Design Principles

1. **Performance First** - Minimize overhead, maximize throughput
2. **Type Safety** - Strong typing throughout the stack
3. **Consistency** - Unified patterns across all endpoints
4. **Simplicity** - Clear, predictable behavior
5. **Extensibility** - Easy to add new features

## Message Format

### Unified Message Structure

All WebSocket messages follow this structure:

```typescript
interface Message {
  // Required fields
  v: number;              // Protocol version (always 2)
  id: string;             // Message ID (UUID v4)
  type: MessageType;      // "req" | "res" | "stream" | "err"

  // Optional fields based on type
  method?: string;        // RPC method name (for req)
  data?: any;             // Payload (for req/res/stream)
  error?: ErrorInfo;      // Error details (for err)
  ts?: number;            // Timestamp (Unix ms, for stream/err)
}

type MessageType = "req" | "res" | "stream" | "err";

interface ErrorInfo {
  code: string;           // Error code (e.g., "AUTH_REQUIRED")
  message: string;        // Human-readable message
  details?: Record<string, any>; // Additional context
}
```

### Examples

**Request:**
```json
{
  "v": 2,
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "type": "req",
  "method": "auth.login",
  "data": {
    "playerId": "player123",
    "clientVersion": "1.0.0"
  }
}
```

**Response:**
```json
{
  "v": 2,
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "type": "res",
  "data": {
    "sessionId": "sess_abc123",
    "expiresAt": 1234567890
  }
}
```

**Stream Event:**
```json
{
  "v": 2,
  "id": "generated-uuid",
  "type": "stream",
  "method": "player.stateUpdate",
  "data": {
    "instanceId": "inst_123",
    "playerId": "player456",
    "velocity": 2.5
  },
  "ts": 1234567890
}
```

**Error:**
```json
{
  "v": 2,
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "type": "err",
  "error": {
    "code": "AUTH_REQUIRED",
    "message": "Authentication token is required"
  }
}
```

## Naming Conventions

- **camelCase** for all field names
- **dot notation** for method names (e.g., `auth.login`, `instance.create`)
- **PascalCase** for error codes (e.g., `AUTH_REQUIRED`, `INSTANCE_NOT_FOUND`)

## Standard Error Codes

```typescript
enum ErrorCode {
  // Client errors (4xx)
  INVALID_MESSAGE = "INVALID_MESSAGE",           // Malformed message
  AUTH_REQUIRED = "AUTH_REQUIRED",               // Authentication needed
  AUTH_FAILED = "AUTH_FAILED",                   // Invalid credentials
  FORBIDDEN = "FORBIDDEN",                       // Insufficient permissions
  NOT_FOUND = "NOT_FOUND",                       // Resource not found
  VALIDATION_ERROR = "VALIDATION_ERROR",         // Invalid parameters
  RATE_LIMIT_EXCEEDED = "RATE_LIMIT_EXCEEDED",   // Too many requests

  // Server errors (5xx)
  INTERNAL_ERROR = "INTERNAL_ERROR",             // Unexpected server error
  SERVICE_UNAVAILABLE = "SERVICE_UNAVAILABLE",   // Temporary outage
  TIMEOUT = "TIMEOUT",                           // Request timeout
}
```

## API Methods

### Authentication

#### `auth.login`
**Request:**
```typescript
{
  playerId: string;      // VRChat player ID
  clientVersion: string; // Client version (semver)
}
```

**Response:**
```typescript
{
  sessionId: string;     // Session identifier
  expiresAt: number;     // Unix timestamp (ms)
}
```

#### `auth.logout`
**Request:** `{}` (no parameters)

**Response:**
```typescript
{ success: true }
```

#### `auth.refresh`
**Request:** `{}` (authenticated)

**Response:**
```typescript
{
  sessionId: string;
  expiresAt: number;
}
```

### Instance Management

#### `instance.create`
**Request:**
```typescript
{
  maxPlayers?: number;   // Default: 10
  settings?: {
    autoSuicideMode?: "Individual" | "Coordinated";
    votingTimeout?: number; // Seconds
  };
}
```

**Response:**
```typescript
{
  instanceId: string;
  createdAt: number;     // Unix timestamp (ms)
}
```

#### `instance.list`
**Request:**
```typescript
{
  filter?: "all" | "available" | "full"; // Default: "available"
  limit?: number;        // Default: 20, Max: 100
  offset?: number;       // Default: 0
}
```

**Response:**
```typescript
{
  instances: Array<{
    instanceId: string;
    currentPlayers: number;
    maxPlayers: number;
    createdAt: number;
    owner: string;
  }>;
  total: number;
}
```

#### `instance.get`
**Request:**
```typescript
{
  instanceId: string;
}
```

**Response:**
```typescript
{
  instanceId: string;
  currentPlayers: number;
  maxPlayers: number;
  settings: Record<string, any>;
  createdAt: number;
  updatedAt: number;
}
```

#### `instance.update`
**Request:**
```typescript
{
  instanceId: string;
  updates: {
    maxPlayers?: number;
    settings?: Record<string, any>;
  };
}
```

**Response:**
```typescript
{
  instanceId: string;
  updatedAt: number;
}
```

#### `instance.delete`
**Request:**
```typescript
{
  instanceId: string;
}
```

**Response:**
```typescript
{ success: true }
```

### Player State

#### `player.updateState`
**Request:**
```typescript
{
  instanceId: string;
  state: {
    playerId: string;
    velocity?: number;
    afkDuration?: number;
    items?: string[];
    damage?: number;
    isAlive?: boolean;
  };
}
```

**Response:**
```typescript
{
  success: true;
  updatedAt: number;
}
```

**Stream Event:** `player.stateUpdate` (broadcast to all instance members)

#### `player.getStates`
**Request:**
```typescript
{
  instanceId: string;
}
```

**Response:**
```typescript
{
  states: Array<{
    playerId: string;
    velocity: number;
    afkDuration: number;
    items: string[];
    damage: number;
    isAlive: boolean;
    lastUpdate: number;
  }>;
}
```

### Game Rounds

#### `round.start`
**Request:**
```typescript
{
  instanceId: string;
  roundType: string;
  mapName?: string;
}
```

**Response:**
```typescript
{
  roundId: string;
  startedAt: number;
}
```

**Stream Event:** `round.started` (broadcast to all instance members)

#### `round.end`
**Request:**
```typescript
{
  roundId: string;
  survived: boolean;
  duration: number;      // Seconds
  damageDealt?: number;
  itemsObtained?: string[];
  terrorName?: string;
}
```

**Response:**
```typescript
{
  success: true;
  endedAt: number;
}
```

**Stream Event:** `round.ended` (broadcast to all instance members)

### Voting System

#### `voting.start`
**Request:**
```typescript
{
  instanceId: string;
  terrorName: string;
  expiresAt: number;     // Unix timestamp (ms)
}
```

**Response:**
```typescript
{
  campaignId: string;
  expiresAt: number;
}
```

**Stream Event:** `voting.started` (broadcast to all instance members)

#### `voting.submit`
**Request:**
```typescript
{
  campaignId: string;
  decision: "proceed" | "cancel";
}
```

**Response:**
```typescript
{ success: true }
```

**Stream Event:** `voting.updated` (broadcast when vote submitted)

**Stream Event:** `voting.resolved` (broadcast when voting completes)

### Profile & Statistics

#### `profile.get`
**Request:**
```typescript
{
  playerId: string;
}
```

**Response:**
```typescript
{
  playerId: string;
  playerName: string;
  skillLevel: number;
  totalRounds: number;
  totalSurvived: number;
  terrorStats: Record<string, {
    played: number;
    survived: number;
    avgDamage: number;
  }>;
}
```

#### `profile.update`
**Request:**
```typescript
{
  playerId: string;
  updates: {
    playerName?: string;
    // ... other updatable fields
  };
}
```

**Response:**
```typescript
{
  success: true;
  updatedAt: number;
}
```

### Settings Sync

#### `settings.get`
**Request:**
```typescript
{} // Uses authenticated user
```

**Response:**
```typescript
{
  settings: Record<string, any>;
  version: number;
  updatedAt: number;
}
```

#### `settings.update`
**Request:**
```typescript
{
  settings: Record<string, any>;
}
```

**Response:**
```typescript
{
  version: number;
  updatedAt: number;
}
```

**Stream Event:** `settings.updated` (to user's other sessions)

#### `settings.sync`
**Request:**
```typescript
{
  localSettings: Record<string, any>;
  localVersion: number;
}
```

**Response:**
```typescript
{
  action: "local_newer" | "remote_newer" | "conflict" | "synced";
  settings: Record<string, any>;  // Merged or remote settings
  version: number;
}
```

## Performance Optimizations

### Connection Management
- **Heartbeat:** 30s ping/pong
- **Idle timeout:** 5 minutes
- **Reconnect:** Exponential backoff (500ms → 30s max)

### Message Optimization
- **Binary frames:** For large payloads (>1KB)
- **Compression:** ws permessage-deflate
- **Batching:** Multiple updates in single message when appropriate

### Broadcasting
- **Instance-scoped:** O(n) where n = instance members, not all clients
- **Rate limiting:** Max 100 updates/second per instance
- **Debouncing:** Player state updates throttled to 10Hz

## Security

### Authentication
- Session-based with expiration
- Automatic token refresh
- Secure session storage

### Rate Limiting
- 100 requests/minute per session
- 10 connections per IP
- Exponential backoff on failures

### Validation
- All inputs validated with Zod schemas
- SQL injection prevention (parameterized queries)
- XSS prevention (sanitized outputs)

## Migration from v1

### Breaking Changes
1. Message format completely redesigned
2. All field names now camelCase (was snake_case)
3. Error format standardized
4. Method naming changed (e.g., `game.roundStart` → `round.start`)

### Migration Strategy
1. Backend supports both v1 and v2 simultaneously
2. Client sends protocol version in connection
3. v1 deprecated after 6 months
4. v1 removed after 12 months
