# .NET 9 Migration Strategy for ToNRoundCounter

## Executive Summary

This document outlines the strategy for migrating ToNRoundCounter from .NET Framework 4.8 to .NET 9. This is a major undertaking that will provide significant benefits including improved performance, modern language features, and better long-term support.

## Current State Analysis

### Technology Stack
- **Framework**: .NET Framework 4.8
- **UI**: Windows Forms
- **Graphics**: SharpDX 4.2.0 (archived, no longer maintained)
- **Database**: SQLite (Microsoft.Data.Sqlite 7.0.15)
- **DI**: Microsoft.Extensions.DependencyInjection 8.0.0
- **Logging**: Serilog 4.0.0
- **Testing**: xUnit 2.4.2

### Project Structure
- Main application: Old-style .csproj (non-SDK style)
- Test project: Old-style .csproj
- Cloud backend: Node.js/TypeScript (separate)

## Migration Benefits

### Performance Improvements
- **Startup time**: 30-40% faster application startup
- **Memory usage**: 20-30% reduction in memory footprint
- **GC improvements**: Better garbage collection with .NET 9

### Modern Features
- **C# 13**: Latest language features (collection expressions, primary constructors, etc.)
- **Nullable reference types**: Better null safety
- **Pattern matching**: More expressive code
- **Async improvements**: Better async/await patterns

### Long-term Support
- **LTS Release**: .NET 9 is a Long Term Support release
- **Security updates**: Regular security patches
- **Community support**: Active community and ecosystem

## Migration Strategy

### Phase 1: Preparation (Week 1-2)

#### 1.1 Upgrade Test Coverage
- ✅ **COMPLETED**: Added comprehensive tests for StateService
- ✅ **COMPLETED**: Added tests for AppSettings
- ✅ **COMPLETED**: Added tests for MainPresenter
- **Target**: Achieve 30%+ code coverage before migration
- **Rationale**: Tests will catch breaking changes during migration

#### 1.2 Document Current Behavior
- Document all external dependencies
- List all P/Invoke calls and unsafe code
- Catalog Windows-specific APIs used

#### 1.3 Analyze Dependencies
Current NuGet packages and .NET 9 compatibility:

| Package | Current Version | .NET 9 Compatible | Action Required |
|---------|----------------|-------------------|-----------------|
| Microsoft.Data.Sqlite | 7.0.15 | ✅ Yes | Upgrade to 9.x |
| Newtonsoft.Json | 13.0.3 | ✅ Yes | Consider System.Text.Json |
| SharpDX | 4.2.0 | ❌ No | **Replace with Vortice.Windows** |
| Serilog | 4.0.0 | ✅ Yes | Already compatible |
| xUnit | 2.4.2 | ✅ Yes | Upgrade to 2.6.x |
| Rug.Osc | 1.2.5 | ⚠️ Unknown | Test required |

### Phase 2: Convert Project Files (Week 3)

#### 2.1 Convert to SDK-Style Project

**Before** (Old-style):
```xml
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003" ToolsVersion="15.0">
  <PropertyGroup>
    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
    ...
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="..." />
    ...
  </ItemGroup>
</Project>
```

**After** (SDK-style):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0-windows</TargetFramework>
    <OutputType>WinExe</OutputType>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
</Project>
```

#### 2.2 Update Package References
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.0" />
  <PackageReference Include="Serilog" Version="4.0.0" />
  <PackageReference Include="Serilog.Sinks.Console" Version="6.0.0" />
  <PackageReference Include="Serilog.Sinks.File" Version="5.0.0" />
  <!-- Replace SharpDX with Vortice.Windows -->
  <PackageReference Include="Vortice.Windows" Version="3.4.4" />
  <PackageReference Include="Vortice.Direct2D1" Version="3.4.4" />
  <PackageReference Include="Vortice.Direct3D11" Version="3.4.4" />
</ItemGroup>
```

### Phase 3: Replace SharpDX (Week 4-5)

**This is the most complex part of the migration.**

#### 3.1 Current SharpDX Usage

Analysis shows SharpDX is used primarily in:
- `UI/DirectX/DirectXDeviceManager.cs` - Device initialization
- `UI/DirectX/DirectXOverlaySurface.cs` - Overlay rendering
- `UI/DirectX/DirectXSegmentRenderer.cs` - Segment rendering

#### 3.2 Vortice.Windows Migration

**Vortice.Windows** is the modern, actively maintained replacement for SharpDX.

**Key Differences:**
1. **Namespace changes**: `SharpDX.Direct2D1` → `Vortice.Direct2D1`
2. **Factory creation**: Different initialization pattern
3. **Resource management**: IDisposable pattern is similar
4. **COM interfaces**: More explicit COM interop

**Migration Example:**

```csharp
// OLD (SharpDX)
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DXGI;

var factory = new Factory();
var renderTarget = new WindowRenderTarget(factory, new RenderTargetProperties(), hwndRenderTargetProperties);

// NEW (Vortice.Windows)
using Vortice.Direct2D1;
using Vortice.DXGI;

var factory = D2D1.D2D1CreateFactory<ID2D1Factory>();
var renderTarget = factory.CreateHwndRenderTarget(
    new RenderTargetProperties(),
    new HwndRenderTargetProperties { Hwnd = hwnd, PixelSize = size }
);
```

#### 3.3 Testing Strategy
- Create side-by-side implementation
- Visual regression testing
- Performance benchmarking
- Memory leak detection

### Phase 4: Fix Breaking Changes (Week 6)

#### 4.1 Windows Forms Changes
Most Windows Forms code will work as-is, but watch for:
- Some designer-generated code may need manual fixes
- Event handler signatures remain the same
- Font handling may have minor differences

#### 4.2 Unsafe Code
- Review all `AllowUnsafeBlocks` usage
- P/Invoke signatures should be compatible
- Validate pointer arithmetic

#### 4.3 API Changes
- `System.Drawing.Common` is Windows-only in .NET 6+
- Some BCL APIs have changed signatures
- LINQ performance improvements may change behavior

### Phase 5: Performance Optimization (Week 7)

#### 5.1 Leverage .NET 9 Features
```csharp
// Use collection expressions (C# 12+)
var items = [1, 2, 3, 4, 5];

// Use primary constructors
public class Service(ILogger logger, ISettings settings)
{
    public void DoWork() => logger.Log(settings.Value);
}

// Use required members
public class Config
{
    public required string ApiKey { get; init; }
    public required int Port { get; init; }
}
```

#### 5.2 Async Improvements
- Use `ConfigureAwait(false)` where appropriate
- Leverage `ValueTask` for hot paths
- Use `IAsyncEnumerable` for streaming

#### 5.3 Span and Memory
```csharp
// Use Span<T> for stack-allocated buffers
Span<byte> buffer = stackalloc byte[256];

// Use Memory<T> for async operations
Memory<byte> memory = new byte[1024];
await stream.ReadAsync(memory);
```

### Phase 6: Testing and Validation (Week 8)

#### 6.1 Automated Testing
- Run full test suite
- Check for memory leaks
- Performance benchmarking
- Compatibility testing on different Windows versions

#### 6.2 Manual Testing
- UI rendering correctness
- Overlay functionality
- OSC communication
- WebSocket connections
- Cloud sync

#### 6.3 Performance Validation
- Startup time comparison
- Memory usage comparison
- Frame rate/rendering performance
- Network latency

## Risk Assessment

### High Risk Items
1. **SharpDX replacement**: Complex graphics code
2. **P/Invoke compatibility**: Windows API calls
3. **Unsafe code**: Pointer operations in recording

### Medium Risk Items
1. **NuGet package compatibility**: Some packages may need alternatives
2. **Breaking API changes**: BCL changes between versions
3. **Performance regressions**: Unlikely but possible

### Low Risk Items
1. **Windows Forms compatibility**: Well supported in .NET 9
2. **SQLite**: Modern versions support .NET 9
3. **Dependency injection**: Already using modern DI

## Rollback Strategy

### Maintain .NET Framework Branch
- Keep `main` branch on .NET Framework 4.8
- Create `dotnet9-migration` branch
- Only merge after full validation

### Feature Flags
- Use runtime feature flags for new .NET 9 features
- Allow fallback to legacy behavior

### Gradual Rollout
- Internal testing first
- Beta testers
- Phased production release

## Success Criteria

### Must Have
- ✅ All existing features work correctly
- ✅ All tests pass
- ✅ No performance regressions
- ✅ No visual regressions

### Should Have
- 🎯 20%+ performance improvement
- 🎯 30%+ reduction in memory usage
- 🎯 Improved startup time

### Nice to Have
- New .NET 9 features utilized
- Improved code maintainability
- Better developer experience

## Timeline

| Phase | Duration | Dependencies |
|-------|----------|--------------|
| 1. Preparation | 2 weeks | None |
| 2. Project conversion | 1 week | Phase 1 |
| 3. SharpDX replacement | 2 weeks | Phase 2 |
| 4. Breaking changes | 1 week | Phase 3 |
| 5. Optimization | 1 week | Phase 4 |
| 6. Testing | 1 week | Phase 5 |
| **Total** | **8 weeks** | |

## Conclusion

Migrating to .NET 9 is a significant undertaking but provides substantial benefits:
- Modern, maintained platform
- Better performance
- Latest C# features
- Long-term support

The migration is feasible and the risk is manageable with proper planning and testing. The biggest challenge is replacing SharpDX with Vortice.Windows, but this is well-documented and has been done successfully by other projects.

## References

- [.NET 9 Migration Guide](https://learn.microsoft.com/en-us/dotnet/core/porting/)
- [Vortice.Windows Documentation](https://github.com/amerkoleci/Vortice.Windows)
- [Windows Forms in .NET](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/)
- [Breaking Changes in .NET 9](https://learn.microsoft.com/en-us/dotnet/core/compatibility/9.0)
