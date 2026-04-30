# ToNRoundCounterSDK

外部 .NET アプリから ToNRoundCounter Cloud に接続するための C# SDK です。

この README は、SDK を自分のアプリに組み込むための実装ガイドです。

## できること

- ユーザーに外部アプリ連携を許可してもらう
- APPToken を受け取って次回以降のログインに使う
- Cloud に接続してインスタンス情報を読む
- 許可されたアプリだけがプレイヤー状態やラウンド結果を送る
- Cloud から届く Stream イベントを `on` で受け取る
- 同じ APPID の SDK クライアント同士でカスタム RPC を送る

## 必要なもの

外部アプリ側で次の値を扱います。

| 値 | アプリ側での扱い |
| --- | --- |
| `playerId` | ユーザーに入力してもらうか、あなたのアプリ側設定から読む |
| `apiKey` | ToNRoundCounter 本体で発行された API キーをユーザーに入力してもらう |
| `appId` | あなたのアプリ固有の ID。例: `com.example.mytool` |
| `appToken` | 初回認可で受け取り、アプリ側で安全に保存する |

`apiKey` と `appToken` は秘密情報です。ログ、クラッシュレポート、画面表示にそのまま出さないでください。

## 参照を追加する

SDK は .NET 9.0 向けです。リポジトリ内でビルドする場合:

```powershell
dotnet build ToNRoundCounterSDK\ToNRoundCounterSDK.csproj -c Release
```

出力された DLL を外部アプリから参照します。

```text
ToNRoundCounterSDK\bin\Release\net9.0\ToNRoundCounterSDK.dll
```

アプリ側のコードでは namespace を追加します。

```csharp
using ToNRoundCounterSDK;
```

## 最初に作る形

まずは「保存済み APPToken がなければ認可を開く -> 接続 -> ログイン」という形にすると扱いやすいです。

```csharp
using ToNRoundCounterSDK;

const string appId = "com.example.mytool";
const string appName = "My ToN Tool";

var requiredScopes = new[]
{
    "read:instances",
};

string playerId = LoadPlayerIdFromYourSettings();
string apiKey = LoadApiKeyFromYourSettings();
string? appToken = LoadAppTokenFromYourSettings();

if (string.IsNullOrWhiteSpace(appToken))
{
    AppAuthorizationResult authorization =
        await ToNRoundCounterCloudClient.RequestAppAuthorizationAsync(
            appId: appId,
            appName: appName,
            scopes: requiredScopes);

    appToken = authorization.AppToken;
    SaveAppTokenToYourSettings(appToken);
}

await using var cloud = new ToNRoundCounterCloudClient(new ToNRoundCounterCloudOptions
{
    AppId = appId,
    AppToken = appToken,
});

await cloud.ConnectAsync();
LoginResult login = await cloud.LoginWithApiKeyAsync(playerId, apiKey);

Console.WriteLine($"Logged in as {login.PlayerId}");
```

`Load...` と `Save...` はあなたのアプリ側の設定保存処理に置き換えてください。

Windows アプリなら Credential Manager、DPAPI、暗号化済み設定ファイルなど、平文保存を避けられる場所を推奨します。

## APPID を決める

`appId` はアプリごとに固定してください。

おすすめは reverse DNS 風の名前です。

```text
com.yourname.overlaytool
dev.yourname.roundviewer
net.yourteam.tonhelper
```

一度ユーザーが許可した APPToken は APPID と結びつきます。後から APPID を変えると、ユーザーは再認可が必要になります。

## 要求する権限を選ぶ

初回認可で `scopes` に、あなたのアプリが使う機能だけを指定します。

例:

| アプリでやりたいこと | 指定する scope |
| --- | --- |
| 同じアプリ同士でカスタムRPC経由で独自のデータをやり取りする | `app:custom_rpc` |
| インスタンス一覧や詳細を読む | `read:instances` |
| ラウンド履歴や分析用データを読む | `read:rounds`, `read:analytics` |
| プロフィールを読む | `read:profiles` |
| インスタンスを作成または更新する | `cloud:instances:write` |
| プレイヤーの状態を送る | `cloud:player_state:write` |
| ラウンドの結果を送る | `cloud:rounds:write` |

`cloud:*:write` 系は特権スコープと呼ばれる非常に強い権限です。多くの場合この権限は必要ありませんし、ToNRoundCounter本体がアップロードした情報との競合を避けるため、原則として開発者yussyより個別に許可されていない場合利用できません。

読み取りだけのツールなら、必要な `read:*` だけを指定してください。カスタム RPC を使わないなら `app:custom_rpc` は不要です。

## スコープ一覧

認証や接続そのものにはスコープは不要です。スコープは「ログイン後に、その APPToken でどの Cloud 機能を使えるか」を決めます。

読み取り系のスコープ:

| scope | SDKメソッド / RPC | 用途 |
| --- | --- | --- |
| `read:instances` | `ListInstancesAsync`, `GetInstanceAsync`, `instance.list`, `instance.get`, `player.instance.get` | インスタンス一覧、詳細、現在参加中のインスタンスを読む |
| `read:player_state` | `player.states.get`, `player.state.get` | インスタンス内のプレイヤー状態や個別プレイヤー状態を読む |
| `read:rounds` | `round.list` | ラウンド履歴を読む |
| `read:voting` | `coordinated.voting.getCampaign`, `coordinated.voting.getActive`, `coordinated.voting.getVotes` | 投票キャンペーン、進行中の投票、投票結果を読む |
| `read:auto_suicide` | `coordinated.autoSuicide.get` | 連携 Auto Suicide 設定や状態を読む |
| `read:wished_terrors` | `wished.terrors.get`, `wished.terrors.findDesirePlayers` | Wished Terror の希望情報や希望プレイヤーを読む |
| `read:profiles` | `profile.get` | プレイヤープロフィールを読む |
| `read:settings` | `settings.get`, `settings.history` | Cloud に保存された設定と履歴を読む |
| `read:monitoring` | `monitoring.status`, `monitoring.errors`, `client.status.get` | 監視情報、エラー情報、クライアント状態を読む |
| `read:analytics` | `analytics.player`, `analytics.terror`, `analytics.trends`, `analytics.export`, `analytics.instance`, `analytics.voting`, `analytics.roundTypes` | プレイヤー統計、テラー統計、傾向、エクスポート、インスタンス分析、投票分析、ラウンド種別分析を読む |
| `read:backups` | `backup.list` | バックアップ一覧を読む |

アプリ同士の連携用スコープ:

| scope | SDKメソッド / RPC | 用途 |
| --- | --- | --- |
| `app:custom_rpc` | `SendCustomRpcAsync`, `custom.rpc.send`, `On/on` でのカスタム RPC 受信 | 同じ APPID でログインしている SDK クライアント同士で、アプリ独自のメッセージを送受信する |

書き込み系の特権スコープ:

| scope | SDKメソッド / RPC | 用途 |
| --- | --- | --- |
| `cloud:instances:write` | `CreateInstanceAsync`, `instance.create`, `instance.join`, `instance.leave`, `instance.update`, `instance.delete` | インスタンスを作成、参加、退出、更新、削除する |
| `cloud:player_state:write` | `UpdatePlayerStateAsync`, `player.state.update` | プレイヤー状態を Cloud に送る |
| `cloud:rounds:write` | `ReportRoundAsync`, `round.report` | ラウンド結果を Cloud に報告する |
| `cloud:threats:write` | `threat.announce`, `threat.response` | 脅威・テラー関連の通知や応答を送る |
| `cloud:voting:write` | `coordinated.voting.start`, `coordinated.voting.vote` | 投票を開始する、投票する |
| `cloud:auto_suicide:write` | `coordinated.autoSuicide.update` | 連携 Auto Suicide 設定を更新する |
| `cloud:wished_terrors:write` | `wished.terrors.update` | Wished Terror の希望情報を更新する |
| `cloud:profiles:write` | `profile.update` | プレイヤープロフィールを更新する |
| `cloud:settings:write` | `settings.update`, `settings.sync` | Cloud 設定を更新、同期する |
| `cloud:monitoring:write` | `monitoring.report` | 監視情報やクライアント状態を報告する |
| `cloud:backups:write` | `backup.create`, `backup.restore`, `backup.delete` | バックアップを作成、復元、削除する |

SDKに専用メソッドがない RPC は、`SendRpcAsync<T>` または `SendRpcJsonAsync` で呼び出します。

```csharp
var result = await cloud.SendRpcJsonAsync("analytics.player");
```

## SDK メソッド早見表

SDK の専用メソッドは、内部で WebSocket RPC を送っています。引数名が C# 側と Cloud 側で違うものは、下の表に両方を書いています。

| SDK メソッド | 内部 RPC / 処理 | 必須引数 | 任意引数 | 戻り値 |
| --- | --- | --- | --- | --- |
| `CreateAppAuthorizationUri` | 認可 URL 作成 | `appId`, `redirectUri` | `cloudBaseUri`, `state`, `appName`, `scopes` | `Uri` |
| `RequestAppAuthorizationAsync` | 認可ページを開き、ローカル callback で APPToken を受け取る | `appId` | `cloudBaseUri`, `appName`, `timeout`, `openAuthorizationUriAsync`, `scopes`, `cancellationToken` | `AppAuthorizationResult` |
| `ConnectAsync` | WebSocket 接続 | なし | `cancellationToken` | なし |
| `LoginWithApiKeyAsync` | `auth.loginWithApiKey` | `playerId`, `apiKey`, `appId`, `appToken` | `cancellationToken` | `LoginResult` |
| `RevokeAppTokenAsync` | `auth.revokeAppToken` | `playerId`, `apiKey`, `appId` | `cancellationToken` | なし |
| `GenerateOneTimeTokenAsync` | `auth.generateOneTimeToken` | `playerId`, `apiKey` | `cancellationToken` | `OneTimeTokenResult` |
| `CreateInstanceAsync` | `instance.create` | なし | `maxPlayers`, `settings`, `cancellationToken` | `InstanceCreateResult` |
| `ListInstancesAsync` | `instance.list` | なし | `filter`, `limit`, `offset`, `cancellationToken` | `InstanceListResult` |
| `GetInstanceAsync` | `instance.get` | `instanceId` | `cancellationToken` | `CloudInstance` |
| `UpdatePlayerStateAsync` | `player.state.update` | `instanceId`, `PlayerStateUpdate state` | `cancellationToken` | なし |
| `ReportRoundAsync` | `round.report` | `RoundReport report` | `cancellationToken` | `JsonElement` |
| `SubscribeAsync` | `SDK.app.{appId}.subscribe` | `channel` | `cancellationToken` | `subscriptionId` 文字列。現在の公開 Cloud では通常使用しません |
| `UnsubscribeAsync` | `SDK.app.{appId}.unsubscribe` | `subscriptionId` | `cancellationToken` | なし。現在の公開 Cloud では通常使用しません |
| `SendAppRpcAsync<T>` | `SDK.app.{appId}.{method}` | `method` | `parameters`, `cancellationToken` | `T` |
| `SendAppRpcJsonAsync` | `SDK.app.{appId}.{method}` | `method` | `parameters`, `cancellationToken` | `JsonElement` |
| `SendAppRpcNoResultAsync` | `SDK.app.{appId}.{method}` | `method` | `parameters`, `cancellationToken` | なし |
| `SendCustomRpcAsync` | `SDK.app.{appId}.custom.rpc.send` | `method` | `payload`, `targetUserIds`, `instanceId`, `includeSelf`, `cancellationToken` | `CustomRpcSendResult` |
| `On` / `on` | カスタム RPC / Stream イベント受信登録 | `method` または `stream`, `handler` | なし | `IDisposable` |
| `SendRpcAsync<T>` | 任意 RPC | `rpc` | `parameters`, `cancellationToken` | `T` |
| `SendRpcJsonAsync` | 任意 RPC | `rpc` | `parameters`, `cancellationToken` | `JsonElement` |
| `SendRpcNoResultAsync` | 任意 RPC | `rpc` | `parameters`, `cancellationToken` | なし |

`LoginWithApiKeyAsync` の `appId` / `appToken` は、メソッド引数で渡す代わりに `ToNRoundCounterCloudOptions` に設定しておけます。

`SDK.app.{appId}.*` 名前空間で現在公開されているのは `custom.rpc.send` だけです。アプリ同士の通信は `SendCustomRpcAsync` と `On` / `on` を使ってください。

```csharp
await using var cloud = new ToNRoundCounterCloudClient(new ToNRoundCounterCloudOptions
{
    AppId = appId,
    AppToken = appToken,
});

await cloud.ConnectAsync();
await cloud.LoginWithApiKeyAsync(playerId, apiKey);
```

## モデル別パラメーター

### `ToNRoundCounterCloudOptions`

| プロパティ | 必須 | 既定値 | 説明 |
| --- | --- | --- | --- |
| `WebSocketUri` | いいえ | `wss://toncloud.sprink.cloud/ws` | 接続先 WebSocket |
| `ClientVersion` | いいえ | `1.0.0` | Cloud に送るクライアントバージョン |
| `RequestTimeout` | いいえ | 30 秒 | 1 RPC の応答待ち時間 |
| `AppId` | ログイン時は実質必須 | なし | 外部アプリの APPID |
| `AppToken` | ログイン時は実質必須 | なし | 初回認可で受け取った APPToken |
| `AppScopes` | いいえ | なし | 現状は保持用。認可時の `scopes` が実際の権限になります |

### `AppAuthorizationResult`

| プロパティ | 説明 |
| --- | --- |
| `AppId` | 認可された APPID |
| `AppToken` | 次回以降の SDK ログインに使うトークン。アプリ側で安全に保存します |
| `State` | CSRF 防止用の state |
| `RedirectUri` | callback を受けた URI |
| `Scopes` | 実際に許可された scope 一覧 |

### `PlayerStateUpdate`

`UpdatePlayerStateAsync` で使います。Cloud 側では `player_id` は認証中ユーザーに上書きされるため、他人の状態として送ることはできません。

| C# プロパティ | Cloud JSON | 必須 | 既定値 | 説明 |
| --- | --- | --- | --- | --- |
| `PlayerId` | `player_id` | C# record 上は必須 | なし | SDK 側の値。Cloud 保存時は認証中ユーザー ID に正規化されます |
| `PlayerName` | `player_name` | いいえ | `player_id` | 表示名 |
| `Velocity` | `velocity` | いいえ | `0` | 移動速度などの任意数値 |
| `AfkDuration` | `afk_duration` | いいえ | `0` | AFK 秒数など |
| `Items` | `items` | いいえ | `[]` | 所持アイテム名の配列。順序は保持されます |
| `Damage` | `damage` | いいえ | `0` | ダメージ量 |
| `IsAlive` | `is_alive` | いいえ | `true` | 生存状態 |

### `RoundReport`

`ReportRoundAsync` で使います。Cloud 側は `instance_id` だけを必須チェックしますが、統計として意味を持たせるために SDK record の各値を埋めて送ってください。

| C# プロパティ | Cloud JSON | 必須 | 説明 |
| --- | --- | --- | --- |
| `InstanceId` | `instance_id` | はい | 報告先インスタンス。報告者がメンバーである必要があります |
| `RoundType` | `round_type` / `round_key` | はい | ラウンド種別。例: `Classic` |
| `TerrorName` | `terror_name` / `terror_key` | いいえ | 出現テラー名 |
| `StartTime` | `start_time` | はい | UTC に変換され ISO 8601 で送信されます |
| `EndTime` | `end_time` | はい | UTC に変換され ISO 8601 で送信されます |
| `InitialPlayerCount` | `initial_player_count` | はい | 開始時人数 |
| `SurvivorCount` | `survivor_count` | はい | 生存人数 |
| `Status` | `status` | はい | `COMPLETED`, `FAILED`, `CANCELLED` など |

### `CustomRpcEventArgs`

`On` / `on` の handler に渡されます。カスタム RPC では `Method` がアプリ内メソッド名、通常の Stream イベントでは `Method` と `Stream` がどちらも stream 名になります。

| プロパティ | 説明 |
| --- | --- |
| `Method` | カスタム RPC のアプリ内メソッド名、または stream 名 |
| `Stream` | Cloud から届いた stream 名 |
| `Payload` | カスタム RPC では `payload`。通常の Stream イベントでは `Data` と同じ内容 |
| `Data` | Cloud から届いた生のイベント JSON |
| `FromUserId` / `FromPlayerId` | 送信者 |
| `InstanceId` | `instanceId` を指定して送られた場合の対象インスタンス |
| `Timestamp` | 送信時刻 |

`Payload` は `GetPayload<T>()`、生の `Data` は `GetData<T>()` で型に変換できます。

```csharp
public sealed record FlashPayload(string color, int duration_ms);

using var subscription = cloud.On("overlay.flash", ev =>
{
    FlashPayload? payload = ev.GetPayload<FlashPayload>();
    Console.WriteLine(payload?.color);
});
```

## RPC パラメーター一覧

`SendRpcJsonAsync` / `SendRpcAsync<T>` で直接呼ぶ場合、`parameters` は Cloud 側の JSON 名で書きます。ほとんどは `snake_case` です。

```csharp
JsonElement result = await cloud.SendRpcJsonAsync(
    "player.states.get",
    new { instance_id = "inst_xxx" });
```

SDK ログイン後の通常 RPC では、SDK が `session_id`, `app_id`, `app_token` を自動で付けます。自分で `parameters` に入れる必要はありません。

### `params` の基本

`SendRpcJsonAsync` / `SendRpcAsync<T>` の第 2 引数 `parameters` が、そのまま WebSocket RPC の `params` に入ります。

```csharp
await cloud.SendRpcJsonAsync(
    "round.list",
    new
    {
        instance_id = "inst_xxx",
        limit = 50,
    });
```

Cloud 側へ送られるイメージ:

```json
{
  "rpc": "round.list",
  "params": {
    "instance_id": "inst_xxx",
    "limit": 50
  }
}
```

引数がない RPC は `parameters` を省略します。

```csharp
JsonElement profile = await cloud.SendRpcJsonAsync("profile.get");
```

`params` には Cloud 側の名前を使います。C# の `PlayerId` ではなく `player_id`、`InstanceId` ではなく `instance_id` のように、基本は `snake_case` です。例外として一部の古いRPCは `instanceId` も受け付けますが、新しく書くコードでは `instance_id` を使ってください。

`session_id`, `app_id`, `app_token` は SDK が自動付与します。通常の RPC 呼び出しで `params` に入れないでください。

### よく使う `params` フィールド

| params フィールド | 型 | 主な RPC | 説明 |
| --- | --- | --- | --- |
| `player_id` | `string` | 認証系 | ユーザー/プレイヤー識別子。SDKログイン後の多くのRPCでは認証中ユーザーが使われるため不要です |
| `api_key` | `string` | `auth.loginWithApiKey`, `auth.revokeAppToken`, `auth.generateOneTimeToken` | ToNRoundCounter 本体で発行された API キー |
| `app_id` | `string` | 認証系 | 外部アプリの APPID。通常は `ToNRoundCounterCloudOptions.AppId` に設定します |
| `app_token` | `string` | 認証系 | 初回認可で受け取った APPToken。通常は `ToNRoundCounterCloudOptions.AppToken` に設定します |
| `client_version` | `string` | `auth.loginWithApiKey` | クライアントバージョン。SDKメソッド使用時は `ClientVersion` から入ります |
| `client_type` | `string` | 認証系 | SDKでは `external-sdk` が使われます |
| `device_info` | `object` | `auth.loginWithApiKey`, `auth.validateSession` | 端末情報。任意です |
| `instance_id` | `string` | インスタンス、状態、ラウンド、投票、分析 | 対象インスタンスID |
| `player_name` | `string` | `instance.join`, `player.state.update` | 表示名。省略時は認証中ユーザーIDなどが使われます |
| `max_players` | `number` | `instance.create`, `instance.update` | 最大人数 |
| `settings` | `object` | `instance.create`, `instance.update`, `settings.update` | インスタンス設定またはユーザー設定。RPCごとに意味が違います |
| `filter` | `string` | `instance.list` | `available`, `active`, `all` |
| `limit` | `number` | 一覧/履歴系 | 取得件数 |
| `offset` | `number` | `instance.list` | ページング開始位置 |
| `player_state` | `object` | `player.state.update` | プレイヤー状態。詳細は下の表 |
| `round_type` / `round_key` | `string` | `round.report`, 投票/テラー系 | ラウンド種別。新規コードでは `round_key` を使うと Cloud 内部表現に近いです |
| `terror_name` / `terror_key` | `string` | `round.report`, `threat.announce`, 投票系 | テラー名 |
| `start_time`, `end_time`, `expires_at` | `string` | `round.report`, `coordinated.voting.start` | ISO 8601 時刻。`DateTimeOffset.UtcNow.ToString("O")` 推奨 |
| `campaign_id` | `string` | 投票系 | 投票キャンペーンID。開始時は省略可能です |
| `decision` | `string` | `coordinated.voting.vote`, `threat.response` | 投票/応答内容。RPCごとに使える値が違います |
| `wished_terrors` | `array` | `wished.terrors.update` | 希望テラー一覧。文字列またはオブジェクトを入れます |
| `state` | `object` | `coordinated.autoSuicide.update` | Coordinated Auto Suicide の共有状態 |
| `local_settings` | `object` | `settings.sync` | ローカル側設定 |
| `local_version` | `number` | `settings.sync` | ローカル側設定バージョン。省略時は `0` |
| `status_data` | `object` | `monitoring.report` | アプリ稼働状態。詳細は下の表 |
| `time_range` | `object` | 分析系 | `start`, `end` を持つ期間指定 |
| `format` | `string` | `analytics.export` | `json` または `csv` |
| `data_type` | `string` | `analytics.export` | `rounds`, `players`, `terrors` |
| `filters` | `object` | `analytics.export` | エクスポート対象を絞る条件 |
| `backup_id` | `string` | `backup.restore`, `backup.delete` | 対象バックアップID |
| `payload` | 任意 | `custom.rpc.send` | アプリ独自の送信データ |
| `target_user_ids` | `string[]` | `custom.rpc.send` | 送信先 userId / playerId。一致したSDKクライアントに送ります |
| `include_self` | `bool` | `custom.rpc.send` | 自分自身にも届けるか |

### ネストする `params` の形

#### `player_state`

`player.state.update` の `params.player_state` に入れます。`player_id` は Cloud 側で認証中ユーザーに正規化されるため、直接 RPC では省略して構いません。

| フィールド | 型 | 必須 | 既定値 | 説明 |
| --- | --- | --- | --- | --- |
| `player_id` | `string` | いいえ | 認証中ユーザー | SDKメソッドでは送りますが、Cloud保存時は認証中ユーザーになります |
| `player_name` | `string` | いいえ | `player_id` | 表示名 |
| `velocity` | `number` | いいえ | `0` | 移動速度など |
| `afk_duration` | `number` | いいえ | `0` | AFK 秒数など |
| `items` | `string[]` | いいえ | `[]` | 所持アイテム。順序は保持されます |
| `damage` | `number` | いいえ | `0` | ダメージ量 |
| `is_alive` | `bool` | いいえ | `true` | 生存状態 |
| `timestamp` | `string` | いいえ | サーバー時刻 | ISO 8601 時刻 |

```csharp
await cloud.SendRpcNoResultAsync(
    "player.state.update",
    new
    {
        instance_id = "inst_xxx",
        player_state = new
        {
            player_name = "Player",
            velocity = 2.5,
            afk_duration = 0,
            items = new[] { "Radar Coil" },
            damage = 10,
            is_alive = true,
        },
    });
```

#### インスタンス `settings`

`instance.create` / `instance.update` の `params.settings` に入れます。任意JSONですが、よく使うフィールドは次です。

| フィールド | 型 | 説明 |
| --- | --- | --- |
| `auto_suicide_mode` | `string` | `Disabled`, `Manual`, `Individual`, `Coordinated` |
| `voting_timeout` | `number` | 投票タイムアウト秒数 |
| その他 | 任意 | アプリ側で使う追加設定 |

#### `round.report` の params

| フィールド | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `instance_id` | `string` | はい | 報告先インスタンス。認証中ユーザーがメンバーである必要があります |
| `round_type` / `round_key` | `string` | いいえ | ラウンド種別。省略時は `UNKNOWN` |
| `terror_name` / `terror_key` | `string` | いいえ | 出現テラー名。指定すると terror appearance も記録されます |
| `start_time` | `string` | いいえ | 開始時刻。省略時はサーバー側の現在時刻 |
| `end_time` | `string` | いいえ | 終了時刻。省略時はサーバー側の現在時刻 |
| `initial_player_count` | `number` | いいえ | 開始時人数。省略時は `0` |
| `survivor_count` | `number` | いいえ | 生存人数。省略時は `0` |
| `status` | `string` | いいえ | `COMPLETED`, `FAILED`, `CANCELLED` など。省略時は `COMPLETED` |
| `events` | `array` | いいえ | ラウンド中イベント配列 |
| `metadata` | `object` | いいえ | 追加情報。Cloud側で `reporter_user_id` が追加されます |

#### 投票/テラー系 params

| フィールド | 型 | 使う RPC | 説明 |
| --- | --- | --- | --- |
| `terror_name` | `string` | `threat.announce`, `coordinated.voting.start`, `wished.terrors.findDesirePlayers` | テラー名。必須のRPCがあります |
| `round_key` | `string` | `threat.announce`, `coordinated.voting.start`, `wished.terrors.findDesirePlayers` | ラウンド種別。空なら全ラウンド扱いになる箇所があります |
| `desire_players` | `array` | `threat.announce` | `{ player_id, player_name }` の配列 |
| `expires_at` | `string` | `coordinated.voting.start` | 投票期限。過去/遠すぎる値は Cloud 側で 1秒後から最大10分後に丸められます |
| `decision` | `string` | `coordinated.voting.vote` | `Continue` または `Skip`。`Proceed` / `Cancel` も互換扱い |
| `decision` | `string` | `threat.response` | `survive`, `cancel`, `skip`, `execute`, `timeout` |

#### `wished_terrors`

`wished.terrors.update` の `params.wished_terrors` に入れます。文字列ならテラー名だけ、オブジェクトならラウンド指定もできます。

| 形 | 説明 |
| --- | --- |
| `"Ao Oni"` | 全ラウンドで `Ao Oni` を希望 |
| `{ terror_name = "Ao Oni", round_key = "Classic" }` | `Classic` の `Ao Oni` を希望 |
| `{ id = "...", terror_name = "...", round_key = "..." }` | `id` を明示。省略時は Cloud 側で生成されます |

#### Coordinated Auto Suicide `state`

`coordinated.autoSuicide.update` の `params.state` に入れます。

| フィールド | 型 | 説明 |
| --- | --- | --- |
| `entries` | `array` | スキップ対象。各要素は `{ id?, terror_name, round_key, created_at?, created_by?, source? }` |
| `presets` | `array` | プリセット。各要素は `{ id?, name, entries, created_at?, created_by? }` |
| `skip_all_without_survival_wish` | `bool` | 生存希望がないテラーをまとめてスキップ扱いにするか |

`terror_name` または `round_key` が空、`*`, `all`, `any`, `全部`, `すべて` などの場合はワイルドカード扱いになります。

#### ユーザー `settings`

`settings.update` の `params.settings`、`settings.sync` の `params.local_settings` に入れます。

| カテゴリ | 例 |
| --- | --- |
| `general` | `{ language = "ja", theme = "dark", notifications = true }` |
| `autoSuicide` | `{ enabled = true, rules = new object[] { ... } }` |
| `recording` | `{ autoRecord = true, format = "mp4", quality = 1080 }` |

カテゴリ名や中身は追加できます。`settings.update` は送ったカテゴリで Cloud 設定を更新し、`settings.sync` は `local_version` と Cloud 側の version を比較して同期します。

#### `status_data`

`monitoring.report` の `params.status_data` に入れます。

| フィールド | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `application_status` | `string` | はい | `RUNNING`, `STOPPED`, `ERROR` |
| `application_version` | `string` | いいえ | アプリバージョン |
| `uptime` | `number` | はい | 稼働秒数など |
| `memory_usage` | `number` | はい | メモリ使用量 |
| `cpu_usage` | `number` | はい | CPU使用率など |
| `osc_status` | `string` | いいえ | `CONNECTED`, `DISCONNECTED`, `ERROR` |
| `osc_latency` | `number` | いいえ | OSC レイテンシ |
| `vrchat_status` | `string` | いいえ | `CONNECTED`, `DISCONNECTED`, `ERROR` |
| `vrchat_world_id` | `string` | いいえ | VRChat world ID |
| `vrchat_instance_id` | `string` | いいえ | VRChat instance ID |

#### 分析系 `time_range` / `filters`

`time_range` は分析系 RPC で使う期間指定です。

```csharp
new
{
    time_range = new
    {
        start = DateTimeOffset.UtcNow.AddDays(-7).ToString("O"),
        end = DateTimeOffset.UtcNow.ToString("O"),
    },
}
```

`analytics.export` の `filters` は `data_type` ごとに使えるキーが違います。

| `data_type` | 使える `filters` |
| --- | --- |
| `rounds` | `instance_id`, `status`, `start_time`, `end_time`, `limit` |
| `players` | `player_id`, `player_name`, `limit` |
| `terrors` | `terror_name`, `round_id`, `limit` |

#### カスタム RPC `params`

`SendCustomRpcAsync` を使う場合は SDK メソッドの引数に渡せば十分です。直接呼ぶ場合は次の形です。

```csharp
await cloud.SendAppRpcJsonAsync(
    "custom.rpc.send",
    new
    {
        method = "overlay.flash",
        payload = new { color = "#ff3366", duration_ms = 800 },
        target_user_ids = new[] { "TargetPlayerId" },
        instance_id = "inst_xxx",
        include_self = true,
    });
```

| フィールド | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `method` | `string` | はい | アプリ内メソッド名。`SDK.app.` / `auth.` 始まりは不可、128文字以内 |
| `payload` | 任意 | いいえ | 受信側の `ev.Payload` / `ev.GetPayload<T>()` で読むデータ |
| `target_user_ids` | `string[]` | いいえ | 宛先 userId / playerId。最大100件 |
| `instance_id` | `string` | いいえ | そのインスタンスにいるSDKクライアントだけへ送る |
| `include_self` | `bool` | いいえ | 送信元自身にも届けるか。既定は `false` |

### 認証 RPC

| RPC | SDK メソッド | scope | 必須 params | 任意 params | 戻り値 |
| --- | --- | --- | --- | --- | --- |
| `auth.loginWithApiKey` | `LoginWithApiKeyAsync` | 不要 | `player_id`, `api_key`, `app_id`, `app_token`, `client_version` | `client_type`, `device_info` | `session_id`, `session_token`, `player_id`, `user_id`, `scopes`, `expires_at` |
| `auth.revokeAppToken` | `RevokeAppTokenAsync` | 不要 | `player_id`, `api_key`, `app_id` | なし | `success`, `app_id` |
| `auth.generateOneTimeToken` | `GenerateOneTimeTokenAsync` | 不要 | `player_id`, `api_key` | なし | `token`, `login_url`, `expires_in` |
| `auth.refresh` | なし | 不要 | なし | なし | 新しい `session_id`, `session_token`, `expires_at` |
| `auth.logout` | なし | 不要 | なし | なし | `success` |
| `auth.validateSession` | なし | 不要 | `session_token`, `player_id` | `client_type`, `app_id`, `app_token` | `session_id`, `session_token`, `player_id`, `user_id`, `scopes`, `expires_at` |

`auth.register`, `auth.registerAppToken`, `auth.loginWithOneTimeToken` は SDK からの通常利用向けではありません。外部アプリは `RequestAppAuthorizationAsync` と `LoginWithApiKeyAsync` を使ってください。

### インスタンス RPC

| RPC | scope | 必須 params | 任意 params | 戻り値 / メモ |
| --- | --- | --- | --- | --- |
| `instance.create` | `cloud:instances:write` | なし | `max_players` 既定 `6`, `settings` | `instance_id`, `created_at`。作成者として自動参加します |
| `instance.join` | `cloud:instances:write` | `instance_id` | `player_name` | `instance_id`, `members`。`player_id` は認証中ユーザーになります |
| `instance.leave` | `cloud:instances:write` | `instance_id` | なし | `success`, `instance_id`, `player_id` |
| `instance.list` | `read:instances` | なし | `filter` (`available`/`active`/`all`), `limit`, `offset` | `instances`, `total`。自分が作成または参加しているインスタンスだけ返ります |
| `instance.get` | `read:instances` | `instance_id` | なし | インスタンス詳細。作成者またはメンバーのみ閲覧できます |
| `instance.update` | `cloud:instances:write` | `instance_id` | `max_players`, `settings` | `instance_id`, `updated_at`。ホストのみ |
| `instance.delete` | `cloud:instances:write` | `instance_id` | なし | `success`。ホストのみ |

`settings` は任意 JSON ですが、現在よく使う形は次です。

```csharp
new
{
    auto_suicide_mode = "Individual", // Disabled / Manual / Individual / Coordinated
    voting_timeout = 30,
}
```

### プレイヤー状態 RPC

| RPC | scope | 必須 params | 任意 params | 戻り値 / メモ |
| --- | --- | --- | --- | --- |
| `player.state.update` | `cloud:player_state:write` | `instance_id`, `player_state` | `player_state.player_name`, `velocity`, `afk_duration`, `items`, `damage`, `is_alive` | `success`, `timestamp`。インスタンスがなければ自動作成し、未参加なら自動参加します |
| `player.states.get` | `read:player_state` | `instance_id` または `instanceId` | なし | `player_states`, `instance_id`, `count`, `timestamp` |
| `player.state.get` | `read:player_state` | `instance_id` または `instanceId` | なし | 認証中ユーザー自身の状態、なければ `null` |
| `player.instance.get` | `read:instances` | なし | なし | 現在参加中のインスタンス、なければ `null` |

直接 RPC で状態を送る例:

```csharp
await cloud.SendRpcNoResultAsync(
    "player.state.update",
    new
    {
        instance_id = "inst_xxx",
        player_state = new
        {
            player_name = "Player",
            velocity = 2.5,
            afk_duration = 0,
            items = new[] { "Radar Coil" },
            damage = 10,
            is_alive = true,
        },
    });
```

### ラウンド / テラー / 投票 RPC

| RPC | scope | 必須 params | 任意 params | 戻り値 / メモ |
| --- | --- | --- | --- | --- |
| `round.report` | `cloud:rounds:write` | `instance_id` | `round_type`/`round_key`, `terror_name`/`terror_key`, `start_time`, `end_time`, `initial_player_count`, `survivor_count`, `status`, `events`, `metadata` | `success`。メンバーのみ |
| `round.list` | `read:rounds` | `instance_id` | `limit` 既定 `100` | ラウンド配列 |
| `threat.announce` | `cloud:threats:write` | `instance_id`, `terror_name` | `round_key`, `desire_players` | `success`。`threat.announced` を配信します |
| `threat.response` | `cloud:threats:write` | `threat_id`, `decision` | なし | `success`。`decision` は `survive`, `cancel`, `skip`, `execute`, `timeout` |
| `coordinated.voting.start` | `cloud:voting:write` | `instance_id`, `terror_name`, `expires_at` | `campaign_id`, `round_key` | `campaign_id`, `expires_at`。期限は 1 秒後から最大 10 分後に丸められます |
| `coordinated.voting.vote` | `cloud:voting:write` | `campaign_id`, `decision` | なし | `success`。`decision` は `Continue` / `Skip`。`Proceed` / `Cancel` も互換扱い |
| `coordinated.voting.getCampaign` | `read:voting` | `campaign_id` | なし | 投票キャンペーン概要 |
| `coordinated.voting.getActive` | `read:voting` | `instance_id` | なし | `{ campaign: null }` またはキャンペーン概要 |
| `coordinated.voting.getVotes` | `read:voting` | `campaign_id` | なし | `votes` 配列 |

`time` 系の値は ISO 8601 文字列で送るのが安全です。

```csharp
await cloud.SendRpcJsonAsync(
    "coordinated.voting.start",
    new
    {
        instance_id = "inst_xxx",
        terror_name = "Ao Oni",
        round_key = "Classic",
        expires_at = DateTimeOffset.UtcNow.AddMinutes(2).ToString("O"),
    });
```

### Coordinated Auto Suicide / Wished Terror RPC

| RPC | scope | 必須 params | 任意 params | 戻り値 / メモ |
| --- | --- | --- | --- | --- |
| `coordinated.autoSuicide.get` | `read:auto_suicide` | `instance_id` | なし | `entries`, `presets`, `skip_all_without_survival_wish`, `updated_at`, `updated_by` |
| `coordinated.autoSuicide.update` | `cloud:auto_suicide:write` | `instance_id` | `state` | 正規化後の state。同じインスタンスへ `coordinated.autoSuicide.updated` を配信します |
| `wished.terrors.update` | `cloud:wished_terrors:write` | `wished_terrors` 配列 | なし | `success`, `updated_at` |
| `wished.terrors.get` | `read:wished_terrors` | なし | なし | `wished_terrors` 配列 |
| `wished.terrors.findDesirePlayers` | `read:wished_terrors` | `instance_id`, `terror_name` | `round_key` | `desire_players` 配列 |

`wished_terrors` は文字列配列でも、オブジェクト配列でも送れます。

```csharp
await cloud.SendRpcNoResultAsync(
    "wished.terrors.update",
    new
    {
        wished_terrors = new object[]
        {
            "Ao Oni",
            new { terror_name = "Miros Bird", round_key = "Classic" },
        },
    });
```

`coordinated.autoSuicide.update` の `state` は部分更新のように送れます。

```csharp
await cloud.SendRpcJsonAsync(
    "coordinated.autoSuicide.update",
    new
    {
        instance_id = "inst_xxx",
        state = new
        {
            entries = new[]
            {
                new { terror_name = "Ao Oni", round_key = "Classic" },
            },
            skip_all_without_survival_wish = false,
        },
    });
```

### プロフィール / 設定 RPC

| RPC | scope | 必須 params | 任意 params | 戻り値 / メモ |
| --- | --- | --- | --- | --- |
| `profile.get` | `read:profiles` | なし | なし | 認証中ユーザーのプロフィール。なければ自動作成されます |
| `profile.update` | `cloud:profiles:write` | なし | `player_name`, `skill_level` | 更新後プロフィール。統計値は直接更新できません |
| `settings.get` | `read:settings` | なし | なし | 設定。なければ既定値が自動作成されます |
| `settings.update` | `cloud:settings:write` | `settings` オブジェクト | なし | 更新後設定 |
| `settings.sync` | `cloud:settings:write` | `local_settings` オブジェクト | `local_version` 既定 `0` | `settings`, `action` (`updated`/`conflict_resolved`/`up_to_date`) |
| `settings.history` | `read:settings` | なし | `limit` 既定 `10` | `history` 配列 |

`settings.update` の `settings` はカテゴリー単位のオブジェクトです。

```csharp
await cloud.SendRpcJsonAsync(
    "settings.update",
    new
    {
        settings = new
        {
            general = new { language = "ja", theme = "dark" },
            recording = new { autoRecord = true, quality = 1080 },
        },
    });
```

### 監視 / 分析 / バックアップ RPC

| RPC | scope | 必須 params | 任意 params | 戻り値 / メモ |
| --- | --- | --- | --- | --- |
| `monitoring.report` | `cloud:monitoring:write` | `status_data` | `instance_id` | 作成された status。`application_status`, `uptime`, `memory_usage`, `cpu_usage` を含めるのが基本です |
| `monitoring.status` | `read:monitoring` | なし | `limit` 既定 `50` | status 配列 |
| `monitoring.errors` | `read:monitoring` | なし | `severity`, `limit` 既定 `100` | error 配列。`severity` は `INFO`, `WARNING`, `ERROR`, `CRITICAL` |
| `client.status.get` | `read:monitoring` | なし | なし | 認証中ユーザーの C# / Web 接続状況と最近のログイン端末 |
| `analytics.player` | `read:analytics` | なし | `time_range` | 認証中ユーザーの統計 |
| `analytics.terror` | `read:analytics` | なし | `terror_name`, `time_range` | テラー統計配列 |
| `analytics.trends` | `read:analytics` | なし | `group_by` (`day`/`week`/`month`), `limit` | トレンド配列 |
| `analytics.export` | `read:analytics` | `format`, `data_type` | `filters` | JSON 文字列または CSV 文字列。`format` は `json`/`csv`、`data_type` は `rounds`/`players`/`terrors` |
| `analytics.instance` | `read:analytics` | `instance_id` | なし | インスタンス統計。メンバーのみ |
| `analytics.voting` | `read:analytics` | なし | `instance_id` | 投票統計。`instance_id` 指定時はメンバーのみ |
| `analytics.roundTypes` | `read:analytics` | なし | `time_range` | ラウンド種別統計配列 |
| `backup.create` | `cloud:backups:write` | なし | `type`, `compress`, `encrypt`, `description` | バックアップメタデータ。`type` は `FULL`, `DIFFERENTIAL`, `INCREMENTAL` |
| `backup.restore` | `cloud:backups:write` | `backup_id` | `validate_before_restore`, `create_backup_before_restore` | `success` |
| `backup.list` | `read:backups` | なし | なし | `backups` 配列 |
| `backup.delete` | `cloud:backups:write` | `backup_id` | なし | `success` |

`time_range` は次の形です。

```csharp
new
{
    time_range = new
    {
        start = DateTimeOffset.UtcNow.AddDays(-7).ToString("O"),
        end = DateTimeOffset.UtcNow.ToString("O"),
    },
}
```

`monitoring.report` の例:

```csharp
await cloud.SendRpcJsonAsync(
    "monitoring.report",
    new
    {
        instance_id = "inst_xxx",
        status_data = new
        {
            application_status = "RUNNING",
            application_version = "1.2.3",
            uptime = 3600,
            memory_usage = 512,
            cpu_usage = 12.5,
            osc_status = "CONNECTED",
        },
    });
```

### カスタム RPC

| RPC / メソッド | scope | 必須 params | 任意 params | 戻り値 / メモ |
| --- | --- | --- | --- | --- |
| `SendCustomRpcAsync` | `app:custom_rpc` | `method` | `payload`, `targetUserIds`, `instanceId`, `includeSelf` | `CustomRpcSendResult` |
| `SDK.app.{appId}.custom.rpc.send` | `app:custom_rpc` | `method` | `payload`, `target_user_ids`, `instance_id`, `include_self` | `success`, `app_id`, `method`, `instance_id`, `delivered_count`, `target_user_count`, `timestamp` |

`method` はアプリ内だけの名前にしてください。`SDK.app.` と `auth.` で始まる名前は拒否されます。長さは 128 文字以内です。

`targetUserIds` / `target_user_ids` を指定すると、その userId または playerId に一致する SDK クライアントだけへ送ります。`instanceId` / `instance_id` を指定すると、そのインスタンスに参加または購読している SDK クライアントだけへ送ります。`includeSelf` / `include_self` が `false` の場合、自分自身には届きません。

## Stream イベント

RPC の応答とは別に、Cloud から stream イベントが届くことがあります。SDK では `StreamReceived` で全 stream を受け取れるほか、`On` / `on` に stream 名を渡して個別に受け取れます。

| stream | 主な data | 発生タイミング |
| --- | --- | --- |
| `instance.member.joined` | `instance_id`, `player_id`, `player_name` | メンバー参加 |
| `instance.member.left` | `instance_id`, `player_id` | メンバー退出 |
| `instance.updated` | `instance_id`, `updates` | インスタンス更新 |
| `instance.deleted` | `instance_id` | インスタンス削除 |
| `player.state.updated` | `instance_id`, `player_state` | プレイヤー状態変更 |
| `round.reported` | `instance_id`, `reporter_user_id`, `round_id`, `round_key`, `terror_name`, `survived`, `timestamp` | ラウンド報告 |
| `threat.announced` | `instance_id`, `terror_name`, `round_key`, `desire_players` | テラー通知 |
| `coordinated.voting.started` | 投票キャンペーン | 投票開始 |
| `coordinated.voting.resolved` | `campaign_id`, `final_decision`, `votes`, `vote_count` | 投票解決 |
| `coordinated.autoSuicide.updated` | `instance_id`, `state` | Coordinated Auto Suicide 更新 |
| `settings.updated` | `user_id`, `version`, `categories` | 設定更新 |
| `SDK.app.{appId}.custom.rpc` | `method`, `payload`, `from_user_id`, `from_player_id`, `instance_id`, `timestamp` | `SendCustomRpcAsync` の受信 |

```csharp
IDisposable subscription = cloud.on("player.state.updated", ev =>
{
    string? instanceId = ev.Data
        .GetProperty("instance_id")
        .GetString();

    Console.WriteLine($"State updated in {instanceId}");
});

// 解除したいとき
subscription.Dispose();
```

全 stream をまとめて見たい場合は `StreamReceived` も使えます。

```csharp
cloud.StreamReceived += (_, ev) =>
{
    Console.WriteLine($"{ev.Stream}: {ev.Data}");
};
```

## 接続先を変える

本番 Cloud へ接続する場合は `WebSocketUri` を指定しなくて構いません。既定では次へ接続します。

```text
wss://toncloud.sprink.cloud/ws
```

初回認可ページも、既定では次を使います。

```text
https://toncloud.sprink.cloud
```

ローカル開発サーバーへ接続する場合:

```csharp
await using var cloud = new ToNRoundCounterCloudClient(new ToNRoundCounterCloudOptions
{
    WebSocketUri = new Uri("ws://localhost:8080/ws"),
    AppId = appId,
    AppToken = appToken,
    RequestTimeout = TimeSpan.FromSeconds(15),
});
```

`RequestAppAuthorizationAsync` の `cloudBaseUri` も同じ環境に合わせてください。

```csharp
await ToNRoundCounterCloudClient.RequestAppAuthorizationAsync(
    cloudBaseUri: new Uri("http://localhost:5173"),
    appId: appId,
    appName: appName,
    scopes: requiredScopes);
```

## プレイヤーのいるインスタンス一覧や情報を読む

`read:instances` が必要です。

```csharp
InstanceListResult list = await cloud.ListInstancesAsync(
    filter: "available",
    limit: 20,
    offset: 0);

foreach (CloudInstanceSummary instance in list.Instances)
{
    Console.WriteLine($"{instance.InstanceId}: {instance.MemberCount}/{instance.MaxPlayers}");
}
```

詳細を読む:

```csharp
CloudInstance instance = await cloud.GetInstanceAsync("inst_xxx");

foreach (CloudInstanceMember member in instance.Members)
{
    Console.WriteLine($"{member.PlayerName} ({member.PlayerId})");
}
```

SDKに専用メソッドがまだ無い読み取りRPCも、`SendRpcAsync` や `SendRpcJsonAsync` で呼び出せます。要求する scope は呼び出すRPCに合わせてください。

```csharp
var json = await cloud.SendRpcJsonAsync(
    "profile.get");

Console.WriteLine(json);
```

## インスタンスを作る

`cloud:instances:write` が必要です。

```csharp
InstanceCreateResult created = await cloud.CreateInstanceAsync(
    maxPlayers: 6,
    settings: new
    {
        auto_suicide_mode = "Individual",
        voting_timeout = 30,
    });

Console.WriteLine(created.InstanceId);
```

## プレイヤー状態を送る

`cloud:player_state:write` が必要です。

```csharp
await cloud.UpdatePlayerStateAsync(
    instanceId: "inst_xxx",
    state: new PlayerStateUpdate(
        PlayerId: playerId,
        PlayerName: playerId,
        Velocity: 2.5,
        AfkDuration: 0,
        Items: new[] { "Radar Coil" },
        Damage: 10,
        IsAlive: true));
```

状態を高頻度で送る場合は、値が変わったときだけ送る、数秒ごとにまとめるなど、アプリ側で送信量を抑えてください。

## ラウンド結果を送る

`cloud:rounds:write` が必要です。

```csharp
await cloud.ReportRoundAsync(new RoundReport(
    InstanceId: "inst_xxx",
    RoundType: "Classic",
    TerrorName: "Unknown",
    StartTime: DateTimeOffset.UtcNow.AddMinutes(-4),
    EndTime: DateTimeOffset.UtcNow,
    InitialPlayerCount: 6,
    SurvivorCount: 4,
    Status: "COMPLETED"));
```

## カスタム RPC を送る

`app:custom_rpc` が必要です。

同じ APPID でログインしている SDK クライアントに、あなたのアプリ専用メッセージを送れます。

```csharp
CustomRpcSendResult result = await cloud.SendCustomRpcAsync(
    method: "overlay.flash",
    payload: new
    {
        color = "#ff3366",
        duration_ms = 800,
    },
    includeSelf: true);

Console.WriteLine($"Delivered: {result.DeliveredCount}");
```

特定ユーザーだけに送る:

```csharp
await cloud.SendCustomRpcAsync(
    method: "overlay.flash",
    payload: new { color = "#66ccff" },
    targetUserIds: new[] { "TargetPlayerId" });
```

受信側は `On` で、受け取りたいメソッド名ごとに関数を登録できます。

```csharp
IDisposable subscription = cloud.On("overlay.flash", ev =>
{
    string? color = ev.Payload
        .GetProperty("color")
        .GetString();

    Console.WriteLine($"Flash: {color}");
});
```

要望に合わせて、小文字の `on` も使えます。

```csharp
cloud.on("overlay.flash", () =>
{
    Console.WriteLine("Flash received");
});
```

登録を解除したい場合は、`On` の戻り値を破棄します。

```csharp
subscription.Dispose();
```

`On/on` はカスタム RPC と Stream イベントの受信用ヘルパーです。通常の読み取りRPCは、専用メソッドまたは `SendRpcAsync` / `SendRpcJsonAsync` で呼び出してください。

## APPToken を取り消す

ユーザーがアプリ連携を解除したい場合は、保存済み APPToken を消したうえで `RevokeAppTokenAsync` を呼びます。

```csharp
await cloud.RevokeAppTokenAsync(
    playerId: playerId,
    apiKey: apiKey,
    appId: appId);

DeleteSavedAppToken();
```

次に接続する時は、もう一度 `RequestAppAuthorizationAsync` でユーザーに許可してもらいます。

## エラー処理

Cloud 側で拒否された場合は `CloudApiException`、応答が返らない場合は `TimeoutException` が主に返ります。

```csharp
try
{
    await cloud.ConnectAsync();
    await cloud.LoginWithApiKeyAsync(playerId, apiKey);
    await cloud.UpdatePlayerStateAsync("inst_xxx", new PlayerStateUpdate(playerId));
}
catch (CloudApiException ex)
{
    Console.WriteLine($"Cloud error: {ex.Code}");
    Console.WriteLine(ex.Message);

    if (ex.Message.Contains("scope", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("必要な権限が足りない可能性があります。");
    }
}
catch (TimeoutException ex)
{
    Console.WriteLine($"Request timed out: {ex.Message}");
}
catch (OperationCanceledException)
{
    Console.WriteLine("Canceled.");
}
```

APPToken が無効になった、またはユーザーが連携を解除した可能性がある場合は、保存済み APPToken を削除して再認可へ戻してください。

## アプリに組み込む例

実アプリでは、SDK 呼び出しを小さなクラスにまとめると UI 側が楽になります。

```csharp
using ToNRoundCounterSDK;

public sealed class TonCloudIntegration : IAsyncDisposable
{
    private const string AppId = "com.example.mytool";
    private const string AppName = "My ToN Tool";

    private readonly string _playerId;
    private readonly string _apiKey;
    private ToNRoundCounterCloudClient? _cloud;

    public TonCloudIntegration(string playerId, string apiKey)
    {
        _playerId = playerId;
        _apiKey = apiKey;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        string? appToken = LoadAppToken();

        if (string.IsNullOrWhiteSpace(appToken))
        {
            AppAuthorizationResult authorization =
                await ToNRoundCounterCloudClient.RequestAppAuthorizationAsync(
                    appId: AppId,
                    appName: AppName,
                    scopes: new[] { "read:instances" },
                    cancellationToken: cancellationToken);

            appToken = authorization.AppToken;
            SaveAppToken(appToken);
        }

        _cloud = new ToNRoundCounterCloudClient(new ToNRoundCounterCloudOptions
        {
            AppId = AppId,
            AppToken = appToken,
            RequestTimeout = TimeSpan.FromSeconds(20),
        });

        await _cloud.ConnectAsync(cancellationToken);
        await _cloud.LoginWithApiKeyAsync(_playerId, _apiKey, cancellationToken);
    }

    public async Task PrintInstancesAsync(CancellationToken cancellationToken = default)
    {
        if (_cloud is null)
        {
            throw new InvalidOperationException("Cloud integration is not started.");
        }

        InstanceListResult list = await _cloud.ListInstancesAsync(
            filter: "available",
            limit: 20,
            offset: 0,
            cancellationToken: cancellationToken);

        foreach (CloudInstanceSummary instance in list.Instances)
        {
            Console.WriteLine(instance.InstanceId);
        }
    }

    private static string? LoadAppToken()
    {
        // あなたのアプリの安全な保存先から読む
        return null;
    }

    private static void SaveAppToken(string appToken)
    {
        // あなたのアプリの安全な保存先へ保存する
    }

    public async ValueTask DisposeAsync()
    {
        if (_cloud is not null)
        {
            await _cloud.DisposeAsync();
        }
    }
}
```

## よくある詰まりどころ

| 症状 | 見直すところ |
| --- | --- |
| `Client is not connected` | `ConnectAsync` の前に RPC を呼んでいないか |
| ログインで失敗する | `playerId`, `apiKey`, `appId`, `appToken` が保存値と合っているか |
| 書き込み系メソッドだけ失敗する | 要求した scope とアプリに許可された権限が足りているか |
| 毎回認可画面が出る | `AppAuthorizationResult.AppToken` を保存できているか |
| カスタム RPC が届かない | 送信側と受信側の APPID が同じか、受信側が接続済みか |
| タイムアウトする | 接続先 URI、ネットワーク、`RequestTimeout` を確認する |

## 実装時の注意

- `ToNRoundCounterCloudClient` は長時間使い回し、アプリ終了時に `DisposeAsync` してください。
- `Connected`, `Disconnected`, `ErrorOccurred`, `StreamReceived`, `On/on` の handler は UI スレッド以外から呼ばれる可能性があります。WinForms や WPF では UI 更新を dispatcher に戻してください。
- API キーと APPToken はユーザーごとに保存してください。別ユーザーへ使い回さないでください。
- APPID を変えると既存 APPToken は使えません。
- 使わない scope は要求しないでください。ユーザーにとっても、あとで問題を切り分ける開発者にとっても、そのほうが楽です。
