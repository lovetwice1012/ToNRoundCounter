# ToNRoundCounterSDK

外部 .NET アプリから ToNRoundCounter Cloud に接続するための C# SDK です。

この README は、SDK を自分のアプリに組み込むための実装ガイドです。

## できること

- ユーザーに外部アプリ連携を許可してもらう
- APPToken を受け取って次回以降のログインに使う
- Cloud に接続してインスタンス情報を読む
- 許可されたアプリだけがプレイヤー状態やラウンド結果を送る
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
| `app:custom_rpc` | `SendCustomRpcAsync`, `On`, `on`, `custom.rpc.send` | 同じ APPID でログインしている SDK クライアント同士で、アプリ独自のメッセージを送受信する |

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
var result = await cloud.SendRpcJsonAsync(
    "analytics.player",
    new { player_id = playerId });
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
    "profile.get",
    new { player_id = playerId });

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

`On/on` はカスタム RPC の受信用ヘルパーです。通常の読み取りRPCは、専用メソッドまたは `SendRpcAsync` / `SendRpcJsonAsync` で呼び出してください。

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
- `Connected`, `Disconnected`, `ErrorOccurred`, `StreamReceived` は UI スレッド以外から呼ばれる可能性があります。WinForms や WPF では UI 更新を dispatcher に戻してください。
- API キーと APPToken はユーザーごとに保存してください。別ユーザーへ使い回さないでください。
- APPID を変えると既存 APPToken は使えません。
- 使わない scope は要求しないでください。ユーザーにとっても、あとで問題を切り分ける開発者にとっても、そのほうが楽です。
