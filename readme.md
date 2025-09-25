# ToNRoundCounter

ToNRoundCounter は ToN プレイ時にラウンド数やテラー情報を表示するための Windows 向けツールです。
WebSocket や OSC による連携、オート自殺設定、クラウド同期などゲームの進行を支援する多くの機能を備えています。

## 必要環境
- .NET Framework 4.8
- Windows 10 以降

## ビルド
リポジトリをクローン後、Visual Studio などの .NET 4.8 対応 IDE で `ToNRoundCounter.sln` を開いてビルドします。

## テスト
`dotnet test` で単体テストを実行できます。
※ .NET Framework 4.8 の参照アセンブリが必要です。

## コマンドラインオプション
- `--launch <path>`: ToNRoundCounter 起動時に指定した実行ファイルを自動的に起動します。
- `--launch-args <arguments>`: `--launch` で起動する実行ファイルに渡すコマンドライン引数を指定します。

同じ内容はアプリ内の設定画面「自動起動設定」からも指定でき、次回以降の起動時に保存された実行ファイルと引数を利用して自動起動できます。

## アイテム音楽ギミック
- 設定画面の「アイテム音楽ギミック」から、対象となるアイテム名、再生したい音声ファイル、速度の下限・上限を指定できます。
- 指定したアイテムを保持した状態で設定した速度範囲を 0.5 秒間維持すると、音声がアイテム所持中にループ再生されます。

## ライセンス
本プロジェクトは [license.md](license.md) の条件の下で提供されます。


[![DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/lovetwice1012/ToNRoundCounter)
