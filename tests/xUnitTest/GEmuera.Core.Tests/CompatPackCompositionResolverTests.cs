using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// T6 组合解析器单测（直接消费手工句柄 + 冻结差量，不经包程序集加载）：
/// 依赖闭包/版本范围/环、conflicts、显式 override/priority、无效果包、capability 组合闭包、
/// 双形态闭包与 CompositionHash 的确定性/顺序无关性。
/// </summary>
public class CompatPackCompositionResolverTests
{
    static CompatPackValidationContext Context(
        IReadOnlyDictionary<string, CapabilityDescriptor>? capabilities = null,
        IReadOnlySet<string>? methodProjectedInstructionNames = null)
        => CompatPackCompositionTestData.Context(capabilities, methodProjectedInstructionNames);

    static CompatPackHandle Handle(
        string packId,
        char shaFill,
        IEnumerable<CompatSymbolState>? states = null,
        IEnumerable<string>? capabilityIds = null,
        IEnumerable<CompatPackDependency>? requires = null,
        IEnumerable<string>? conflicts = null,
        IEnumerable<CompatPackOverride>? overrides = null,
        string packVersion = "1.0.0")
        => CompatPackCompositionTestData.Handle(
            packId, CompatPackCompositionTestData.ShaOf(shaFill), states, capabilityIds,
            requires: requires, conflicts: conflicts, overrides: overrides, packVersion: packVersion);

    static CompatSymbolState Present(string name, string owner)
        => CompatPackCompositionTestData.PresentInstruction(name, owner);

    static CompatSymbolState Hidden(string name, string owner)
        => CompatPackCompositionTestData.HiddenInstruction(name, owner);

    static bool Resolve(
        IReadOnlyList<CompatPackHandle> handles,
        out CompatPackCompositionResult result,
        out IReadOnlyList<string> errors,
        CompatPackValidationContext? context = null)
        => CompatPackCompositionResolver.TryResolve(handles, context ?? Context(), out result, out errors);

    [Fact]
    public void Resolve_MissingRequiredPack_Fails()
    {
        var a = Handle("community.a", 'a',
            states: new[] { Present("TEST_A", "community.a") },
            requires: new[] { new CompatPackDependency("community.b", "1.0.0") });

        Assert.False(Resolve(new[] { a }, out var result, out var errors));

        Assert.Null(result);
        Assert.Contains(errors, error => error.Contains("community.a -> community.b")
            && error.Contains("1.0.0"));
    }

    [Fact]
    public void Resolve_DependencyChainListsFullPath()
    {
        var a = Handle("community.a", 'a',
            states: new[] { Present("TEST_A", "community.a") },
            requires: new[] { new CompatPackDependency("community.b", "1.0.0") });
        var b = Handle("community.b", 'b',
            states: new[] { Present("TEST_B", "community.b") },
            requires: new[] { new CompatPackDependency("community.c", "1.0.0") });

        Assert.False(Resolve(new[] { a, b }, out var result, out var errors));

        Assert.Null(result);
        Assert.Contains(errors, error => error.Contains("community.a -> community.b -> community.c"));
    }

    [Fact]
    public void Resolve_CyclicPackRequirement_Fails()
    {
        var a = Handle("community.a", 'a',
            states: new[] { Present("TEST_A", "community.a") },
            requires: new[] { new CompatPackDependency("community.b", "1.0.0") });
        var b = Handle("community.b", 'b',
            states: new[] { Present("TEST_B", "community.b") },
            requires: new[] { new CompatPackDependency("community.a", "1.0.0") });

        Assert.False(Resolve(new[] { a, b }, out var result, out var errors));

        Assert.Null(result);
        Assert.Contains(errors, error => error.Contains("环")
            && error.Contains("community.a -> community.b -> community.a"));
    }

    [Fact]
    public void Resolve_RequiredVersionRangeMismatch_Fails()
    {
        var a = Handle("community.a", 'a',
            states: new[] { Present("TEST_A", "community.a") },
            requires: new[] { new CompatPackDependency("community.b", "[2.0.0,3.0.0)") });
        var b = Handle("community.b", 'b', states: new[] { Present("TEST_B", "community.b") });

        Assert.False(Resolve(new[] { a, b }, out var result, out var errors));

        Assert.Null(result);
        Assert.Contains(errors, error => error.Contains("community.a -> community.b")
            && error.Contains("[2.0.0,3.0.0)")
            && error.Contains("1.0.0"));
    }

    [Fact]
    public void Resolve_DeclaredConflict_Fails()
    {
        var a = Handle("community.a", 'a',
            states: new[] { Present("TEST_A", "community.a") },
            conflicts: new[] { "community.b" });
        var b = Handle("community.b", 'b', states: new[] { Present("TEST_B", "community.b") });

        Assert.False(Resolve(new[] { a, b }, out var result, out var errors));

        Assert.Null(result);
        Assert.Contains(errors, error => error.Contains("community.a")
            && error.Contains("community.b")
            && error.Contains("冲突"));
    }

    [Fact]
    public void Resolve_DeclaredConflict_CannotBeOverriddenByPriority()
    {
        // conflicts 是硬冲突：priority 不参与冲突仲裁（"priority 不能覆盖冲突"）。
        var a = Handle("community.a", 'a',
            states: new[] { Hidden("SHARED", "community.a") },
            conflicts: new[] { "community.b" },
            overrides: new[] { new CompatPackOverride("instruction:SHARED", 10) });
        var b = Handle("community.b", 'b',
            states: new[] { Present("SHARED", "community.b") },
            overrides: new[] { new CompatPackOverride("instruction:SHARED", 20) });

        Assert.False(Resolve(new[] { a, b }, out var result, out var errors));

        Assert.Null(result);
        Assert.Contains(errors, error => error.Contains("冲突") && error.Contains("community.b"));
    }

    [Fact]
    public void Resolve_ExplicitOverrideHighestPriorityWins()
    {
        var a = Handle("community.a", 'a',
            states: new[] { Hidden("SHARED", "community.a") },
            overrides: new[] { new CompatPackOverride("instruction:SHARED", 10) });
        var b = Handle("community.b", 'b',
            states: new[] { Present("SHARED", "community.b") },
            overrides: new[] { new CompatPackOverride("instruction:SHARED", 20) });

        Assert.True(Resolve(new[] { a, b }, out var result, out var errors), string.Join("; ", errors));

        var key = new CompatSymbolKey(CompatSymbolKind.Instruction, "SHARED");
        Assert.True(result.SymbolStates.TryGetValue(key, out var state));
        Assert.Equal(CompatSymbolStateKind.Present, state!.Kind);
        Assert.Equal("community.b", state.OwnerPackId);
    }

    [Fact]
    public void Resolve_OverridePriorityTie_Fails()
    {
        var a = Handle("community.a", 'a',
            states: new[] { Hidden("SHARED", "community.a") },
            overrides: new[] { new CompatPackOverride("instruction:SHARED", 20) });
        var b = Handle("community.b", 'b',
            states: new[] { Present("SHARED", "community.b") },
            overrides: new[] { new CompatPackOverride("instruction:SHARED", 20) });

        Assert.False(Resolve(new[] { a, b }, out var result, out var errors));

        Assert.Null(result);
        Assert.Contains(errors, error => error.Contains("priority")
            && error.Contains("community.a") && error.Contains("community.b"));
    }

    [Fact]
    public void Resolve_VariantOverrideHighestPriorityWins()
    {
        // spec §6.5.5 / plan Task 6：变体选择与指令/函数一样可被 overrides 仲裁，取唯一
        // 最高 priority（final review M9 补齐 winner 路径的自动化证据）。
        var a = Handle("community.a", 'a',
            states: new[]
            {
                CompatPackCompositionTestData.VariantSelection("SETBGIMAGE", "builtin:snake", "community.a"),
            },
            overrides: new[] { new CompatPackOverride("variant:SETBGIMAGE", 10) });
        var b = Handle("community.b", 'b',
            states: new[]
            {
                CompatPackCompositionTestData.VariantSelection("SETBGIMAGE", "builtin:megaten", "community.b"),
            },
            overrides: new[] { new CompatPackOverride("variant:SETBGIMAGE", 20) });

        Assert.True(Resolve(new[] { a, b }, out var result, out var errors), string.Join("; ", errors));

        var key = new CompatSymbolKey(CompatSymbolKind.Variant, "SETBGIMAGE");
        Assert.True(result.SymbolStates.TryGetValue(key, out var state));
        Assert.Equal(CompatSymbolStateKind.VariantSelection, state!.Kind);
        Assert.Equal("builtin:megaten", state.BuiltinVariant);
        Assert.Equal("community.b", state.OwnerPackId);
    }

    [Fact]
    public void Resolve_VariantOverridePriorityTie_Fails()
    {
        var a = Handle("community.a", 'a',
            states: new[]
            {
                CompatPackCompositionTestData.VariantSelection("SETBGIMAGE", "builtin:snake", "community.a"),
            },
            overrides: new[] { new CompatPackOverride("variant:SETBGIMAGE", 20) });
        var b = Handle("community.b", 'b',
            states: new[]
            {
                CompatPackCompositionTestData.VariantSelection("SETBGIMAGE", "builtin:megaten", "community.b"),
            },
            overrides: new[] { new CompatPackOverride("variant:SETBGIMAGE", 20) });

        Assert.False(Resolve(new[] { a, b }, out var result, out var errors));

        Assert.Null(result);
        // 并列最高 priority 在 override 仲裁层直接拒载（与 instruction 覆盖同一条规则）。
        Assert.Contains(errors, error => error.Contains("variant:SETBGIMAGE")
            && error.Contains("priority") && error.Contains("20"));
    }

    [Fact]
    public void Resolve_SameFinalState_MergesWithoutOverride()
    {
        // 同一最终状态（都隐藏）可合并：owner 取 Ordinal 最小者，结果与输入顺序无关。
        var a = Handle("community.a", 'a', states: new[] { Hidden("SHARED", "community.a") });
        var b = Handle("community.b", 'b', states: new[] { Hidden("SHARED", "community.b") });

        Assert.True(Resolve(new[] { a, b }, out var result, out var errors), string.Join("; ", errors));

        var key = new CompatSymbolKey(CompatSymbolKind.Instruction, "SHARED");
        Assert.True(result.SymbolStates.TryGetValue(key, out var state));
        Assert.Equal(CompatSymbolStateKind.Hidden, state!.Kind);
        Assert.Equal("community.a", state.OwnerPackId);
    }

    [Fact]
    public void Resolve_NoEffectPack_Fails()
    {
        var empty = Handle("community.empty", 'e');

        Assert.False(Resolve(new[] { empty }, out var result, out var errors));

        Assert.Null(result);
        Assert.Contains(errors, error => error.Contains("无效果") && error.Contains("community.empty"));
    }

    [Fact]
    public void Resolve_OverrideTargetMissing_Fails()
    {
        var a = Handle("community.a", 'a',
            states: new[] { Present("TEST_A", "community.a") },
            overrides: new[] { new CompatPackOverride("instruction:MISSING", 10) });

        Assert.False(Resolve(new[] { a }, out var result, out var errors));

        Assert.Null(result);
        Assert.Contains(errors, error => error.Contains("override 目标不存在")
            && error.Contains("instruction:MISSING"));
    }

    [Fact]
    public void Resolve_OverrideBaselineHandler_Fails()
    {
        // 不许覆盖 v24 基线 instruction/function handler（改既有语义必须走变体通道）。
        var a = Handle("community.a", 'a',
            states: new[] { Present("TEST_A", "community.a") },
            overrides: new[] { new CompatPackOverride("instruction:ABS", 10) });

        Assert.False(Resolve(new[] { a }, out var result, out var errors));

        Assert.Null(result);
        Assert.Contains(errors, error => error.Contains("v24 基线")
            && error.Contains("instruction:ABS"));
    }

    [Fact]
    public void Resolve_OverrideDeclaringPackWithoutTargetSymbol_Fails()
    {
        var a = Handle("community.a", 'a',
            states: new[] { Present("TEST_A", "community.a") },
            overrides: new[] { new CompatPackOverride("instruction:SHARED", 30) });
        var b = Handle("community.b", 'b', states: new[] { Present("SHARED", "community.b") });

        Assert.False(Resolve(new[] { a, b }, out var result, out var errors));

        Assert.Null(result);
        Assert.Contains(errors, error => error.Contains("override") && error.Contains("未贡献"));
    }

    [Fact]
    public void Resolve_DuplicatePackId_Fails()
    {
        var first = Handle("community.a", 'a', states: new[] { Present("TEST_A", "community.a") });
        var second = Handle("community.a", 'b', states: new[] { Present("TEST_B", "community.a") });

        Assert.False(Resolve(new[] { first, second }, out var result, out var errors));

        Assert.Null(result);
        Assert.Contains(errors, error => error.Contains("packId 重复") && error.Contains("community.a"));
    }

    [Fact]
    public void Resolve_DependencyFirstTopologicalOrder_AndInputOrderIndependentHash()
    {
        // 拓扑序 = (深度, packId Ordinal)：独立包 a/z 同层按 Ordinal，m 依赖 a 排在其后。
        var z = Handle("community.z", 'z', states: new[] { Present("TEST_Z", "community.z") });
        var m = Handle("community.m", 'm',
            states: new[] { Present("TEST_M", "community.m") },
            requires: new[] { new CompatPackDependency("community.a", "1.0.0") });
        var a = Handle("community.a", 'a', states: new[] { Present("TEST_A", "community.a") });

        Assert.True(Resolve(new[] { z, m, a }, out var first, out var firstErrors),
            string.Join("; ", firstErrors));

        Assert.Equal(
            new[] { "community.a", "community.z", "community.m" },
            first.OrderedPacks.Select(handle => handle.Manifest.PackId));

        // 输入顺序不得影响结果（含 CompositionHash）。
        Assert.True(Resolve(new[] { a, m, z }, out var second, out var secondErrors),
            string.Join("; ", secondErrors));
        Assert.Equal(first.CompositionHash, second.CompositionHash);
        Assert.Equal(
            first.OrderedPacks.Select(handle => handle.Manifest.PackId),
            second.OrderedPacks.Select(handle => handle.Manifest.PackId));
    }

    [Fact]
    public void Resolve_CompositionHash_IsDeterministicAndSensitiveToPackBytesAndCapabilities()
    {
        static CompatPackHandle Build(char shaFill, bool withCapability)
            => CompatPackCompositionTestData.Handle(
                "community.hash", CompatPackCompositionTestData.ShaOf(shaFill),
                new[] { CompatPackCompositionTestData.PresentInstruction("TEST_HASH", "community.hash") },
                withCapability ? new[] { "math.times-clamp.v1" } : null);

        Assert.True(Resolve(new[] { Build('a', false) }, out var baseline, out var errors),
            string.Join("; ", errors));
        Assert.True(Resolve(new[] { Build('a', false) }, out var repeated, out _));
        Assert.Equal(baseline.CompositionHash, repeated.CompositionHash);

        Assert.True(Resolve(new[] { Build('b', false) }, out var otherBytes, out _));
        Assert.NotEqual(baseline.CompositionHash, otherBytes.CompositionHash);

        Assert.True(Resolve(new[] { Build('a', true) }, out var withCapability, out _));
        Assert.NotEqual(baseline.CompositionHash, withCapability.CompositionHash);
    }

    [Fact]
    public void Resolve_CompositionLevelCapabilityClosure_ExpandsRequiresAndImplies()
    {
        var catalog = EngineCapabilityCatalog.ComposeCatalog(new[]
        {
            CompatPackCompositionTestData.Wired("root.v1", requires: new[] { "base.v1" }, implies: new[] { "implied.v1" }),
            CompatPackCompositionTestData.Wired("base.v1"),
            CompatPackCompositionTestData.Wired("implied.v1"),
        });
        var handle = CompatPackCompositionTestData.Handle(
            "community.caps",
            CompatPackCompositionTestData.ShaOf('c'),
            new[] { CompatPackCompositionTestData.PresentInstruction("TEST_CAPS", "community.caps") },
            capabilityIds: new[] { "root.v1" });
        var context = Context(capabilities: catalog);

        Assert.True(Resolve(new[] { handle }, out var result, out var errors, context),
            string.Join("; ", errors));

        Assert.Equal(new[] { "base.v1", "implied.v1", "root.v1" }, result.CapabilityIds);
    }

    [Fact]
    public void Resolve_CapabilityConflictAcrossPacks_Fails()
    {
        var catalog = EngineCapabilityCatalog.ComposeCatalog(new[]
        {
            CompatPackCompositionTestData.Wired("left.v1", conflictsWith: new[] { "right.v1" }),
            CompatPackCompositionTestData.Wired("right.v1"),
        });
        var left = CompatPackCompositionTestData.Handle(
            "community.left", CompatPackCompositionTestData.ShaOf('l'),
            new[] { CompatPackCompositionTestData.PresentInstruction("TEST_LEFT", "community.left") },
            capabilityIds: new[] { "left.v1" });
        var right = CompatPackCompositionTestData.Handle(
            "community.right", CompatPackCompositionTestData.ShaOf('r'),
            new[] { CompatPackCompositionTestData.PresentInstruction("TEST_RIGHT", "community.right") },
            capabilityIds: new[] { "right.v1" });

        Assert.False(Resolve(new[] { left, right }, out var result, out var errors, Context(capabilities: catalog)));

        Assert.Null(result);
        Assert.Contains(errors, error => error.Contains("capability conflict")
            && error.Contains("left.v1") && error.Contains("right.v1"));
    }

    [Fact]
    public void Resolve_DualFormMismatch_Fails()
    {
        // METHOD 投影名两侧必须同态：只声明 function 一侧的 Present 会让 plan 指令面与
        // 运行期注册表（函数可见性驱动的 METHOD 指令）不一致，按 fail-closed 拒载。
        var context = Context(methodProjectedInstructionNames: new HashSet<string>(StringComparer.Ordinal)
        {
            "MIRRORTEST",
        });
        var handle = CompatPackCompositionTestData.Handle(
            "community.mirror", CompatPackCompositionTestData.ShaOf('m'),
            new[] { CompatPackCompositionTestData.PresentFunction("MIRRORTEST", "community.mirror", "Int64") });

        Assert.False(Resolve(new[] { handle }, out var result, out var errors, context));

        Assert.Null(result);
        Assert.Contains(errors, error => error.Contains("双形态") && error.Contains("MIRRORTEST"));
    }

    [Fact]
    public void Resolve_DualFormConsistent_MirrorsBothSides()
    {
        var context = Context(methodProjectedInstructionNames: new HashSet<string>(StringComparer.Ordinal)
        {
            "MIRRORTEST",
        });
        var handle = CompatPackCompositionTestData.Handle(
            "community.mirror", CompatPackCompositionTestData.ShaOf('m'),
            new[]
            {
                CompatPackCompositionTestData.PresentFunction("MIRRORTEST", "community.mirror", "Int64"),
                CompatPackCompositionTestData.PresentInstruction("MIRRORTEST", "community.mirror"),
            });

        Assert.True(Resolve(new[] { handle }, out var result, out var errors, context),
            string.Join("; ", errors));

        var functionKey = new CompatSymbolKey(CompatSymbolKind.Function, "MIRRORTEST");
        var instructionKey = new CompatSymbolKey(CompatSymbolKind.Instruction, "MIRRORTEST");
        Assert.Equal(CompatSymbolStateKind.Present, result.SymbolStates[functionKey].Kind);
        Assert.Equal(CompatSymbolStateKind.Present, result.SymbolStates[instructionKey].Kind);
        Assert.Equal("community.mirror", result.SymbolStates[functionKey].OwnerPackId);
        Assert.Equal("community.mirror", result.SymbolStates[instructionKey].OwnerPackId);
    }

    [Fact]
    public void Resolve_CrossPackHideInstructionAndVariantSelection_Fails()
    {
        // 跨 kind 死契约：A 隐藏 instruction:X、B 选择 variant:X 时，plan 会同时含"X 不可见"
        // 与"变体选择"，运行期变体静默无效果。单包层已由 CompatPackRules 拒载，组合层必须同判。
        var hider = Handle("community.hider", 'h',
            states: new[] { Hidden("SETBGIMAGE", "community.hider") });
        var selector = CompatPackCompositionTestData.Handle(
            "community.selector", CompatPackCompositionTestData.ShaOf('s'),
            surfaceStates: new[]
            {
                CompatPackCompositionTestData.VariantSelection(
                    "SETBGIMAGE", "builtin:snake", "community.selector"),
            });

        Assert.False(Resolve(new[] { hider, selector }, out var result, out var errors));

        Assert.Null(result);
        Assert.Contains(errors, error => error.Contains("SETBGIMAGE")
            && error.Contains("community.hider")
            && error.Contains("community.selector")
            && error.Contains("不可共存"));
    }

    [Fact]
    public void Resolve_CrossPackHideMethodProjectedFunctionAndVariantSelection_Fails()
    {
        // 函数侧语义与规则层一致：METHOD 投影名的 function 隐藏镜像为 instruction 不可见，
        // 因而同样让同名变体选择成为死契约（双形态闭包也会独立报错，两条错误都保留）。
        var context = Context(methodProjectedInstructionNames: new HashSet<string>(StringComparer.Ordinal)
        {
            "EXISTVAR",
        });
        var hider = Handle("community.hider", 'h',
            states: new[] { CompatPackCompositionTestData.HiddenFunction("EXISTVAR", "community.hider") });
        var selector = CompatPackCompositionTestData.Handle(
            "community.selector", CompatPackCompositionTestData.ShaOf('s'),
            surfaceStates: new[]
            {
                CompatPackCompositionTestData.VariantSelection(
                    "EXISTVAR", "builtin:snake", "community.selector"),
            });

        Assert.False(Resolve(new[] { hider, selector }, out var result, out var errors, context));

        Assert.Null(result);
        Assert.Contains(errors, error => error.Contains("EXISTVAR")
            && error.Contains("community.hider")
            && error.Contains("community.selector")
            && error.Contains("不可共存"));
    }

    [Fact]
    public void Resolve_DeltaPackVersionMismatch_Fails()
    {
        // 防御性对账：依赖版本匹配/哈希用 manifest.PackVersion，若冻结差量版本漂移，
        // "被校验的版本"与"入账的版本"会不同；必须在组合入口拒载。
        var handle = CompatPackCompositionTestData.Handle(
            "community.version", CompatPackCompositionTestData.ShaOf('v'),
            new[] { CompatPackCompositionTestData.PresentInstruction("TEST_VERSION", "community.version") },
            deltaPackVersion: "2.0.0");

        Assert.False(Resolve(new[] { handle }, out var result, out var errors));

        Assert.Null(result);
        Assert.Contains(errors, error => error.Contains("packVersion")
            && error.Contains("2.0.0") && error.Contains("1.0.0"));
    }

    [Fact]
    public void InvalidVersionRange_IsRejectedAtManifestParse_BeforeResolution()
    {
        // 版本范围在清单解析段即 fail-closed（与 Core SemanticVersionRange 同语义），非法范围
        // 的包无法进入组合；resolver 仍保留 IsValid 防御（手工差量不得绕过契约）。
        const string json = """
        {
          "packId": "community.a",
          "packVersion": "1.0.0",
          "contractAbi": "1.0",
          "engineApi": "1.0",
          "requires": {
            "packs": [
              { "packId": "community.b", "versionRange": "[1.0.0,)" }
            ]
          }
        }
        """;

        Assert.False(CompatPackManifest.TryParse(json, out _, out var errors));
        Assert.Contains(errors, error => error.Contains("版本范围不合法") && error.Contains("[1.0.0,)"));
    }
}
