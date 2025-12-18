# 協力型生存システム 要件定義書・仕様設計書

**最終更新:** 2025年11月5日  
**バージョン:** 2.0  
**対象:** ToNRoundCounter マルチプレイ協力プレイ機能

> **⚠️ プライバシーに関する注意**  
> 本システムはプレイヤーのリアルタイム状態（HP、位置、アイテム、生存状態など）を  
> インスタンス内の他のToNRoundCounterユーザーと共有します。  
> 詳細は [プライバシーポリシー](../PRIVACY_POLICY.md) をご確認ください。

---

## 📋 目次

1. [基本コンセプト](#基本コンセプト)
2. [要件定義](#要件定義)
3. [ユースケース](#ユースケース)
4. [ほしいテラー設定仕様](#ほしいテラー設定仕様)
5. [プレイヤーリストオーバーレイ](#プレイヤーリストオーバーレイ)
6. [統率自動自殺アーキテクチャ](#統率自動自殺アーキテクチャ)
7. [システム処理フロー](#システム処理フロー)
8. [UIコンポーネント](#uiコンポーネント)
9. [実装仕様](#実装仕様)
10. [テスト仕様](#テスト仕様)

---

## 🎯 基本コンセプト

```
生存率（統計的リスク）を見せるのではなく、
「今、生存を望んでいるプレイヤー」の希望を見せて、
みんなで協力して生存を目指すシステム
```

### キー機能

| 機能 | 説明 |
|------|------|
| **ほしいテラー登録** | プレイヤー各自が「ラウンド × テラー名」で生存希望を事前登録 |
| **テラー出現時アナウンス** | 「XXさんが生存希望」と通知 + プレイヤーリストオーバーレイでハイライト |
| **協力判断** | 自動自殺設定（統率/通常/手動）に応じた協力フロー |
| **投票システム** | 統率される自殺モード時は投票で決定（10秒制限） |
| **リアルタイム表示** | プレイヤーの状態（アイテム・ダメージ・生死）をオーバーレイ表示 |

---

## 📖 要件定義

### 機能要件

#### FR-001: ほしいテラー登録機能

**要件:** プレイヤーは「ラウンド × テラー名」の組み合わせで生存希望を登録できる

| ID | 要件 | 優先度 |
|----|------|--------|
| FR-001-1 | ラウンド指定（特定ラウンド or すべてのラウンド） | 必須 |
| FR-001-2 | テラー名選択（15種類のテラー） | 必須 |
| FR-001-3 | 複数登録可能（無制限） | 必須 |
| FR-001-4 | 削除機能 | 必須 |
| FR-001-5 | LocalStorage永続化 | 必須 |
| FR-001-6 | Cloud Backend同期 | 必須 |

**受け入れ基準:**
- ✅ SettingsPanelから登録・削除が可能
- ✅ ラウンド空白時は全ラウンド対象
- ✅ 設定がアプリ再起動後も保持される

#### FR-002: テラー出現時通知機能

**要件:** 登録したテラーが出現時、生存希望プレイヤーを表示

| ID | 要件 | 優先度 |
|----|------|--------|
| FR-002-1 | マッチング判定（ラウンド × テラー名） | 必須 |
| FR-002-2 | 生存希望プレイヤーがいない場合は通知なし | 必須 |
| FR-002-3 | ThreatAlertOverlay表示 | 必須 |
| FR-002-4 | プレイヤーリストオーバーレイでハイライト | 必須 |

**受け入れ基準:**
- ✅ マッチしたプレイヤーのみ通知
- ✅ 誰も登録していない場合は何も表示しない
- ✅ 10秒以内に表示

#### FR-003: 自動自殺モード別フロー

**要件:** 自動自殺モードに応じた処理分岐

| モード | 生存希望者あり | 生存希望者なし |
|--------|---------------|---------------|
| **Disabled** | 何も表示しない | 何も表示しない |
| **Manual** | 通知のみ（3秒） | 何も表示しない |
| **Individual** | ボタン表示（10秒） | 通知のみ（3秒） |
| **Coordinated** | 投票ボタン（10秒） | 投票ボタン（10秒） |

**受け入れ基準:**
- ✅ 各モードで正しいUI表示
- ✅ タイムアウト時の正しい処理
- ✅ 設定変更が即時反映

#### FR-004: プレイヤーリストオーバーレイ

**要件:** リアルタイムでプレイヤーの状態を表示

| ID | 要件 | 優先度 |
|----|------|--------|
| FR-004-1 | 生存希望者セクション（別枠） | 必須 |
| FR-004-2 | 全メンバーリスト | 必須 |
| FR-004-3 | 表示: 名前:アイテム(ダメージ) | 必須 |
| FR-004-4 | 色分け: 生存希望者（緑/赤）、その他（白/灰色） | 必須 |
| FR-004-5 | 200ms更新 | 必須 |

**受け入れ基準:**
- ✅ 生存時と死亡後で色が変わる
- ✅ ダメージがリアルタイムで更新
- ✅ アイテム変更が即時反映

#### FR-005: 統率自動自殺投票システム

**要件:** チーム全体で投票して自動自殺を制御

| ID | 要件 | 優先度 |
|----|------|--------|
| FR-005-1 | 投票キャンペーン作成（10秒制限） | 必須 |
| FR-005-2 | プレイヤー投票受付 | 必須 |
| FR-005-3 | 過半数判定 | 必須 |
| FR-005-4 | タイムアウト処理（未投票=Cancel） | 必須 |
| FR-005-5 | 結果反映（実行 or スキップ） | 必須 |

**受け入れ基準:**
- ✅ 10秒以内に投票完了
- ✅ 過半数以上で実行
- ✅ 未投票は実行扱い

### 非機能要件

#### NFR-001: パフォーマンス

| 項目 | 要件 |
|------|------|
| PlayerState更新 | 200ms間隔 |
| UI更新頻度 | 60fps（16ms） |
| ネットワーク遅延 | < 100ms (p95) |
| メモリ使用量 | < 50MB増加 |

#### NFR-002: 可用性

| 項目 | 要件 |
|------|------|
| 接続断時の自動再接続 | 3回リトライ |
| フォールバック動作 | ローカルキャッシュ使用 |
| エラー通知 | UI上に表示 |

#### NFR-003: セキュリティ

| 項目 | 要件 |
|------|------|
| 設定の暗号化 | LocalStorage暗号化 |
| WebSocket認証 | Session Token |
| データ検証 | Backend側バリデーション |

---

## 📋 ユースケース

### UC-001: 希望テラーが出現 + 統率される自殺有効

```
Actor: プレイヤー（6人）
Preconditions: 
  - player-456, player-789 が「hallway × Silent Crush」を登録
  - 自動自殺モード: Coordinated
  - 現在のラウンド: hallway

Flow:
  1. Backend: Silent Crush 出現検知
  2. ToNRoundCounter: マッチング判定
     → player-456, player-789 が生存希望と判定
  3. ThreatAlertOverlay 表示
     ┌─────────────────────────────────┐
     │ 🌙 Silent Crush 出現!           │
     ├─────────────────────────────────┤
     │ 💚 player-456 生存希望          │
     │ 💚 player-789 生存希望          │
     │                                 │
     │ 🔴 統率される自殺が有効         │
     ├─────────────────────────────────┤
     │ [❌ キャンセル]                 │
     │ [✅ 生存を目指す]               │
     │ Time: 5s / 10s                  │
     └─────────────────────────────────┘
  4. プレイヤー投票
     - player-123: ✅ 生存を目指す
     - player-456: ✅ 生存を目指す
     - player-789: ✅ 生存を目指す
     - player-321: (未投票) → キャンセル扱い
  5. Backend: 投票集計（3/4 = 75% > 50%）
  6. 決定: 生存を目指す
  7. AutoSuicideService: 今周期の自殺をスキップ
  8. チーム全体で生存プレイ開始

Postconditions:
  - 自動自殺が実行されない
  - プレイヤーリストで生存希望者が緑色表示
```

### UC-002: 希望テラーが出現 + 通常の自動自殺有効

```
Actor: プレイヤー（個人）
Preconditions: 
  - player-456 が「office × Ducky」を登録
  - 自動自殺モード: Individual
  - 現在のラウンド: office

Flow:
  1. Backend: Ducky 出現検知
  2. ToNRoundCounter: マッチング判定
     → player-456 が生存希望と判定
  3. ThreatAlertOverlay 表示
     ┌─────────────────────────────────┐
     │ 🦆 Ducky 出現!                  │
     ├─────────────────────────────────┤
     │ 💚 player-456 生存希望          │
     │                                 │
     │ ℹ️ 通常の自動自殺モード         │
     ├─────────────────────────────────┤
     │ [❌ スキップ] [✅ 実行]        │
     │ Time: 5s / 10s                  │
     └─────────────────────────────────┘
  4. player-456: [❌ スキップ] 選択
  5. AutoSuicideService: 今周期の自殺をスキップ
  6. 個人で生存プレイ

Postconditions:
  - player-456 の自動自殺が実行されない
  - 他のプレイヤーは通常通り実行
```

### UC-003: 希望テラーが出現 + 手動のみ

```
Actor: プレイヤー
Preconditions: 
  - player-789 が「(All) × Piranhas」を登録
  - 自動自殺モード: Manual

Flow:
  1. Backend: Piranhas 出現検知（どのラウンドでも）
  2. ToNRoundCounter: マッチング判定
     → player-789 が生存希望と判定
  3. ThreatAlertOverlay 表示（3秒で自動閉じ）
     ┌─────────────────────────────────┐
     │ 🐟 Piranhas 出現!               │
     ├─────────────────────────────────┤
     │ 💚 player-789 生存希望          │
     │ ℹ️ 手動モード                   │
     └─────────────────────────────────┘
  4. 自動処理なし
  5. プレイヤーが手動で判断

Postconditions:
  - 通知のみ表示
  - 自動自殺は実行されない
```

### UC-004: 希望していないテラー出現

```
Actor: プレイヤー（6人）
Preconditions: 
  - 誰も「Ogre」を登録していない
  - 現在のラウンド: courtyard

Flow:
  1. Backend: Ogre 出現検知
  2. ToNRoundCounter: マッチング判定
     → 生存希望プレイヤー = 0人
  3. Backend: RPC送信なし
  4. ToNRoundCounter: 通知なし
  5. 通常の自動自殺ロジック実行

Postconditions:
  - 何も表示されない
  - 自動自殺設定通りに実行
```

---

## ⚙️ ほしいテラー設定仕様

### データ構造

```csharp
public class WishedTerror
{
    public string Id { get; set; }              // UUID
    public string TerrorName { get; set; }      // e.g. "Silent Crush"
    public string RoundKey { get; set; }        // e.g. "hallway" or ""
    public DateTime CreatedAt { get; set; }
}

public class WishedTerrorPreference
{
    public string PlayerId { get; set; }
    public List<WishedTerror> WishedTerrors { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### 設定例

| TerrorName | RoundKey | 意味 |
|-----------|----------|------|
| Silent Crush | "hallway" | ハロウェイラウンドのSilent Crushでのみ生存希望 |
| Silent Crush | "" | すべてのラウンドのSilent Crushで生存希望 |
| Mr. Hands | "hallway" | ハロウェイラウンドのMr.Handsでのみ生存希望 |
| Ogre | "" | すべてのラウンドのOgreで生存希望 |

### LocalStorage永続化

```json
{
  "wishedTerrors": [
    {
      "id": "uuid-001",
      "terrorName": "Silent Crush",
      "roundKey": "hallway",
      "createdAt": "2025-11-05T10:00:00Z"
    },
    {
      "id": "uuid-002",
      "terrorName": "Silent Crush",
      "roundKey": "",
      "createdAt": "2025-11-05T10:01:00Z"
    },
    {
      "id": "uuid-003",
      "terrorName": "Piranhas",
      "roundKey": "",
      "createdAt": "2025-11-05T10:02:00Z"
    }
  ]
}
```

### マッチング判定ロジック

```csharp
public bool IsTerrorMatched(
    WishedTerror wished,
    string currentTerrorName,
    string currentRoundKey)
{
    // テラー名は必ずマッチ必須
    if (wished.TerrorName != currentTerrorName)
        return false;
    
    // ラウンドキーが空白 = すべてのラウンドで有効
    if (string.IsNullOrEmpty(wished.RoundKey))
        return true;
    
    // ラウンドキーが指定 = 完全一致のみ
    return wished.RoundKey == currentRoundKey;
}
```

### プレイヤー検索

```csharp
private List<string> GetSurvivalWishersForTerror(
    string terrorName,
    string roundKey)
{
    var wishers = new List<string>();
    
    foreach (var player in _instance.Players)
    {
        var preferences = _repo.GetWishedTerrors(player.Id);
        
        foreach (var wished in preferences.WishedTerrors)
        {
            if (IsTerrorMatched(wished, terrorName, roundKey))
            {
                wishers.Add(player.Id);
                break;  // このプレイヤーは1回だけ追加
            }
        }
    }
    
    return wishers;
}
```

---

## 🎮 プレイヤーリストオーバーレイ

### UI デザイン

```
┌───────────────────────────────────────────┐
│                                           │
│ 📋 SURVIVAL WISHES                        │
│ ─────────────────────────────────────────│
│ 💚 player-456: Diamond + Shield (45dmg)  │
│ 💚 player-789: Hammer (12dmg)            │
│                                           │
│ 📋 OTHER MEMBERS                          │
│ ─────────────────────────────────────────│
│ ⚪ player-123: Sword (78dmg)              │
│ ⚪ player-321: Axe (32dmg)                │
│                                           │
└───────────────────────────────────────────┘

＝＝ プレイヤー死亡後 ＝＝

┌───────────────────────────────────────────┐
│                                           │
│ 📋 SURVIVAL WISHES                        │
│ ─────────────────────────────────────────│
│ 💚 player-456: Diamond + Shield (45dmg)  │
│ 🔴 player-789: Hammer (12dmg)            │ ← 赤色
│                                           │
│ 📋 OTHER MEMBERS                          │
│ ─────────────────────────────────────────│
│ ⚪ player-123: Sword (78dmg)              │
│ ⚫ player-321: Axe (32dmg)                │ ← 灰色
│                                           │
└───────────────────────────────────────────┘
```

### 色分けルール

| ステータス | 生存希望者 | その他 |
|-----------|---------|--------|
| **生存中** | 🟢 **緑** (#00FF00) | ⚪ **白** (#FFFFFF) |
| **死亡** | 🔴 **赤** (#FF0000) | ⚫ **灰色** (#808080) |

### 表示内容

```
形式: 名前 : アイテム (ダメージ)

例:
💚 player-456: Diamond + Shield (45dmg)
💚 player-789: Hammer (12dmg)
⚪ player-123: Sword (78dmg)
🔴 player-999: Iron Armor (120dmg) ← 死亡
⚫ player-888: Stone Sword (0dmg)   ← 死亡
```

### 動的更新タイミング

| イベント | 更新内容 |
|---------|---------|
| プレイヤー参加 | リスト追加 |
| プレイヤー離脱 | リスト削除 |
| ダメージ受敵 | ダメージ値更新 |
| プレイヤー死亡 | 色変更（緑→赤 or 白→灰） |
| アイテム変更 | アイテム表示更新 |
| 同期オン/オフ | リスト表示/非表示 |

**更新頻度:** 200ms ごと

### 実装コード

```csharp
public class PlayerListOverlay : Form
{
    private ListBox _survivalWishersListBox;
    private ListBox _otherMembersListBox;
    private readonly IStateService _state;
    private System.Threading.Timer _updateTimer;
    
    public PlayerListOverlay(IStateService state)
    {
        _state = state;
        InitializeUI();
        StartUpdateTimer();
    }
    
    private void InitializeUI()
    {
        // SURVIVAL WISHES セクション
        var wishersLabel = new Label
        {
            Text = "📋 SURVIVAL WISHES",
            Location = new Point(10, 10),
            Size = new Size(400, 30),
            ForeColor = Color.White
        };
        
        _survivalWishersListBox = new ListBox
        {
            Location = new Point(10, 50),
            Size = new Size(400, 150),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.Lime
        };
        
        // OTHER MEMBERS セクション
        var othersLabel = new Label
        {
            Text = "📋 OTHER MEMBERS",
            Location = new Point(10, 220),
            Size = new Size(400, 30),
            ForeColor = Color.White
        };
        
        _otherMembersListBox = new ListBox
        {
            Location = new Point(10, 260),
            Size = new Size(400, 150),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White
        };
        
        Controls.Add(wishersLabel);
        Controls.Add(_survivalWishersListBox);
        Controls.Add(othersLabel);
        Controls.Add(_otherMembersListBox);
    }
    
    private void StartUpdateTimer()
    {
        _updateTimer = new System.Threading.Timer(
            _ => UpdatePlayerList(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(500)
        );
    }
    
    private void UpdatePlayerList()
    {
        if (InvokeRequired)
        {
            Invoke(new Action(UpdatePlayerList));
            return;
        }
        
        var players = _state.GetInstancePlayers();
        var wishers = _state.GetSurvivalWishers();
        
        // SURVIVAL WISHES 更新
        _survivalWishersListBox.Items.Clear();
        foreach (var wisher in wishers)
        {
            var color = wisher.IsAlive ? Color.Lime : Color.Red;
            var item = $"{wisher.Name}: {wisher.Items} ({wisher.Damage}dmg)";
            _survivalWishersListBox.Items.Add(item);
            // 色変更は DrawMode.OwnerDrawFixed で実装
        }
        
        // OTHER MEMBERS 更新
        _otherMembersListBox.Items.Clear();
        var others = players.Except(wishers);
        foreach (var other in others)
        {
            var color = other.IsAlive ? Color.White : Color.Gray;
            var item = $"{other.Name}: {other.Items} ({other.Damage}dmg)";
            _otherMembersListBox.Items.Add(item);
        }
    }
}
```

---

## 🔌 統率自動自殺アーキテクチャ

### Domain Models

```csharp
// 投票キャンペーン
public class VotingCampaign
{
    public string CampaignId { get; set; }
    public string InstanceId { get; set; }
    public string TerrorName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }  // 10秒後
    
    // 投票結果
    public List<PlayerVote> Votes { get; set; }
    
    // 最終決定
    public CoordinatedDecision Decision { get; set; }
}

// プレイヤーの投票
public class PlayerVote
{
    public string PlayerId { get; set; }
    public CoordinatedDecision Vote { get; set; }
    public DateTime VotedAt { get; set; }
}

// 投票オプション
public enum CoordinatedDecision
{
    Cancel,        // 自殺をキャンセル
    Proceed,       // 自殺を実行
    Pending        // 未投票
}
```

### サービス層

```csharp
public class CoordinatedAutoSuicideService
{
    private readonly IWebSocketClient _ws;
    private readonly IEventBus _events;
    private readonly IStateService _state;
    private readonly IEventLogger _logger;
    
    // 統率投票開始
    public async Task InitiateVotingAsync(
        string instanceId, 
        string terrorName,
        List<string> survivalWisherIds)
    {
        var campaign = new VotingCampaign
        {
            CampaignId = Guid.NewGuid().ToString(),
            InstanceId = instanceId,
            TerrorName = terrorName,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddSeconds(10),
            Votes = new List<PlayerVote>(),
            Decision = CoordinatedDecision.Pending
        };
        
        // Backend に投票キャンペーン作成
        await _ws.SendAsync(new
        {
            rpc = "coordinated.voting.start",
            campaign = campaign
        });
        
        // UI に投票画面表示 (イベント発火)
        _events.Publish(new CoordinatedVotingStartedEvent
        {
            Campaign = campaign,
            SurvivalWisherIds = survivalWisherIds
        });
    }
    
    // プレイヤーが投票
    public async Task SubmitVoteAsync(
        string campaignId,
        string playerId,
        CoordinatedDecision vote)
    {
        await _ws.SendAsync(new
        {
            rpc = "coordinated.voting.vote",
            campaign_id = campaignId,
            player_id = playerId,
            decision = vote.ToString()
        });
    }
    
    // 投票結果を反映（Backend から通知）
    public async Task OnVotingResolvedAsync(
        VotingCampaign campaign)
    {
        var finalDecision = AggregateVotes(campaign.Votes);
        
        _events.Publish(new CoordinatedVotingResolvedEvent
        {
            Campaign = campaign,
            FinalDecision = finalDecision
        });
        
        if (finalDecision == CoordinatedDecision.Proceed)
        {
            await _autoSuicide.ExecuteAsync();
        }
        else
        {
            // Cancel → 今周期は自殺しない
            await _autoSuicide.SkipCurrentCycleAsync();
        }
    }
    
    // 投票集計ロジック
    private CoordinatedDecision AggregateVotes(
        List<PlayerVote> votes)
    {
        if (votes.Count == 0)
            return CoordinatedDecision.Cancel;
        
        var proceedCount = votes.Count(v => v.Vote == CoordinatedDecision.Proceed);
        var totalCount = votes.Count;
        
        // 過半数以上が Proceed なら実行
        return (proceedCount * 2 >= totalCount) 
            ? CoordinatedDecision.Proceed 
            : CoordinatedDecision.Cancel;
    }
    
    // 10秒でタイムアウト
    public async Task OnVotingTimeoutAsync(string campaignId)
    {
        var campaign = await _repository.GetCampaignAsync(campaignId);
        
        // 未投票プレイヤーを Cancel で集計
        foreach (var player in campaign.GetUnvotedPlayers())
        {
            campaign.Votes.Add(new PlayerVote
            {
                PlayerId = player.Id,
                Vote = CoordinatedDecision.Cancel,
                VotedAt = DateTime.UtcNow
            });
        }
        
        await OnVotingResolvedAsync(campaign);
    }
}
```

### イベント定義

```csharp
// 投票開始イベント
public class CoordinatedVotingStartedEvent
{
    public VotingCampaign Campaign { get; set; }
    public List<string> SurvivalWisherIds { get; set; }
    public DateTime Timestamp { get; set; }
}

// 投票完了イベント
public class CoordinatedVotingResolvedEvent
{
    public VotingCampaign Campaign { get; set; }
    public CoordinatedDecision FinalDecision { get; set; }
    public DateTime Timestamp { get; set; }
}

// 統率自殺実行イベント
public class CoordinatedAutoSuicideExecutedEvent
{
    public string TerrorName { get; set; }
    public CoordinatedDecision Decision { get; set; }
    public DateTime Timestamp { get; set; }
}
```

### AutoSuicideService統合

```csharp
public class AutoSuicideService
{
    public enum Mode
    {
        Disabled,           // 無効
        Manual,             // 手動のみ
        Individual,         // 個別自殺
        Coordinated         // 統率される自殺
    }
    
    public async Task ExecuteCycleAsync(
        AutoSuicideMode mode,
        ThreatInfo threat)
    {
        if (mode == Mode.Coordinated)
        {
            // 統率自殺ロジック
            await _coordinated.InitiateVotingAsync(
                _instance.Id,
                threat.Name,
                threat.DesiredPlayers
            );
            // → 投票待機
            // → 決定待機
        }
        else if (mode == Mode.Individual)
        {
            // 個別自殺ロジック（生存希望者通知付き）
            if (threat.DesiredPlayers.Any())
            {
                await ShowThreatAlertWithButtonsAsync(threat);
                // → ユーザー選択待機
            }
            else
            {
                // 自動実行
                await ExecuteAsync();
            }
        }
        else if (mode == Mode.Manual)
        {
            // 手動のみ
            if (threat.DesiredPlayers.Any())
            {
                await ShowThreatAlertNotificationOnlyAsync(threat);
            }
        }
    }
}
```

---

## 🔄 システム処理フロー

### テラー出現時の総合判定フロー

```
Backend: Threat検知 (terrorName, roundKey)
  ↓
ToNRoundCounter: RealTimeSyncService受信
  ↓
📊 チェック1: 生存希望プレイヤー（マッチング結果）存在？
  
  ❌ NO → Backend: RPC送信なし
         → ToNRoundCounter: 通知なし
         → 通常の自動自殺ロジック実行
  
  ✅ YES → 生存希望プレイヤーあり
         ↓
         📊 チェック2: 自動自殺モードは何？
         
         ├─ Disabled
         │  └─ 何も表示しない
         │     (自動自殺なし)
         │
         ├─ Manual
         │  └─ 📌 通知のみ表示 (3秒で自動閉じ)
         │     ┌──────────────────────────────┐
         │     │ 🌙 Silent Crush 出現!        │
         │     │ 💚 player-456 生存希望       │
         │     │ 💚 player-789 生存希望       │
         │     │ ℹ️ 手動モード                │
         │     └──────────────────────────────┘
         │
         ├─ Individual
         │  └─ ⚠️ ボタン + 通知表示 (10秒)
         │     ┌──────────────────────────────┐
         │     │ 🌙 Silent Crush 出現!        │
         │     │ 💚 player-456 生存希望       │
         │     │ 💚 player-789 生存希望       │
         │     │ ℹ️ 通常の自動自殺モード      │
         │     ├──────────────────────────────┤
         │     │ [❌ スキップ] [✅ 実行]    │
         │     │ Time: 5s / 10s               │
         │     └──────────────────────────────┘
         │     → User選択 or タイムアウト
         │
         └─ Coordinated
            └─ 🗳️ 投票 + 通知表示 (10秒)
               ┌──────────────────────────────┐
               │ 🌙 Silent Crush 出現!        │
               │ 💚 player-456 生存希望       │
               │ 💚 player-789 生存希望       │
               │ 🔴 統率される自殺が有効      │
               ├──────────────────────────────┤
               │ [❌ キャンセル]              │
               │ [✅ 生存を目指す]            │
               │ Time: 5s / 10s               │
               └──────────────────────────────┘
               → 投票集計 → 決定実行
```

### 投票フロー詳細

```
1. Backend: Threat検知 (生存希望者あり)
   ↓
2. CoordinatedAutoSuicideService.InitiateVotingAsync()
   → VotingCampaign 作成
   → EventBus: CoordinatedVotingStartedEvent 発火
   ↓
3. VotingOverlayPanel: イベント受け取り
   → 投票UI 表示
   ↓
4. User: [❌ キャンセル] or [✅ 実行] クリック
   ↓
5. SubmitVoteAsync()
   → RPC: coordinated.voting.vote
   → Backend: 投票集計
   ↓
6. Backend: 投票締切 (10秒) or 全員投票
   → RPC: coordinated.voting.resolved
   ↓
7. ToNRoundCounter: OnVotingResolvedAsync()
   → 投票結果に基づき実行/キャンセル
   → EventBus: CoordinatedVotingResolvedEvent 発火
   ↓
8. AutoSuicideService: 投票決定を反映
   → 実行 or スキップ
```

---

## 🎨 UIコンポーネント

### 1. ThreatAlertOverlay（協力判定版）

#### 統率される自殺モード

```
┌────────────────────────────────┐
│ 🌙 Silent Crush 出現!          │
├────────────────────────────────┤
│                                │
│ 💚 player-456 生存希望        │
│ 💚 player-789 生存希望        │
│                                │
│ 🔴 統率される自殺が有効       │
│                                │
├────────────────────────────────┤
│                                │
│   [❌ キャンセル]              │
│   [✅ 生存を目指す]            │
│                                │
│ Time: 5s / 10s                 │
└────────────────────────────────┘
```

#### 個別自殺モード

```
┌────────────────────────────────┐
│ 🌙 Silent Crush 出現!          │
├────────────────────────────────┤
│                                │
│ 💚 player-456 生存希望        │
│ 💚 player-789 生存希望        │
│                                │
│ ℹ️ 通常の自動自殺モード        │
│                                │
├────────────────────────────────┤
│                                │
│   [❌ スキップ] [✅ 実行]     │
│                                │
│ Time: 5s / 10s                 │
└────────────────────────────────┘
```

#### 手動モード（通知のみ）

```
┌────────────────────────────────┐
│ 🌙 Silent Crush 出現!          │
├────────────────────────────────┤
│                                │
│ 💚 player-456 生存希望        │
│ 💚 player-789 生存希望        │
│                                │
│ ℹ️ 手動モード                  │
│                                │
└────────────────────────────────┘
(3秒で自動閉じ)
```

### 2. SettingsPanel: 「ほしいテラー設定」タブ

```
┌───────────────────────────────────────────┐
│ Cloud - ほしいテラー設定                  │
├───────────────────────────────────────────┤
│                                           │
│ 💡 生存希望通知対象テラー                 │
│    (ラウンド × テラー名 の組み合わせ)     │
│                                           │
│ ┌─────────────────────────────────────┐  │
│ │ Round  │ Terror Name      │ [Delete]│  │
│ ├─────────────────────────────────────┤  │
│ │ hallway│ Silent Crush     │ [  🗑️ ]│  │
│ │ hallway│ Mr. Hands        │ [  🗑️ ]│  │
│ │  (All) │ Piranhas         │ [  🗑️ ]│  │
│ │ office │ Ducky            │ [  🗑️ ]│  │
│ │  (All) │ Ogre             │ [  🗑️ ]│  │
│ │        │                  │         │  │
│ ├─────────────────────────────────────┤  │
│ │ Round:   [hallway▼]                 │  │
│ │ Terror:  [Silent Crush     ▼]      │  │
│ │          [Add]                      │  │
│ └─────────────────────────────────────┘  │
│                                           │
│ 💾 [保存]  🔄 [リセット]                  │
└───────────────────────────────────────────┘
```

#### ドロップダウン選択肢

**Round ドロップダウン:**
```
- (All) ← ラウンド指定なし（すべてのラウンド）
- hallway
- office
- darkroom
- cafeteria
- library
- courtyard
```

**Terror ドロップダウン:**
```
- (Select)
- Silent Crush
- Mr. Hands
- Piranhas
- Ducky
- Ogre
- Leo
- Hanny & Lanny
- Rig
- Gummer
- Aloe Vera
- Yoo & Boyo
- Tiramisu
- Fart Filter
- Spider/Boy
- Sly Marbo
```

---

## 💻 実装仕様

### Phase 1: 基盤 (1週間)

#### タスク一覧

- [ ] WishedTerror Domain 定義
- [ ] IAppSettings に ほしいテラー 設定追加
- [ ] LocalStorage 永続化 (JSON)
- [ ] SettingsPanel UI 実装 (テーブル + Add/Delete)

#### 実装コード例

```csharp
// IAppSettings 拡張
public interface IAppSettings
{
    // 既存設定...
    
    // ほしいテラー設定
    List<WishedTerror> WishedTerrors { get; set; }
    void SaveWishedTerrors(List<WishedTerror> terrors);
    List<WishedTerror> LoadWishedTerrors();
}

// SettingsRepository 実装
public class SettingsRepository : ISettingsRepository
{
    private readonly string _settingsPath = "settings.json";
    
    public void SaveWishedTerrors(List<WishedTerror> terrors)
    {
        var json = JsonConvert.SerializeObject(new
        {
            wishedTerrors = terrors
        });
        
        File.WriteAllText(_settingsPath, json);
    }
    
    public List<WishedTerror> LoadWishedTerrors()
    {
        if (!File.Exists(_settingsPath))
            return new List<WishedTerror>();
        
        var json = File.ReadAllText(_settingsPath);
        var obj = JsonConvert.DeserializeObject<dynamic>(json);
        
        return obj.wishedTerrors.ToObject<List<WishedTerror>>();
    }
}
```

### Phase 2: マッチング & 検索 (1週間)

#### タスク一覧

- [ ] `GetSurvivalWishersForTerror()` 実装
- [ ] `IsTerrorMatched()` ロジック実装
- [ ] ユニットテスト (8パターン)

### Phase 3: 通知フロー (1週間)

#### タスク一覧

- [ ] RealTimeSyncService: threat.announced 受信
- [ ] 生存希望プレイヤー 0 人 → 通知なし
- [ ] 生存希望プレイヤー > 0 → ThreatAlertOverlay 表示

### Phase 4: UI & タイムアウト (1週間)

#### タスク一覧

- [ ] ThreatAlertOverlay ボタン表示/非表示
- [ ] 10秒タイムアウト実装
- [ ] 自動閉じ
- [ ] PlayerListOverlay 実装

### Phase 5: 投票連携 (1週間)

#### タスク一覧

- [ ] CoordinatedAutoSuicideService: タイムアウト時の Cancel 集計
- [ ] IndividualAutoSuicideService: タイムアウト時の Skip
- [ ] E2E テスト

---

## 🧪 テスト仕様

### ユニットテスト

#### TEST-001: マッチング判定

```csharp
[TestClass]
public class WishedTerrorMatchingTests
{
    [TestMethod]
    public void WishedTerror_RoundAndTerrorMatch()
    {
        // Arrange
        var wished = new WishedTerror
        {
            TerrorName = "Silent Crush",
            RoundKey = "hallway"
        };
        
        // Act
        bool result = IsTerrorMatched(wished, "Silent Crush", "hallway");
        
        // Assert
        Assert.IsTrue(result);
    }
    
    [TestMethod]
    public void WishedTerror_NoRound_MatchesAll()
    {
        // Arrange
        var wished = new WishedTerror
        {
            TerrorName = "Silent Crush",
            RoundKey = ""
        };
        
        // Act & Assert
        Assert.IsTrue(IsTerrorMatched(wished, "Silent Crush", "hallway"));
        Assert.IsTrue(IsTerrorMatched(wished, "Silent Crush", "office"));
        Assert.IsTrue(IsTerrorMatched(wished, "Silent Crush", ""));
    }
    
    [TestMethod]
    public void WishedTerror_DifferentTerror_NoMatch()
    {
        // Arrange
        var wished = new WishedTerror
        {
            TerrorName = "Silent Crush",
            RoundKey = "hallway"
        };
        
        // Act
        bool result = IsTerrorMatched(wished, "Ogre", "hallway");
        
        // Assert
        Assert.IsFalse(result);
    }
    
    [TestMethod]
    public void WishedTerror_DifferentRound_NoMatch()
    {
        // Arrange
        var wished = new WishedTerror
        {
            TerrorName = "Silent Crush",
            RoundKey = "hallway"
        };
        
        // Act
        bool result = IsTerrorMatched(wished, "Silent Crush", "office");
        
        // Assert
        Assert.IsFalse(result);
    }
}
```

#### TEST-002: 投票集計ロジック

```csharp
[TestClass]
public class VotingAggregationTests
{
    [TestMethod]
    public void Voting_Majority_Proceed()
    {
        // Arrange
        var votes = new List<PlayerVote>
        {
            new PlayerVote { Vote = CoordinatedDecision.Proceed },
            new PlayerVote { Vote = CoordinatedDecision.Proceed },
            new PlayerVote { Vote = CoordinatedDecision.Proceed },
            new PlayerVote { Vote = CoordinatedDecision.Cancel }
        };
        
        // Act
        var result = AggregateVotes(votes);
        
        // Assert
        Assert.AreEqual(CoordinatedDecision.Proceed, result);
    }
    
    [TestMethod]
    public void Voting_Minority_Cancel()
    {
        // Arrange
        var votes = new List<PlayerVote>
        {
            new PlayerVote { Vote = CoordinatedDecision.Proceed },
            new PlayerVote { Vote = CoordinatedDecision.Cancel },
            new PlayerVote { Vote = CoordinatedDecision.Cancel },
            new PlayerVote { Vote = CoordinatedDecision.Cancel }
        };
        
        // Act
        var result = AggregateVotes(votes);
        
        // Assert
        Assert.AreEqual(CoordinatedDecision.Cancel, result);
    }
    
    [TestMethod]
    public void Voting_NoVotes_Cancel()
    {
        // Arrange
        var votes = new List<PlayerVote>();
        
        // Act
        var result = AggregateVotes(votes);
        
        // Assert
        Assert.AreEqual(CoordinatedDecision.Cancel, result);
    }
}
```

### E2Eテスト

#### TEST-E2E-001: 統率自殺フロー

```
Scenario: 生存希望テラー出現 → 投票 → 実行
Given: player-456, player-789 が「hallway × Silent Crush」を登録
  And: 自動自殺モード = Coordinated
  And: 現在のラウンド = hallway
When: Silent Crush 出現
Then: ThreatAlertOverlay が表示される
  And: 「player-456, player-789 生存希望」と表示
  And: 投票ボタンが表示される
When: player-456 が [✅ 生存を目指す] を選択
  And: player-789 が [✅ 生存を目指す] を選択
  And: player-123 が [❌ キャンセル] を選択
  And: player-321 がタイムアウト (未投票)
Then: 投票集計 = Proceed (2/4 = 50%)
  And: AutoSuicideService がスキップされる
  And: チーム全体で生存プレイ開始
```

#### TEST-E2E-002: 個別自殺フロー

```
Scenario: 生存希望テラー出現 → 個人判断 → スキップ
Given: player-456 が「office × Ducky」を登録
  And: 自動自殺モード = Individual
  And: 現在のラウンド = office
When: Ducky 出現
Then: ThreatAlertOverlay が表示される
  And: 「player-456 生存希望」と表示
  And: [スキップ] [実行] ボタンが表示される
When: player-456 が [❌ スキップ] を選択
Then: player-456 の自動自殺がスキップされる
  And: 他のプレイヤーは通常通り実行
```

#### TEST-E2E-003: 生存希望者なし

```
Scenario: 登録していないテラー出現 → 通知なし → 通常フロー
Given: 誰も「Ogre」を登録していない
  And: 自動自殺モード = Coordinated
When: Ogre 出現
Then: Backend は RPC を送信しない
  And: ToNRoundCounter は通知を受け取らない
  And: ThreatAlertOverlay は表示されない
  And: 通常の自動自殺ロジックが実行される
```

---

## 📊 Backend通信仕様

### RPC: `threat.announced`

生存希望プレイヤー**がいる場合のみ** 送信

```json
{
  "id": "threat_announced_hallway_sc_001",
  "rpc": "threat.announced",
  "params": {
    "terror_name": "Silent Crush",
    "round_key": "hallway",
    "instance_id": "inst_456",
    "desire_players": [
      {
        "player_id": "player_456",
        "player_name": "Alice"
      },
      {
        "player_id": "player_789",
        "player_name": "Bob"
      }
    ],
    "timestamp": "2025-11-05T10:30:45Z"
  }
}
```

### RPC: `threat.response`

```json
{
  "id": "threat_response_123",
  "rpc": "threat.response",
  "params": {
    "threat_id": "threat_announced_hallway_sc_001",
    "player_id": "player_123",
    "decision": "survive",
    "timestamp": "2025-11-05T10:30:48Z"
  }
}
```

**`decision` の値:**
- `"survive"` → [✅ 生存を目指す] 選択
- `"cancel"` → [❌ キャンセル] 選択
- `"skip"` → [❌ スキップ] 選択
- `"execute"` → [✅ 実行] 選択
- `"timeout"` → 10秒以内に選択しなかった (自動キャンセル)

### RPC: `coordinated.voting.start`

```json
{
  "rpc": "coordinated.voting.start",
  "params": {
    "campaign_id": "campaign_123",
    "instance_id": "inst_456",
    "terror_name": "Silent Crush",
    "expires_at": "2025-11-05T10:31:00Z"
  }
}
```

### RPC: `coordinated.voting.vote`

```json
{
  "rpc": "coordinated.voting.vote",
  "params": {
    "campaign_id": "campaign_123",
    "player_id": "player_456",
    "decision": "Proceed"
  }
}
```

### RPC: `coordinated.voting.resolved`

```json
{
  "rpc": "coordinated.voting.resolved",
  "params": {
    "campaign_id": "campaign_123",
    "final_decision": "Proceed",
    "votes": [
      {
        "player_id": "player_456",
        "decision": "Proceed"
      },
      {
        "player_id": "player_789",
        "decision": "Proceed"
      },
      {
        "player_id": "player_123",
        "decision": "Cancel"
      },
      {
        "player_id": "player_321",
        "decision": "Cancel"
      }
    ],
    "timestamp": "2025-11-05T10:31:00Z"
  }
}
```

---

## ⏱️ タイムアウト仕様

### 投票時間: 10秒

| イベント | タイム | 動作 |
|---------|--------|------|
| ボタン表示開始 | 0s | ThreatAlertOverlay 出現 |
| ボタン有効 | 0-10s | ユーザーが選択可能 |
| ボタン無効化 | 10s | 自動タイムアウト |
| UI自動閉じ | 11s | オーバーレイ消える |

### タイムアウト時の処理

```csharp
public async Task OnThreatAlertTimeoutAsync(string threatId)
{
    var threat = await _repo.GetThreatAsync(threatId);
    
    if (threat.Mode == ThreatAlertMode.Coordinated)
    {
        // 投票モード: 未投票を Cancel で集計
        var campaign = await _voting.GetCampaignAsync(threatId);
        await _voting.OnVotingTimeoutAsync(campaign);
    }
    else if (threat.Mode == ThreatAlertMode.Individual)
    {
        // 個別モード: スキップ扱い
        await _autoSuicide.SkipCurrentCycleAsync();
    }
    
    // UI自動閉じ
    await _ui.CloseAlertAsync();
}
```

---

## 📋 実装チェックリスト

### Phase 1: 基盤

- [ ] WishedTerror Domain 定義
- [ ] IAppSettings に ほしいテラー 設定追加
- [ ] LocalStorage 永続化 (JSON)
- [ ] SettingsPanel UI 実装 (テーブル + Add/Delete)

### Phase 2: マッチング & 検索

- [ ] `GetSurvivalWishersForTerror()` 実装
- [ ] `IsTerrorMatched()` ロジック実装
- [ ] ユニットテスト (8パターン)

### Phase 3: 通知フロー

- [ ] RealTimeSyncService: threat.announced 受信
- [ ] 生存希望プレイヤー 0 人 → 通知なし
- [ ] 生存希望プレイヤー > 0 → ThreatAlertOverlay 表示

### Phase 4: UI & タイムアウト

- [ ] ThreatAlertOverlay ボタン表示/非表示
- [ ] 10秒タイムアウト実装
- [ ] 自動閉じ
- [ ] PlayerListOverlay 実装

### Phase 5: 投票連携

- [ ] CoordinatedAutoSuicideService: タイムアウト時の Cancel 集計
- [ ] IndividualAutoSuicideService: タイムアウト時の Skip
- [ ] E2E テスト

---

## 🎯 まとめ

| 項目 | 仕様 |
|------|------|
| **投票時間** | 10秒 |
| **表示条件** | 生存希望プレイヤー > 0 |
| **ラウンド指定** | "hallway" = 特定ラウンド、"" = すべてのラウンド |
| **マッチング** | テラー名必須 + ラウンド一致 (OR 空白) |
| **タイムアウト** | 10秒 → 投票は Cancel、個別は Skip |
| **通知のみ** | 手動モード: 3秒で自動閉じ |
| **色分け** | 生存希望者（緑/赤）、その他（白/灰色） |
| **更新頻度** | PlayerList: 200ms |
| **投票集計** | 過半数以上で Proceed |

---


**END OF DOCUMENT**
