# CompatPack v1 双轴版本、组合与发布门禁 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 按已批准的 spec 实现 `contractAbi` + `engineApi` 双轴、v1 ABI 冻结规则、显式包依赖/覆盖/优先级、严格符号闭包，以及 P-D 与社区包发布硬门禁。

**Architecture:** 契约程序集只定义稳定的包作者面；Core loader 只在加载期执行一次包贡献并冻结不可变 delta；composition resolver 负责依赖图、capability 闭包、冲突和覆盖；legacy bridge 只消费最终 plan，不允许静默回退到全量 registry。

**Tech Stack:** C# / .NET 8+9、xUnit、PowerShell gate scripts、Godot .NET、legacy Emuera bridge。

**Spec:** `docs/superpowers/specs/2026-11-02-compatpack-v1-contract-versioning-composition-design.md`

## Global Constraints

- 社区包是可信本地代码；ALC 不是安全沙箱，不实现进程隔离。
- v1 承诺为 1.x 成员级二进制兼容；破坏性契约变更升 major。
- manifest 新格式必须同时声明 `contractAbi` 和 `engineApi`，格式为 `major.minor`。
- `targetEngineApi: 1` 只作为 legacy 迁移字段；与新型字段混用/reject，v2 major 删除。
- v1 `baseProfileId` 必须是 `v24pure`。
- 包组合允许 `requires`、`conflicts`、`overrides`、`priority`；没有显式 override 的冲突必须拒载。
- priority 只仲裁显式 override，不能把 conflicts 覆盖成通过。
- v24 基线 handler 只能通过 `variantSelections` / `builtin:*` 变体通道改变，不允许包直接 override baseline instruction/function。
- 加载期贡献只执行一次；组装期只读 `ValidatedPackDelta`。
- 运行时 registry 与最终 plan 必须精确闭包；禁止 `BindCompatibilityPlan` 静默回退全量 registry。
- 官方 v18 包和社区模板必须迁移到新版本字段，并有有效 `baseSurfaceHash`。
- Gate 1-7 全绿前，P-D 内置模块退役和社区包公开发布均保持锁定。

## Review Focus

- legacy `targetEngineApi: 1` 必须映射到 `1.0/1.0`；混合字段必须明确拒载，不能被静默忽略。
- 包 A `requires` 包 B 时，B 缺失、版本不满足、循环依赖都必须整组拒载，且错误包含 packId 和版本范围。
- 两个包覆盖同一 symbol 时，只有显式 override 的唯一最高 priority 可以生效；同 priority 必须拒载。
- `hideInstructions:["ABS"]` 不能让 METHOD 投影继续可见；`addInstructions:["ACOS"]` 不能在函数面未打开时静默零效果。
- Android/真实 ALC 回收/旧 facade 包必须在目标环境有证据，不能仅以桌面 xUnit 代替发布结论。

---

### Task 1: 双轴版本模型与 manifest 解析

**Files:**
- Create: `src/EmueraCompatPack/CompatVersion.cs`
- Modify: `src/EmueraCompatPack/CompatPackManifest.cs`
- Modify: `src/Core/Compatibility/Packs/CompatPackValidation.cs`
- Modify: `src/Core/Compatibility/Packs/CompatPackRules.cs`
- Modify: `scripts/Emuera/Compatibility/CompatPackHost.cs`
- Test: `tests/xUnitTest/EmueraFacade.Tests/CompatPackManifestTests.cs`
- Test: `tests/xUnitTest/GEmuera.Core.Tests/CompatPackRulesTests.cs`

**Interfaces:**
- Create `public readonly record struct CompatVersion(int Major, int Minor)` with:
  - `static bool TryParse(string? value, out CompatVersion version)`
  - `bool CanRunOn(CompatVersion engineVersion)`，语义为 major 相等且 minor <= engine minor
  - `ToString()` 返回 `"major.minor"`
- `CompatPackManifest` 新增：
  - `CompatVersion ContractAbi { get; }`
  - `CompatVersion EngineApi { get; }`
  - `bool UsedLegacyTargetEngineApi { get; }`
- `CompatPackValidationContext` 将 `EngineModuleApiVersion` 替换为：
  - `CompatVersion CurrentContractAbi { get; }`
  - `CompatVersion CurrentEngineApi { get; }`
- `CompatPackRules.Validate` 用 `Manifest.ContractAbi.CanRunOn(context.CurrentContractAbi)` 和 `Manifest.EngineApi.CanRunOn(context.CurrentEngineApi)` 检查版本。

- [ ] **Step 1: 写失败测试：解析双轴字段**

在 `CompatPackManifestTests.cs` 增加：

```csharp
[Fact]
public void Parse_DualVersionAxes_AreRequiredTogether()
{
    const string json = """
    {
      "packId": "test.dual",
      "packVersion": "1.0.0",
      "contractAbi": "1.2",
      "engineApi": "1.3",
      "baseProfileId": "v24pure"
    }
    """;

    Assert.True(CompatPackManifest.TryParse(json, out var manifest, out var errors));
    Assert.Empty(errors);
    Assert.Equal(new CompatVersion(1, 2), manifest!.ContractAbi);
    Assert.Equal(new CompatVersion(1, 3), manifest.EngineApi);
    Assert.False(manifest.UsedLegacyTargetEngineApi);
}
```

同时增加 `Parse_LegacyTargetEngineApiOnly1_MapsToDualAxes` 和 `Parse_TargetEngineApiMixedWithDualAxes_Rejects`。

- [ ] **Step 2: 写失败测试：版本比较规则**

在 `CompatPackRulesTests.cs` 增加 `Validate_ContractMinorHigherThanEngine_Rejects`、`Validate_EngineMinorHigherThanEngine_Rejects`、`Validate_MajorMismatch_Rejects`、`Validate_BaseProfileNotV24Pure_Rejects`。

- [ ] **Step 3: 运行测试确认失败**

Run:

```powershell
dotnet test tests\xUnitTest\EmueraFacade.Tests\EmueraFacade.Tests.csproj -c Release --filter "FullyQualifiedName~CompatPackManifestTests"
dotnet test tests\xUnitTest\GEmuera.Core.Tests\GEmuera.Core.Tests.csproj -c Release --filter "FullyQualifiedName~CompatPackRulesTests"
```

Expected: FAIL，缺少 `CompatVersion`、双轴属性或新比较规则。

- [ ] **Step 4: 实现版本模型和解析**

实现 `CompatVersion`：

```csharp
public readonly record struct CompatVersion(int Major, int Minor)
{
    public static bool TryParse(string? value, out CompatVersion version);
    public bool CanRunOn(CompatVersion engineVersion);
    public override string ToString();
}
```

在 `CompatPackManifest.TryParse` 中：

- 新格式必须同时存在 `contractAbi` / `engineApi`；
- 旧格式只接受 `targetEngineApi: 1`，映射为 `1.0` / `1.0`；
- 新旧字段混用拒载；
- 其他 legacy 值拒载；
- 未知字段仍整体拒载。

- [ ] **Step 5: 接入 context/rules/host**

在 `CompatPackHost` 中提供当前值：

```csharp
internal static readonly CompatVersion CurrentContractAbi = new(1, 0);
internal static readonly CompatVersion CurrentEngineApi = new(1, 0);
```

`BuildValidationContext` 使用这两个值；`CompatPackRules` 使用 `CanRunOn`，错误文案指出是 contract 还是 engine 轴；同时显式拒载 `baseProfileId != "v24pure"` 的包。

- [ ] **Step 6: 运行测试确认通过**

Run:

```powershell
dotnet test tests\xUnitTest\EmueraFacade.Tests\EmueraFacade.Tests.csproj -c Release --filter "FullyQualifiedName~CompatPackManifestTests"
dotnet test tests\xUnitTest\GEmuera.Core.Tests\GEmuera.Core.Tests.csproj -c Release --filter "FullyQualifiedName~CompatPackRulesTests"
```

Expected: PASS。

- [ ] **Step 7: Commit**

```powershell
git add src/EmueraCompatPack/CompatVersion.cs src/EmueraCompatPack/CompatPackManifest.cs src/Core/Compatibility/Packs/CompatPackValidation.cs src/Core/Compatibility/Packs/CompatPackRules.cs scripts/Emuera/Compatibility/CompatPackHost.cs tests/xUnitTest/EmueraFacade.Tests/CompatPackManifestTests.cs tests/xUnitTest/GEmuera.Core.Tests/CompatPackRulesTests.cs
git commit -m "feat(compatpack): add contractAbi and engineApi version axes"
```

---

### Task 2: Manifest schema 与官方包迁移到双轴字段

**Files:**
- Modify: `src/EmueraCompatPack/compatpack.manifest.schema.json`
- Modify: `packs/gemuera.v18/compatpack.manifest.json`
- Modify: `packs/CommunityPackTemplate/compatpack.manifest.json`
- Modify: `tools/compat-pack/Generate-V18PackManifest/Program.cs`
- Test: `tests/xUnitTest/GEmuera.Core.Tests/V18PackManifestTests.cs`
- Test: `tests/xUnitTest/GEmuera.Core.Tests/CommunityPackTemplateTests.cs`
- Test: `tests/xUnitTest/EmueraFacade.Tests/CompatPackManifestTests.cs`

**Interfaces:**
- Schema `$id` 变为 `gemuera:compatpack:manifest:schema:v1.1`。
- Schema 顶层 `required` 只保留 `packId`、`packVersion`。
- Schema 顶层 `oneOf`：
  - 新格式要求 `contractAbi`、`engineApi`，禁止 `targetEngineApi`；
  - legacy 格式要求 `targetEngineApi` 为 `1`，禁止 `contractAbi`、`engineApi`。
- `additionalProperties` 仍为 `false`。

- [ ] **Step 1: 写失败测试：官方 manifest 使用双轴**

在 `V18PackManifestTests.cs` 增加断言：

```csharp
Assert.Equal(new CompatVersion(1, 0), handle.Manifest.ContractAbi);
Assert.Equal(new CompatVersion(1, 0), handle.Manifest.EngineApi);
Assert.False(handle.Manifest.UsedLegacyTargetEngineApi);
```

在 `CommunityPackTemplateTests.cs` 做同样断言。

- [ ] **Step 2: 写失败测试：schema 同时承认双轴和 legacy**

在 `CompatPackManifestTests.cs` 增加一个读取 schema JSON 的测试：

```csharp
[Fact]
public void Schema_DeclaresDualAxisAndLegacyBranches()
{
    string path = Path.Combine( FindRepositoryRoot(), "src", "EmueraCompatPack", "compatpack.manifest.schema.json");
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    JsonElement properties = document.RootElement.GetProperty("properties");
    Assert.True(properties.TryGetProperty("contractAbi", out _));
    Assert.True(properties.TryGetProperty("engineApi", out _));
    Assert.True(properties.TryGetProperty("targetEngineApi", out _));
}
```

`FindRepositoryRoot` 可从当前测试程序集向上查找 `AGENTS.md`；不要硬编码机器路径。

- [ ] **Step 3: 运行测试确认失败**

Run:

```powershell
dotnet test tests\xUnitTest\GEmuera.Core.Tests\GEmuera.Core.Tests.csproj -c Release --filter "FullyQualifiedName~V18PackManifestTests|FullyQualifiedName~CommunityPackTemplateTests"
```

Expected: FAIL，官方 manifest 缺少双轴字段。

- [ ] **Step 4: 更新 schema**

在 schema 中为 `contractAbi`、`engineApi` 增加 `major.minor` pattern，保留 `targetEngineApi` 但限制 legacy 分支，增加 `oneOf`，确保未识别字段仍被拒绝。

- [ ] **Step 5: 更新生成器与官方 manifest**

`Generate-V18PackManifest/Program.cs` 输出的 `BuildManifestJson` 改为：

```json
{
  "packId": "pack.gemuera.v18",
  "packVersion": "1.0.0",
  "contractAbi": "1.0",
  "engineApi": "1.0",
  "baseProfileId": "v24pure",
  "capabilities": [],
  "surface": { ... }
}
```

不要输出 `targetEngineApi`。更新 `packs/CommunityPackTemplate/compatpack.manifest.json` 同样删除 `targetEngineApi`，增加双轴字段。

- [ ] **Step 6: 重新生成并跑测试**

Run:

```powershell
dotnet run --project tools\compat-pack\Generate-V18PackManifest -- .
dotnet test tests\xUnitTest\GEmuera.Core.Tests\GEmuera.Core.Tests.csproj -c Release --filter "FullyQualifiedName~V18PackManifestTests|FullyQualifiedName~CommunityPackTemplateTests"
dotnet test tests\xUnitTest\EmueraFacade.Tests\EmueraFacade.Tests.csproj -c Release --filter "FullyQualifiedName~CompatPackManifestTests"
```

Expected: PASS，且生成的 v18 manifest 不包含 `targetEngineApi`。

- [ ] **Step 7: Commit**

```powershell
git add src/EmueraCompatPack/compatpack.manifest.schema.json packs/gemuera.v18/compatpack.manifest.json packs/CommunityPackTemplate/compatpack.manifest.json tools/compat-pack/Generate-V18PackManifest/Program.cs tests/xUnitTest/GEmuera.Core.Tests/V18PackManifestTests.cs tests/xUnitTest/GEmuera.Core.Tests/CommunityPackTemplateTests.cs tests/xUnitTest/EmueraFacade.Tests/CompatPackManifestTests.cs
git commit -m "docs(compatpack): migrate manifests to dual version axes"
```

---

### Task 3: Engine capability registry 与闭合解析

**Files:**
- Create: `src/Core/Compatibility/EngineCapabilityCatalog.cs`
- Modify: `src/Core/Compatibility/Packs/CompatPackValidation.cs`
- Modify: `src/Core/Compatibility/Packs/CompatPackRules.cs`
- Modify: `scripts/Emuera/Compatibility/CompatPackHost.cs`
- Modify: `scripts/Emuera/Compatibility/LegacyCompatibilityModules.cs`
- Test: `tests/xUnitTest/GEmuera.Core.Tests/EngineCapabilityCatalogTests.cs`

**Interfaces:**
- `public enum CapabilityStatus { Wired, DiagnosticOnly }`
- `public sealed record CapabilityDescriptor(string CapabilityId, CapabilityStatus Status, IReadOnlyList<string> Requires, IReadOnlyList<string> Implies, IReadOnlyList<string> ConflictsWith, string ConsumerContractId, CompatVersion IntroducedInEngineApi)`
- `public sealed class CapabilityResolution { public IReadOnlyList<string> OrderedCapabilityIds { get; } }`
- `public static class EngineCapabilityCatalog`:
  - `static IReadOnlyDictionary<string, CapabilityDescriptor> All { get; }`
  - `static bool TryResolveForPack(IEnumerable<string> requested, out CapabilityResolution resolution, out IReadOnlyList<string> errors)`
  - `static bool TryResolveForProfile(IEnumerable<string> requested, out CapabilityResolution resolution, out IReadOnlyList<string> errors)`
- `CompatPackValidationContext.KnownCapabilityIds` 替换为 `EngineCapabilityCatalog`（或传入 `IReadOnlyDictionary`），pack 校验只接受 `Wired`。

- [ ] **Step 1: 写失败测试：未知/诊断/依赖/冲突**

`EngineCapabilityCatalogTests.cs` 至少包含：

```csharp
[Fact]
public void ResolveForPack_UnknownCapability_Fails()
{
    Assert.False(EngineCapabilityCatalog.TryResolveForPack(
        new[] { "does.not.exist.v1" }, out _, out var errors));
    Assert.Contains(errors, e => e.Contains("unknown", StringComparison.OrdinalIgnoreCase));
}

[Fact]
public void ResolveForPack_DiagnosticOnly_Fails()
{
    Assert.False(EngineCapabilityCatalog.TryResolveForPack(
        new[] { "markup.div-v2.v1" }, out _, out var errors));
}

[Fact]
public void ResolveForPack_RequiresAndImplies_AreExpandedOnce()
{
    Assert.True(EngineCapabilityCatalog.TryResolveForPack(
        new[] { "math.times-clamp.v1" }, out var resolution, out _));
    Assert.Contains("math.times-clamp.v1", resolution.OrderedCapabilityIds);
}

[Fact]
public void ResolveForPack_ConflictingCapabilities_Fails()
{
    // 选一对 catalog 中显式冲突的 Wired capability
    Assert.False(EngineCapabilityCatalog.TryResolveForPack(
        new[] { "a.conflict.v1", "b.conflict.v1" }, out _, out var errors));
}
```

- [ ] **Step 2: 迁移已有 capability 清单**

在 `EngineCapabilityCatalog` 中注册现有常量：

- `SnakeCompatibilityCapabilities` 中每个有真实 policy 属性的 ID 标为 `Wired`，`ConsumerContractId` 指向 `ISnakeCompatibilityPolicy.<Property>`。
- `EraFlCompatibilityModule` 中每个有真实 policy 属性的 ID 标为 `Wired`，`ConsumerContractId` 指向 `IEraFlCompatibilityPolicy.<Property>`。
- 当前没有真实消费者的 eraFL 表面 capability（如 `markup.div-v2.v1`）和 `plugin.external-assembly.v1` 标为 `DiagnosticOnly`。
- `MegatenCompatibilityModule` 的三个 parser 行为标为 `Wired`，五个 policy 行为按真实消费者标 `Wired`。
- 所有 ID 的 `IntroducedInEngineApi` 当前为 `new CompatVersion(1,0)`。

- [ ] **Step 3: 调整 rules**

`CompatPackRules` 对 manifest 和贡献中的 capability：

- 必须存在于 `EngineCapabilityCatalog.All`；
- 必须是 `Wired`；
- 通过 `TryResolveForPack` 做 Requires/Implies/Conflicts 闭包。

内置 profile 使用 `TryResolveForProfile`，允许 DiagnosticOnly 作为账本但不激活。

- [ ] **Step 4: 调整兼容策略**

在 `LegacyCompatibilityModules.cs`：

- `SnakeCompatibilityPolicy.IsEnabled` 改为依赖 `RequiredWiredCapabilityIds`，不再用 19 项全集推断。
- `EraFlCompatibilityPolicy.IsEnabled` 同样改为 Wired 子集。
- 移除或标记 diagnostic-only fallback，避免无关 capability 放大行为。

- [ ] **Step 5: 运行测试**

Run:

```powershell
dotnet test tests\xUnitTest\GEmuera.Core.Tests\GEmuera.Core.Tests.csproj -c Release --filter "FullyQualifiedName~EngineCapabilityCatalogTests|FullyQualifiedName~CompatPackRulesTests"
```

Expected: PASS。

- [ ] **Step 6: Commit**

```powershell
git add src/Core/Compatibility/EngineCapabilityCatalog.cs src/Core/Compatibility/Packs/CompatPackValidation.cs src/Core/Compatibility/Packs/CompatPackRules.cs scripts/Emuera/Compatibility/CompatPackHost.cs scripts/Emuera/Compatibility/LegacyCompatibilityModules.cs tests/xUnitTest/GEmuera.Core.Tests/EngineCapabilityCatalogTests.cs
git commit -m "feat(compatpack): add wired capability catalog"
```

---

### Task 4: 表面符号双形态闭包与运行时严格绑定

**Files:**
- Create: `src/Core/Compatibility/Packs/CompatSurfaceSymbols.cs`
- Modify: `src/Core/Compatibility/Packs/CompatPackValidation.cs`
- Modify: `src/Core/Compatibility/Packs/CompatPackRules.cs`
- Modify: `src/Core/Compatibility/Packs/CompatPackPlanAssembler.cs`
- Modify: `src/Core/Compatibility/CompatibilityDescriptorRoute.cs`
- Modify: `src/Core/Compatibility/LegacyCompatibilityPlanConsumption.cs`
- Modify: `scripts/Emuera/GameData/IdentifierDictionary.cs`
- Modify: `scripts/Emuera/GameProc/Process.cs`
- Modify: `scripts/Emuera/Program.cs`
- Modify: `tools/dialect-inventory/LegacyDialectInventoryGenerator/Program.cs`
- Regenerate: `src/Core/Compatibility/LegacyDialectInventories.Generated.cs`
- Test: `tests/xUnitTest/GEmuera.Core.Tests/CompatSurfaceSymbolTests.cs`
- Test: `tests/xUnitTest/GEmuera.Core.Tests/CompatPackRulesTests.cs`
- Test: `tests/xUnitTest/GEmuera.Core.Tests/CompatibilityDescriptorRouteTests.cs`
- Test: `tests/xUnitTest/EmueraFacade.Tests/CompatPackManifestTests.cs`

**Interfaces:**
- `public enum CompatSymbolKind { Instruction, Function, Variant }`
- `public readonly record struct CompatSymbolKey(CompatSymbolKind Kind, string Name)`:
  - `static bool TryParse(string text, out CompatSymbolKey key)`
  - `string ToContractString()`
- `public enum CompatSymbolStateKind { Present, Hidden, VariantSelection }`
- `public sealed record CompatSymbolState(CompatSymbolKey Key, CompatSymbolStateKind Kind, string? OwnerPackId, string? ReturnType, string? BuiltinVariant)`
- `CompatPackValidationContext` 新增 `IReadOnlySet<string> MethodProjectedInstructionNames`。
- `CompatPackRules` 必须产出规范化后的 surface 状态，不能只返回原始 add/hide 名称集合。
- `CompatibilityDescriptorRoute.TryCreateSessionView` 必须在 registry 超出 plan 时返回 false；`IdentifierDictionary.BindCompatibilityPlan` 不得静默落回全量 registry。

- [ ] **Step 1: 写失败测试：ABS hide 与 ACOS add**

在 `CompatPackRulesTests.cs` 增加：

```csharp
[Fact]
public void Validate_HideMethodProjectedInstruction_ClosesFunctionSide()
{
    // hideInstructions:["ABS"]，其中 ABS 是 Method 投影
    // 期望：规范化状态同时包含 instruction:ABS 和 function:ABS 的 Hidden
}

[Fact]
public void Validate_AddMethodProjectedInstruction_RejectsWithUseAddFunctionsHint()
{
    // addInstructions:["ACOS"] 且 ACOS 是 Method 投影
    // 期望：失败，错误包含 "addFunctions"
}

[Fact]
public void Validate_AddFunction_MirrorsMethodProjectionInstruction()
{
    // addFunctions:["ACOS"] 且 ACOS 是 Method 投影
    // 期望：规范化状态同时包含 function:ACOS Present 和 instruction:ACOS Present
}
```

- [ ] **Step 2: 扩展 inventory 生成器**

在 `LegacyDialectInventoryGenerator` 中读取 engine 的 `FunctionIdentifier` 真实 `Method` 属性，生成：

```csharp
public static readonly string[] MethodProjectedInstructionNames = { ... };
```

生成内容按 Ordinal 排序；`LegacyDialectSurfaceSmoke` 增加与运行时 `FunctionIdentifier.GetInstructionNameDic` 的对照断言。

Run:

```powershell
dotnet run --project tools\dialect-inventory\LegacyDialectInventoryGenerator -- .
```

Expected: `LegacyDialectInventories.Generated.cs` 更新，新增 `MethodProjectedInstructionNames`。

- [ ] **Step 3: 实现规范化规则**

在 `CompatPackRules` 中：

- `hideInstructions` 命中 `MethodProjectedInstructionNames` → 自动补同名 `hideFunctions`；
- `hideFunctions` 命中 method-projected 名称 → 自动补同名 `hideInstructions`；
- `addInstructions` 命中 method-projected 名称 → 拒载，提示使用 `addFunctions`；
- `addFunctions` 命中 method-projected 名称 → 自动补 `instruction` 的 Present 状态；
- 保持 `hide` 只能来自 v24 基线、`add` 不得与 v24 基线同名的既有规则。

- [ ] **Step 4: 写失败测试：registry 超出 plan 必须失败**

新增 `CompatibilityDescriptorRouteTests.cs`：

```csharp
[Fact]
public void TryCreateSessionView_WhenRegistryHasUndeclaredName_ReturnsFalse()
{
    // plan 只有 A，registry 有 A+B
    // 期望 false，且不返回可用 route
}

[Fact]
public void TryCreateSessionView_WhenRegistryMatchesPlan_ReturnsTrue()
{
    // 期望 true
}
```

- [ ] **Step 5: 移除静默 fallback**

修改 `IdentifierDictionary.BindCompatibilityPlan`：

- 改为 `internal bool BindCompatibilityPlan(CompatibilityPlan plan)`；
- `TryCreateSessionView` 失败时返回 false；
- 不再把 `compatibilityInstructionDic` / `compatibilityMethodDic` 置空后继续使用全量 registry；
- `Process.Initialize` 在 false 时调用新的 `Program.TryFallbackToNoPackBaseline()`，重建当前 profile 的无包 plan 并重新绑定；
- 如果 fallback 也失败，抛出带 plan hash、profile、symbol 名称的异常，终止会话。

在 `Program.cs` 增加：

```csharp
internal static bool TryFallbackToNoPackBaseline(string profileId, out CompatibilityPlan baselinePlan)
```

实现要求：

- 先 Dispose 当前 `CompatPackSession`；
- 用 `BuiltInDialectCatalog.CreateLegacySessionPlan(profileId)` 创建无包基线；
- 通过 `ConfigureCompatibilityPlan` 重新绑定；
- 记录 `[LOAD] CompatPack projection failed; fallback to no-pack baseline`。

- [ ] **Step 6: 运行测试**

Run:

```powershell
dotnet test tests\xUnitTest\GEmuera.Core.Tests\GEmuera.Core.Tests.csproj -c Release --filter "FullyQualifiedName~CompatSurfaceSymbolTests|FullyQualifiedName~CompatPackRulesTests|FullyQualifiedName~CompatibilityDescriptorRouteTests"
dotnet run --project tools\dialect-inventory\LegacyDialectSurfaceSmoke -c Release
```

Expected: PASS；`hideInstructions:["ABS"]` 不再导致全量 registry 回退，`addInstructions:["ACOS"]` 在校验期被拒。

- [ ] **Step 7: Commit**

```powershell
git add src/Core/Compatibility/Packs/CompatSurfaceSymbols.cs src/Core/Compatibility/Packs/CompatPackValidation.cs src/Core/Compatibility/Packs/CompatPackRules.cs src/Core/Compatibility/Packs/CompatPackPlanAssembler.cs src/Core/Compatibility/CompatibilityDescriptorRoute.cs src/Core/Compatibility/LegacyCompatibilityPlanConsumption.cs scripts/Emuera/GameData/IdentifierDictionary.cs scripts/Emuera/GameProc/Process.cs scripts/Emuera/Program.cs tools/dialect-inventory/LegacyDialectInventoryGenerator/Program.cs src/Core/Compatibility/LegacyDialectInventories.Generated.cs tests/xUnitTest/GEmuera.Core.Tests/CompatSurfaceSymbolTests.cs tests/xUnitTest/GEmuera.Core.Tests/CompatPackRulesTests.cs tests/xUnitTest/GEmuera.Core.Tests/CompatibilityDescriptorRouteTests.cs
git commit -m "fix(compatpack): close instruction and function symbol surfaces"
```

---

### Task 5: 加载完整性、依赖哈希与 ValidatedPackDelta

**Files:**
- Create: `src/Core/Compatibility/Packs/ValidatedPackDelta.cs`
- Modify: `src/Core/Compatibility/Packs/CompatPackHandle.cs`
- Modify: `src/Core/Compatibility/Packs/CompatPackLoader.cs`
- Modify: `src/Core/Compatibility/Packs/CompatPackLoadContext.cs`
- Modify: `src/Core/Compatibility/Packs/CompatPackRules.cs`
- Modify: `src/Core/Compatibility/Packs/CompatPackValidation.cs`
- Test: `tests/xUnitTest/GEmuera.Core.Tests/CompatPackLoaderTests.cs`
- Test: `tests/xUnitTest/GEmuera.Core.Tests/CompatPackAlcIsolationTests.cs`
- Test: `tests/xUnitTest/GEmuera.Core.Tests/CompatPackAssemblyTests.cs`

**Interfaces:**
- `public sealed class ValidatedPackDelta`:
  - `string PackId`
  - `string PackVersion`
  - `string PackSha256`
  - `IReadOnlyList<string> CapabilityIds`
  - `IReadOnlyList<CompatSymbolState> SurfaceStates`
  - `IReadOnlyDictionary<string, string> VariantSelections`
  - `string DeltaSemanticHash`
- `CompatPackHandle` 新增 `ValidatedPackDelta ValidatedDelta { get; }`。
- `CompatPackRules.Validate` 增加 out 参数：
  - `out ValidatedPackDelta? validatedDelta`
- `CompatPackLoader` 读取 pack directory 顶层全部 `*.dll`，生成 `IReadOnlyDictionary<string, byte[]>` 依赖字节图；主 DLL 也必须在图中。
- `PackSha256` 的输入按 Ordinal 排序：主程序集 + manifest 原始字节 + 依赖文件名/长度/字节。
- `CompatPackLoadContext` 构造函数改为接收：
  - `string packAssemblyPath`
  - `IReadOnlyDictionary<string, byte[]> dependencyBytes`
- 依赖加载使用 `LoadFromStream(new MemoryStream(bytes))`，不再在 `Load()` 中按路径读盘。
- `ResolvePackEntry` 遇到 `ReflectionTypeLoadException` 直接拒载，不再把零入口降级为 manifest-only。

- [ ] **Step 1: 写失败测试：依赖 DLL 改变必须改变 PackSha256**

在 `CompatPackLoaderTests.cs` 增加：

```csharp
[Fact]
public void TryLoad_ChangingDependencyBytes_ChangesPackSha256()
{
    // 复制 fixture 到临时目录，添加一个名为 DummyDependency.dll 的依赖
    // 同一主 DLL/manifest，只改依赖字节
    // 断言两次 PackSha256 不同
}
```

- [ ] **Step 2: 写失败测试：类型加载失败不得变成 manifest-only**

增加 `TryLoad_TypeLoadFailure_IsRejected`，构造一个带坏依赖或坏类型元数据的夹具；期望 `TryLoad` 返回 false，错误包含 `ReflectionTypeLoadException` 或依赖名。

- [ ] **Step 3: 写失败测试：贡献只执行一次**

用一个计数 `ISurfaceContribution`：

```csharp
[Fact]
public void ValidatePackEntry_ExecutesSurfaceContributionOnce()
{
    // Apply 计数必须恰好为 1
}
```

- [ ] **Step 4: 实现 ValidatedPackDelta**

`CompatPackRules.Validate` 在完成所有规则检查后，把结果冻结为 `ValidatedPackDelta`：

- SurfaceStates 来自 Task 4 的规范化 symbol 状态；
- CapabilityIds 来自 manifest + 贡献并通过 capability closure；
- VariantSelections 复制为不可变字典；
- DeltaSemanticHash 用规范排序后的字段计算。

`CompatPackHandle` 只保存这份 delta，不保存可再次执行的贡献对象用于组装。

- [ ] **Step 5: 实现依赖字节图和哈希**

`CompatPackLoader.TryLoad`：

1. 读取主 DLL 字节；
2. 枚举 pack directory 顶层 `*.dll`，按 Ordinal 排序；
3. 拒绝指向包目录外的 symlink/junction；
4. 读入依赖字节图；
5. 用同一依赖字节图构造 ALC；
6. 计算 `PackSha256`，把依赖名称、长度、字节都纳入哈希；
7. 将依赖闭包写入 `[LOAD]` 诊断（只记录 basename 和 SHA256，不记录完整路径）。
8. 入口类 `Manifest` 与内嵌 manifest 全字段相等；任一字段不一致即拒载。

- [ ] **Step 6: 实现类型加载失败 fail-closed**

`ResolvePackEntry`：

- `ReflectionTypeLoadException` → 收集错误并返回 null；
- 完整枚举且零入口 → manifest-only；
- 多个 `ICompatPack` → 仍拒载；
- 入口构造异常 → 保持当前错误路径。

- [ ] **Step 7: 运行测试**

Run:

```powershell
dotnet test tests\xUnitTest\GEmuera.Core.Tests\GEmuera.Core.Tests.csproj -c Release --filter "FullyQualifiedName~CompatPackLoaderTests|FullyQualifiedName~CompatPackAlcIsolationTests|FullyQualifiedName~CompatPackAssemblyTests"
```

Expected: PASS；依赖变化影响哈希；类型加载失败拒载；贡献 Apply 恰一次。

- [ ] **Step 8: Commit**

```powershell
git add src/Core/Compatibility/Packs/ValidatedPackDelta.cs src/Core/Compatibility/Packs/CompatPackHandle.cs src/Core/Compatibility/Packs/CompatPackLoader.cs src/Core/Compatibility/Packs/CompatPackLoadContext.cs src/Core/Compatibility/Packs/CompatPackRules.cs src/Core/Compatibility/Packs/CompatPackValidation.cs tests/xUnitTest/GEmuera.Core.Tests/CompatPackLoaderTests.cs tests/xUnitTest/GEmuera.Core.Tests/CompatPackAlcIsolationTests.cs tests/xUnitTest/GEmuera.Core.Tests/CompatPackAssemblyTests.cs
git commit -m "feat(compatpack): freeze validated deltas and hash dependency closure"
```

---

### Task 6: 包图依赖、冲突、覆盖与确定性组装

**Files:**
- Create: `src/EmueraCompatPack/CompatPackComposition.cs`
- Create: `src/Core/Compatibility/Packs/CompatPackCompositionResolver.cs`
- Modify: `src/EmueraCompatPack/CompatPackManifest.cs`
- Modify: `src/EmueraCompatPack/compatpack.manifest.schema.json`
- Modify: `src/Core/Compatibility/Packs/CompatPackRules.cs`
- Modify: `src/Core/Compatibility/Packs/CompatPackPlanAssembler.cs`
- Modify: `src/Core/Compatibility/Packs/CompatPackHandle.cs`
- Test: `tests/xUnitTest/EmueraFacade.Tests/CompatPackManifestTests.cs`
- Test: `tests/xUnitTest/GEmuera.Core.Tests/CompatPackCompositionResolverTests.cs`
- Test: `tests/xUnitTest/GEmuera.Core.Tests/CompatPackAssemblyTests.cs`

**Interfaces:**
- Contract records:
  - `public sealed record CompatPackDependency(string PackId, string VersionRange)`
  - `public sealed record CompatPackOverride(string Symbol, int Priority)`
- `CompatPackManifest` 新增：
  - `IReadOnlyList<CompatPackDependency> RequiredPacks`
  - `IReadOnlyList<string> ConflictingPacks`
  - `IReadOnlyList<CompatPackOverride> Overrides`
- `public sealed class CompatPackCompositionResult`:
  - `IReadOnlyList<CompatPackHandle> OrderedPacks`
  - `IReadOnlyList<string> CapabilityIds`
  - `IReadOnlyDictionary<CompatSymbolKey, CompatSymbolState> SymbolStates`
  - `string CompositionHash`
- `public static class CompatPackCompositionResolver`:
  - `static bool TryResolve(IReadOnlyList<CompatPackHandle> handles, CompatPackValidationContext context, out CompatPackCompositionResult result, out IReadOnlyList<string> errors)`
- `CompatPackPlanAssembler.TryAssemble` 先调用 resolver，再只读取 `ValidatedDelta` 和 `SymbolStates`；不再调用任何包贡献对象。

- [ ] **Step 1: 写失败测试：manifest 解析组合字段**

在 `CompatPackManifestTests.cs` 增加：

```csharp
[Fact]
public void Parse_CompositionFields_AreImmutableAndOrdered()
{
    const string json = """
    {
      "packId": "test.compose",
      "packVersion": "1.0.0",
      "contractAbi": "1.0",
      "engineApi": "1.0",
      "baseProfileId": "v24pure",
      "requires": {
        "packs": [
          { "packId": "community.shared", "versionRange": "[1.0.0,2.0.0)" }
        ]
      },
      "conflicts": { "packs": ["community.legacy"] },
      "overrides": [
        { "symbol": "instruction:SETANIMETIMER", "priority": 20 }
      ]
    }
    """;

    Assert.True(CompatPackManifest.TryParse(json, out var manifest, out var errors));
    Assert.Empty(errors);
    Assert.Single(manifest!.RequiredPacks);
    Assert.Equal("community.shared", manifest.RequiredPacks[0].PackId);
    Assert.Equal("[1.0.0,2.0.0)", manifest.RequiredPacks[0].VersionRange);
    Assert.Equal(new[] { "community.legacy" }, manifest.ConflictingPacks);
    Assert.Equal(20, manifest.Overrides[0].Priority);
}
```

- [ ] **Step 2: 写失败测试：依赖/冲突/覆盖**

`CompatPackCompositionResolverTests.cs` 至少：

```csharp
[Fact]
public void Resolve_MissingRequiredPack_Fails()
{
    // A requires B, only A enabled
}

[Fact]
public void Resolve_CyclicPackRequirement_Fails()
{
    // A requires B, B requires A
}

[Fact]
public void Resolve_RequiredVersionRangeMismatch_Fails()
{
    // A requires B [2.0.0,3.0.0), enabled B 1.0.0
}

[Fact]
public void Resolve_DeclaredConflict_Fails()
{
    // A conflicts B, both enabled
}

[Fact]
public void Resolve_ExplicitOverrideHighestPriorityWins()
{
    // A/B both hide instruction:Foo, B declares override priority 20
}

[Fact]
public void Resolve_OverridePriorityTie_Fails()
{
    // A/B both override instruction:Foo with priority 20
}

[Fact]
public void Resolve_NoEffectPack_Fails()
{
    // 空 surface、空 capability、空 variant、无依赖贡献
}
```

- [ ] **Step 3: 写失败测试：组装只读 delta**

在 `CompatPackAssemblyTests.cs` 增加：

```csharp
[Fact]
public void Assemble_DoesNotExecuteSurfaceContributionAgain()
{
    // 计数贡献 Apply；加载后调用 TryAssemble
    // 断言 Apply == 1
}
```

- [ ] **Step 4: 实现 manifest 组合字段**

schema 增加：

- `requires.packs[]`: `packId`、`versionRange`；版本范围使用现有 `SemanticVersionRange` 语义。
- `conflicts.packs[]`: packId 字符串；任意版本冲突。
- `overrides[]`: `symbol`、`priority`。
- 未知字段、重复 packId、空 symbol、负 priority 全部拒载。

- [ ] **Step 5: 实现 resolver**

按 spec 第 6.2 节顺序实现：

1. 建立 packId → handle；
2. 验证 requires 闭包、版本范围、环；
3. 计算 capability 闭包；
4. 拓扑排序，同层按 packId Ordinal；
5. 合并 symbol states；
6. 应用显式 override/priority；
7. 检测冲突；
8. 拒绝无效果包：无任何有效 symbol/capability/variant 贡献的包不得进入最终组合；`bundle` 不在 v1 范围。
9. CompositionHash 必须包含有序 packId/version/PackSha256、capability closure 和最终 symbol states，输入顺序不得影响结果。

必须用有向图 DFS 或等价算法，错误中包含依赖链，例如：

```text
community.a -> community.b -> community.c
```

- [ ] **Step 6: 调整 assembler**

`CompatPackPlanAssembler.TryAssemble` 不再接收 `handle.Surface`，只接收 `handle.ValidatedDelta`；symbol states 由 resolver 生成；所有新增/隐藏/变体/返回值进入最终 plan。

- [ ] **Step 7: 运行测试**

Run:

```powershell
dotnet test tests\xUnitTest\EmueraFacade.Tests\EmueraFacade.Tests.csproj -c Release --filter "FullyQualifiedName~CompatPackManifestTests"
dotnet test tests\xUnitTest\GEmuera.Core.Tests\GEmuera.Core.Tests.csproj -c Release --filter "FullyQualifiedName~CompatPackCompositionResolverTests|FullyQualifiedName~CompatPackAssemblyTests"
```

Expected: PASS。

- [ ] **Step 8: Commit**

```powershell
git add src/EmueraCompatPack/CompatPackComposition.cs src/EmueraCompatPack/CompatPackManifest.cs src/EmueraCompatPack/compatpack.manifest.schema.json src/Core/Compatibility/Packs/CompatPackCompositionResolver.cs src/Core/Compatibility/Packs/CompatPackRules.cs src/Core/Compatibility/Packs/CompatPackPlanAssembler.cs src/Core/Compatibility/Packs/CompatPackHandle.cs tests/xUnitTest/EmueraFacade.Tests/CompatPackManifestTests.cs tests/xUnitTest/GEmuera.Core.Tests/CompatPackCompositionResolverTests.cs tests/xUnitTest/GEmuera.Core.Tests/CompatPackAssemblyTests.cs
git commit -m "feat(compatpack): resolve dependencies conflicts and overrides deterministically"
```

---

### Task 7: Host 接线、语义级 baseSurfaceHash 与文档同步

**Files:**
- Modify: `src/Core/Compatibility/Packs/LegacySurfaceHash.cs`
- Modify: `src/Core/Compatibility/Packs/CompatPackValidation.cs`
- Modify: `src/Core/Compatibility/Packs/CompatPackRules.cs`
- Modify: `scripts/Emuera/Compatibility/CompatPackHost.cs`
- Modify: `src/Core/Compatibility/Packs/CompatPackGameIdentity.cs`
- Modify: `tools/compat-pack/Generate-V18PackManifest/Program.cs`
- Modify: `packs/gemuera.v18/compatpack.manifest.json`
- Modify: `packs/CommunityPackTemplate/compatpack.manifest.json`
- Modify: `docs/designs/compat-pack-interface.md`
- Modify: `docs/plans/2026-10-03-compatpack-contract-assembly-split-adr.md`
- Modify: `docs/plans/2026-10-03-compatpack-v2-code-contributions-spec.md`
- Modify: `packs/README.md`
- Test: `tests/xUnitTest/GEmuera.Core.Tests/LegacySurfaceHashTests.cs`
- Test: `tests/xUnitTest/GEmuera.Core.Tests/V18PackManifestTests.cs`
- Test: `tests/xUnitTest/GEmuera.Core.Tests/CommunityPackTemplateTests.cs`
- Test: `tests/xUnitTest/GEmuera.Core.Tests/CompatPackGameIdentityTests.cs`

**Interfaces:**
- `LegacySurfaceHash` 增加新的语义输入接口：

```csharp
public sealed record SurfaceHashInput(
    string BaseProfileId,
    CompatVersion EngineApi,
    IReadOnlyDictionary<string, string> InstructionDescriptors,
    IReadOnlyDictionary<string, string> FunctionDescriptors,
    IReadOnlyDictionary<string, IReadOnlySet<string>> BuiltinVariants,
    IReadOnlyDictionary<string, CapabilityDescriptor> Capabilities);

public static string ComputeV24SurfaceHash(SurfaceHashInput input);
```

- 指令 descriptor 字符串至少包含：name、signature、completion mode、module id；函数 descriptor 至少包含 name、signature、return type。
- Hash 文本规范：UTF-8、LF、无 BOM、按 Ordinal 排序、字段以 `|` 分隔、每行以 `\n` 结尾。
- `CompatPackHost.BuildValidationContext` 必须使用：
  - `CompatPackHost.CurrentContractAbi`
  - `CompatPackHost.CurrentEngineApi`
  - `EngineCapabilityCatalog.All`
  - `LegacyDialectInventories.MethodProjectedInstructionNames`
  - 新的 `LegacySurfaceHash.ComputeV24SurfaceHash(...)`
- 官方 v18 包和模板必须写入非空 `baseSurfaceHash`。

- [ ] **Step 1: 写失败测试：hash 覆盖 engineApi/capability/variant**

在 `LegacySurfaceHashTests.cs` 增加：

```csharp
[Fact]
public void V24Hash_ChangesWhenEngineApiChanges()
{
    var baseline = BuildInput(new CompatVersion(1, 0));
    var changed = BuildInput(new CompatVersion(1, 1));
    Assert.NotEqual(
        LegacySurfaceHash.ComputeV24SurfaceHash(baseline),
        LegacySurfaceHash.ComputeV24SurfaceHash(changed));
}

[Fact]
public void V24Hash_ChangesWhenWiredCapabilitySetChanges()
{
    // 修改 capability 集合的一个 Wired ID，hash 必须变化
}

[Fact]
public void V24Hash_ChangesWhenBuiltinVariantMapChanges()
{
    // 修改 BuiltinVariants，hash 必须变化
}
```

- [ ] **Step 2: 写失败测试：官方包必须有 baseSurfaceHash**

在 `V18PackManifestTests` 和 `CommunityPackTemplateTests` 增加：

```csharp
Assert.False(string.IsNullOrWhiteSpace(handle.Manifest.BaseSurfaceHash));
Assert.Matches("^[0-9a-f]{64}$", handle.Manifest.BaseSurfaceHash);
```

- [ ] **Step 3: 实现语义 hash**

实现 `SurfaceHashInput` 和新的 `ComputeV24SurfaceHash`。保留旧的 `ComputeV24SurfaceHash()` 仅用于旧测试迁移；新逻辑必须显式使用完整输入。

Hash canonical 行示例：

```text
profile=v24pure
engineApi=1.0
instruction=ABS|legacy|CoreImmediate|gemuera.v24
function=ABS|legacy|Integer|gemuera.v24
variant=SETBGIMAGE|builtin:snake
capability=math.times-clamp.v1|Wired|ISnakeCompatibilityPolicy.UsesTimesClamp|1.0
```

不要包含绝对路径、时间戳或进程相关值。

- [ ] **Step 4: 更新生成器和官方包**

`Generate-V18PackManifest/Program.cs` 计算 v24 surface hash 并写入 manifest。生成器必须使用与宿主相同的 `SurfaceHashInput` 构造方式；禁止手写 hash。

Run:

```powershell
dotnet run --project tools\compat-pack\Generate-V18PackManifest -- .
dotnet run --project tools\dialect-inventory\LegacyDialectSurfaceSmoke -c Release
```

Expected: v18 manifest 有合法 64 位 hash；smoke 通过。

- [ ] **Step 4b: 修正 gameIdentity.versionAccept**

在 `CompatPackGameIdentityTests.cs` 增加：

- `3.43` vs `3.50` 在 `minor` 下必须失败；
- `3.43` vs `3.43.7` 在 `minor` 下必须通过；
- `305` vs `305` 精确通过；
- `305` vs `306` 失败；
- `exact` 始终全串相等。

实现语义：

- `minor`：双方至少有 `major.minor` 两段时，比较前两段；只有单段时退化为全等；
- 字段名保留 `minor`，不得再按第一段相等。

- [ ] **Step 5: 接线 host**

`CompatPackHost.BuildValidationContext` 用新的上下文构造：

```csharp
new CompatPackValidationContext(
    CurrentContractAbi,
    CurrentEngineApi,
    EngineCapabilityCatalog.All,
    BuiltinVariantNames,
    ...,
    baselineSurfaceHash: LegacySurfaceHash.ComputeV24SurfaceHash(BuildSurfaceHashInput(baselineProfileId)));
```

不得再使用 profile `RequiredCapabilityIds` 的并集作为 known capability 词汇表。

- [ ] **Step 6: 同步文档**

- `compat-pack-interface.md` 的版本轴/兼容承诺/组合章节改为引用本 spec。
- ADR 的无限期兼容改为 1.x 成员级兼容。
- v2 spec 删除原地扩参，改为新类型/新重载。
- `packs/README.md` 修正安装根目录、双轴字段、依赖/覆盖说明。

- [ ] **Step 7: 运行测试**

Run:

```powershell
dotnet test tests\xUnitTest\GEmuera.Core.Tests\GEmuera.Core.Tests.csproj -c Release --filter "FullyQualifiedName~LegacySurfaceHashTests|FullyQualifiedName~V18PackManifestTests|FullyQualifiedName~CommunityPackTemplateTests"
dotnet run --project tools\dialect-inventory\LegacyDialectSurfaceSmoke -c Release
dotnet run --project tools\dialect-inventory\LegacyDialectRuntimeSmoke -c Release
```

Expected: PASS。

- [ ] **Step 8: Commit**

```powershell
git add src/Core/Compatibility/Packs/LegacySurfaceHash.cs src/Core/Compatibility/Packs/CompatPackValidation.cs src/Core/Compatibility/Packs/CompatPackRules.cs src/Core/Compatibility/Packs/CompatPackGameIdentity.cs scripts/Emuera/Compatibility/CompatPackHost.cs tools/compat-pack/Generate-V18PackManifest/Program.cs packs/gemuera.v18/compatpack.manifest.json packs/CommunityPackTemplate/compatpack.manifest.json docs/designs/compat-pack-interface.md docs/plans/2026-10-03-compatpack-contract-assembly-split-adr.md docs/plans/2026-10-03-compatpack-v2-code-contributions-spec.md packs/README.md tests/xUnitTest/GEmuera.Core.Tests/LegacySurfaceHashTests.cs tests/xUnitTest/GEmuera.Core.Tests/V18PackManifestTests.cs tests/xUnitTest/GEmuera.Core.Tests/CommunityPackTemplateTests.cs tests/xUnitTest/GEmuera.Core.Tests/CompatPackGameIdentityTests.cs
git commit -m "feat(compatpack): wire semantic surface hash and host contexts"
```

---

### Task 8: ABI 基线、旧包夹具与真实 ALC 回收

**Files:**
- Create: `tests/xUnitTest/EmueraFacade.Tests/CompatPackPublicApiTests.cs`
- Create: `tests/xUnitTest/EmueraFacade.Tests/CompatPackPublicApi.v1.0.txt`
- Create: `tests/xUnitTest/CompatPackLegacyFacadeFixture/CompatPackLegacyFacadeFixture.csproj`
- Create: `tests/xUnitTest/CompatPackLegacyFacadeFixture/LegacyFacadePack.cs`
- Create: `tests/xUnitTest/CompatPackLegacyFacadeFixture/compatpack.manifest.json`
- Create: `tests/xUnitTest/GEmuera.Core.Tests/CompatPackLegacyFacadeTests.cs`
- Create: `tests/xUnitTest/GEmuera.Core.Tests/CompatPackAlcReclamationTests.cs`
- Modify: `tests/xUnitTest/EmueraPluginAbi.Tests/PluginAbiBoundaryTests.cs`
- Modify: `tests/xUnitTest/GEmuera.Core.Tests/CompatPackSessionTests.cs`

**Interfaces:**
- Public API 快照必须覆盖 `Emuera.CompatPack` 所有 public type、constructor、method、property、record positional parameter；
- 旧 facade 夹具必须只引用 `src/EmueraFacade/Emuera.csproj`，不能引用 `src/EmueraCompatPack`；
- ALC 回收测试必须使用真实 `CompatPackLoader` + `CompatPackSession`，不能只用 `CountingLease`。

- [ ] **Step 1: 写失败测试：public API snapshot**

`CompatPackPublicApiTests.cs` 用反射生成 canonical API 文本：

```csharp
[Fact]
public void CompatPackPublicApi_MatchesV1Baseline()
{
    string actual = CompatPackPublicApiSnapshot.Render(typeof(ICompatPack).Assembly);
    string expected = File.ReadAllText("CompatPackPublicApi.v1.0.txt");
    Assert.Equal(expected, actual);
}
```

首次运行时输出 actual；将其保存为 `CompatPackPublicApi.v1.0.txt`。之后任何删除/改签名/record 位置参数变化都必须让测试失败。

- [ ] **Step 2: 写失败测试：旧 facade 包**

在 `CompatPackLegacyFacadeTests.cs`：

```csharp
[Fact]
public void LegacyFacadePack_LoadsWhenFacadeIsPreloaded()
{
    _ = typeof(MinorShift.Emuera.Runtime.Utils.PluginSystem.IPluginMethod).Assembly;
    // TryLoad legacy fixture，并验证贡献被加载
}

[Fact]
public void LegacyFacadePack_WithoutFacadePreload_FailsWithActionableError()
{
    // 在 clean ALC/clean process seam 下加载，确认错误包含 facade/TypeForwardedTo 提示
}
```

旧夹具的 `LegacyFacadePack.cs` 必须使用 `Emuera.Compatibility.Packs` 类型并只引用 facade 项目；若 facade 转发链未生效，测试应失败。

- [ ] **Step 3: 写失败测试：真实 ALC 回收**

在 `CompatPackAlcReclamationTests.cs`：

```csharp
[Fact]
public void SessionDispose_UnloadsRealAlcAndAllowsCollection()
{
    // 1. TryLoad 真实 fixture，取得 handle
    // 2. 创建 CompatPackSession
    // 3. 保存 WeakReference 到 handle.LoadContext 或 pack 实例
    // 4. Dispose session
    // 5. GC.Collect / WaitForPendingFinalizers 三轮
    // 6. Assert.False(weak.IsAlive)
}
```

测试必须重复至少三轮，且验证 `Program`/session 静态字段无残留。

- [ ] **Step 4: 生成并冻结 v1 API 基线**

在 API 测试通过后，提交：

- `CompatPackPublicApi.v1.0.txt`
- `CompatPackPublicApiTests.cs`
- `CompatPackLegacyFacadeFixture/**`
- `CompatPackLegacyFacadeTests.cs`
- `CompatPackAlcReclamationTests.cs`

API 基线冻结后，后续任何 public contract 变化都必须先修改 spec 并明确 major/minor。

- [ ] **Step 5: 运行测试**

Run:

```powershell
dotnet test tests\xUnitTest\EmueraFacade.Tests\EmueraFacade.Tests.csproj -c Release --filter "FullyQualifiedName~CompatPackPublicApiTests"
dotnet test tests\xUnitTest\GEmuera.Core.Tests\GEmuera.Core.Tests.csproj -c Release --filter "FullyQualifiedName~CompatPackLegacyFacadeTests|FullyQualifiedName~CompatPackAlcReclamationTests"
dotnet test tests\xUnitTest\EmueraPluginAbi.Tests\EmueraPluginAbi.Tests.csproj -c Release
```

Expected: PASS。

- [ ] **Step 6: Commit**

```powershell
git add tests/xUnitTest/EmueraFacade.Tests/CompatPackPublicApiTests.cs tests/xUnitTest/EmueraFacade.Tests/CompatPackPublicApi.v1.0.txt tests/xUnitTest/CompatPackLegacyFacadeFixture tests/xUnitTest/GEmuera.Core.Tests/CompatPackLegacyFacadeTests.cs tests/xUnitTest/GEmuera.Core.Tests/CompatPackAlcReclamationTests.cs tests/xUnitTest/EmueraPluginAbi.Tests/PluginAbiBoundaryTests.cs tests/xUnitTest/GEmuera.Core.Tests/CompatPackSessionTests.cs
git commit -m "test(compatpack): freeze v1 public api and prove legacy/unload behavior"
```

---

### Task 9: 发布门禁脚本、六游戏/Android 证据与治理同步

**Files:**
- Create: `tools/compat-pack/Test-CompatPackReleaseGate.ps1`
- Create: `docs/compat-pack-release-gates.md`
- Modify: `tools/governance/Test-Governance.ps1`
- Modify: `AGENTS.md`（验证节引用新门禁）
- Test: gate script 自身
- Artifacts: Android APK SHA256、设备信息、六游戏运行日志、ALC/legacy fixture 结果

**Interfaces:**
- Gate script 参数：

```powershell
param(
    [string]$ProjectRoot = '.',
    [string]$AndroidEvidencePath = '',
    [string]$SixGameEvidencePath = '',
    [switch]$RequireAndroid,
    [switch]$RequireSixGame
)
```

- 无 `-RequireAndroid` / `-RequireSixGame` 时：运行桌面 xUnit、三冒烟、生命周期守卫和组合测试。
- 有 `-RequireAndroid` 时：要求提供 APK 文件、设备/Android 版本/ABI、启动/Back/Restart/二次启动日志；缺一即 exit 1。
- 有 `-RequireSixGame` 时：要求六游戏 JSON 记录，字段包含 gameId、profile、packIds、result、keyOutput、logPath。
- 输出统一 `PASS/FAIL` 和最终 `exit 0/1`。

- [ ] **Step 1: 写 gate 脚本失败测试**

用一个临时目录构造缺少 Android evidence 的调用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/compat-pack/Test-CompatPackReleaseGate.ps1 -ProjectRoot . -RequireAndroid
```

Expected: FAIL，错误明确指出缺少 APK/设备/日志。

- [ ] **Step 2: 写桌面门禁脚本**

按顺序运行：

```powershell
dotnet test tests\xUnitTest\GEmuera.Core.Tests\GEmuera.Core.Tests.csproj -c Release
dotnet test tests\xUnitTest\EmueraFacade.Tests\EmueraFacade.Tests.csproj -c Release
dotnet test tests\xUnitTest\EmueraPluginAbi.Tests\EmueraPluginAbi.Tests.csproj -c Release
dotnet run --project tools\dialect-inventory\LegacyDialectSurfaceSmoke -c Release
dotnet run --project tools\dialect-inventory\LegacyDialectRuntimeSmoke -c Release
$env:DOTNET_ROLL_FORWARD = 'Major'
dotnet run --project tools\core-contracts\CoreContractSmoke.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tools\compat-pack\Test-CompatPackLifecycle.ps1 -ProjectRoot .
```

每个命令失败都必须让 gate exit 1；记录命令、退出码、测试摘要。

- [ ] **Step 3: 写 Android evidence 校验**

要求 evidence 文件为 JSON，至少：

```json
{
  "apkSha256": "...",
  "deviceModel": "...",
  "androidVersion": "...",
  "abi": "arm64-v8a",
  "runs": [
    { "scenario": "start", "log": "..." },
    { "scenario": "back-restart", "log": "..." },
    { "scenario": "second-start", "log": "..." }
  ],
  "loadLogFound": true,
  "fatalErrorFound": false
}
```

gate 必须验证：

- APK 文件存在且 SHA256 匹配；
- 三次场景都有日志；
- 日志包含 `[LOAD]`，且不含未处理的 plan hash / ALC 失败；
- 失败回退时日志包含 `fallback` 和目标基线；
- 缺项 exit 1。

- [ ] **Step 4: 写六游戏 evidence 校验**

要求六个游戏记录齐全，每个包含：

- `gameId`
- `profile`
- `baseProfile`
- `packIds`
- `result`
- `keyOutput`
- `logPath`

gate 校验所有 `result` 为 PASS，且 log 文件存在；缺一 exit 1。

- [ ] **Step 5: 接入 governance**

`tools/governance/Test-Governance.ps1` 在桌面验证阶段调用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\compat-pack\Test-CompatPackReleaseGate.ps1 -ProjectRoot $ProjectRoot
```

只有 P-D 发布任务再带 `-RequireAndroid -RequireSixGame`；日常 CI 保持桌面门禁，不因没有真机而阻塞开发。

- [ ] **Step 6: 文档和 AGENTS 同步**

`docs/compat-pack-release-gates.md` 写明：

- Android 证据格式和采集步骤；
- 六游戏矩阵命令和日志要求；
- ALC/旧包夹具结果；
- P-D 的解锁顺序；
- 门禁全绿前不发布社区包。

`AGENTS.md` 的兼容包验证节引用新脚本，并保留真机 e2e 是最终验收的口径。

- [ ] **Step 7: 运行门禁**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\compat-pack\Test-CompatPackReleaseGate.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools\governance\Test-Governance.ps1
```

Expected: 桌面门禁 PASS；Android/六游戏证据缺失时明确提示，不得假绿。

- [ ] **Step 8: Commit**

```powershell
git add tools/compat-pack/Test-CompatPackReleaseGate.ps1 docs/compat-pack-release-gates.md tools/governance/Test-Governance.ps1 AGENTS.md
git commit -m "chore(compatpack): add release gate and evidence requirements"
```
