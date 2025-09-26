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
- `IModule` 実装クラスはパラメーター無しのコンストラクターを持っている必要があります。`ModuleLoader` は `Activator.CreateInstance` を使用してモジュールを生成するためです。【F:Infrastructure/ModuleLoader.cs†L15-L36】

## モジュールの読み込み仕組み
`Program` 起動時に `ModuleLoader.LoadModules` が呼び出され、`Modules` フォルダー内の DLL を列挙して `IModule` 実装を探索します。【F:Program.cs†L61-L69】【F:Infrastructure/ModuleLoader.cs†L15-L36】

- DLL 名に制限はありませんが、`IModule` を実装した型を公開し、`public` である必要があります。
- 読み込みに失敗した場合は `ModuleLoadFailed` イベントが発行され、ログにもエラーが出力されます。【F:Infrastructure/ModuleLoader.cs†L32-L36】

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

- `IAfkWarningHandler` を `AddSingleton` で登録し、警告発生時の挙動をオーバーライドしています。【F:Modules/AfkJumpModule/AfkJumpModule.cs†L14-L51】
- `Rug.Osc` を用いて `/input/Jump` への OSC メッセージを送信し、モジュール独自のアクションを実行しています。【F:Modules/AfkJumpModule/AfkJumpModule.cs†L33-L47】
- 処理中の例外は `IEventLogger` を通じてログ出力し、ユーザーに診断しやすい情報を提供しています。【F:Modules/AfkJumpModule/AfkJumpModule.cs†L31-L50】

このモジュールをテンプレートとして、独自機能を実装したクラスと DI 登録を組み合わせることで簡単に拡張機能を追加できます。

