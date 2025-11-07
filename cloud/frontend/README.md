# ToNRoundCounter Cloud Frontend

React + TypeScript + Viteで構築されたフロントエンドアプリケーション

## 📋 実装済み機能

### コアコンポーネント
- ✅ WebSocketクライアントライブラリ
- ✅ Zustandによるグローバル状態管理
- ✅ React Routerによるページ遷移

### ページ
- ✅ ログインページ (`/login`)
- ✅ ダッシュボード (`/dashboard`)

### ダッシュボード機能
- ✅ 接続ステータス表示
- ✅ インスタンス管理（作成、参加、離脱、一覧）
- ✅ プレイヤー状態表示（リアルタイム更新）
- ✅ 統率自動自殺投票パネル
- ✅ 統計・分析ビューア（グラフ、データエクスポート）

## 🚀 セットアップ

### 依存関係のインストール

```bash
cd cloud/frontend
npm install
```

### 開発サーバー起動

```bash
npm run dev
```

ブラウザで `http://localhost:5173` を開く

### プロダクションビルド

```bash
npm run build
```

ビルドされたファイルは `dist/` ディレクトリに出力されます。

## 📁 プロジェクト構造

```
src/
├── components/          # Reactコンポーネント
│   ├── ConnectionStatus.tsx
│   ├── InstanceList.tsx
│   ├── PlayerStates.tsx
│   ├── VotingPanel.tsx
│   └── StatisticsPanel.tsx
├── pages/              # ページコンポーネント
│   ├── Login.tsx
│   └── Dashboard.tsx
├── lib/                # ライブラリ
│   └── websocket-client.ts   # WebSocketクライアント
├── store/              # 状態管理
│   └── appStore.ts     # Zustandストア
├── App.tsx             # メインアプリ
├── App.css             # スタイル
└── main.tsx            # エントリポイント
```

## 🔧 技術スタック

- **React 18** - UIライブラリ
- **TypeScript 5.3** - 型安全性
- **Vite 5** - ビルドツール
- **Zustand 4** - 状態管理
- **React Router 6** - ルーティング
- **Recharts 2** - グラフ描画
- **Date-fns 3** - 日時操作

## 📡 WebSocketクライアント

### 基本的な使い方

```typescript
import { ToNRoundCloudClient } from './lib/websocket-client';

// クライアント作成
const client = new ToNRoundCloudClient('ws://localhost:3000/ws');

// 接続
await client.connect();

// ログイン
const session = await client.login('player123', '1.0.0');

// RPCメソッド呼び出し
const instances = await client.listInstances();

// ストリームイベント購読
const unsubscribe = client.onPlayerStateUpdate((data) => {
    console.log('Player state updated:', data);
});

// 購読解除
unsubscribe();
```

### 利用可能なRPCメソッド

#### 認証
- `login(playerId, clientVersion)` - ログイン
- `logout()` - ログアウト

#### インスタンス管理
- `createInstance(maxPlayers, settings)` - インスタンス作成
- `joinInstance(instanceId)` - インスタンス参加
- `leaveInstance(instanceId)` - インスタンス離脱
- `listInstances()` - インスタンス一覧取得

#### プレイヤー状態
- `updatePlayerState(playerId, state, data)` - 状態更新

#### 投票
- `startVoting(instanceId, terrorName, expiresAt)` - 投票開始
- `submitVote(campaignId, playerId, decision)` - 投票

#### 希望テロール
- `updateWishedTerrors(playerId, wishedTerrors)` - 更新
- `getWishedTerrors(playerId)` - 取得

#### プロファイル
- `getProfile(playerId)` - プロファイル取得

#### 設定
- `getSettings(userId?)` - 設定取得
- `updateSettings(userId, settings)` - 設定更新
- `syncSettings(userId, localSettings, localVersion)` - 設定同期

#### 監視
- `reportStatus(instanceId, statusData)` - ステータス報告
- `getMonitoringStatus(userId?, limit)` - ステータス履歴取得
- `getMonitoringErrors(userId?, severity?, limit)` - エラーログ取得

#### リモート制御
- `createRemoteCommand(instanceId, commandType, action, parameters, priority)` - コマンド作成
- `executeRemoteCommand(commandId)` - コマンド実行
- `getRemoteCommandStatus(commandId?, instanceId?)` - ステータス確認

#### 分析
- `getPlayerAnalytics(playerId, timeRange?)` - プレイヤー統計
- `getTerrorAnalytics(terrorName?, timeRange?)` - テロール統計
- `getAnalyticsTrends(groupBy, limit)` - トレンド分析
- `exportAnalytics(format, dataType, filters?)` - データエクスポート

#### バックアップ
- `createBackup(type, compress, encrypt, description?)` - バックアップ作成
- `restoreBackup(backupId, options)` - バックアップ復元
- `listBackups(userId?)` - バックアップ一覧

### ストリームイベント

- `onPlayerStateUpdate(callback)` - プレイヤー状態更新
- `onInstanceMemberJoined(callback)` - メンバー参加
- `onInstanceMemberLeft(callback)` - メンバー離脱
- `onVotingStarted(callback)` - 投票開始
- `onVotingResolved(callback)` - 投票結果
- `onConnectionStateChange(callback)` - 接続状態変化

## 🎨 カスタマイズ

### スタイルの変更

`src/App.css`を編集してスタイルをカスタマイズできます。

### 新しいページの追加

1. `src/pages/`に新しいページコンポーネントを作成
2. `src/App.tsx`にルートを追加

```typescript
<Route path="/new-page" element={<NewPage />} />
```

### 新しいコンポーネントの追加

1. `src/components/`に新しいコンポーネントを作成
2. 必要なページでインポートして使用

## 🔒 認証フロー

1. ユーザーがログインページでプレイヤーIDを入力
2. WebSocketサーバーに接続
3. `auth.login` RPCでログイン
4. セッショントークンを受け取り、ストアに保存
5. ダッシュボードにリダイレクト
6. 以降のRPC呼び出しで自動的にセッション認証

## 🐛 トラブルシューティング

### WebSocketに接続できない

- バックエンドサーバーが起動しているか確認
- サーバーURLが正しいか確認（デフォルト: `ws://localhost:3000/ws`）
- ブラウザのコンソールでエラーを確認

### データが表示されない

- ブラウザのコンソールでエラーを確認
- ネットワークタブでWebSocket通信を確認
- バックエンドのログを確認

### ビルドエラー

```bash
# node_modulesをクリーンアップして再インストール
rm -rf node_modules
npm install
```

## 📝 今後の改善点

### 優先度: 高
- [ ] エラーハンドリングの改善
- [ ] ローディング状態の改善
- [ ] レスポンシブデザイン対応
- [ ] ユニットテスト追加

### 優先度: 中
- [ ] ダークモード対応
- [ ] 多言語対応（i18n）
- [ ] オフライン対応
- [ ] PWA化

### 優先度: 低
- [ ] アニメーション追加
- [ ] アクセシビリティ改善
- [ ] SEO最適化

## 📄 ライセンス

MIT License
