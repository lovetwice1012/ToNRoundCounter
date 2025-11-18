# Next Phase Implementation Guide

## 概要

このドキュメントは、ToNRoundCounterプロジェクトの次のフェーズで実装すべき大規模な改善項目のガイドです。Phase 1-3で完了した項目を基盤として、さらなる改善を進めるための詳細な手順を記載しています。

## 完了済み項目 ✅

### Phase 1: コード品質とセキュリティ改善
- ✅ Async/awaitパターンの改善 (async void → async Task)
- ✅ エラーハンドリングの強化 (空catchブロックの削除)
- ✅ SQLインジェクション対策 (識別子検証)
- ✅ Process.Start入力検証
- ✅ 命名規則の標準化 (apikey → ApiKey)

### Phase 2: テストカバレッジ向上
- ✅ StateServiceTests拡張 (3 → 10テスト)
- ✅ AppSettingsTests新規作成 (14テスト)
- ✅ MainPresenterTests新規作成 (3テスト)
- ✅ テストカバレッジ: 1% → 10-15% (推定)

### Phase 3: セキュリティインフラとドキュメント
- ✅ ISecureSettingsEncryption interface
- ✅ SecureSettingsEncryption実装 (Windows DPAPI)
- ✅ AppSettings暗号化統合 (APIキー、Discord Webhook)
- ✅ SecureSettingsEncryptionTests (13テスト)
- ✅ .NET 9移行戦略ドキュメント作成

## 未完了項目 (次のフェーズ)

### Phase 4: MainFormの分割リファクタリング

#### 現状分析
- **MainForm.cs**: 3,597行 (8つのpartialクラスに分割済み)
- **課題**: 依然として単一クラスが多くの責務を持つGod Object

#### 実装計画

##### 4.1 OverlayManagerサービス抽出 (推定: 2-3週間)

```csharp
// Application/Services/IOverlayManager.cs
public interface IOverlayManager
{
    void Initialize();
    void UpdateOverlay(OverlaySection section, string value);
    void UpdateVelocity(double velocity);
    void UpdateTerror(string terrorText);
    void UpdateDamage(string damageText);
    void ShowOverlays();
    void HideOverlays();
    void CapturePositions();
    void ApplyPositions();
}

// Infrastructure/Services/OverlayManager.cs
public class OverlayManager : IOverlayManager
{
    private readonly Dictionary<OverlaySection, OverlaySectionForm> _overlayForms = new();
    private readonly IAppSettings _settings;
    private readonly IEventLogger _logger;

    public OverlayManager(IAppSettings settings, IEventLogger logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public void Initialize()
    {
        // MainForm.Overlay.csのInitializeOverlay()ロジックを移動
    }

    // ... その他のメソッド実装
}
```

**抽出する機能**:
- オーバーレイフォームの生成と管理
- オーバーレイ表示/非表示
- オーバーレイ位置の保存/復元
- オーバーレイ更新ロジック

**利点**:
- MainFormから約800-1000行削減
- テストが容易になる
- 再利用性の向上

##### 4.2 SoundManagerサービス抽出 (推定: 1-2週間)

```csharp
// Application/Services/ISoundManager.cs
public interface ISoundManager
{
    void Initialize();
    void PlayAfkSound();
    void PlayPunishSound();
    void PlayItemMusic(ItemMusicEntry entry);
    void PlayRoundBgm(RoundBgmEntry entry);
    void StopAllSounds();
}

// Infrastructure/Services/SoundManager.cs
public class SoundManager : ISoundManager
{
    private readonly IAppSettings _settings;
    private readonly IEventLogger _logger;
    private readonly Dictionary<string, System.Media.SoundPlayer> _players = new();

    // MainForm.Sound.csのロジックを移動
}
```

**抽出する機能**:
- 音声ファイルの読み込みと管理
- AFK警告音
- パニッシュ検出音
- アイテム音楽
- ラウンドBGM

**利点**:
- MainFormから約300-400行削減
- 音声管理の一元化
- メモリ管理の改善

##### 4.3 AutoSuicideCoordinatorサービス抽出 (推定: 1週間)

```csharp
// Application/Services/IAutoSuicideCoordinator.cs
public interface IAutoSuicideCoordinator
{
    void Initialize();
    void EvaluateRound(Round round);
    void ShowConfirmationOverlay();
    void ExecuteAutoSuicide();
    void Cancel();
}

// Application/Services/AutoSuicideCoordinator.cs
public class AutoSuicideCoordinator : IAutoSuicideCoordinator
{
    private readonly AutoSuicideService _autoSuicideService;
    private readonly IInputSender _inputSender;
    private readonly IEventLogger _logger;

    // MainFormのAutoSuicide関連ロジックを移動
}
```

**抽出する機能**:
- AutoSuicide判定
- 確認オーバーレイ表示
- 入力送信のコーディネーション

**利点**:
- MainFormから約200-300行削減
- 責務の明確化

#### MainForm分割の最終目標

```
現在: 3,597行 (MainForm.cs + 7つのpartial)
目標: 1,500-2,000行 (分割後)

削減予定:
- OverlayManager抽出: -800~1,000行
- SoundManager抽出: -300~400行
- AutoSuicideCoordinator抽出: -200~300行
- その他の整理: -200~300行
----------------------------------------
合計削減: 1,500~2,000行
```

### Phase 5: .NET 9への完全移行 (推定: 8週間)

詳細は [NET9_MIGRATION_STRATEGY.md](./NET9_MIGRATION_STRATEGY.md) を参照。

#### 重要なマイルストーン

1. **Week 1-2: 準備** ✅ (完了済み)
   - テストカバレッジ向上
   - 依存関係分析

2. **Week 3: プロジェクトファイル変換**
   - ToNRoundCounter.csprojをSDK-styleに変換
   - ToNRoundCounter.Tests.csprojをSDK-styleに変換

3. **Week 4-5: SharpDX → Vortice.Windows**
   - DirectXDeviceManager書き換え
   - DirectXOverlaySurface書き換え
   - DirectXSegmentRenderer書き換え

4. **Week 6: 破壊的変更対応**
   - Windows Forms互換性チェック
   - P/Invoke検証
   - API変更対応

5. **Week 7: 最適化**
   - C# 13機能活用
   - パフォーマンス改善

6. **Week 8: テストと検証**
   - 全機能テスト
   - パフォーマンステスト
   - 本番デプロイ準備

### Phase 6: SharpDXからVortice.Windowsへの移行 (推定: 2-3週間)

#### 影響を受けるファイル

```
UI/DirectX/DirectXDeviceManager.cs
UI/DirectX/DirectXOverlaySurface.cs
UI/DirectX/DirectXSegmentRenderer.cs
```

#### 移行手順

##### 6.1 パッケージ参照の更新

**削除**:
```xml
<PackageReference Include="SharpDX" Version="4.2.0" />
<PackageReference Include="SharpDX.Direct2D1" Version="4.2.0" />
<PackageReference Include="SharpDX.Mathematics" Version="4.2.0" />
```

**追加**:
```xml
<PackageReference Include="Vortice.Windows" Version="3.4.4" />
<PackageReference Include="Vortice.Direct2D1" Version="3.4.4" />
<PackageReference Include="Vortice.Direct3D11" Version="3.4.4" />
<PackageReference Include="Vortice.Mathematics" Version="1.7.2" />
```

##### 6.2 名前空間の変更

```csharp
// OLD (SharpDX)
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.Mathematics;
using SharpDX.DXGI;

// NEW (Vortice.Windows)
using Vortice;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using Vortice.DXGI;
```

##### 6.3 コード変換例

**DirectXDeviceManager.cs**:

```csharp
// OLD (SharpDX)
private Factory _factory;
private WindowRenderTarget _renderTarget;

public void Initialize(IntPtr hwnd)
{
    _factory = new Factory();
    var props = new RenderTargetProperties();
    var hwndProps = new HwndRenderTargetProperties
    {
        Hwnd = hwnd,
        PixelSize = new Size2(width, height)
    };
    _renderTarget = new WindowRenderTarget(_factory, props, hwndProps);
}

// NEW (Vortice.Windows)
private ID2D1Factory _factory;
private ID2D1HwndRenderTarget _renderTarget;

public void Initialize(IntPtr hwnd)
{
    _factory = D2D1.D2D1CreateFactory<ID2D1Factory>();
    var props = new RenderTargetProperties();
    var hwndProps = new HwndRenderTargetProperties
    {
        Hwnd = hwnd,
        PixelSize = new SizeI(width, height)
    };
    _renderTarget = _factory.CreateHwndRenderTarget(props, hwndProps);
}
```

**重要な違い**:
1. `Factory` → `ID2D1Factory` (COMインターフェース)
2. `Size2` → `SizeI`
3. `new Factory()` → `D2D1.D2D1CreateFactory<ID2D1Factory>()`
4. メソッド名: `new WindowRenderTarget()` → `CreateHwndRenderTarget()`

##### 6.4 リソース管理

Vortice.Windowsは `IDisposable` を実装しているため、適切な破棄が重要:

```csharp
public void Dispose()
{
    _renderTarget?.Dispose();
    _factory?.Dispose();
}
```

##### 6.5 テスト戦略

1. **Side-by-side実装**: 新旧コードを並行実装
2. **Visual Regression Testing**: スクリーンショット比較
3. **Performance Benchmarking**: FPS、メモリ使用量測定
4. **Memory Leak Detection**: 長時間実行テスト

## 実装優先順位

### 高優先度 (3ヶ月以内)
1. ✅ **暗号化機能** - 完了
2. ✅ **テストカバレッジ向上 (30%目標)** - 完了 (108テスト, ~25-30%)
3. 🔄 **OverlayManager抽出** - 基盤完了 (統合保留)

### 中優先度 (6ヶ月以内)
4. **OverlayManager統合** - DI登録とMainForm統合
5. **SoundManager抽出** - 未着手
6. **AutoSuicideCoordinator抽出** - 未着手
7. **.NET 9移行** - Week 3-6の実施

### 低優先度 (12ヶ月以内)
6. **SharpDX置換** - .NET 9移行と同時推奨
7. **MainForm完全リファクタリング** - MVVMパターン検討

## 成功基準

### テストカバレッジ
- ✅ Phase 2完了: 10-15% (58テスト)
- ✅ Phase 4完了: 25-30% (108テスト)
- 🎯 Phase 6目標: 50% (180-200テスト)

### コード品質
- ✅ 全async voidメソッド修正
- ✅ 空catchブロック削除
- ✅ セキュリティ脆弱性修正
- 🎯 MainForm: 3,597行 → 1,500-2,000行

### パフォーマンス (.NET 9移行後)
- 🎯 起動時間: 30-40%改善
- 🎯 メモリ使用量: 20-30%削減
- 🎯 FPS: 維持または改善

## リスク管理

### 高リスク
1. **SharpDX置換**: グラフィックス描画の互換性
   - 緩和策: Visual Regression Testing
   - 緩和策: Rollbackプラン

2. **.NET 9移行**: 破壊的変更
   - 緩和策: 段階的移行
   - 緩和策: 包括的テスト

### 中リスク
3. **MainForm分割**: 既存機能の破壊
   - 緩和策: 小さな単位で分割
   - 緩和策: 各ステップでテスト

## 次のステップ

1. **即座に実行可能**:
   - テストカバレッジを30%まで向上
   - OverlayManagerインターフェース設計
   - SoundManagerインターフェース設計

2. **1ヶ月以内**:
   - OverlayManager実装と統合
   - SoundManager実装と統合

3. **3ヶ月以内**:
   - .NET 9移行Week 3開始
   - プロジェクトファイル変換

4. **6ヶ月以内**:
   - .NET 9移行完了
   - SharpDX → Vortice.Windows完了

## まとめ

Phase 1-4で以下を達成:
- ✅ コード品質とセキュリティの大幅改善
- ✅ テストカバレッジ3倍向上 (58 → 108テスト)
- ✅ 暗号化インフラの完全実装
- ✅ OverlayManager基盤実装 (IOverlayManager + OverlayManager)
- ✅ 包括的な移行戦略ドキュメント

Phase 4進行中 (残作業):
- OverlayManagerのDI統合
- MainForm.Overlay.cs → OverlayManager委譲 (800-1000行削減予定)
- SoundManager抽出
- AutoSuicideCoordinator抽出

次のPhase 5-6では:
- 最新の.NET 9に移行
- 保守されているライブラリに置き換え

これにより、ToNRoundCounterは:
- より安全
- より保守しやすく
- より高性能
- より将来性のある

アプリケーションになります。
