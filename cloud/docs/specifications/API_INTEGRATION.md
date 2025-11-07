# Cloud Backend API仕様書

**バージョン:** 1.0  
**最終更新:** 2025年11月5日  
**対象システム:** ToNRoundCounter Cloud Backend

---

## 📋 目次

1. [概要](#概要)
2. [認証・認可](#認証認可)
3. [WebSocket API](#websocket-api)
4. [REST API](#rest-api)
5. [データモデル](#データモデル)
6. [エラーハンドリング](#エラーハンドリング)
7. [レート制限](#レート制限)
8. [パフォーマンス要件](#パフォーマンス要件)

---

## 🎯 概要

### システムアーキテクチャ

```
ToNRoundCounter Desktop Client
        ↕ (WebSocket + REST)
ToNRoundCounter Cloud Backend
        ↕
    PostgreSQL + Redis
```

### 通信プロトコル

| プロトコル | 用途 | エンドポイント |
|-----------|------|---------------|
| **WebSocket** | リアルタイム通信（RPC + Stream） | `wss://cloud.tonround.com/ws` |
| **REST API** | HTTP CRUD操作 | `https://cloud.tonround.com/api/v1` |

### 認証方式

- **Session Token**: WebSocket接続時に発行
- **JWT**: REST API用（オプション）
- **有効期限**: 24時間

---

## 🔐 認証・認可

### セッション確立フロー

```
1. Client → Backend: WebSocket接続
   wss://cloud.tonround.com/ws
   
2. Backend → Client: 接続確立
   {"type": "connected", "session_id": "sess_xxx"}
   
3. Client → Backend: auth.login RPC
   {
     "rpc": "auth.login",
     "params": {
       "player_id": "player_123",
       "client_version": "2.5.0"
     }
   }
   
4. Backend → Client: 認証成功
   {
     "rpc": "auth.login",
     "result": {
       "session_token": "token_yyy",
       "expires_at": "2025-11-06T10:00:00Z"
     }
   }
```

### セッショントークン仕様

| 項目 | 仕様 |
|------|------|
| **形式** | `sess_` + UUID v4 |
| **有効期限** | 24時間 |
| **更新** | アクティビティごとに自動延長 |
| **保存場所** | Redis（key: `session:{session_id}`) |

---

## 🔌 WebSocket API

### 接続仕様

```
URL: wss://cloud.tonround.com/ws
Protocol: WebSocket (RFC 6455)
Encoding: UTF-8
Max Message Size: 1MB
Heartbeat: 30秒ごと
```

### メッセージフォーマット

#### RPC (Request-Response)

```json
{
  "id": "req_uuid",
  "rpc": "method.name",
  "params": {
    "param1": "value1",
    "param2": "value2"
  }
}
```

#### Stream (Server → Client 一方向)

```json
{
  "stream": "event.name",
  "data": {
    "field1": "value1",
    "field2": "value2"
  },
  "timestamp": "2025-11-05T10:00:00Z"
}
```

---

## 📡 WebSocket RPC 一覧

### 1. 認証系 (auth.*)

#### auth.login

**用途:** プレイヤーログイン・セッション確立

**Request:**
```json
{
  "rpc": "auth.login",
  "params": {
    "player_id": "string",
    "client_version": "string"
  }
}
```

**Response:**
```json
{
  "rpc": "auth.login",
  "result": {
    "session_token": "string",
    "player_id": "string",
    "expires_at": "ISO8601"
  }
}
```

#### auth.logout

**用途:** セッション終了

**Request:**
```json
{
  "rpc": "auth.logout",
  "params": {}
}
```

**Response:**
```json
{
  "rpc": "auth.logout",
  "result": {
    "success": true
  }
}
```

---

### 2. インスタンス管理 (instance.*)

#### instance.create

**用途:** 新規インスタンス作成

**Request:**
```json
{
  "rpc": "instance.create",
  "params": {
    "max_players": 6,
    "settings": {
      "auto_suicide_mode": "coordinated",
      "voting_timeout": 10
    }
  }
}
```

**Response:**
```json
{
  "rpc": "instance.create",
  "result": {
    "instance_id": "inst_xxx",
    "created_at": "ISO8601"
  }
}
```

#### instance.join

**用途:** インスタンスに参加

**Request:**
```json
{
  "rpc": "instance.join",
  "params": {
    "instance_id": "inst_xxx"
  }
}
```

**Response:**
```json
{
  "rpc": "instance.join",
  "result": {
    "instance_id": "inst_xxx",
    "members": [
      {
        "player_id": "player_456",
        "player_name": "Alice",
        "joined_at": "ISO8601"
      }
    ]
  }
}
```

#### instance.leave

**用途:** インスタンスから離脱

**Request:**
```json
{
  "rpc": "instance.leave",
  "params": {
    "instance_id": "inst_xxx"
  }
}
```

**Response:**
```json
{
  "rpc": "instance.leave",
  "result": {
    "success": true
  }
}
```

#### instance.list

**用途:** 参加可能なインスタンス一覧取得

**Request:**
```json
{
  "rpc": "instance.list",
  "params": {
    "filter": "available",
    "limit": 20
  }
}
```

**Response:**
```json
{
  "rpc": "instance.list",
  "result": {
    "instances": [
      {
        "instance_id": "inst_xxx",
        "member_count": 3,
        "max_players": 6,
        "created_at": "ISO8601"
      }
    ],
    "total": 10
  }
}
```

---

### 3. プレイヤー状態同期 (player.*)

#### player.state.update

**用途:** プレイヤー状態のブロードキャスト

**Request:**
```json
{
  "rpc": "player.state.update",
  "params": {
    "instance_id": "inst_xxx",
    "player_state": {
      "player_id": "player_123",
      "velocity": 2.5,
      "afk_duration": 0,
      "items": ["Diamond", "Shield"],
      "damage": 45,
      "is_alive": true
    }
  }
}
```

**Response:**
```json
{
  "rpc": "player.state.update",
  "result": {
    "success": true,
    "timestamp": "ISO8601"
  }
}
```

---

### 4. テラー関連 (threat.*)

#### threat.announced

**用途:** テラー出現通知（Server → Client Stream）

**Stream (Backend → Client):**
```json
{
  "stream": "threat.announced",
  "data": {
    "terror_name": "Silent Crush",
    "round_key": "hallway",
    "instance_id": "inst_xxx",
    "desire_players": [
      {
        "player_id": "player_456",
        "player_name": "Alice"
      },
      {
        "player_id": "player_789",
        "player_name": "Bob"
      }
    ]
  },
  "timestamp": "ISO8601"
}
```

#### threat.response

**用途:** テラー出現への対応選択

**Request:**
```json
{
  "rpc": "threat.response",
  "params": {
    "threat_id": "threat_xxx",
    "player_id": "player_123",
    "decision": "survive"
  }
}
```

**`decision` の値:**
- `"survive"` - 生存を目指す
- `"cancel"` - キャンセル
- `"skip"` - スキップ
- `"execute"` - 実行
- `"timeout"` - タイムアウト

**Response:**
```json
{
  "rpc": "threat.response",
  "result": {
    "success": true
  }
}
```

---

### 5. 統率投票 (coordinated.voting.*)

#### coordinated.voting.start

**用途:** 投票キャンペーン開始

**Request:**
```json
{
  "rpc": "coordinated.voting.start",
  "params": {
    "instance_id": "inst_xxx",
    "campaign_id": "campaign_xxx",
    "terror_name": "Silent Crush",
    "expires_at": "ISO8601"
  }
}
```

**Response:**
```json
{
  "rpc": "coordinated.voting.start",
  "result": {
    "campaign_id": "campaign_xxx",
    "expires_at": "ISO8601"
  }
}
```

#### coordinated.voting.vote

**用途:** 投票送信

**Request:**
```json
{
  "rpc": "coordinated.voting.vote",
  "params": {
    "campaign_id": "campaign_xxx",
    "player_id": "player_123",
    "decision": "Proceed"
  }
}
```

**`decision` の値:**
- `"Proceed"` - 実行
- `"Cancel"` - キャンセル

**Response:**
```json
{
  "rpc": "coordinated.voting.vote",
  "result": {
    "success": true
  }
}
```

#### coordinated.voting.resolved

**用途:** 投票結果通知（Server → Client Stream）

**Stream (Backend → Client):**
```json
{
  "stream": "coordinated.voting.resolved",
  "data": {
    "campaign_id": "campaign_xxx",
    "final_decision": "Proceed",
    "votes": [
      {
        "player_id": "player_456",
        "decision": "Proceed"
      },
      {
        "player_id": "player_789",
        "decision": "Proceed"
      },
      {
        "player_id": "player_123",
        "decision": "Cancel"
      }
    ],
    "vote_count": {
      "proceed": 2,
      "cancel": 1
    }
  },
  "timestamp": "ISO8601"
}
```

---

### 6. ほしいテラー設定 (wished.*)

#### wished.terrors.update

**用途:** ほしいテラー一覧を更新

**Request:**
```json
{
  "rpc": "wished.terrors.update",
  "params": {
    "player_id": "player_123",
    "wished_terrors": [
      {
        "terror_name": "Silent Crush",
        "round_key": "hallway"
      },
      {
        "terror_name": "Piranhas",
        "round_key": ""
      }
    ]
  }
}
```

**Response:**
```json
{
  "rpc": "wished.terrors.update",
  "result": {
    "success": true,
    "updated_at": "ISO8601"
  }
}
```

#### wished.terrors.get

**用途:** ほしいテラー一覧を取得

**Request:**
```json
{
  "rpc": "wished.terrors.get",
  "params": {
    "player_id": "player_123"
  }
}
```

**Response:**
```json
{
  "rpc": "wished.terrors.get",
  "result": {
    "wished_terrors": [
      {
        "id": "uuid_xxx",
        "terror_name": "Silent Crush",
        "round_key": "hallway",
        "created_at": "ISO8601"
      }
    ]
  }
}
```

---

### 7. プロフィール (profile.*)

#### profile.get

**用途:** プレイヤープロフィール取得

**Request:**
```json
{
  "rpc": "profile.get",
  "params": {
    "player_id": "player_123"
  }
}
```

**Response:**
```json
{
  "rpc": "profile.get",
  "result": {
    "player_id": "player_123",
    "player_name": "Alice",
    "skill_level": 0.72,
    "terror_stats": {
      "Silent Crush": {
        "survival_rate": 0.68,
        "total_rounds": 25,
        "survived": 17
      }
    },
    "last_active": "ISO8601"
  }
}
```

---

## 🌐 REST API

### ベースURL

```
https://cloud.tonround.com/api/v1
```

### 認証ヘッダー

```
Authorization: Bearer {session_token}
Content-Type: application/json
```

---

### エンドポイント一覧

#### GET /instances

**用途:** インスタンス一覧取得

**Query Parameters:**
- `filter`: `available` | `active` | `all`
- `limit`: 整数（デフォルト: 20）
- `offset`: 整数（デフォルト: 0）

**Response:**
```json
{
  "instances": [
    {
      "instance_id": "inst_xxx",
      "member_count": 3,
      "max_players": 6,
      "created_at": "ISO8601"
    }
  ],
  "total": 10,
  "limit": 20,
  "offset": 0
}
```

#### GET /instances/{instance_id}

**用途:** インスタンス詳細取得

**Response:**
```json
{
  "instance_id": "inst_xxx",
  "members": [
    {
      "player_id": "player_456",
      "player_name": "Alice",
      "joined_at": "ISO8601"
    }
  ],
  "settings": {
    "auto_suicide_mode": "coordinated",
    "voting_timeout": 10
  },
  "created_at": "ISO8601"
}
```

#### POST /instances

**用途:** インスタンス作成

**Request Body:**
```json
{
  "max_players": 6,
  "settings": {
    "auto_suicide_mode": "coordinated",
    "voting_timeout": 10
  }
}
```

**Response:**
```json
{
  "instance_id": "inst_xxx",
  "created_at": "ISO8601"
}
```

#### DELETE /instances/{instance_id}

**用途:** インスタンス削除

**Response:**
```json
{
  "success": true
}
```

#### GET /profiles/{player_id}

**用途:** プレイヤープロフィール取得

**Response:**
```json
{
  "player_id": "player_123",
  "player_name": "Alice",
  "skill_level": 0.72,
  "terror_stats": {
    "Silent Crush": {
      "survival_rate": 0.68,
      "total_rounds": 25,
      "survived": 17
    }
  },
  "last_active": "ISO8601"
}
```

#### GET /stats/terrors

**用途:** テラー統計取得

**Query Parameters:**
- `player_id`: プレイヤーID（オプション）

**Response:**
```json
{
  "terror_stats": [
    {
      "terror_name": "Silent Crush",
      "total_rounds": 1250,
      "avg_survival_rate": 0.45,
      "difficulty": "hard"
    }
  ]
}
```

---

## 📦 データモデル

### PlayerState

```json
{
  "player_id": "string",
  "velocity": "number",
  "afk_duration": "number",
  "items": ["string"],
  "damage": "number",
  "is_alive": "boolean",
  "timestamp": "ISO8601"
}
```

### Instance

```json
{
  "instance_id": "string",
  "members": [
    {
      "player_id": "string",
      "player_name": "string",
      "joined_at": "ISO8601"
    }
  ],
  "settings": {
    "auto_suicide_mode": "string",
    "voting_timeout": "number"
  },
  "created_at": "ISO8601"
}
```

### WishedTerror

```json
{
  "id": "string",
  "terror_name": "string",
  "round_key": "string",
  "created_at": "ISO8601"
}
```

### VotingCampaign

```json
{
  "campaign_id": "string",
  "instance_id": "string",
  "terror_name": "string",
  "votes": [
    {
      "player_id": "string",
      "decision": "string",
      "voted_at": "ISO8601"
    }
  ],
  "final_decision": "string",
  "created_at": "ISO8601",
  "expires_at": "ISO8601"
}
```

---

## ⚠️ エラーハンドリング

### エラーレスポンス形式

```json
{
  "error": {
    "code": "string",
    "message": "string",
    "details": {}
  }
}
```

### エラーコード一覧

| コード | HTTP Status | 説明 |
|--------|-------------|------|
| `AUTH_REQUIRED` | 401 | 認証が必要 |
| `AUTH_EXPIRED` | 401 | セッション期限切れ |
| `INVALID_TOKEN` | 401 | 無効なトークン |
| `PERMISSION_DENIED` | 403 | 権限不足 |
| `NOT_FOUND` | 404 | リソースが存在しない |
| `INSTANCE_FULL` | 409 | インスタンスが満員 |
| `ALREADY_JOINED` | 409 | 既に参加済み |
| `INVALID_PARAMS` | 400 | パラメータが不正 |
| `RATE_LIMIT_EXCEEDED` | 429 | レート制限超過 |
| `INTERNAL_ERROR` | 500 | サーバーエラー |

### エラー例

```json
{
  "error": {
    "code": "INSTANCE_FULL",
    "message": "Instance is full (6/6 members)",
    "details": {
      "instance_id": "inst_xxx",
      "current_members": 6,
      "max_players": 6
    }
  }
}
```

---

## 🚦 レート制限

### WebSocket RPC

| 操作 | 制限 |
|------|------|
| `player.state.update` | 2回/秒 |
| `instance.*` | 10回/分 |
| `wished.terrors.*` | 5回/分 |
| その他 | 60回/分 |

### REST API

| エンドポイント | 制限 |
|--------------|------|
| `GET /instances` | 60回/分 |
| `POST /instances` | 10回/分 |
| `GET /profiles/*` | 120回/分 |
| その他 | 60回/分 |

### レート制限超過時

**Response:**
```json
{
  "error": {
    "code": "RATE_LIMIT_EXCEEDED",
    "message": "Rate limit exceeded. Try again in 30 seconds.",
    "details": {
      "limit": 60,
      "remaining": 0,
      "reset_at": "ISO8601"
    }
  }
}
```

---

## ⚡ パフォーマンス要件

| 項目 | 要件 |
|------|------|
| **WebSocket接続確立** | < 500ms |
| **RPC応答時間** | < 100ms (p95) |
| **Stream配信遅延** | < 50ms (p95) |
| **REST API応答** | < 200ms (p95) |
| **同時接続数** | 1000クライアント |
| **メッセージスループット** | 10,000 msg/sec |

---

## 📊 監視・ログ

### メトリクス

- WebSocket接続数
- RPC呼び出し回数・レイテンシ
- エラーレート
- レート制限違反回数

### ログレベル

- **INFO**: 正常動作ログ
- **WARN**: 警告（レート制限など）
- **ERROR**: エラー（認証失敗など）
- **FATAL**: システムクリティカルエラー

---

## 🔄 バージョニング

### API バージョン管理

- **現行バージョン**: v1
- **URL形式**: `/api/v1/*`
- **WebSocket**: クエリパラメータ `?version=v1`
- **後方互換性**: 1年間保証

---

**END OF DOCUMENT**