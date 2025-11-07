# ToNRoundCounter Cloud

モダンで拡張性の高いクラウドプラットフォームの実装。WebSocket RPC通信、リアルタイムダッシュボード、マルチプレイヤー機能を備えています。

## プロジェクト構成

```
cloud/
├── backend/           # Node.js WebSocket サーバー
│   ├── src/
│   │   ├── index.ts   # エントリーポイント
│   │   ├── server.ts  # WebSocket サーバー実装
│   │   ├── core.ts    # セッション・ルーター・DB管理
│   │   └── logger.ts  # ロギング
│   ├── package.json
│   └── tsconfig.json
├── frontend/          # React ダッシュボード
│   ├── src/
│   │   ├── App.tsx    # メインアプリ
│   │   ├── lib/       # ユーティリティ
│   │   │   └── client.ts  # WebSocket クライアント
│   │   └── main.tsx   # エントリーポイント
│   ├── index.html
│   ├── package.json
│   ├── tsconfig.json
│   └── vite.config.ts
├── shared/            # 型定義・共有ライブラリ
│   ├── src/
│   │   └── index.ts   # 全ての型定義とヘルパー
│   ├── package.json
│   └── tsconfig.json
└── docs/
    ├── API_SPECIFICATION.md  # 完全な API 仕様書
    ├── DEPLOYMENT.md         # デプロイガイド
    └── DEVELOPMENT.md        # 開発環境セットアップ
```

## API 仕様

完全な API 仕様は `docs/API_SPECIFICATION.md` を参照してください。

### 通信パターン

#### 1. Request-Response
```json
{
  "version": "1.0",
  "id": "uuid",
  "type": "request",
  "method": "game.roundStart",
  "params": { ... }
}
```

#### 2. Stream
```json
{
  "version": "1.0",
  "id": "uuid",
  "type": "stream",
  "event": "game.playerUpdate",
  "data": { ... }
}
```

#### 3. Subscription
```json
{
  "method": "subscribe",
  "params": {
    "channel": "game.playerUpdate",
    "filters": { ... }
  }
}
```

## セットアップ

### 前提条件
- Node.js 18+
- npm または yarn
- .NET Framework 4.8 (クライアント側)

### バックエンド

```bash
cd cloud/backend
npm install
npm run dev  # 開発モード
npm run build
npm start    # 本番環境
```

ポート: `8080`

### フロントエンド

```bash
cd cloud/frontend
npm install
npm run dev  # 開発サーバー (localhost:5173)
npm run build
npm run preview
```

### 共有型ライブラリ

```bash
cd cloud/shared
npm install
npm run build
```

## .NET クライアント統合

既存の ToNRoundCounter デスクトップアプリケーションは新しい `CloudWebSocketClient` を使用しています。

### 使用例

```csharp
// クライアント初期化
var client = new CloudWebSocketClient(
    "ws://cloud.example.com:8080",
    eventBus,
    cancellationProvider,
    logger
);

// 接続開始
await client.StartAsync();

// ラウンド開始
var roundId = await client.GameRoundStartAsync("PlayerName", "Normal", "MapName");

// ラウンド終了
var stats = await client.GameRoundEndAsync(
    roundId,
    survived: true,
    duration: 300,
    terrorName: "Crush"
);

// インスタンス参加
var subscriptionId = await client.InstanceJoinAsync("shared-party", "PlayerName");
```

## 拡張性設計

### 新しい RPC メソッド追加

backend/src/server.ts の `setupRPCHandlers()` に追加:

```typescript
this.rpcRouter.register('custom.method', async (req) => {
  const params = req.message.params as any;
  // ビジネスロジック
  return result;
});
```

### ストリームイベント追加

フロントエンド側で購読:

```typescript
await client.call('subscribe', {
  channel: 'custom.events'
});

client.on('message', (msg) => {
  if (msg.event === 'custom.events') {
    console.log(msg.data);
  }
});
```

## ロードマップ

- [ ] データベース統合 (SQLite → PostgreSQL)
- [ ] 認証・認可 (JWT)
- [ ] レート制限・クォータ
- [ ] メトリクス・監視
- [ ] Kubernetes デプロイメント
- [ ] WebSocket 圧縮
- [ ] バイナリメッセージサポート

## トラブルシューティング

### WebSocket 接続が切れる

- ファイアウォール設定を確認
- ロードバランサーのタイムアウト設定を確認 (推奨: 30秒以上)
- サーバーログ (`logs/`) を確認

### メッセージが受信されない

- `version` フィールドが `"1.0"` であることを確認
- `method` または `event` フィールドが正しく指定されているか確認
- `.NET` 側で `CloudWebSocketClient` が接続状態であることを確認

### パフォーマンス問題

- ストリーム購読時に `minUpdateInterval` を設定して更新頻度を制限
- データベースクエリをプロファイルして最適化
- WebSocket メッセージサイズを圧縮

## ライセンス

ToNRoundCounter Cloud は ToNRoundCounter と同じライセンスの下で提供されます。詳細は `license.md` を参照してください。

---

**最終更新:** 2025年1月15日
