# モジュール開発ガイド

このドキュメントでは、ToNRoundCounter に独自モジュールを追加する手順とベストプラクティスを説明します。モジュールは `Modules` フォルダーに配置された .NET アセンブリとして読み込まれ、アプリケーションの DI コンテナに対してサービスを登録することで機能を拡張できます。

## 前提条件
- .NET Framework 4.8 に対応した開発環境 (Visual Studio 2022 など)
- ToNRoundCounter リポジトリのソースコード
- 基本的な C# と依存性注入 (Dependency Injection) の知識

## プロジェクトの作成
1. `Modules` フォルダー配下に新しいクラスライブラリ プロジェクトを作成します。ターゲット フレームワークは `net48` を指定してください。
2. プロジェクト ファイル (`*.csproj`) に ToNRoundCounter 本体への `ProjectReference` を追加し、アプリケーションが公開しているインターフェースを参照できるようにします。
3. 必要に応じて NuGet パッケージ参照や外部 DLL を追加します。既存の `AfkJumpModule` では `Serilog` や `Rug.Osc` を利用しています。
4. ビルド後に DLL を自動的に `Modules` フォルダーへコピーするには、以下の MSBuild ターゲットを `*.csproj` に追加します。
   ```xml
   <Target Name="CopyModuleToOutput" AfterTargets="Build">
     <MakeDir Directories="$(SolutionDir)Modules" />
     <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(SolutionDir)Modules" SkipUnchangedFiles="true" />
   </Target>
   ```

## IModule の実装
モジュールは `ToNRoundCounter.Application.IModule` を実装するクラスを少なくとも 1 つ公開する必要があります。`RegisterServices` メソッド内で必要なサービスを `IServiceCollection` に追加すると、アプリ本体の DI コンテナから解決できるようになります。

`IModule` にはアプリケーションのライフサイクルに応じた多数のフックメソッドが用意されており、必要に応じて任意のメソッドをオーバーライドできます。主なメソッドは以下の通りです。

- `OnModuleLoaded` / `OnBeforeServiceRegistration` / `OnAfterServiceRegistration` などの DI 登録フェーズ向けフック。
- `OnBeforeServiceProviderBuild` / `OnAfterServiceProviderBuild` によるサービスプロバイダー構築前後の処理。
- `OnBeforeMainWindowCreation` / `OnAfterMainWindowCreation` / `OnMainWindowShown` / `OnMainWindowClosing` によるメインウィンドウ操作の拡張。
- `OnSettingsLoading` / `OnSettingsLoaded` / `OnSettingsSaving` / `OnSettingsSaved` による設定ファイル読み書きの監視。
- `OnAppRunStarting` / `OnAppRunCompleted` / `OnBeforeAppShutdown` / `OnAfterAppShutdown` / `OnUnhandledException` といったアプリケーション全体のライフサイクルイベント。【F:Application/IModule.cs†L13-L126】
- WebSocket・OSC 通信のライフサイクルを監視する `OnWebSocket...` および `OnOsc...` 系のハンドラー。再接続通知や受信メッセージを横取りして独自ロジックを挿入できます。【F:Application/IModule.cs†L128-L177】
- 設定バリデーションを拡張する `OnBeforeSettingsValidation` / `OnSettingsValidated` / `OnSettingsValidationFailed`。エラーメッセージの追加や検証成功時の後処理を実装できます。【F:Application/IModule.cs†L179-L207】【F:Infrastructure/AppSettings.cs†L82-L134】
- オート自殺ロジックをカスタマイズする `OnAutoSuicideRulesPrepared` / `OnAutoSuicideDecisionEvaluated` / `OnAutoSuicideScheduled` などのハンドラー。ルール集合の編集や判定結果の上書き、キャンセル検知が可能です。【F:Application/IModule.cs†L209-L247】【F:UI/MainForm.cs†L1548-L1587】【F:Application/AutoSuicideService.cs†L10-L81】
- 自動起動機能を制御する `OnAutoLaunchEvaluating` / `OnAutoLaunchStarting` / `OnAutoLaunchFailed` / `OnAutoLaunchCompleted`。起動対象の追加・削除や失敗時のフォールバック処理を実装できます。【F:Application/IModule.cs†L249-L263】【F:Program.cs†L118-L189】
- `OnThemeCatalogBuilding` / `OnMainWindowMenuBuilding` / `OnMainWindowUiComposed` / `OnMainWindowThemeChanged` / `OnMainWindowLayoutUpdated` による UI カスタマイズ。テーマカタログの拡張、メニューの挿入、ウィンドウ再構成の通知を一括で扱えます。【F:Application/IModule.cs†L241-L330】【F:UI/MainForm.cs†L115-L219】
- モジュール間連携を可能にする `OnPeerModule...` ハンドラー群。`ModulePeerNotificationContext<T>` を受け取り、他モジュールが DI 登録・サービスプロバイダー構築・メインウィンドウ操作・設定読み書き・アプリ終了・例外通知など各フェーズに入ったタイミングを監視できます。診断ログの追加や順序制御など、複数モジュールを跨いだ拡張に活用できます。【F:Application/IModule.cs†L128-L240】【F:Application/IModule.cs†L434-L455】

これらのメソッドはすべて任意実装であり、特定のフェーズだけをターゲットにした拡張も可能です。既存の `AfkJumpModule` のように未使用のメソッドは空実装としておくこともできます。【F:Modules/AfkJumpModule/AfkJumpModule.cs†L12-L165】

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
    }
}
```

- 依存関係の解決やロギングのために、アプリ本体のサービス (`IEventLogger` や `IEventBus` など) をコンストラクターインジェクションで受け取ることができます。
- `IModule` 実装クラスはパラメーター無しのコンストラクターを持っている必要があります。`ModuleLoader` は `Activator.CreateInstance` を使用してモジュールを生成するためです。【F:Infrastructure/ModuleLoader.cs†L15-L40】

## モジュールの読み込み仕組み
`Program` 起動時に `ModuleLoader.LoadModules` が呼び出され、`Modules` フォルダー内の DLL を列挙して `IModule` 実装を探索します。【F:Program.cs†L43-L112】【F:Infrastructure/ModuleLoader.cs†L13-L47】

- DLL 名に制限はありませんが、`IModule` を実装した型を公開し、`public` である必要があります。
- 読み込みに失敗した場合は `ModuleLoadFailed` イベントが発行され、ログにもエラーが出力されます。【F:Infrastructure/ModuleLoader.cs†L35-L40】

読み込み・初期化に関する詳細なイベントは `ModuleHost` が管理します。`ModuleHost` は `IEventBus` と連携して各フェーズの開始・完了イベント (`ModuleDiscoveryStarted`, `ModuleServicesRegistering`, `ServiceProviderBuilt`, `MainWindowShown` など) を発行し、モジュールや他サービスが購読できるようになっています。WebSocket/OSC の接続状態や設定バリデーション、オート自殺・自動起動・メインウィンドウのテーマ更新といったコア機能についても同様に通知されるため、ほぼすべての重要なアプリ内イベントをモジュールから観測・拡張できます。【F:Infrastructure/ModuleHost.cs†L1-L625】【F:Application/Events.cs†L7-L39】

### コア機能のカスタマイズ例

- **オート自殺の柔軟化**: `ModuleAutoSuicideRuleContext.Rules` は `List<AutoSuicideRule>` として公開されており、ルールの追加・削除や並び替えが可能です。`ModuleAutoSuicideDecisionContext.OverrideDecision` を呼び出すことで判定結果や遅延フラグを上書きできます。【F:Application/IModule.cs†L209-L243】【F:UI/MainForm.cs†L1548-L1587】
- **自動起動の差し替え**: `ModuleAutoLaunchEvaluationContext.Plans` に対して `AutoLaunchPlan` を追加すれば、設定ファイルに書かれていない実行ファイルも起動対象に含められます。`OnAutoLaunchFailed` でエラーを検知し、リトライや代替起動を実装することもできます。【F:Application/IModule.cs†L249-L263】【F:Program.cs†L134-L186】【F:Infrastructure/ModuleHost.cs†L222-L332】
- **UI テーマ・レイアウト・メニューの調整**: `OnThemeCatalogBuilding` で `Theme.RegisterTheme` を呼び出すと、ライト／ダーク以外のテーマをカタログへ追加できます。`OnMainWindowMenuBuilding` と `OnMainWindowUiComposed` はメニューやメインフォーム上のコントロールを並べ替えたり追加するための拡張ポイントです。テーマ変更時には `OnMainWindowThemeChanged` がテーマキーと `ThemeDescriptor` を渡すため、カスタムテーマに合わせた描画を行えます。【F:Application/IModule.cs†L241-L330】【F:UI/MainForm.cs†L115-L219】
- **設定ビューの拡張**: `OnSettingsViewBuilding` で `ModuleSettingsViewBuildContext.AddSettingsGroup` や `AddExtensionControl` を利用すると、モジュール専用のグループボックスや任意のコントロールを `SettingsPanel` 上に動的追加できます。`OnSettingsViewOpened`/`Applying`/`Closing`/`Closed` ではダイアログのライフサイクルを追跡しながら設定値の読み書きを実装できます。【F:Application/IModule.cs†L988-L1004】【F:UI/MainForm.cs†L372-L520】【F:UI/SettingsPanel.cs†L1225-L1330】
- **補助ウィンドウの提供**: `OnAuxiliaryWindowCatalogBuilding` で `AuxiliaryWindowDescriptor` を登録すると、「ウィンドウ」メニューにモジュール独自のフォームを追加でき、`OnAuxiliaryWindowOpening`/`Opened`/`Closing`/`Closed` で表示状態を監視できます。【F:Application/IModule.cs†L282-L330】【F:Infrastructure/ModuleHost.cs†L200-L365】【F:UI/MainForm.cs†L115-L520】

## サービス登録のベストプラクティス
- ライフタイム: ステートレスなサービスには `AddSingleton` を使用し、状態を持つサービスには `AddTransient` または `AddScoped` を検討してください (WinForms アプリであるためスコープは限定的です)。
- 既存のサービスを拡張する場合、`IEventBus` を利用して独自イベントをパブリッシュ／サブスクライブできます。
- UI スレッドとの連携が必要な場合は `IUiDispatcher` を使用してメインスレッドに処理をディスパッチしてください。

## ビルドと配置
1. モジュール プロジェクトをビルドすると、`Modules` フォルダーに DLL が生成されます。
2. ToNRoundCounter 本体を起動すると、生成された DLL が自動的に検出されます。
3. モジュールを削除したい場合は、該当する DLL を `Modules` フォルダーから削除します。

## 例: AFK ジャンプ モジュール
`AfkJumpModule` は AFK 警告イベントをフックしてジャンプ入力を送信するサンプル モジュールです。以下のポイントが参考になります。

- `IAfkWarningHandler` を `AddSingleton` で登録し、警告発生時の挙動をオーバーライドしています。【F:Modules/AfkJumpModule/AfkJumpModule.cs†L23-L26】
- `Rug.Osc` を用いて `/input/Jump` への OSC メッセージを送信し、モジュール独自のアクションを実行しています。【F:Modules/AfkJumpModule/AfkJumpModule.cs†L189-L205】
- 設定ビュー拡張フックでは `AddSettingsGroup` を使って案内ラベルを追加する実装例を含み、モジュール固有の UI 要素を差し込む方法を示しています。【F:Modules/AfkJumpModule/AfkJumpModule.cs†L72-L86】
- 処理中の例外は `IEventLogger` を通じてログ出力し、ユーザーに診断しやすい情報を提供しています。【F:Modules/AfkJumpModule/AfkJumpModule.cs†L197-L205】

このモジュールをテンプレートとして、独自機能を実装したクラスと DI 登録を組み合わせることで簡単に拡張機能を追加できます。

