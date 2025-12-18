# ToNRoundCounter Cloud 認証システム

## 概要

ToNRoundCounter Cloudは、セキュアな認証システムを採用しています。誰でも他人のアカウントにログインできないように、APIキーとワンタイムトークンを使用した認証を実装しています。

**プレイヤーID（ユーザーID）**: VRChatのプレイヤー名が自動的に使用されます。ToNRoundCounterアプリがVRChatに接続した際に取得される`DisplayName`がユーザーIDとなります。

## 認証フロー

### 1. 初回登録（自動・VRChat接続時）

ユーザーがクラウド同期を有効にしてVRChatに接続すると、自動的に登録が行われます。

**⚠️ 重要**: クラウド同期を初めて有効化した場合、ToNRoundCounterアプリを再起動してからVRChatに接続してください。

**処理の流れ**:
1. VRChatの`CONNECTED`イベントでプレイヤー名（DisplayName）を取得
2. 保存されたAPIキーが無い場合、新規登録を実行
3. 返却されたAPIキーを設定に自動保存

```csharp
// C# (ToNRoundCounterアプリ内 - 自動実行)
// VRChatのCONNECTEDイベントハンドラ内
if (string.IsNullOrWhiteSpace(settings.CloudApiKey))
{
    var (userId, apiKey) = await cloudClient.RegisterUserAsync(
        stateService.PlayerDisplayName  // VRChatのプレイヤー名
    );
    settings.CloudApiKey = apiKey;
    await settings.SaveAsync();
}
```

**重要**: APIキーは一度しか表示されません。アプリケーションが自動的に保存します。

### 2. 通常のログイン（APIキー使用・VRChat接続時）

2回目以降のVRChat接続では、保存されたAPIキーを使用して自動ログインします。

```csharp
// C# (ToNRoundCounterアプリ内 - 自動実行)
// VRChatのCONNECTEDイベントハンドラ内
var sessionToken = await cloudClient.LoginWithApiKeyAsync(
    stateService.PlayerDisplayName,  // VRChatのプレイヤー名
    settings.CloudApiKey
);
```

### 3. ブラウザでのログイン（ワンタイムトークン）

設定画面から「ToNRoundCounter_cloudを開く」ボタンをクリックしたときの流れ:

#### Step 1: ToNRoundCounterアプリでワンタイムトークンを生成

```csharp
// C# (ToNRoundCounterアプリ内)
var (token, loginUrl) = await cloudClient.GenerateOneTimeTokenAsync(
    stateService.PlayerDisplayName,  // VRChatのプレイヤー名
    settings.CloudApiKey
);
// ブラウザでloginUrlを開く
Process.Start(new ProcessStartInfo(loginUrl) { UseShellExecute = true });
```

#### Step 2: ブラウザで自動ログイン

```
http://localhost:8080/login?token={one-time-token}
```

フロントエンドはURLパラメータからトークンを取得し、自動的にログインします。

```typescript
// TypeScript (フロントエンド)
const params = new URLSearchParams(window.location.search);
const token = params.get('token');

if (token) {
    const session = await client.loginWithOneTimeToken(token, '1.0.0');
    // ダッシュボードに遷移
}
```

## API仕様

### 1. ユーザー登録

**RPC**: `auth.register`

**リクエスト**:
```json
{
    "rpc": "auth.register",
    "params": {
        "player_id": "PlayerName",
        "client_version": "1.0.0"
    }
}
```

**レスポンス**:
```json
{
    "result": {
        "user_id": "PlayerName",
        "api_key": "a1b2c3d4e5f6...",
        "message": "User registered successfully. Please save your API key securely - it cannot be recovered!"
    }
}
```

### 2. APIキーログイン

**RPC**: `auth.loginWithApiKey`

**リクエスト**:
```json
{
    "rpc": "auth.loginWithApiKey",
    "params": {
        "player_id": "PlayerName",
        "api_key": "a1b2c3d4e5f6...",
        "client_version": "1.0.0"
    }
}
```

**レスポンス**:
```json
{
    "result": {
        "session_token": "token_xxx...",
        "player_id": "PlayerName",
        "expires_at": "2025-01-16T12:00:00Z"
    }
}
```

### 3. ワンタイムトークン生成

**RPC**: `auth.generateOneTimeToken`

**リクエスト**:
```json
{
    "rpc": "auth.generateOneTimeToken",
    "params": {
        "player_id": "PlayerName",
        "api_key": "a1b2c3d4e5f6..."
    }
}
```

**レスポンス**:
```json
{
    "result": {
        "token": "uuid-token-here",
        "expires_in": 300,
        "login_url": "http://localhost:8080/login?token=uuid-token-here"
    }
}
```

### 4. ワンタイムトークンログイン

**RPC**: `auth.loginWithOneTimeToken`

**リクエスト**:
```json
{
    "rpc": "auth.loginWithOneTimeToken",
    "params": {
        "token": "uuid-token-here",
        "client_version": "1.0.0"
    }
}
```

**レスポンス**:
```json
{
    "result": {
        "session_token": "token_xxx...",
        "player_id": "PlayerName",
        "expires_at": "2025-01-16T12:00:00Z"
    }
}
```

## セキュリティ機能

### 1. APIキーのハッシュ化

- APIキーはSHA-256でハッシュ化してデータベースに保存
- 平文のAPIキーは登録時のみ返され、再取得不可

### 2. ワンタイムトークン

- 5分間のみ有効
- 一度使用したら無効化
- UUIDv4で生成され、推測不可能

### 3. セッション管理

- セッショントークンは24時間有効
- 自動リフレッシュ機能
- localStorage永続化でリロード後も維持

## データベーススキーマ

### users テーブル

```sql
CREATE TABLE users (
    user_id VARCHAR(255) PRIMARY KEY,
    username VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,  -- APIキーのハッシュ
    ...
);
```

### one_time_tokens テーブル

```sql
CREATE TABLE one_time_tokens (
    token VARCHAR(255) PRIMARY KEY,
    player_id VARCHAR(255) NOT NULL,
    expires_at TIMESTAMP NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
```

## 使用例

### ToNRoundCounterアプリでの実装例

```csharp
public class CloudService
{
    private CloudWebSocketClient _client;
    private IAppSettings _settings;

    // クラウド同期を初めて有効にする
    public async Task EnableCloudSyncAsync()
    {
        if (string.IsNullOrEmpty(_settings.CloudApiKey))
        {
            // 新規登録
            var (userId, apiKey) = await _client.RegisterUserAsync(
                _settings.CloudPlayerName
            );
            
            _settings.CloudApiKey = apiKey;
            await _settings.SaveAsync();
            
            MessageBox.Show(
                "クラウド登録が完了しました！\n" +
                "APIキーは安全に保存されました。",
                "登録完了"
            );
        }
        else
        {
            // 既存のAPIキーでログイン
            await _client.LoginWithApiKeyAsync(
                _settings.CloudPlayerName,
                _settings.CloudApiKey
            );
        }
    }

    // ダッシュボードをブラウザで開く
    public async Task OpenDashboardAsync()
    {
        var (token, loginUrl) = await _client.GenerateOneTimeTokenAsync(
            _settings.CloudPlayerName,
            _settings.CloudApiKey
        );
        
        Process.Start(new ProcessStartInfo(loginUrl) 
        { 
            UseShellExecute = true 
        });
    }
}
```

## トラブルシューティング

### APIキーを紛失した場合

現在、APIキーのリカバリ機能はありません。新しいユーザーとして再登録する必要があります。

### ワンタイムトークンが期限切れの場合

5分以内にブラウザでログインしてください。期限切れの場合は、ToNRoundCounterアプリから再度「開く」ボタンをクリックしてください。

### セッションが切れた場合

フロントエンドはlocalStorageにセッション情報を保存しているため、ブラウザをリロードしても自動的に再接続されます。

## 今後の拡張

- [ ] パスワードリセット機能
- [ ] 2要素認証（2FA）
- [ ] OAuth2対応
- [ ] APIキーの再発行機能
- [ ] セッションの遠隔無効化
