# モジュール開発ガイド

このドキュメントは、ToNRoundCounter のモジュール拡張ポイントを体系的に整理し、リポジトリのコードを直接参照しなくてもモジュールを構築できるようにすることを目的としています。モジュールの雛形作成からビルド・配布、各ライフサイクルイベントの詳細、UI 拡張や自動化機能との連携方法までを網羅的に説明します。

---

## 1. アーキテクチャの全体像

### 1.1 モジュール読み込みの流れ
1. アプリケーション起動時、`Program.Main` が DI コンテナ (`ServiceCollection`) を構成し、モジュールホスト (`ModuleHost`) を生成します。【F:Program.cs†L46-L85】
2. `ModuleLoader.LoadModules` が `Modules` フォルダー配下の DLL を列挙し、`IModule` を実装する公開型を探索します。【F:Infrastructure/ModuleLoader.cs†L15-L40】
3. 発見された各型は `Activator.CreateInstance` でインスタンス化され、`ModuleHost.RegisterModule` に登録されます。【F:Infrastructure/ModuleLoader.cs†L34-L39】【F:Infrastructure/ModuleHost.cs†L297-L342】
4. ホストはモジュールに対してライフサイクルコールバック (`OnModuleLoaded` など) を順次呼び出し、同時にイベントバス経由でも同様のイベントを公開します。【F:Infrastructure/ModuleHost.cs†L283-L399】
5. サービスプロバイダー構築後は `ModuleHost` が UI や通信、設定、オート起動／自殺などアプリのコアイベントを継続的に通知します。【F:Infrastructure/ModuleHost.cs†L344-L399】【F:Infrastructure/ModuleHost.cs†L400-L455】

### 1.2 提供されるコンテキスト オブジェクト
各ライフサイクルメソッドは、状態や依存関係へアクセスするためのコンテキスト型を受け取ります。主なプロパティは以下の通りです。

| コンテキスト | 主要プロパティ | 用途 |
| --- | --- | --- |
| `ModuleDiscoveryContext` | `ModuleName`, `Assembly`, `SourcePath` | モジュール自身のメタデータ確認やログ出力。【F:Application/IModule.cs†L466-L480】 |
| `ModuleServiceRegistrationContext` | `Services`, `Logger`, `Bus` | DI 登録時にホストサービスへアクセス。【F:Application/IModule.cs†L483-L501】 |
| `ModuleServiceProviderContext` | `ServiceProvider`, `Logger`, `Bus` | `IServiceProvider` から依存性を解決する際に利用。【F:Application/IModule.cs†L521-L541】 |
| `ModuleMainWindowContext` | `Form`, `ServiceProvider` | メインフォームインスタンスへのアクセス。【F:Application/IModule.cs†L573-L609】 |
| `ModuleSettingsViewBuildContext` | `AddSettingsGroup`, `AddExtensionControl` などの API | 設定ビューに WinForms コントロールを挿入。【F:Application/IModule.cs†L918-L1004】 |
| `ModuleWebSocketConnectionContext` | `Phase`, `Exception`, `ServiceProvider` | WebSocket 状態変化の把握と復旧処理。【F:Application/IModule.cs†L642-L685】 |
| `ModuleAutoLaunchEvaluationContext` | `Plans`, `Settings`, `ServiceProvider` | 自動起動対象の追加・削除。【F:Application/IModule.cs†L776-L800】 |
| `ModuleAuxiliaryWindowCatalogContext` | `RegisterWindow`, `ServiceProvider` | 補助ウィンドウの登録と起動制御。【F:Application/IModule.cs†L878-L902】 |

> **TIP**: コンテキストが `ServiceProvider` を公開している場合は、`GetRequiredService` を使ってモジュール専用サービスやホスト提供サービスを安全に取得できます。

### 1.3 アプリケーションが提供する既定サービス
`Program.Main` は以下のサービスを DI コンテナへ登録しています。モジュールは `RegisterServices` や後続イベントでこれらを解決して利用できます。【F:Program.cs†L46-L85】

- `IEventLogger` / `IEventBus` : ログ出力とアプリ内イベント配信
- `ICancellationProvider` : 長時間処理のキャンセル制御
- `IUiDispatcher` : UI スレッドでの実行保証
- `IWebSocketClient`, `IOSCListener` : 通信クライアント
- `AutoSuicideService`, `StateService`, `MainPresenter`, `IAppSettings` などコア機能

---

## 2. 開発準備とプロジェクト雛形

### 2.1 推奨開発環境
- .NET Framework 4.8 をターゲットにできる IDE (Visual Studio 2022 など)
- このリポジトリをクローンしたローカル環境
- C# / WinForms / DI コンテナの基礎知識

### 2.2 モジュール プロジェクトの作成手順
1. ソリューション内の `Modules` フォルダーに C# クラスライブラリ プロジェクトを追加し、ターゲットフレームワークを `net48` に設定します。
2. `ToNRoundCounter` 本体 (`ToNRoundCounter.csproj`) への `ProjectReference` を追加し、`Application.IModule` や UI ヘルパーを参照できるようにします。
3. ビルド成果物を自動的に `Modules` フォルダーへコピーするには、以下の MSBuild ターゲットを `*.csproj` に追加します。
   ```xml
   <Target Name="CopyModuleToOutput" AfterTargets="Build">
     <MakeDir Directories="$(SolutionDir)Modules" />
     <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(SolutionDir)Modules" SkipUnchangedFiles="true" />
   </Target>
   ```
4. 外部ライブラリを利用する場合は通常通り NuGet を追加し、ライセンス要件に注意してください。

### 2.3 推奨フォルダ構成
```
Modules/
  SampleModule/
    SampleModule.csproj
    Module.cs            // IModule 実装
    Services/
      SampleFeature.cs   // DI 登録するサービス
    Views/
      SampleDialog.cs    // 補助ウィンドウ / 設定 UI
```

---

## 3. モジュールの基本骨格

### 3.1 最小実装
モジュールは `IModule` を実装し、パラメーターなしの `public` コンストラクターを持つ必要があります。【F:Infrastructure/ModuleLoader.cs†L31-L40】

```csharp
using Microsoft.Extensions.DependencyInjection;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Modules.Sample
{
    public sealed class SampleModule : IModule
    {
        public void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton<ISampleFeature, SampleFeature>();
        }

        // 必要なイベントだけをオーバーライド (未使用メソッドは空で可)
        public void OnModuleLoaded(ModuleDiscoveryContext context) { }
        public void OnAfterServiceProviderBuild(ModuleServiceProviderContext context) { }
        public void OnSettingsViewBuilding(ModuleSettingsViewBuildContext context) { }
        public void OnWebSocketMessageReceived(ModuleWebSocketMessageContext context) { }
        // ... 省略 ...
    }
}
```

空実装のままでも問題ありませんが、後述のライフサイクルを理解し目的に応じたイベントを選択することで、アプリの任意ポイントをカスタマイズできます。既存の `AfkJumpModule` は未使用イベントを空実装にした最小例です。【F:Modules/AfkJumpModule/AfkJumpModule.cs†L13-L324】

### 3.2 DI サービスと依存関係の定義
`RegisterServices` ではホストが生成するサービスと同じようにライフタイムを指定し登録できます。`ModuleServiceRegistrationContext` から `Logger` や `Bus` を取得して初期化ログを出力することも可能です。【F:Application/IModule.cs†L483-L501】

```csharp
public void RegisterServices(IServiceCollection services)
{
    services.AddSingleton<ISampleSettings, SampleSettings>();
    services.AddTransient<ISampleCommand, SampleCommand>();
}

public void OnAfterServiceRegistration(ModuleServiceRegistrationContext context)
{
    context.Logger.LogEvent("SampleModule", "サービス登録が完了しました。");
}
```

---

## 4. ライフサイクル リファレンス

### 4.1 フェーズ一覧

| フェーズ | 主なメソッド | 主題 |
| --- | --- | --- |
| 発見・メタデータ | `OnModuleLoaded`, `OnPeerModuleLoaded` | モジュール情報の登録、相互依存性チェック。【F:Application/IModule.cs†L18-L82】【F:Infrastructure/ModuleHost.cs†L297-L320】 |
| DI 登録 | `OnBeforeServiceRegistration`, `RegisterServices`, `OnAfterServiceRegistration` | サービス追加、前提条件検証、イベント購読設定。【F:Application/IModule.cs†L24-L52】【F:Infrastructure/ModuleHost.cs†L313-L341】 |
| サービスプロバイダー構築 | `OnBeforeServiceProviderBuild`, `OnAfterServiceProviderBuild` | シングルトン初期化、外部接続のウォームアップ。【F:Application/IModule.cs†L42-L53】【F:Infrastructure/ModuleHost.cs†L344-L368】 |
| メインウィンドウ生成 | `OnBeforeMainWindowCreation` ～ `OnMainWindowClosing` | UI 差し込み、フォームイベントのフック。【F:Application/IModule.cs†L54-L125】【F:Infrastructure/ModuleHost.cs†L371-L399】 |
| 設定ロード/保存 | `OnSettingsLoading` ～ `OnSettingsSaved` | 設定スキーマ移行、外部設定同期。【F:Application/IModule.cs†L78-L101】 |
| 設定ビュー | `OnSettingsViewBuilding` ～ `OnSettingsViewClosed` | 設定画面 UI、検証とロールバック制御。【F:Application/IModule.cs†L102-L130】【F:UI/SettingsPanel.cs†L1225-L1330】 |
| アプリ実行/終了 | `OnAppRunStarting` ～ `OnAfterAppShutdown`, `OnUnhandledException` | 起動シーケンス調整、終了処理、例外通知。【F:Application/IModule.cs†L132-L160】【F:Infrastructure/ModuleHost.cs†L397-L455】 |
| 通信 | `OnWebSocketConnecting` ～ `OnOscMessageReceived` | 接続監視、受信メッセージ処理。【F:Application/IModule.cs†L162-L214】【F:Infrastructure/ModuleHost.cs†L29-L207】 |
| 設定検証 | `OnBeforeSettingsValidation` ～ `OnSettingsValidationFailed` | 追加バリデーション、エラー表示制御。【F:Application/IModule.cs†L216-L233】 |
| 自動処理 | `OnAutoSuicide*`, `OnAutoLaunch*` | 自殺ルール／自動起動計画の拡張。【F:Application/IModule.cs†L234-L287】【F:Program.cs†L129-L198】 |
| UI 拡張 | `OnThemeCatalogBuilding` ～ `OnMainWindowLayoutUpdated`, `OnAuxiliaryWindow*` | テーマ登録、メニュー・レイアウト操作、補助ウィンドウ。【F:Application/IModule.cs†L288-L346】【F:UI/MainForm.cs†L115-L520】 |
| モジュール連携 | `OnPeerModule*` シリーズ | 他モジュールの状態観測と協調制御。【F:Application/IModule.cs†L348-L460】【F:Infrastructure/ModuleHost.cs†L319-L393】 |

### 4.2 代表的な実装パターン

#### 4.2.1 ディスカバリと初期ログ
```csharp
public void OnModuleLoaded(ModuleDiscoveryContext context)
{
    context.Logger.Information(
        "{Module} v{Version} をロードしました", context.Module.ModuleName, context.Module.Assembly.GetName().Version);
}
```

#### 4.2.2 サービス登録の条件分岐
モジュール設定や環境変数によってサービス登録を切り替える場合、`OnBeforeServiceRegistration` で可否判定を行い、`ModuleServiceRegistrationContext.Services` を直接操作します。【F:Application/IModule.cs†L483-L501】

#### 4.2.3 サービスプロバイダー完成時の初期化
```csharp
public void OnAfterServiceProviderBuild(ModuleServiceProviderContext context)
{
    var feature = context.ServiceProvider?.GetService<ISampleFeature>();
    feature?.WarmUp();
}
```

#### 4.2.4 メインウィンドウ初期化
`ModuleMainWindowContext.Form` は `MainForm` インスタンスなので、フォームのイベントを追加で購読できます。【F:Application/IModule.cs†L562-L609】

```csharp
public void OnAfterMainWindowCreation(ModuleMainWindowContext context)
{
    if (context.Form is MainForm main)
    {
        main.StatusText = "SampleModule がロードされました";
    }
}
```

#### 4.2.5 設定ビューの構築
`ModuleSettingsViewBuildContext.AddSettingsGroup` でグループを生成し、WinForms コントロールを追加してバインドします。【F:Application/IModule.cs†L918-L1004】

```csharp
public void OnSettingsViewBuilding(ModuleSettingsViewBuildContext context)
{
    var group = context.AddSettingsGroup("Sample Module");
    var checkbox = new CheckBox { Text = "機能を有効化", AutoSize = true };
    checkbox.DataBindings.Add("Checked", context.Settings, nameof(ISampleSettings.Enabled));
    group.Controls.Add(checkbox);
}
```

#### 4.2.6 WebSocket/OSC メッセージ処理
```csharp
public void OnWebSocketMessageReceived(ModuleWebSocketMessageContext context)
{
    context.Logger.Debug("受信: {Message}", context.Message);
}

public void OnOscMessageReceived(ModuleOscMessageContext context)
{
    if (context.Message.Address == "/input/Jump")
    {
        // 受信フック
    }
}
```

---

## 5. 設定とバリデーション

### 5.1 設定ライフサイクル
- `OnSettingsLoading` / `OnSettingsLoaded`: 設定ファイルの読み込み前後でマイグレーションや外部ストレージ同期を実行します。【F:Application/IModule.cs†L78-L101】
- `OnSettingsSaving` / `OnSettingsSaved`: 永続化前後に検証済み値の確定やバックアップを作成します。
- `OnBeforeSettingsValidation` / `OnSettingsValidated` / `OnSettingsValidationFailed`: アプリの標準検証前後で独自ルールを追加し、エラーの修正ヒントを提示します。【F:Application/IModule.cs†L216-L233】

### 5.2 設定ビュー フック
`ModuleSettingsViewLifecycleContext` は `DialogResult` などを参照し、ユーザーが OK を押したかを確認できます。`OnSettingsViewApplying` で入力値の最終チェックを行い、失敗時は `context.Cancel()` を呼び出して適用を阻止します。【F:Application/IModule.cs†L102-L130】

### 5.3 サンプル: URL 設定の検証
```csharp
public void OnBeforeSettingsValidation(ModuleSettingsValidationContext context)
{
    if (context.Settings is ISampleSettings settings && !Uri.IsWellFormedUriString(settings.Endpoint, UriKind.Absolute))
    {
        context.AddError("Endpoint に有効な URL を入力してください。");
    }
}

public void OnSettingsValidationFailed(ModuleSettingsValidationContext context)
{
    context.Logger.LogEvent("SampleModule", string.Join("\n", context.Errors));
}
```

---

## 6. 通信イベントの活用

### 6.1 WebSocket
- 接続開始 (`OnWebSocketConnecting`) / 成功 (`OnWebSocketConnected`) / 切断 (`OnWebSocketDisconnected`) / 再接続 (`OnWebSocketReconnecting`) の各フェーズでリトライ戦略や通知を実装できます。【F:Application/IModule.cs†L162-L190】【F:Infrastructure/ModuleHost.cs†L29-L196】
- `ModuleWebSocketMessageContext.Message` に生の JSON/Payload が入るため、`System.Text.Json` などでデシリアライズしイベントドメインへ転送できます。【F:Application/IModule.cs†L687-L701】

### 6.2 OSC
- `OnOscMessageReceived` では `Rug.Osc` の `OscMessage` を直接取得できるので、アドレスでフィルタリングし数値や文字列を抽出します。【F:Application/IModule.cs†L738-L749】
- `AfkJumpModule` は `/input/Jump` へジャンプ信号を送信する実装例です。OSC で外部アプリ連携を行いたい場合に参考になります。【F:Modules/AfkJumpModule/AfkJumpModule.cs†L337-L371】

---

## 7. UI 拡張

### 7.1 メインウィンドウメニュー
`OnMainWindowMenuBuilding` は `ModuleMainWindowMenuContext.AddMenu` を提供し、標準メニューにモジュール項目を簡潔に追加できます。【F:Application/IModule.cs†L288-L304】

```csharp
public void OnMainWindowMenuBuilding(ModuleMainWindowMenuContext context)
{
    var toolsMenu = context.AddMenu("ツール(&T)");
    var item = new ToolStripMenuItem("Sample Module を開く");
    item.Click += (_, _) => ShowSampleDialog(context.Form);
    toolsMenu.DropDownItems.Add(item);
}
```

### 7.2 レイアウト・テーマ
- `OnMainWindowUiComposed` で `FlowLayoutPanel` や `UserControl` を追加し、`OnMainWindowLayoutUpdated` でサイズ調整します。【F:Application/IModule.cs†L300-L346】【F:UI/MainForm.cs†L115-L219】
- `OnMainWindowThemeChanged` はテーマキーと `ThemeDescriptor` を渡すため、独自テーマの適用や背景色変更を行えます。【F:Application/IModule.cs†L336-L346】

### 7.3 補助ウィンドウ
`OnAuxiliaryWindowCatalogBuilding` では `AuxiliaryWindowDescriptor` を登録すると「ウィンドウ」メニューから開けるサブフォームを提供できます。【F:Application/IModule.cs†L306-L334】

```csharp
public void OnAuxiliaryWindowCatalogBuilding(ModuleAuxiliaryWindowCatalogContext context)
{
    context.RegisterWindow(new AuxiliaryWindowDescriptor(
        "SampleConsole",
        () => new SampleConsoleForm(),
        isSingleton: true));
}
```

---

## 8. 自動処理 (Auto Suicide / Auto Launch)

### 8.1 Auto Suicide
- `ModuleAutoSuicideRuleContext.Rules` はリストとして公開されており、ルールの追加・削除・ソートが可能です。【F:Application/IModule.cs†L784-L800】
- `ModuleAutoSuicideDecisionContext.OverrideDecision` を呼び出すと、アプリ本体の判定結果を上書きできます。【F:Application/IModule.cs†L804-L820】
- 実行タイミングは `ModuleHost` が `AutoSuicideScheduled` などのイベントを発火して通知します。【F:Infrastructure/ModuleHost.cs†L221-L229】

### 8.2 Auto Launch
- `Program.Main` はコマンドライン引数や設定ファイルから起動対象を収集し、モジュールに `ModuleAutoLaunchEvaluationContext` を通じて渡します。【F:Program.cs†L129-L155】
- モジュールは `Plans` コレクションへ独自 `AutoLaunchPlan` を追加することで、新規アプリケーションを起動対象に含められます。【F:Application/IModule.cs†L766-L787】
- 起動処理前後、および失敗時には `OnAutoLaunchStarting` / `OnAutoLaunchCompleted` / `OnAutoLaunchFailed` が呼ばれます。【F:Application/IModule.cs†L270-L286】【F:Program.cs†L171-L195】

---

## 9. モジュール間連携

`ModuleHost` はモジュール固有イベントを受信した後、他モジュールへ `OnPeerModule...` 系のコールバックとして転送します。【F:Infrastructure/ModuleHost.cs†L317-L393】これを利用すると、ロード順序や設定適用タイミングを協調できます。

例: 設定検証結果を監視し、失敗したモジュールへリトライ要求を送る。
```csharp
public void OnPeerModuleSettingsValidationFailed(ModulePeerNotificationContext<ModuleSettingsValidationContext> context)
{
    if (context.Module.Module.ModuleName == "Sample.DependentModule")
    {
        // 依存モジュールの設定失敗を検知した際の処理
    }
}
```

---

## 10. ロギングと診断

### 10.1 ログの取得
- `IEventLogger` は `LogEvent(string source, string message, LogEventLevel level = LogEventLevel.Information)` を公開しています。`Program.Main` でシングルトンとして登録されているため、コンストラクターインジェクションで取得可能です。【F:Program.cs†L48-L63】
- `ModuleServiceRegistrationContext.Logger` からも同じインスタンスへアクセスできます。【F:Application/IModule.cs†L483-L501】

### 10.2 イベントバス
- `IEventBus` はモジュール間通信やアプリ内イベント購読に利用できます。`ModuleHost` も同じバスを利用して WebSocket/OSC/設定などのイベントをパブリッシュしています。【F:Infrastructure/ModuleHost.cs†L24-L47】
- モジュールは `RegisterServices` 内でリスナーを登録し、`Dispose` が必要な場合はシングルトンサービス内でクリーンアップ処理を実装してください。

### 10.3 例外処理
`OnUnhandledException` はアプリケーション全体で捕捉されなかった例外を受け取ります。【F:Application/IModule.cs†L150-L160】ここでクラッシュレポート送信やユーザー通知を実装できます。

---

## 11. テストとデバッグのワークフロー

1. モジュール DLL をビルドし `Modules` フォルダーへ配置 (MSBuild ターゲットがある場合は自動)。
2. ToNRoundCounter をデバッグ起動し、コンソールログおよび設定画面でモジュールが認識されているか確認します。`ModuleHost.Modules` で登録状況を確認するデバッグビューを作成することもできます。【F:Infrastructure/ModuleHost.cs†L269-L276】
3. `IEventLogger` の出力や `logs` ディレクトリをチェックし、例外が発生していないか確認します。
4. 必要に応じて単体テストプロジェクト (xUnit など) をモジュールソリューション内に追加し、モジュールのサービスクラスを個別にテストします。

---

## 12. 配布とバージョニング

- モジュール DLL と依存 DLL をまとめて配布し、ユーザーに `Modules` フォルダーへコピーしてもらいます。`ModuleLoader` は DLL 名に制限を課していません。【F:Infrastructure/ModuleLoader.cs†L25-L49】
- バージョン管理にはアセンブリ情報 (`AssemblyVersion` / `AssemblyFileVersion`) を活用し、`OnModuleLoaded` でバージョンをログに記録すると更新確認が容易になります。【F:Application/IModule.cs†L18-L40】
- 互換性のない更新を行う場合は、`OnBeforeServiceRegistration` でホストのバージョンや依存モジュールの存在を検査し、失敗時には例外を投げて読み込みを中断できます。

---

## 13. 開発チェックリスト

- [ ] `IModule` を実装し、空実装も含めて必要なライフサイクルメソッドを整理した
- [ ] `RegisterServices` で必要なサービスを登録し、スレッドセーフなライフタイムを選択した
- [ ] 設定値の読み書きと検証 (`OnSettings*`) を実装し、ユーザーへフィードバックできるようにした
- [ ] UI 拡張ポイント (メニュー、補助ウィンドウ、テーマ) の必要性を確認し実装した
- [ ] WebSocket / OSC / 自動処理イベントへの対応が必要か検討し、必要なハンドラーを実装した
- [ ] ログとイベントバスを活用し、トラブルシューティングに十分な情報を残すようにした
- [ ] モジュール DLL の配布・更新手順を文書化し、ユーザー向けリリースノートを準備した

---

## 14. 参考実装

`AfkJumpModule` は AFK 警告イベントをフックしてジャンプ入力を送信するモジュールで、以下のポイントが参考になります。

- `IAfkWarningHandler` を DI で登録し、AFK 警告を置き換えている。【F:Modules/AfkJumpModule/AfkJumpModule.cs†L23-L26】
- 設定ビューに説明ラベルを追加する `OnSettingsViewBuilding` の例。【F:Modules/AfkJumpModule/AfkJumpModule.cs†L72-L87】
- OSC 送信処理で `Rug.Osc` を活用し、エラー時に `IEventLogger` へ出力している。【F:Modules/AfkJumpModule/AfkJumpModule.cs†L337-L363】

これらをテンプレートとして、モジュール固有のサービス／UI／自動化ロジックを組み合わせることで、ToNRoundCounter を柔軟に拡張できます。
