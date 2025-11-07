# ToNRoundCounter Cloud Backend

完全実装されたクラウドバックエンドサーバー

## 📋 実装済み機能

### 1. コア機能
- ✅ WebSocket通信基盤
- ✅ REST API
- ✅ SQLiteデータベース
- ✅ セッション管理
- ✅ 認証システム

### 2. 協力サバイバルシステム
- ✅ ほしいテラー設定
- ✅ プレイヤー状態同期
- ✅ 統率自動自殺投票システム
- ✅ テラー出現通知
- ✅ リアルタイムブロードキャスト

### 3. インスタンス管理
- ✅ インスタンス作成・削除
- ✅ メンバー参加・離脱
- ✅ インスタンス一覧取得
- ✅ メンバー管理

### 4. プロフィール管理
- ✅ プレイヤープロフィール
- ✅ テラー統計
- ✅ 生存率計算

### 5. ラウンドデータ同期
- ✅ ラウンド開始・終了
- ✅ イベント記録
- ✅ テラー出現記録

### 6. リモート設定同期
- ✅ 設定バージョン管理
- ✅ 競合解決（リモート優先）
- ✅ 設定履歴追跡
- ✅ リアルタイム同期

### 7. ステータス監視
- ✅ アプリケーションステータス報告
- ✅ エラーログ記録（重症度別）
- ✅ 履歴追跡
- ✅ 自動クリーンアップ

### 8. リモート制御
- ✅ コマンド実行フレームワーク
- ✅ ラウンド制御（開始/停止/リセット）
- ✅ 設定変更
- ✅ 緊急停止
- ✅ アプリ再起動

### 9. 統計・分析
- ✅ プレイヤー統計
- ✅ テラー統計
- ✅ トレンド分析
- ✅ データエクスポート（JSON/CSV）

### 10. バックアップ・リストア
- ✅ 完全/差分/増分バックアップ
- ✅ 圧縮サポート
- ✅ 暗号化サポート
- ✅ リストア機能

## 🚀 セットアップ

### 必要要件
- Node.js 18+
- npm または yarn

### インストール

```bash
# 依存関係のインストール
npm install

# 環境変数の設定
cp .env.example .env

# データディレクトリの作成
mkdir -p data
```

### 開発モードで起動

```bash
npm run dev
```

### ビルドと本番起動

```bash
# TypeScriptをビルド
npm run build

# 本番モードで起動
npm start
```

## 📡 API エンドポイント

### WebSocket API
```
ws://localhost:3000/ws
```

#### 接続と認証
1. クライアントは接続直後に `auth.connect` を送信し、サーバーバージョンとセッション ID を取得します。
2. 必要に応じて `auth.login` を呼び出し、クラウドユーザー ID を紐付けます。
3. 以降の RPC 呼び出しでは `type: "request"` / `type: "response"` のメッセージフォーマットを使用します。
4. サーバーからの通知は `type: "stream"`、`event` にイベント名が格納されます。

#### 認証
- `auth.login` - ログイン
- `auth.logout` - ログアウト

#### インスタンス管理
- `instance.create` - インスタンス作成
- `instance.join` - インスタンス参加
- `instance.leave` - インスタンス離脱
- `instance.list` - インスタンス一覧

#### プレイヤー状態
- `player.state.update` - 状態更新

#### 投票システム
- `coordinated.voting.start` - 投票開始
- `coordinated.voting.vote` - 投票
- `coordinated.voting.resolved` - 投票結果（Stream）

#### ほしいテラー
- `wished.terrors.update` - 更新
- `wished.terrors.get` - 取得

#### プロフィール
- `profile.get` - プロフィール取得

#### 設定
- `settings.get` - 設定取得
- `settings.update` - 設定更新
- `settings.sync` - 設定同期

#### 監視
- `monitoring.report` - ステータス報告
- `monitoring.status` - ステータス履歴取得
- `monitoring.errors` - エラーログ取得

#### リモート制御
- `remote.command.create` - コマンド作成
- `remote.command.execute` - コマンド実行
- `remote.command.status` - ステータス確認

#### 分析
- `analytics.player` - プレイヤー統計
- `analytics.terror` - テラー統計
- `analytics.trends` - トレンド分析
- `analytics.export` - データエクスポート

#### バックアップ
- `backup.create` - バックアップ作成
- `backup.restore` - バックアップリストア
- `backup.list` - バックアップ一覧

#### 主なストリームイベント
- `instance.member.joined` / `instance.member.left`
- `player.state.updated`
- `settings.updated`
- `monitoring.status.updated` / `monitoring.error.logged`
- `remote.command.completed` / `remote.command.failed`
- `threat.announced` / `threat.response.recorded`

### REST API
```
http://localhost:3000/api/v1
```

#### インスタンス
- `GET /instances` - インスタンス一覧
- `GET /instances/:id` - インスタンス詳細
- `POST /instances` - インスタンス作成
- `DELETE /instances/:id` - インスタンス削除

#### プロフィール
- `GET /profiles/:playerId` - プロフィール取得

#### 統計
- `GET /stats/terrors` - テラー統計

## 🗄️ データベーススキーマ

### 主要テーブル（17テーブル）
- `users` - ユーザー情報
- `sessions` - セッション管理
- `instances` - インスタンス
- `instance_members` - インスタンスメンバー
- `player_states` - プレイヤー状態
- `wished_terrors` - ほしいテラー設定
- `voting_campaigns` - 投票キャンペーン
- `player_votes` - プレイヤー投票
- `rounds` - ラウンド情報
- `terror_appearances` - テラー出現記録
- `player_profiles` - プレイヤープロフィール
- `settings` - 設定管理
- `status_monitoring` - ステータス監視
- `error_logs` - エラーログ
- `backups` - バックアップメタデータ
- `remote_commands` - リモートコマンド
- `event_notifications` - イベント通知

## 📊 仕様準拠状況

### API仕様書準拠
- ✅ WebSocket RPC メソッド
- ✅ REST API エンドポイント
- ✅ データモデル
- ✅ エラーハンドリング
- ✅ レート制限（実装可能）

### 協力サバイバルシステム仕様準拠
- ✅ ほしいテラー登録
- ✅ マッチング判定
- ✅ 投票システム
- ✅ タイムアウト処理
- ✅ リアルタイム通知

## 🔧 開発

### ログ
pinoロガーを使用。開発モードではpretty出力。

### デバッグ
```bash
npm run dev
```

### テスト
```bash
npm test
```

## 📝 実装詳細

### WebSocket通信
- RFC 6455準拠
- 30秒ごとのハートビート
- 自動再接続対応（クライアント側）

### セッション管理
- UUIDベースのセッションID
- 24時間有効期限
- 自動延長機能

### 投票システム
- 10秒タイムアウト
- 過半数判定
- 未投票者は自動的にCancel

### データベース
- SQLite3
- 外部キー制約有効
- インデックス最適化済み

## 🐳 Docker対応

```bash
# Dockerイメージのビルド
docker build -t tonround-backend .

# コンテナ起動
docker run -p 3000:3000 -v $(pwd)/data:/app/data tonround-backend
```

## 📄 ライセンス

MIT License
