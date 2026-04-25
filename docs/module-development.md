# 繝｢繧ｸ繝･繝ｼ繝ｫ髢狗匱繧ｬ繧､繝・

縺薙・繝峨く繝･繝｡繝ｳ繝医・縲ゝoNRoundCounter 縺ｮ繝｢繧ｸ繝･繝ｼ繝ｫ諡｡蠑ｵ繝昴う繝ｳ繝医ｒ菴鍋ｳｻ逧・↓謨ｴ逅・＠縲√Μ繝昴ず繝医Μ縺ｮ繧ｳ繝ｼ繝峨ｒ逶ｴ謗･蜿ら・縺励↑縺上※繧ゅΔ繧ｸ繝･繝ｼ繝ｫ繧呈ｧ狗ｯ峨〒縺阪ｋ繧医≧縺ｫ縺吶ｋ縺薙→繧堤岼逧・→縺励※縺・∪縺吶ゅΔ繧ｸ繝･繝ｼ繝ｫ縺ｮ髮帛ｽ｢菴懈・縺九ｉ繝薙Ν繝峨・驟榊ｸ・∝推繝ｩ繧､繝輔し繧､繧ｯ繝ｫ繧､繝吶Φ繝医・隧ｳ邏ｰ縲ゞI 諡｡蠑ｵ繧・・蜍募喧讖溯・縺ｨ縺ｮ騾｣謳ｺ譁ｹ豕輔∪縺ｧ繧堤ｶｲ鄒・噪縺ｫ隱ｬ譏弱＠縺ｾ縺吶・

---

## 1. 繧｢繝ｼ繧ｭ繝・け繝√Ε縺ｮ蜈ｨ菴灘ワ

### 1.1 繝｢繧ｸ繝･繝ｼ繝ｫ隱ｭ縺ｿ霎ｼ縺ｿ縺ｮ豬√ｌ
1. 繧｢繝励Μ繧ｱ繝ｼ繧ｷ繝ｧ繝ｳ襍ｷ蜍墓凾縲～Program.Main` 縺・DI 繧ｳ繝ｳ繝・リ (`ServiceCollection`) 繧呈ｧ区・縺励√Δ繧ｸ繝･繝ｼ繝ｫ繝帙せ繝・(`ModuleHost`) 繧堤函謌舌＠縺ｾ縺吶ゅ色:Program.cs窶L46-L85縲・
2. `ModuleLoader.LoadModules` 縺・`Modules` 繝輔か繝ｫ繝繝ｼ驟堺ｸ九・ DLL 繧貞・謖吶＠縲～IModule` 繧貞ｮ溯｣・☆繧句・髢句梛繧呈爾邏｢縺励∪縺吶ゅ色:Infrastructure/ModuleLoader.cs窶L15-L40縲・
3. 逋ｺ隕九＆繧後◆蜷・梛縺ｯ `Activator.CreateInstance` 縺ｧ繧､繝ｳ繧ｹ繧ｿ繝ｳ繧ｹ蛹悶＆繧後～ModuleHost.RegisterModule` 縺ｫ逋ｻ骭ｲ縺輔ｌ縺ｾ縺吶ゅ色:Infrastructure/ModuleLoader.cs窶L34-L39縲代色:Infrastructure/ModuleHost.cs窶L297-L342縲・
4. 繝帙せ繝医・繝｢繧ｸ繝･繝ｼ繝ｫ縺ｫ蟇ｾ縺励※繝ｩ繧､繝輔し繧､繧ｯ繝ｫ繧ｳ繝ｼ繝ｫ繝舌ャ繧ｯ (`OnModuleLoaded` 縺ｪ縺ｩ) 繧帝・ｬ｡蜻ｼ縺ｳ蜃ｺ縺励∝酔譎ゅ↓繧､繝吶Φ繝医ヰ繧ｹ邨檎罰縺ｧ繧ょ酔讒倥・繧､繝吶Φ繝医ｒ蜈ｬ髢九＠縺ｾ縺吶ゅ色:Infrastructure/ModuleHost.cs窶L283-L399縲・
5. 繧ｵ繝ｼ繝薙せ繝励Ο繝舌う繝繝ｼ讒狗ｯ牙ｾ後・ `ModuleHost` 縺・UI 繧・壻ｿ｡縲∬ｨｭ螳壹√が繝ｼ繝郁ｵｷ蜍包ｼ剰・谿ｺ縺ｪ縺ｩ繧｢繝励Μ縺ｮ繧ｳ繧｢繧､繝吶Φ繝医ｒ邯咏ｶ夂噪縺ｫ騾夂衍縺励∪縺吶ゅ色:Infrastructure/ModuleHost.cs窶L344-L399縲代色:Infrastructure/ModuleHost.cs窶L400-L455縲・

### 1.2 謠蝉ｾ帙＆繧後ｋ繧ｳ繝ｳ繝・く繧ｹ繝・繧ｪ繝悶ず繧ｧ繧ｯ繝・
蜷・Λ繧､繝輔し繧､繧ｯ繝ｫ繝｡繧ｽ繝・ラ縺ｯ縲∫憾諷九ｄ萓晏ｭ倬未菫ゅ∈繧｢繧ｯ繧ｻ繧ｹ縺吶ｋ縺溘ａ縺ｮ繧ｳ繝ｳ繝・く繧ｹ繝亥梛繧貞女縺大叙繧翫∪縺吶ゆｸｻ縺ｪ繝励Ο繝代ユ繧｣縺ｯ莉･荳九・騾壹ｊ縺ｧ縺吶・

| 繧ｳ繝ｳ繝・く繧ｹ繝・| 荳ｻ隕√・繝ｭ繝代ユ繧｣ | 逕ｨ騾・|
| --- | --- | --- |
| `ModuleDiscoveryContext` | `ModuleName`, `Assembly`, `SourcePath` | 繝｢繧ｸ繝･繝ｼ繝ｫ閾ｪ霄ｫ縺ｮ繝｡繧ｿ繝・・繧ｿ遒ｺ隱阪ｄ繝ｭ繧ｰ蜃ｺ蜉帙ゅ色:Application/IModule.cs窶L466-L480縲・|
| `ModuleServiceRegistrationContext` | `Services`, `Logger`, `Bus` | DI 逋ｻ骭ｲ譎ゅ↓繝帙せ繝医し繝ｼ繝薙せ縺ｸ繧｢繧ｯ繧ｻ繧ｹ縲ゅ色:Application/IModule.cs窶L483-L501縲・|
| `ModuleServiceProviderContext` | `ServiceProvider`, `Logger`, `Bus` | `IServiceProvider` 縺九ｉ萓晏ｭ俶ｧ繧定ｧ｣豎ｺ縺吶ｋ髫帙↓蛻ｩ逕ｨ縲ゅ色:Application/IModule.cs窶L521-L541縲・|
| `ModuleMainWindowContext` | `Form`, `ServiceProvider` | 繝｡繧､繝ｳ繝輔か繝ｼ繝繧､繝ｳ繧ｹ繧ｿ繝ｳ繧ｹ縺ｸ縺ｮ繧｢繧ｯ繧ｻ繧ｹ縲ゅ色:Application/IModule.cs窶L573-L609縲・|
| `ModuleSettingsViewBuildContext` | `AddSettingsGroup`, `AddExtensionControl` 縺ｪ縺ｩ縺ｮ API | 險ｭ螳壹ン繝･繝ｼ縺ｫ WinForms 繧ｳ繝ｳ繝医Ο繝ｼ繝ｫ繧呈諺蜈･縲ゅ色:Application/IModule.cs窶L918-L1004縲・|
| `ModuleWebSocketConnectionContext` | `Phase`, `Exception`, `ServiceProvider` | WebSocket 迥ｶ諷句､牙喧縺ｮ謚頑升縺ｨ蠕ｩ譌ｧ蜃ｦ逅・ゅ色:Application/IModule.cs窶L642-L685縲・|
| `ModuleAutoLaunchEvaluationContext` | `Plans`, `Settings`, `ServiceProvider` | 閾ｪ蜍戊ｵｷ蜍募ｯｾ雎｡縺ｮ霑ｽ蜉繝ｻ蜑企勁縲ゅ色:Application/IModule.cs窶L776-L800縲・|
| `ModuleAuxiliaryWindowCatalogContext` | `RegisterWindow`, `ServiceProvider` | 陬懷勧繧ｦ繧｣繝ｳ繝峨え縺ｮ逋ｻ骭ｲ縺ｨ襍ｷ蜍募宛蠕｡縲ゅ色:Application/IModule.cs窶L878-L902縲・|

> **TIP**: 繧ｳ繝ｳ繝・く繧ｹ繝医′ `ServiceProvider` 繧貞・髢九＠縺ｦ縺・ｋ蝣ｴ蜷医・縲～GetRequiredService` 繧剃ｽｿ縺｣縺ｦ繝｢繧ｸ繝･繝ｼ繝ｫ蟆ら畑繧ｵ繝ｼ繝薙せ繧・・繧ｹ繝域署萓帙し繝ｼ繝薙せ繧貞ｮ牙・縺ｫ蜿門ｾ励〒縺阪∪縺吶・

### 1.3 繧｢繝励Μ繧ｱ繝ｼ繧ｷ繝ｧ繝ｳ縺梧署萓帙☆繧区里螳壹し繝ｼ繝薙せ
`Program.Main` 縺ｯ莉･荳九・繧ｵ繝ｼ繝薙せ繧・DI 繧ｳ繝ｳ繝・リ縺ｸ逋ｻ骭ｲ縺励※縺・∪縺吶ゅΔ繧ｸ繝･繝ｼ繝ｫ縺ｯ `RegisterServices` 繧・ｾ檎ｶ壹う繝吶Φ繝医〒縺薙ｌ繧峨ｒ隗｣豎ｺ縺励※蛻ｩ逕ｨ縺ｧ縺阪∪縺吶ゅ色:Program.cs窶L46-L85縲・

- `IEventLogger` / `IEventBus` : 繝ｭ繧ｰ蜃ｺ蜉帙→繧｢繝励Μ蜀・う繝吶Φ繝磯・菫｡
- `ICancellationProvider` : 髟ｷ譎る俣蜃ｦ逅・・繧ｭ繝｣繝ｳ繧ｻ繝ｫ蛻ｶ蠕｡
- `IUiDispatcher` : UI 繧ｹ繝ｬ繝・ラ縺ｧ縺ｮ螳溯｡御ｿ晁ｨｼ
- `IWebSocketClient`, `IOSCListener` : 騾壻ｿ｡繧ｯ繝ｩ繧､繧｢繝ｳ繝・
- `AutoSuicideService`, `StateService`, `MainPresenter`, `IAppSettings` 縺ｪ縺ｩ繧ｳ繧｢讖溯・

---

## 2. 髢狗匱貅門ｙ縺ｨ繝励Ο繧ｸ繧ｧ繧ｯ繝磯屁蠖｢

### 2.1 謗ｨ螂ｨ髢狗匱迺ｰ蠅・
- .NET Framework 4.8 繧偵ち繝ｼ繧ｲ繝・ヨ縺ｫ縺ｧ縺阪ｋ IDE (Visual Studio 2022 縺ｪ縺ｩ)
- 縺薙・繝ｪ繝昴ず繝医Μ繧偵け繝ｭ繝ｼ繝ｳ縺励◆繝ｭ繝ｼ繧ｫ繝ｫ迺ｰ蠅・
- C# / WinForms / DI 繧ｳ繝ｳ繝・リ縺ｮ蝓ｺ遉守衍隴・

### 2.2 繝｢繧ｸ繝･繝ｼ繝ｫ 繝励Ο繧ｸ繧ｧ繧ｯ繝医・菴懈・謇矩・
1. 繧ｽ繝ｪ繝･繝ｼ繧ｷ繝ｧ繝ｳ蜀・・ `Modules` 繝輔か繝ｫ繝繝ｼ縺ｫ C# 繧ｯ繝ｩ繧ｹ繝ｩ繧､繝悶Λ繝ｪ 繝励Ο繧ｸ繧ｧ繧ｯ繝医ｒ霑ｽ蜉縺励√ち繝ｼ繧ｲ繝・ヨ繝輔Ξ繝ｼ繝繝ｯ繝ｼ繧ｯ繧・`net48` 縺ｫ險ｭ螳壹＠縺ｾ縺吶・
2. `ToNRoundCounter` 譛ｬ菴・(`ToNRoundCounter.csproj`) 縺ｸ縺ｮ `ProjectReference` 繧定ｿｽ蜉縺励～Application.IModule` 繧・UI 繝倥Ν繝代・繧貞盾辣ｧ縺ｧ縺阪ｋ繧医≧縺ｫ縺励∪縺吶・
3. 繝薙Ν繝画・譫懃黄繧定・蜍慕噪縺ｫ `Modules` 繝輔か繝ｫ繝繝ｼ縺ｸ繧ｳ繝斐・縺吶ｋ縺ｫ縺ｯ縲∽ｻ･荳九・ MSBuild 繧ｿ繝ｼ繧ｲ繝・ヨ繧・`*.csproj` 縺ｫ霑ｽ蜉縺励∪縺吶・
   ```xml
   <Target Name="CopyModuleToOutput" AfterTargets="Build">
     <MakeDir Directories="$(SolutionDir)Modules" />
     <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(SolutionDir)Modules" SkipUnchangedFiles="true" />
   </Target>
   ```
4. 螟夜Κ繝ｩ繧､繝悶Λ繝ｪ繧貞茜逕ｨ縺吶ｋ蝣ｴ蜷医・騾壼ｸｸ騾壹ｊ NuGet 繧定ｿｽ蜉縺励√Λ繧､繧ｻ繝ｳ繧ｹ隕∽ｻｶ縺ｫ豕ｨ諢上＠縺ｦ縺上□縺輔＞縲・

### 2.3 謗ｨ螂ｨ繝輔か繝ｫ繝讒区・
```
Modules/
  SampleModule/
    SampleModule.csproj
    Module.cs            // IModule 螳溯｣・
    Services/
      SampleFeature.cs   // DI 逋ｻ骭ｲ縺吶ｋ繧ｵ繝ｼ繝薙せ
    Views/
      SampleDialog.cs    // 陬懷勧繧ｦ繧｣繝ｳ繝峨え / 險ｭ螳・UI
```

---

## 3. 繝｢繧ｸ繝･繝ｼ繝ｫ縺ｮ蝓ｺ譛ｬ鬪ｨ譬ｼ

### 3.1 譛蟆丞ｮ溯｣・
繝｢繧ｸ繝･繝ｼ繝ｫ縺ｯ `IModule` 繧貞ｮ溯｣・＠縲√ヱ繝ｩ繝｡繝ｼ繧ｿ繝ｼ縺ｪ縺励・ `public` 繧ｳ繝ｳ繧ｹ繝医Λ繧ｯ繧ｿ繝ｼ繧呈戟縺､蠢・ｦ√′縺ゅｊ縺ｾ縺吶ゅ色:Infrastructure/ModuleLoader.cs窶L31-L40縲・

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

        // 蠢・ｦ√↑繧､繝吶Φ繝医□縺代ｒ繧ｪ繝ｼ繝舌・繝ｩ繧､繝・(譛ｪ菴ｿ逕ｨ繝｡繧ｽ繝・ラ縺ｯ遨ｺ縺ｧ蜿ｯ)
        public void OnModuleLoaded(ModuleDiscoveryContext context) { }
        public void OnAfterServiceProviderBuild(ModuleServiceProviderContext context) { }
        public void OnSettingsViewBuilding(ModuleSettingsViewBuildContext context) { }
        public void OnWebSocketMessageReceived(ModuleWebSocketMessageContext context) { }
        // ... 逵∫払 ...
    }
}
```

遨ｺ螳溯｣・・縺ｾ縺ｾ縺ｧ繧ょ撫鬘後≠繧翫∪縺帙ｓ縺後∝ｾ瑚ｿｰ縺ｮ繝ｩ繧､繝輔し繧､繧ｯ繝ｫ繧堤炊隗｣縺礼岼逧・↓蠢懊§縺溘う繝吶Φ繝医ｒ驕ｸ謚槭☆繧九％縺ｨ縺ｧ縲√い繝励Μ縺ｮ莉ｻ諢上・繧､繝ｳ繝医ｒ繧ｫ繧ｹ繧ｿ繝槭う繧ｺ縺ｧ縺阪∪縺吶よ里蟄倥・ `AfkJumpModule` 縺ｯ譛ｪ菴ｿ逕ｨ繧､繝吶Φ繝医ｒ遨ｺ螳溯｣・↓縺励◆譛蟆丈ｾ九〒縺吶ゅ色:Modules/AfkJumpModule/AfkJumpModule.cs窶L13-L324縲・

### 3.2 DI 繧ｵ繝ｼ繝薙せ縺ｨ萓晏ｭ倬未菫ゅ・螳夂ｾｩ
`RegisterServices` 縺ｧ縺ｯ繝帙せ繝医′逕滓・縺吶ｋ繧ｵ繝ｼ繝薙せ縺ｨ蜷後§繧医≧縺ｫ繝ｩ繧､繝輔ち繧､繝繧呈欠螳壹＠逋ｻ骭ｲ縺ｧ縺阪∪縺吶ＡModuleServiceRegistrationContext` 縺九ｉ `Logger` 繧・`Bus` 繧貞叙蠕励＠縺ｦ蛻晄悄蛹悶Ο繧ｰ繧貞・蜉帙☆繧九％縺ｨ繧ょ庄閭ｽ縺ｧ縺吶ゅ色:Application/IModule.cs窶L483-L501縲・

```csharp
public void RegisterServices(IServiceCollection services)
{
    services.AddSingleton<ISampleSettings, SampleSettings>();
    services.AddTransient<ISampleCommand, SampleCommand>();
}

public void OnAfterServiceRegistration(ModuleServiceRegistrationContext context)
{
    context.Logger.LogEvent("SampleModule", "繧ｵ繝ｼ繝薙せ逋ｻ骭ｲ縺悟ｮ御ｺ・＠縺ｾ縺励◆縲・);
}
```

---

## 4. 繝ｩ繧､繝輔し繧､繧ｯ繝ｫ 繝ｪ繝輔ぃ繝ｬ繝ｳ繧ｹ

### 4.1 繝輔ぉ繝ｼ繧ｺ荳隕ｧ

| 繝輔ぉ繝ｼ繧ｺ | 荳ｻ縺ｪ繝｡繧ｽ繝・ラ | 荳ｻ鬘・|
| --- | --- | --- |
| 逋ｺ隕九・繝｡繧ｿ繝・・繧ｿ | `OnModuleLoaded`, `OnPeerModuleLoaded` | 繝｢繧ｸ繝･繝ｼ繝ｫ諠・ｱ縺ｮ逋ｻ骭ｲ縲∫嶌莠剃ｾ晏ｭ俶ｧ繝√ぉ繝・け縲ゅ色:Application/IModule.cs窶L18-L82縲代色:Infrastructure/ModuleHost.cs窶L297-L320縲・|
| DI 逋ｻ骭ｲ | `OnBeforeServiceRegistration`, `RegisterServices`, `OnAfterServiceRegistration` | 繧ｵ繝ｼ繝薙せ霑ｽ蜉縲∝燕謠先擅莉ｶ讀懆ｨｼ縲√う繝吶Φ繝郁ｳｼ隱ｭ險ｭ螳壹ゅ色:Application/IModule.cs窶L24-L52縲代色:Infrastructure/ModuleHost.cs窶L313-L341縲・|
| 繧ｵ繝ｼ繝薙せ繝励Ο繝舌う繝繝ｼ讒狗ｯ・| `OnBeforeServiceProviderBuild`, `OnAfterServiceProviderBuild` | 繧ｷ繝ｳ繧ｰ繝ｫ繝医Φ蛻晄悄蛹悶∝､夜Κ謗･邯壹・繧ｦ繧ｩ繝ｼ繝繧｢繝・・縲ゅ色:Application/IModule.cs窶L42-L53縲代色:Infrastructure/ModuleHost.cs窶L344-L368縲・|
| 繝｡繧､繝ｳ繧ｦ繧｣繝ｳ繝峨え逕滓・ | `OnBeforeMainWindowCreation` ・・`OnMainWindowClosing` | UI 蟾ｮ縺苓ｾｼ縺ｿ縲√ヵ繧ｩ繝ｼ繝繧､繝吶Φ繝医・繝輔ャ繧ｯ縲ゅ色:Application/IModule.cs窶L54-L125縲代色:Infrastructure/ModuleHost.cs窶L371-L399縲・|
| 險ｭ螳壹Ο繝ｼ繝・菫晏ｭ・| `OnSettingsLoading` ・・`OnSettingsSaved` | 險ｭ螳壹せ繧ｭ繝ｼ繝樒ｧｻ陦後∝､夜Κ險ｭ螳壼酔譛溘ゅ色:Application/IModule.cs窶L78-L101縲・|
| 險ｭ螳壹ン繝･繝ｼ | `OnSettingsViewBuilding` ・・`OnSettingsViewClosed` | 險ｭ螳夂判髱｢ UI縲∵､懆ｨｼ縺ｨ繝ｭ繝ｼ繝ｫ繝舌ャ繧ｯ蛻ｶ蠕｡縲ゅ色:Application/IModule.cs窶L102-L130縲代色:UI/SettingsPanel.cs窶L1225-L1330縲・|
| 繧｢繝励Μ螳溯｡・邨ゆｺ・| `OnAppRunStarting` ・・`OnAfterAppShutdown`, `OnUnhandledException` | 襍ｷ蜍輔す繝ｼ繧ｱ繝ｳ繧ｹ隱ｿ謨ｴ縲∫ｵゆｺ・・逅・∽ｾ句､夜夂衍縲ゅ色:Application/IModule.cs窶L132-L160縲代色:Infrastructure/ModuleHost.cs窶L397-L455縲・|
| 騾壻ｿ｡ | `OnWebSocketConnecting` ・・`OnOscMessageReceived` | 謗･邯夂屮隕悶∝女菫｡繝｡繝・そ繝ｼ繧ｸ蜃ｦ逅・ゅ色:Application/IModule.cs窶L162-L214縲代色:Infrastructure/ModuleHost.cs窶L29-L207縲・|
| 險ｭ螳壽､懆ｨｼ | `OnBeforeSettingsValidation` ・・`OnSettingsValidationFailed` | 霑ｽ蜉繝舌Μ繝・・繧ｷ繝ｧ繝ｳ縲√お繝ｩ繝ｼ陦ｨ遉ｺ蛻ｶ蠕｡縲ゅ色:Application/IModule.cs窶L216-L233縲・|
| 閾ｪ蜍募・逅・| `OnAutoSuicide*`, `OnAutoLaunch*` | 閾ｪ谿ｺ繝ｫ繝ｼ繝ｫ・剰・蜍戊ｵｷ蜍戊ｨ育判縺ｮ諡｡蠑ｵ縲ゅ色:Application/IModule.cs窶L234-L287縲代色:Program.cs窶L129-L198縲・|
| UI 諡｡蠑ｵ | `OnThemeCatalogBuilding` ・・`OnMainWindowLayoutUpdated`, `OnAuxiliaryWindow*` | 繝・・繝樒匳骭ｲ縲√Γ繝九Η繝ｼ繝ｻ繝ｬ繧､繧｢繧ｦ繝域桃菴懊∬｣懷勧繧ｦ繧｣繝ｳ繝峨え縲ゅ色:Application/IModule.cs窶L288-L346縲代色:UI/MainForm.cs窶L115-L520縲・|
| 繝｢繧ｸ繝･繝ｼ繝ｫ騾｣謳ｺ | `OnPeerModule*` 繧ｷ繝ｪ繝ｼ繧ｺ | 莉悶Δ繧ｸ繝･繝ｼ繝ｫ縺ｮ迥ｶ諷玖ｦｳ貂ｬ縺ｨ蜊碑ｪｿ蛻ｶ蠕｡縲ゅ色:Application/IModule.cs窶L348-L460縲代色:Infrastructure/ModuleHost.cs窶L319-L393縲・|

### 4.2 莉｣陦ｨ逧・↑螳溯｣・ヱ繧ｿ繝ｼ繝ｳ

#### 4.2.1 繝・ぅ繧ｹ繧ｫ繝舌Μ縺ｨ蛻晄悄繝ｭ繧ｰ
```csharp
public void OnModuleLoaded(ModuleDiscoveryContext context)
{
    context.Logger.Information(
        "{Module} v{Version} 繧偵Ο繝ｼ繝峨＠縺ｾ縺励◆", context.Module.ModuleName, context.Module.Assembly.GetName().Version);
}
```

#### 4.2.2 繧ｵ繝ｼ繝薙せ逋ｻ骭ｲ縺ｮ譚｡莉ｶ蛻・ｲ・
繝｢繧ｸ繝･繝ｼ繝ｫ險ｭ螳壹ｄ迺ｰ蠅・､画焚縺ｫ繧医▲縺ｦ繧ｵ繝ｼ繝薙せ逋ｻ骭ｲ繧貞・繧頑崛縺医ｋ蝣ｴ蜷医～OnBeforeServiceRegistration` 縺ｧ蜿ｯ蜷ｦ蛻､螳壹ｒ陦後＞縲～ModuleServiceRegistrationContext.Services` 繧堤峩謗･謫堺ｽ懊＠縺ｾ縺吶ゅ色:Application/IModule.cs窶L483-L501縲・

#### 4.2.3 繧ｵ繝ｼ繝薙せ繝励Ο繝舌う繝繝ｼ螳梧・譎ゅ・蛻晄悄蛹・
```csharp
public void OnAfterServiceProviderBuild(ModuleServiceProviderContext context)
{
    var feature = context.ServiceProvider?.GetService<ISampleFeature>();
    feature?.WarmUp();
}
```

#### 4.2.4 繝｡繧､繝ｳ繧ｦ繧｣繝ｳ繝峨え蛻晄悄蛹・
`ModuleMainWindowContext.Form` 縺ｯ `MainForm` 繧､繝ｳ繧ｹ繧ｿ繝ｳ繧ｹ縺ｪ縺ｮ縺ｧ縲√ヵ繧ｩ繝ｼ繝縺ｮ繧､繝吶Φ繝医ｒ霑ｽ蜉縺ｧ雉ｼ隱ｭ縺ｧ縺阪∪縺吶ゅ色:Application/IModule.cs窶L562-L609縲・

```csharp
public void OnAfterMainWindowCreation(ModuleMainWindowContext context)
{
    if (context.Form is MainForm main)
    {
        main.StatusText = "SampleModule 縺後Ο繝ｼ繝峨＆繧後∪縺励◆";
    }
}
```

#### 4.2.5 險ｭ螳壹ン繝･繝ｼ縺ｮ讒狗ｯ・
`ModuleSettingsViewBuildContext.AddSettingsGroup` 縺ｧ繧ｰ繝ｫ繝ｼ繝励ｒ逕滓・縺励仝inForms 繧ｳ繝ｳ繝医Ο繝ｼ繝ｫ繧定ｿｽ蜉縺励※繝舌う繝ｳ繝峨＠縺ｾ縺吶ゅ色:Application/IModule.cs窶L918-L1004縲・

```csharp
public void OnSettingsViewBuilding(ModuleSettingsViewBuildContext context)
{
    var group = context.AddSettingsGroup("Sample Module");
    var checkbox = new CheckBox { Text = "讖溯・繧呈怏蜉ｹ蛹・, AutoSize = true };
    checkbox.DataBindings.Add("Checked", context.Settings, nameof(ISampleSettings.Enabled));
    group.Controls.Add(checkbox);
}
```

#### 4.2.6 WebSocket/OSC 繝｡繝・そ繝ｼ繧ｸ蜃ｦ逅・
```csharp
public void OnWebSocketMessageReceived(ModuleWebSocketMessageContext context)
{
    context.Logger.Debug("蜿嶺ｿ｡: {Message}", context.Message);
}

public void OnOscMessageReceived(ModuleOscMessageContext context)
{
    if (context.Message.Address == "/input/Jump")
    {
        // 蜿嶺ｿ｡繝輔ャ繧ｯ
    }
}
```

---

## 5. 險ｭ螳壹→繝舌Μ繝・・繧ｷ繝ｧ繝ｳ

### 5.1 險ｭ螳壹Λ繧､繝輔し繧､繧ｯ繝ｫ
- `OnSettingsLoading` / `OnSettingsLoaded`: 險ｭ螳壹ヵ繧｡繧､繝ｫ縺ｮ隱ｭ縺ｿ霎ｼ縺ｿ蜑榊ｾ後〒繝槭う繧ｰ繝ｬ繝ｼ繧ｷ繝ｧ繝ｳ繧・､夜Κ繧ｹ繝医Ξ繝ｼ繧ｸ蜷梧悄繧貞ｮ溯｡後＠縺ｾ縺吶ゅ色:Application/IModule.cs窶L78-L101縲・
- `OnSettingsSaving` / `OnSettingsSaved`: 豌ｸ邯壼喧蜑榊ｾ後↓讀懆ｨｼ貂医∩蛟､縺ｮ遒ｺ螳壹ｄ繝舌ャ繧ｯ繧｢繝・・繧剃ｽ懈・縺励∪縺吶・
- `OnBeforeSettingsValidation` / `OnSettingsValidated` / `OnSettingsValidationFailed`: 繧｢繝励Μ縺ｮ讓呎ｺ匁､懆ｨｼ蜑榊ｾ後〒迢ｬ閾ｪ繝ｫ繝ｼ繝ｫ繧定ｿｽ蜉縺励√お繝ｩ繝ｼ縺ｮ菫ｮ豁｣繝偵Φ繝医ｒ謠千､ｺ縺励∪縺吶ゅ色:Application/IModule.cs窶L216-L233縲・

### 5.2 險ｭ螳壹ン繝･繝ｼ 繝輔ャ繧ｯ
`ModuleSettingsViewLifecycleContext` 縺ｯ `DialogResult` 縺ｪ縺ｩ繧貞盾辣ｧ縺励√Θ繝ｼ繧ｶ繝ｼ縺・OK 繧呈款縺励◆縺九ｒ遒ｺ隱阪〒縺阪∪縺吶ＡOnSettingsViewApplying` 縺ｧ蜈･蜉帛､縺ｮ譛邨ゅメ繧ｧ繝・け繧定｡後＞縲∝､ｱ謨玲凾縺ｯ `context.Cancel()` 繧貞他縺ｳ蜃ｺ縺励※驕ｩ逕ｨ繧帝仆豁｢縺励∪縺吶ゅ色:Application/IModule.cs窶L102-L130縲・

### 5.3 繧ｵ繝ｳ繝励Ν: URL 險ｭ螳壹・讀懆ｨｼ
```csharp
public void OnBeforeSettingsValidation(ModuleSettingsValidationContext context)
{
    if (context.Settings is ISampleSettings settings && !Uri.IsWellFormedUriString(settings.Endpoint, UriKind.Absolute))
    {
        context.AddError("Endpoint 縺ｫ譛牙柑縺ｪ URL 繧貞・蜉帙＠縺ｦ縺上□縺輔＞縲・);
    }
}

public void OnSettingsValidationFailed(ModuleSettingsValidationContext context)
{
    context.Logger.LogEvent("SampleModule", string.Join("\n", context.Errors));
}
```

---

## 6. 騾壻ｿ｡繧､繝吶Φ繝医・豢ｻ逕ｨ

### 6.1 WebSocket
- 謗･邯夐幕蟋・(`OnWebSocketConnecting`) / 謌仙粥 (`OnWebSocketConnected`) / 蛻・妙 (`OnWebSocketDisconnected`) / 蜀肴磁邯・(`OnWebSocketReconnecting`) 縺ｮ蜷・ヵ繧ｧ繝ｼ繧ｺ縺ｧ繝ｪ繝医Λ繧､謌ｦ逡･繧・夂衍繧貞ｮ溯｣・〒縺阪∪縺吶ゅ色:Application/IModule.cs窶L162-L190縲代色:Infrastructure/ModuleHost.cs窶L29-L196縲・
- `ModuleWebSocketMessageContext.Message` 縺ｫ逕溘・ JSON/Payload 縺悟・繧九◆繧√～System.Text.Json` 縺ｪ縺ｩ縺ｧ繝・す繝ｪ繧｢繝ｩ繧､繧ｺ縺励う繝吶Φ繝医ラ繝｡繧､繝ｳ縺ｸ霆｢騾√〒縺阪∪縺吶ゅ色:Application/IModule.cs窶L687-L701縲・

### 6.2 OSC
- `OnOscMessageReceived` 縺ｧ縺ｯ `Rug.Osc` 縺ｮ `OscMessage` 繧堤峩謗･蜿門ｾ励〒縺阪ｋ縺ｮ縺ｧ縲√い繝峨Ξ繧ｹ縺ｧ繝輔ぅ繝ｫ繧ｿ繝ｪ繝ｳ繧ｰ縺玲焚蛟､繧・枚蟄怜・繧呈歓蜃ｺ縺励∪縺吶ゅ色:Application/IModule.cs窶L738-L749縲・
- `AfkJumpModule` 縺ｯ `/input/Jump` 縺ｸ繧ｸ繝｣繝ｳ繝嶺ｿ｡蜿ｷ繧帝∽ｿ｡縺吶ｋ螳溯｣・ｾ九〒縺吶０SC 縺ｧ螟夜Κ繧｢繝励Μ騾｣謳ｺ繧定｡後＞縺溘＞蝣ｴ蜷医↓蜿り・↓縺ｪ繧翫∪縺吶ゅ色:Modules/AfkJumpModule/AfkJumpModule.cs窶L337-L371縲・

---

## 7. UI 諡｡蠑ｵ

### 7.1 繝｡繧､繝ｳ繧ｦ繧｣繝ｳ繝峨え繝｡繝九Η繝ｼ
`OnMainWindowMenuBuilding` 縺ｯ `ModuleMainWindowMenuContext.AddMenu` 繧呈署萓帙＠縲∵ｨ呎ｺ悶Γ繝九Η繝ｼ縺ｫ繝｢繧ｸ繝･繝ｼ繝ｫ鬆・岼繧堤ｰ｡貎斐↓霑ｽ蜉縺ｧ縺阪∪縺吶ゅ色:Application/IModule.cs窶L288-L304縲・

```csharp
public void OnMainWindowMenuBuilding(ModuleMainWindowMenuContext context)
{
    var toolsMenu = context.AddMenu("繝・・繝ｫ(&T)");
    var item = new ToolStripMenuItem("Sample Module 繧帝幕縺・);
    item.Click += (_, _) => ShowSampleDialog(context.Form);
    toolsMenu.DropDownItems.Add(item);
}
```

### 7.2 繝ｬ繧､繧｢繧ｦ繝医・繝・・繝・
- `OnMainWindowUiComposed` 縺ｧ `FlowLayoutPanel` 繧・`UserControl` 繧定ｿｽ蜉縺励～OnMainWindowLayoutUpdated` 縺ｧ繧ｵ繧､繧ｺ隱ｿ謨ｴ縺励∪縺吶ゅ色:Application/IModule.cs窶L300-L346縲代色:UI/MainForm.cs窶L115-L219縲・
- `OnMainWindowThemeChanged` 縺ｯ繝・・繝槭く繝ｼ縺ｨ `ThemeDescriptor` 繧呈ｸ｡縺吶◆繧√∫峡閾ｪ繝・・繝槭・驕ｩ逕ｨ繧・レ譎ｯ濶ｲ螟画峩繧定｡後∴縺ｾ縺吶ゅ色:Application/IModule.cs窶L336-L346縲・

### 7.3 陬懷勧繧ｦ繧｣繝ｳ繝峨え
`OnAuxiliaryWindowCatalogBuilding` 縺ｧ縺ｯ `AuxiliaryWindowDescriptor` 繧堤匳骭ｲ縺吶ｋ縺ｨ縲後え繧｣繝ｳ繝峨え縲阪Γ繝九Η繝ｼ縺九ｉ髢九￠繧九し繝悶ヵ繧ｩ繝ｼ繝繧呈署萓帙〒縺阪∪縺吶ゅ色:Application/IModule.cs窶L306-L334縲・

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

## 8. 閾ｪ蜍募・逅・(Auto Suicide / Auto Launch)

### 8.1 Auto Suicide
- `ModuleAutoSuicideRuleContext.Rules` 縺ｯ繝ｪ繧ｹ繝医→縺励※蜈ｬ髢九＆繧後※縺翫ｊ縲√Ν繝ｼ繝ｫ縺ｮ霑ｽ蜉繝ｻ蜑企勁繝ｻ繧ｽ繝ｼ繝医′蜿ｯ閭ｽ縺ｧ縺吶ゅ色:Application/IModule.cs窶L784-L800縲・
- `ModuleAutoSuicideDecisionContext.OverrideDecision` 繧貞他縺ｳ蜃ｺ縺吶→縲√い繝励Μ譛ｬ菴薙・蛻､螳夂ｵ先棡繧剃ｸ頑嶌縺阪〒縺阪∪縺吶ゅ色:Application/IModule.cs窶L804-L820縲・
- 螳溯｡後ち繧､繝溘Φ繧ｰ縺ｯ `ModuleHost` 縺・`AutoSuicideScheduled` 縺ｪ縺ｩ縺ｮ繧､繝吶Φ繝医ｒ逋ｺ轣ｫ縺励※騾夂衍縺励∪縺吶ゅ色:Infrastructure/ModuleHost.cs窶L221-L229縲・

### 8.2 Auto Launch
- `Program.Main` 縺ｯ繧ｳ繝槭Φ繝峨Λ繧､繝ｳ蠑墓焚繧・ｨｭ螳壹ヵ繧｡繧､繝ｫ縺九ｉ襍ｷ蜍募ｯｾ雎｡繧貞庶髮・＠縲√Δ繧ｸ繝･繝ｼ繝ｫ縺ｫ `ModuleAutoLaunchEvaluationContext` 繧帝壹§縺ｦ貂｡縺励∪縺吶ゅ色:Program.cs窶L129-L155縲・
- 繝｢繧ｸ繝･繝ｼ繝ｫ縺ｯ `Plans` 繧ｳ繝ｬ繧ｯ繧ｷ繝ｧ繝ｳ縺ｸ迢ｬ閾ｪ `AutoLaunchPlan` 繧定ｿｽ蜉縺吶ｋ縺薙→縺ｧ縲∵眠隕上い繝励Μ繧ｱ繝ｼ繧ｷ繝ｧ繝ｳ繧定ｵｷ蜍募ｯｾ雎｡縺ｫ蜷ｫ繧√ｉ繧後∪縺吶ゅ色:Application/IModule.cs窶L766-L787縲・
- 襍ｷ蜍募・逅・燕蠕後√♀繧医・螟ｱ謨玲凾縺ｫ縺ｯ `OnAutoLaunchStarting` / `OnAutoLaunchCompleted` / `OnAutoLaunchFailed` 縺悟他縺ｰ繧後∪縺吶ゅ色:Application/IModule.cs窶L270-L286縲代色:Program.cs窶L171-L195縲・

---

## 9. 繝｢繧ｸ繝･繝ｼ繝ｫ髢馴｣謳ｺ

`ModuleHost` 縺ｯ繝｢繧ｸ繝･繝ｼ繝ｫ蝗ｺ譛峨う繝吶Φ繝医ｒ蜿嶺ｿ｡縺励◆蠕後∽ｻ悶Δ繧ｸ繝･繝ｼ繝ｫ縺ｸ `OnPeerModule...` 邉ｻ縺ｮ繧ｳ繝ｼ繝ｫ繝舌ャ繧ｯ縺ｨ縺励※霆｢騾√＠縺ｾ縺吶ゅ色:Infrastructure/ModuleHost.cs窶L317-L393縲代％繧後ｒ蛻ｩ逕ｨ縺吶ｋ縺ｨ縲√Ο繝ｼ繝蛾・ｺ上ｄ險ｭ螳夐←逕ｨ繧ｿ繧､繝溘Φ繧ｰ繧貞鵠隱ｿ縺ｧ縺阪∪縺吶・

萓・ 險ｭ螳壽､懆ｨｼ邨先棡繧堤屮隕悶＠縲∝､ｱ謨励＠縺溘Δ繧ｸ繝･繝ｼ繝ｫ縺ｸ繝ｪ繝医Λ繧､隕∵ｱゅｒ騾√ｋ縲・
```csharp
public void OnPeerModuleSettingsValidationFailed(ModulePeerNotificationContext<ModuleSettingsValidationContext> context)
{
    if (context.Module.Module.ModuleName == "Sample.DependentModule")
    {
        // 萓晏ｭ倥Δ繧ｸ繝･繝ｼ繝ｫ縺ｮ險ｭ螳壼､ｱ謨励ｒ讀懃衍縺励◆髫帙・蜃ｦ逅・
    }
}
```

---

## 10. 繝ｭ繧ｮ繝ｳ繧ｰ縺ｨ險ｺ譁ｭ

### 10.1 繝ｭ繧ｰ縺ｮ蜿門ｾ・
- `IEventLogger` 縺ｯ `LogEvent(string source, string message, LogEventLevel level = LogEventLevel.Information)` 繧貞・髢九＠縺ｦ縺・∪縺吶ＡProgram.Main` 縺ｧ繧ｷ繝ｳ繧ｰ繝ｫ繝医Φ縺ｨ縺励※逋ｻ骭ｲ縺輔ｌ縺ｦ縺・ｋ縺溘ａ縲√さ繝ｳ繧ｹ繝医Λ繧ｯ繧ｿ繝ｼ繧､繝ｳ繧ｸ繧ｧ繧ｯ繧ｷ繝ｧ繝ｳ縺ｧ蜿門ｾ怜庄閭ｽ縺ｧ縺吶ゅ色:Program.cs窶L48-L63縲・
- `ModuleServiceRegistrationContext.Logger` 縺九ｉ繧ょ酔縺倥う繝ｳ繧ｹ繧ｿ繝ｳ繧ｹ縺ｸ繧｢繧ｯ繧ｻ繧ｹ縺ｧ縺阪∪縺吶ゅ色:Application/IModule.cs窶L483-L501縲・

### 10.2 繧､繝吶Φ繝医ヰ繧ｹ
- `IEventBus` 縺ｯ繝｢繧ｸ繝･繝ｼ繝ｫ髢馴壻ｿ｡繧・い繝励Μ蜀・う繝吶Φ繝郁ｳｼ隱ｭ縺ｫ蛻ｩ逕ｨ縺ｧ縺阪∪縺吶ＡModuleHost` 繧ょ酔縺倥ヰ繧ｹ繧貞茜逕ｨ縺励※ WebSocket/OSC/險ｭ螳壹↑縺ｩ縺ｮ繧､繝吶Φ繝医ｒ繝代ヶ繝ｪ繝・す繝･縺励※縺・∪縺吶ゅ色:Infrastructure/ModuleHost.cs窶L24-L47縲・
- 繝｢繧ｸ繝･繝ｼ繝ｫ縺ｯ `RegisterServices` 蜀・〒繝ｪ繧ｹ繝翫・繧堤匳骭ｲ縺励～Dispose` 縺悟ｿ・ｦ√↑蝣ｴ蜷医・繧ｷ繝ｳ繧ｰ繝ｫ繝医Φ繧ｵ繝ｼ繝薙せ蜀・〒繧ｯ繝ｪ繝ｼ繝ｳ繧｢繝・・蜃ｦ逅・ｒ螳溯｣・＠縺ｦ縺上□縺輔＞縲・

### 10.3 萓句､門・逅・
`OnUnhandledException` 縺ｯ繧｢繝励Μ繧ｱ繝ｼ繧ｷ繝ｧ繝ｳ蜈ｨ菴薙〒謐墓拷縺輔ｌ縺ｪ縺九▲縺滉ｾ句､悶ｒ蜿励￠蜿悶ｊ縺ｾ縺吶ゅ色:Application/IModule.cs窶L150-L160縲代％縺薙〒繧ｯ繝ｩ繝・す繝･繝ｬ繝昴・繝磯∽ｿ｡繧・Θ繝ｼ繧ｶ繝ｼ騾夂衍繧貞ｮ溯｣・〒縺阪∪縺吶・

---

## 11. 繝・せ繝医→繝・ヰ繝・げ縺ｮ繝ｯ繝ｼ繧ｯ繝輔Ο繝ｼ

1. 繝｢繧ｸ繝･繝ｼ繝ｫ DLL 繧偵ン繝ｫ繝峨＠ `Modules` 繝輔か繝ｫ繝繝ｼ縺ｸ驟咲ｽｮ (MSBuild 繧ｿ繝ｼ繧ｲ繝・ヨ縺後≠繧句ｴ蜷医・閾ｪ蜍・縲・
2. ToNRoundCounter 繧偵ョ繝舌ャ繧ｰ襍ｷ蜍輔＠縲√さ繝ｳ繧ｽ繝ｼ繝ｫ繝ｭ繧ｰ縺翫ｈ縺ｳ險ｭ螳夂判髱｢縺ｧ繝｢繧ｸ繝･繝ｼ繝ｫ縺瑚ｪ崎ｭ倥＆繧後※縺・ｋ縺狗｢ｺ隱阪＠縺ｾ縺吶ＡModuleHost.Modules` 縺ｧ逋ｻ骭ｲ迥ｶ豕√ｒ遒ｺ隱阪☆繧九ョ繝舌ャ繧ｰ繝薙Η繝ｼ繧剃ｽ懈・縺吶ｋ縺薙→繧ゅ〒縺阪∪縺吶ゅ色:Infrastructure/ModuleHost.cs窶L269-L276縲・
3. `IEventLogger` 縺ｮ蜃ｺ蜉帙ｄ `logs` 繝・ぅ繝ｬ繧ｯ繝医Μ繧偵メ繧ｧ繝・け縺励∽ｾ句､悶′逋ｺ逕溘＠縺ｦ縺・↑縺・°遒ｺ隱阪＠縺ｾ縺吶・
4. 蠢・ｦ√↓蠢懊§縺ｦ蜊倅ｽ薙ユ繧ｹ繝医・繝ｭ繧ｸ繧ｧ繧ｯ繝・(xUnit 縺ｪ縺ｩ) 繧偵Δ繧ｸ繝･繝ｼ繝ｫ繧ｽ繝ｪ繝･繝ｼ繧ｷ繝ｧ繝ｳ蜀・↓霑ｽ蜉縺励√Δ繧ｸ繝･繝ｼ繝ｫ縺ｮ繧ｵ繝ｼ繝薙せ繧ｯ繝ｩ繧ｹ繧貞句挨縺ｫ繝・せ繝医＠縺ｾ縺吶・

---

## 12. 驟榊ｸ・→繝舌・繧ｸ繝ｧ繝九Φ繧ｰ

- 繝｢繧ｸ繝･繝ｼ繝ｫ DLL 縺ｨ萓晏ｭ・DLL 繧偵∪縺ｨ繧√※驟榊ｸ・＠縲√Θ繝ｼ繧ｶ繝ｼ縺ｫ `Modules` 繝輔か繝ｫ繝繝ｼ縺ｸ繧ｳ繝斐・縺励※繧ゅｉ縺・∪縺吶ＡModuleLoader` 縺ｯ DLL 蜷阪↓蛻ｶ髯舌ｒ隱ｲ縺励※縺・∪縺帙ｓ縲ゅ色:Infrastructure/ModuleLoader.cs窶L25-L49縲・
- 繝舌・繧ｸ繝ｧ繝ｳ邂｡逅・↓縺ｯ繧｢繧ｻ繝ｳ繝悶Μ諠・ｱ (`AssemblyVersion` / `AssemblyFileVersion`) 繧呈ｴｻ逕ｨ縺励～OnModuleLoaded` 縺ｧ繝舌・繧ｸ繝ｧ繝ｳ繧偵Ο繧ｰ縺ｫ險倬鹸縺吶ｋ縺ｨ譖ｴ譁ｰ遒ｺ隱阪′螳ｹ譏薙↓縺ｪ繧翫∪縺吶ゅ色:Application/IModule.cs窶L18-L40縲・
- 莠呈鋤諤ｧ縺ｮ縺ｪ縺・峩譁ｰ繧定｡後≧蝣ｴ蜷医・縲～OnBeforeServiceRegistration` 縺ｧ繝帙せ繝医・繝舌・繧ｸ繝ｧ繝ｳ繧・ｾ晏ｭ倥Δ繧ｸ繝･繝ｼ繝ｫ縺ｮ蟄伜惠繧呈､懈渊縺励∝､ｱ謨玲凾縺ｫ縺ｯ萓句､悶ｒ謚輔￡縺ｦ隱ｭ縺ｿ霎ｼ縺ｿ繧剃ｸｭ譁ｭ縺ｧ縺阪∪縺吶・

---

## 13. 髢狗匱繝√ぉ繝・け繝ｪ繧ｹ繝・

- [ ] `IModule` 繧貞ｮ溯｣・＠縲∫ｩｺ螳溯｣・ｂ蜷ｫ繧√※蠢・ｦ√↑繝ｩ繧､繝輔し繧､繧ｯ繝ｫ繝｡繧ｽ繝・ラ繧呈紛逅・＠縺・
- [ ] `RegisterServices` 縺ｧ蠢・ｦ√↑繧ｵ繝ｼ繝薙せ繧堤匳骭ｲ縺励√せ繝ｬ繝・ラ繧ｻ繝ｼ繝輔↑繝ｩ繧､繝輔ち繧､繝繧帝∈謚槭＠縺・
- [ ] 險ｭ螳壼､縺ｮ隱ｭ縺ｿ譖ｸ縺阪→讀懆ｨｼ (`OnSettings*`) 繧貞ｮ溯｣・＠縲√Θ繝ｼ繧ｶ繝ｼ縺ｸ繝輔ぅ繝ｼ繝峨ヰ繝・け縺ｧ縺阪ｋ繧医≧縺ｫ縺励◆
- [ ] UI 諡｡蠑ｵ繝昴う繝ｳ繝・(繝｡繝九Η繝ｼ縲∬｣懷勧繧ｦ繧｣繝ｳ繝峨え縲√ユ繝ｼ繝・ 縺ｮ蠢・ｦ∵ｧ繧堤｢ｺ隱阪＠螳溯｣・＠縺・
- [ ] WebSocket / OSC / 閾ｪ蜍募・逅・う繝吶Φ繝医∈縺ｮ蟇ｾ蠢懊′蠢・ｦ√°讀懆ｨ弱＠縲∝ｿ・ｦ√↑繝上Φ繝峨Λ繝ｼ繧貞ｮ溯｣・＠縺・
- [ ] 繝ｭ繧ｰ縺ｨ繧､繝吶Φ繝医ヰ繧ｹ繧呈ｴｻ逕ｨ縺励√ヨ繝ｩ繝悶Ν繧ｷ繝･繝ｼ繝・ぅ繝ｳ繧ｰ縺ｫ蜊∝・縺ｪ諠・ｱ繧呈ｮ九☆繧医≧縺ｫ縺励◆
- [ ] 繝｢繧ｸ繝･繝ｼ繝ｫ DLL 縺ｮ驟榊ｸ・・譖ｴ譁ｰ謇矩・ｒ譁・嶌蛹悶＠縲√Θ繝ｼ繧ｶ繝ｼ蜷代￠繝ｪ繝ｪ繝ｼ繧ｹ繝弱・繝医ｒ貅門ｙ縺励◆

---

## 14. 蜿り・ｮ溯｣・

`AfkJumpModule` 縺ｯ AFK 隴ｦ蜻翫う繝吶Φ繝医ｒ繝輔ャ繧ｯ縺励※繧ｸ繝｣繝ｳ繝怜・蜉帙ｒ騾∽ｿ｡縺吶ｋ繝｢繧ｸ繝･繝ｼ繝ｫ縺ｧ縲∽ｻ･荳九・繝昴う繝ｳ繝医′蜿り・↓縺ｪ繧翫∪縺吶・

- `IAfkWarningHandler` 繧・DI 縺ｧ逋ｻ骭ｲ縺励、FK 隴ｦ蜻翫ｒ鄂ｮ縺肴鋤縺医※縺・ｋ縲ゅ色:Modules/AfkJumpModule/AfkJumpModule.cs窶L23-L26縲・
- 險ｭ螳壹ン繝･繝ｼ縺ｫ隱ｬ譏弱Λ繝吶Ν繧定ｿｽ蜉縺吶ｋ `OnSettingsViewBuilding` 縺ｮ萓九ゅ色:Modules/AfkJumpModule/AfkJumpModule.cs窶L72-L87縲・
- OSC 騾∽ｿ｡蜃ｦ逅・〒 `Rug.Osc` 繧呈ｴｻ逕ｨ縺励√お繝ｩ繝ｼ譎ゅ↓ `IEventLogger` 縺ｸ蜃ｺ蜉帙＠縺ｦ縺・ｋ縲ゅ色:Modules/AfkJumpModule/AfkJumpModule.cs窶L337-L363縲・

縺薙ｌ繧峨ｒ繝・Φ繝励Ξ繝ｼ繝医→縺励※縲√Δ繧ｸ繝･繝ｼ繝ｫ蝗ｺ譛峨・繧ｵ繝ｼ繝薙せ・酋I・剰・蜍募喧繝ｭ繧ｸ繝・け繧堤ｵ・∩蜷医ｏ縺帙ｋ縺薙→縺ｧ縲ゝoNRoundCounter 繧呈沐霆溘↓諡｡蠑ｵ縺ｧ縺阪∪縺吶・

---

## 15. 繝｢繧ｸ繝･繝ｼ繝ｫ逕ｨ Sound API (IModuleSoundApi)

繝｢繧ｸ繝･繝ｼ繝ｫ縺九ｉ髻ｳ螢ｰ繧貞・逕溘☆繧九◆繧√・蜈ｬ髢九ヵ繧｡繧ｵ繝ｼ繝峨〒縺吶ＡISoundManager` 繧堤峩謗･菴ｿ縺・ｻ｣繧上ｊ縺ｫ縲∝ｮ牙ｮ・API 縺ｨ縺励※ `IModuleSoundApi` 繧・DI 縺九ｉ蜿門ｾ励☆繧九％縺ｨ繧呈耳螂ｨ縺励∪縺吶・
### 15.1 蜿門ｾ玲婿豕・
`RegisterServices` 莉･髯阪～IServiceProvider` 縺九ｉ蜿門ｾ励〒縺阪∪縺吶・
```csharp
public void OnAfterServiceProviderBuild(ModuleServiceProviderContext context)
{
    var sound = context.ServiceProvider.GetRequiredService<IModuleSoundApi>();
    // sound.Play(\"C:\\sound\\alert.wav\", volume: 0.8);
}
```

### 15.2 繝｡繧ｽ繝・ラ

| 繝｡繝ｳ繝・| 隱ｬ譏・|
| --- | --- |
| `Play(pathOrUrl, volume, loop)` | 蜊倅ｸ縺ｮ繝ｭ繝ｼ繧ｫ繝ｫ繝輔ぃ繧､繝ｫ繧ゅ＠縺上・ YouTube URL (`youtube.com` / `youtu.be`) 繧貞・逕溘＠縺ｾ縺吶ＡIDisposable` 繧・`Dispose()` 縺吶ｋ縺ｨ蛛懈ｭ｢縺励∪縺吶・|
| `PlayPlaylist(pathsOrUrls, volume, loop)` | 隍・焚繝医Λ繝・け繧帝・ｬ｡蜀咲函縺励∪縺吶Ａloop=true` 縺ｧ蜈磯ｭ縺ｫ謌ｻ縺｣縺ｦ郢ｰ繧願ｿ斐＠縺ｾ縺吶・|
| `GetCurrentMasterVolume()` | 迴ｾ蝨ｨ縺ｮ譛牙柑繝槭せ繧ｿ繝ｼ髻ｳ驥・(0.0縲・.0)縲ゅ・繧ｹ繧ｿ繝ｼ繝溘Η繝ｼ繝井ｸｭ縺ｯ 0 繧定ｿ斐＠縺ｾ縺吶・|
| `IsMasterMuted` | 繝槭せ繧ｿ繝ｼ繝溘Η繝ｼ繝育憾諷九・|

### 15.3 莉墓ｧ倥Γ繝｢

- `volume` 縺ｯ繝槭せ繧ｿ繝ｼ髻ｳ驥上→荵礼ｮ励＆繧後√・繧ｹ繧ｿ繝ｼ繝溘Η繝ｼ繝域凾縺ｯ辟｡髻ｳ縺ｫ縺ｪ繧翫∪縺吶・- YouTube URL 縺ｯ蛻晏屓蛻ｩ逕ｨ譎ゅ↓ `%LOCALAPPDATA%/ToNRoundCounter/yt-cache` 縺ｸ髱槫酔譛溘ム繧ｦ繝ｳ繝ｭ繝ｼ繝峨＆繧後∪縺吶ゅム繧ｦ繝ｳ繝ｭ繝ｼ繝牙ｮ御ｺ・燕縺ｯ蠖楢ｩｲ繝医Λ繝・け縺後せ繧ｭ繝・・縺輔ｌ縲∝ｮ御ｺ・ｾ後↓繧｢繝励Μ蛛ｴ縺ｮ繝ｩ繧ｦ繝ｳ繝・繧｢繧､繝・Β BGM 縺ｧ縺ｯ閾ｪ蜍慕噪縺ｫ蜀阪Ο繝ｼ繝峨＆繧後∪縺吶ゅΔ繧ｸ繝･繝ｼ繝ｫ縺九ｉ `Play` 繧貞他繧薙□蝣ｴ蜷医∵悴繝繧ｦ繝ｳ繝ｭ繝ｼ繝臥憾諷九□縺ｨ辟｡髻ｳ `IDisposable` 縺瑚ｿ斐＆繧後ｋ縺溘ａ縲∝ｿ・ｦ√↑繧画凾髢薙ｒ鄂ｮ縺・※蜀崎ｩｦ陦後＠縺ｦ縺上□縺輔＞縲・- 蜃ｺ蜉帙ョ繝舌う繧ｹ縺ｨ繧､繧ｳ繝ｩ繧､繧ｶ繝ｼ險ｭ螳壹・繧｢繝励Μ縺ｮ險ｭ螳壼､縺悟・騾壹〒驕ｩ逕ｨ縺輔ｌ縺ｾ縺吶・- 謌ｻ繧雁､縺ｯ蠢・★ `Dispose()` 縺励※縺上□縺輔＞縲Ａusing` 縺ｧ蝗ｲ縺・°縲・聞譛滉ｿ晄戟縺吶ｋ蝣ｴ蜷医・繝｢繧ｸ繝･繝ｼ繝ｫ邨ゆｺ・(`OnApplicationExiting`) 縺ｧ隗｣謾ｾ縺励∪縺吶・

