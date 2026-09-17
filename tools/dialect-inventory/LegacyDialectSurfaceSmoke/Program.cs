using System;
using System.Linq;
using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using MinorShift.Emuera.Compatibility;

static class Program
{
    private static readonly string[] SnakeOnlyInstructions =
    {
        "CALLSTR", "CLEARIMAGELAYER", "CLEARIMAGELAYER_ALL",
        "HTML_PRINTC", "HTML_PRINTLC", "JUMPSTR", "SET_SKIA_QUALITY", "SET_TEXT_DRAWING_MODE",
        "SETANIMETIMER", "SETIMAGELAYER", "SETIMAGELAYERL", "STRICT_FONT_FALLBACK",
        "TEXT_BGC_OFF", "TEXT_BGC_ON", "TINPUTNF", "TINPUTSNF", "TONEINPUTNF",
        "TONEINPUTSNF", "TRYCALLSTR", "TRYCCALLSTR", "TRYCJUMPSTR", "TRYJUMPSTR",
    };

    private static readonly string[] V24ExcludedFunctions =
    {
        "ACOS", "ARGLEN", "ASIN", "ATAN", "BGMCONTROL", "BITGET", "BITINDEXOFFIRST",
        "BITSET", "BITTOGGLE", "CBGSETCIMG", "CEIL", "COS", "DISABLE_INPUT_MACRO",
        "DT_CELL_GETF", "DT_CELL_SETF", "ENABLE_INPUT_MACRO", "EVAL", "EVALF", "EVALS",
        "EXISTSIMAGELAYER", "FLOOR", "GCLEARLOWALPHA", "GDRAWPOLYGON", "GDRAWPOLYGONADDPOINT",
        "GDRAWPOLYGONCLEARPOINT", "GDRAWSTRING", "GET_SKIA_QUALITY", "GET_TEXT_DRAWING_MODE",
        "GETANIMETIMER", "GETARGCOUNT", "GETCSVNOBYCALLNAME", "GETCSVNOBYMASTERNAME",
        "GETCSVNOBYNAME", "GETCSVNOBYNICKNAME", "GETLINEY", "GETMETHF", "GETPLATFORM",
        "GETSOUNDORBGMINFO", "GETVARF", "GFILLPOLYGON", "G_POLYGON_DRAW", "G_POLYGON_FILL", "GROTATE",
        "G_POLYGON_POINT_ADD", "G_POLYGON_POINT_CLEAR", "ISPLAYINGBGM", "ISPLAYINGSOUND",
        "MAP_FINDKEY", "MAP_FROMSTRING", "MAP_MERGE", "MAP_REMOVEIF", "MAP_TOSTRING", "MAP_VALUES",
        "MATCHALL", "MATCHALLEX", "MOUSEBUTTON", "ROUND", "SEQUENCEINPUT", "SIN", "SOUNDCONTROL",
        "SPRITECREATEFROMFILE", "SQL_CONNECT", "SQL_CONNECTION_OPEN", "SQL_DISCONNECT", "SQL_ESCAPE",
        "SQL_EXECUTE_NONQUERY", "SQL_EXECUTE_READER", "SQL_EXECUTE_SCALAR_FLOAT", "SQL_EXECUTE_SCALAR_LONG",
        "SQL_EXECUTE_SCALAR_STRING", "SQL_EXPORT_DT_XML", "SQL_EXPORT_MAP_XML", "SQL_IMPORT_DT_XML",
        "SQL_IMPORT_MAP_XML", "SQL_IMPORT_XML_CUSTOM", "SQL_P_EXECUTE_NONQUERY", "SQL_P_EXECUTE_READER",
        "SQL_P_EXECUTE_SCALAR_FLOAT", "SQL_P_EXECUTE_SCALAR_LONG", "SQL_P_EXECUTE_SCALAR_STRING",
        "SQL_READER_CLOSE", "SQL_READER_GET_FLOAT", "SQL_READER_GET_LONG", "SQL_READER_GET_STRING",
        "SQL_READER_ISNULL", "SQL_READER_READ", "STRFORMCHECK", "TAN", "TOFLOAT", "TOSTRF",
        "UNCHECKED_ADD", "UNCHECKED_MUL", "UNCHECKED_NEG", "UNCHECKED_SUB", "陥落状態", "陷落状态",
    };

    // 注意：陥落状態/陷落状态 是 gEmuera 为 eraTW 口上定制的兜底函数（SnakeFallenStateMethod，
    // 模块注释明确 "must NOT be hidden"）——它在 snake 会话中必须可见，不在排除清单内。
    private static readonly string[] SnakeExcludedFunctions =
    {
        "BITMAP_CACHE_ENABLE", "CBGSETCIMG", "DT_CELL_SETF", "GCLEARLOWALPHA",
        "GDRAWPOLYGON", "GDRAWPOLYGONADDPOINT", "GDRAWPOLYGONCLEARPOINT", "GDRAWSTRING", "GROTATE",
        "GETARGCOUNT", "GFILLPOLYGON", "MOUSEBUTTON", "SETANIMETIMER",
    };

    private static int Main()
    {
        try
        {
            LegacyCompatibilityProfile v24 = LegacyCompatibilityProfile.CreateForProfile("v24pure", true);
            LegacyCompatibilityProfile v24WithoutScopedVariables = LegacyCompatibilityProfile.CreateForProfile("v24pure", false);
            LegacyCompatibilityProfile snake = LegacyCompatibilityProfile.CreateForProfile("snake", true);

            Assert(v24.IsInstructionVisible("PRINT"), "v24 lost a baseline instruction.");
            Assert(v24.IsInstructionVisible("CALLSHARP"), "v24 lost a baseline v24 instruction.");
            // BITMAP_CACHE_ENABLE：v24 参考无该指令（仅函数，经 METHOD 投影重新引入）；
            // snake 参考有指令。v24 会话中指令形态隐藏、函数经 ShouldProject… 投影可用。
            Assert(!v24.IsInstructionVisible("BITMAP_CACHE_ENABLE") && v24.ShouldProjectExpressionFunctionAsInstruction("BITMAP_CACHE_ENABLE"),
                "v24 must expose BITMAP_CACHE_ENABLE via METHOD projection, not the shared Snake instruction.");
            Assert(snake.IsInstructionVisible("BITMAP_CACHE_ENABLE"), "BITMAP_CACHE_ENABLE instruction must be visible in Snake.");
            Assert(v24.IsInstructionVisible("VARI") && v24.IsInstructionVisible("VARS"), "v24 lost enabled scoped-variable instructions.");
            Assert(!v24.IsInstructionVisible("OUTPUTLOG") && !snake.IsInstructionVisible("OUTPUTLOG"), "Godot-port-only instruction leaked into an upstream dialect.");
            Assert(!v24WithoutScopedVariables.IsInstructionVisible("VARI") && !v24WithoutScopedVariables.IsInstructionVisible("VARS"), "disabled scoped-variable instructions leaked into v24.");

            foreach (string key in SnakeOnlyInstructions)
            {
                Assert(!v24.IsInstructionVisible(key), $"Snake instruction leaked into v24: {key}.");
                Assert(snake.IsInstructionVisible(key), $"Snake instruction is missing from Snake: {key}.");
            }

            Assert(!v24.IsFunctionVisible("GROTATE"), "Godot-port-only expression function leaked into v24.");
            foreach (string key in V24ExcludedFunctions)
                Assert(!v24.IsFunctionVisible(key), $"Non-v24 expression function leaked into v24: {key}.");

            Assert(snake.IsFunctionVisible("ACOS"), "Snake lost a Snake expression function.");
            foreach (string key in SnakeExcludedFunctions)
                Assert(!snake.IsFunctionVisible(key), $"Snake exposed a key absent from its reference registry: {key}.");

            // eraFL 接口（闭包 {v24, erafl}）必须提供其目标游戏实测依赖的能力：
            // eraFL 游戏（erafl-master グラフィック生成.ERB:356）用指令语法
            // "SETANIMETIMER 1000 / フレームレート"，该指令在 v24 参考中不存在、
            // 由 snake 系共享仓注册——erafl 模块需显式声明才能解除 v24 基线的隐藏。
            LegacyCompatibilityProfile erafl = LegacyCompatibilityProfile.CreateForProfile("erafl", true);
            Assert(erafl.IsInstructionVisible("SETANIMETIMER"),
                "eraFL 接口必须提供 SETANIMETIMER 指令（eraFL 游戏启动依赖，グラフィック生成.ERB:356）。");
            // 隔离护栏：erafl 不得获得其他 snake 专属能力（函数层与指令层抽查）。
            Assert(!erafl.IsFunctionVisible("SQL_CONNECT"), "eraFL 接口泄漏了 snake 专属函数 SQL_CONNECT。");
            Assert(!erafl.IsInstructionVisible("CALLSTR"), "eraFL 接口泄漏了 snake 专属指令 CALLSTR。");
            // v24pure 保持对 v24 参考的忠实：SETANIMETIMER 函数可见（v24 参考 Creator.cs:211）、指令不可见。
            Assert(v24.IsFunctionVisible("SETANIMETIMER"), "v24pure 丢失了 v24 参考的 SETANIMETIMER 表达式函数。");
            Assert(!v24.IsInstructionVisible("SETANIMETIMER"), "v24pure 泄漏了 snake 系 SETANIMETIMER 指令。");
            // snake 会话：指令可见、函数形态隐藏（snake 参考无函数版，Creator.cs 只有 GETANIMETIMER）。
            Assert(snake.IsInstructionVisible("SETANIMETIMER"), "snake 会话丢失了 SETANIMETIMER 指令。");
            Assert(!snake.IsFunctionVisible("SETANIMETIMER"), "snake 会话泄漏了参考源码不存在的 SETANIMETIMER 函数形态。");

            // 指令变体表（模块替换贡献）：同名指令的 handler 变体由模块声明，取代投影缝硬编码。
            // 预期与迁移前 CreateProfileInstruction 的行为逐位一致：
            //   v24pure/erafl 的 FOR 走 v24 计数文法，snake 的 FOR 回退共享表；
            //   SETBGIMAGE 三态：v24pure/erafl=V24 变体，snake=SNAKE 变体；
            //   erafl 的 SETANIMETIMER 显式绑定共享表（模块化绑定归属，防 snake 侧分叉时隐式跟随）。
            Assert(v24.TryGetInstructionVariant("FOR", out LegacyInstructionVariant forV24)
                && forV24 == LegacyInstructionVariant.ForCountV24,
                "v24pure 的 FOR 必须绑定 v24 计数文法变体。");
            Assert(v24.TryGetInstructionVariant("SETBGIMAGE", out LegacyInstructionVariant bgV24)
                && bgV24 == LegacyInstructionVariant.SetBgImageV24,
                "v24pure 的 SETBGIMAGE 必须绑定 v24 变体。");
            Assert(!v24.TryGetInstructionVariant("SETANIMETIMER", out _),
                "v24pure 不应声明 SETANIMETIMER 变体绑定。");
            Assert(snake.TryGetInstructionVariant("FOR", out LegacyInstructionVariant forSnake)
                && forSnake == LegacyInstructionVariant.SharedTable,
                "snake 的 FOR 必须显式回退共享表（覆盖 v24 基线声明）。");
            Assert(snake.TryGetInstructionVariant("SETBGIMAGE", out LegacyInstructionVariant bgSnake)
                && bgSnake == LegacyInstructionVariant.SetBgImageSnake,
                "snake 的 SETBGIMAGE 必须绑定 snake 变体。");
            Assert(erafl.TryGetInstructionVariant("FOR", out LegacyInstructionVariant forErafl)
                && forErafl == LegacyInstructionVariant.ForCountV24,
                "erafl 的 FOR 继承 v24 基线计数文法。");
            Assert(erafl.TryGetInstructionVariant("SETBGIMAGE", out LegacyInstructionVariant bgErafl)
                && bgErafl == LegacyInstructionVariant.SetBgImageV24,
                "erafl 的 SETBGIMAGE 继承 v24 基线变体（与迁移前行为一致）。");
            Assert(erafl.TryGetInstructionVariant("SETANIMETIMER", out LegacyInstructionVariant timerErafl)
                && timerErafl == LegacyInstructionVariant.SharedTable,
                "erafl 的 SETANIMETIMER 必须显式绑定共享表 handler。");

            // capability 账本（quirk ledger）：snake 声明 7 项解析/调用/显示 quirk，erafl 声明
            // markup 系 + 3 项布尔 policy quirk，v24pure 不声明任何 quirk。
            // [LOAD] 日志输出的能力清单即此账本的运行期消费。
            foreach (string quirkId in GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.RequiredCapabilityIds)
            {
                Assert(snake.Plan.CapabilityIds.Contains(quirkId), $"snake 计划必须声明 quirk capability：{quirkId}。");
                Assert(!v24.Plan.CapabilityIds.Contains(quirkId), $"v24pure 不得声明 snake quirk：{quirkId}。");
                Assert(!erafl.Plan.CapabilityIds.Contains(quirkId), $"erafl 不得声明 snake quirk：{quirkId}。");
            }
            Assert(erafl.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.EraFlCompatibilityModule.DisplayExtendedHistoryBehavior)
                && erafl.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.EraFlCompatibilityModule.InputOmittedDefaultArgumentBehavior)
                && erafl.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.EraFlCompatibilityModule.PointerBlankStringBehavior),
                "erafl 计划必须声明三项布尔 policy quirk（display.extended-history / input.omitted-default-argument / input.pointer-blank-string）。");
            Assert(v24.Plan.CapabilityIds.Count == 0, "v24pure 不应声明任何 capability。");

            // quirk 账本 → policy 映射穷尽性：ISnakeCompatibilityPolicy 的全部布尔属性
            //（除 IsEnabled 与已语义塌缩的 UsesLazyResourceIndex）必须能被映射表覆盖，
            // 且映射表引用的属性名真实存在于接口——新增 policy 属性而不登记 capability 时在此失败。
            var snakePolicyProperties = typeof(ISnakeCompatibilityPolicy)
                .GetProperties()
                .Where(property => property.PropertyType == typeof(bool)
                    && property.Name != nameof(ISnakeCompatibilityPolicy.IsEnabled)
                    && property.Name != nameof(ISnakeCompatibilityPolicy.UsesLazyResourceIndex))
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var mappedProperties = GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.PolicyPropertyByCapabilityId.Values
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Assert(snakePolicyProperties.SequenceEqual(mappedProperties),
                $"蛇系 policy 属性与 capability 映射表不穷尽：接口=[{string.Join(',', snakePolicyProperties)}] 映射=[{string.Join(',', mappedProperties)}]。");

            // policy 取值由账本派生：snake 全真、v24pure 全假、erafl 的三项 quirk 生效。
            Assert(snake.Snake.AllowsPrivateArguments && snake.Snake.AllowsExtraCallArguments
                && snake.Snake.UsesParserDiagnostics && snake.Snake.ContinuesAfterStartupFault,
                "snake 会话的 quirk policy 必须由 capability 账本派生为真。");
            Assert(!v24.Snake.AllowsPrivateArguments && !v24.Snake.UsesFastDisplayRefresh,
                "v24pure 会话不得激活任何蛇系 quirk。");
            Assert(erafl.EraFl.UsesExtendedDisplayHistory
                && erafl.EraFl.IsOmittedDefaultArgument(',')
                && erafl.EraFl.ShouldSubmitBlankPointerStringInput(2, true),
                "erafl 会话的三项布尔 quirk 必须由 capability 账本派生为真。");
            Assert(!v24.EraFl.UsesExtendedDisplayHistory && !v24.EraFl.IsOmittedDefaultArgument(','),
                "v24pure 会话不得激活 erafl quirk。");

            // 描述符通道（生成清单）：plan.Dialect.Instructions/Functions 必须非空且与
            // LegacyDialectInventories 生成数据逐量一致——这是描述符路由激活的前置契约。
            Assert(v24.Plan.Dialect.Instructions.Count == GEmuera.Core.Compatibility.LegacyDialectInventories.V24InstructionNames.Length,
                "v24pure 指令描述符数量与生成清单不一致。");
            Assert(v24.Plan.Dialect.Functions.Count == GEmuera.Core.Compatibility.LegacyDialectInventories.V24Functions.Length,
                "v24pure 函数描述符数量与生成清单不一致。");
            Assert(snake.Plan.Dialect.Instructions.Count
                    == GEmuera.Core.Compatibility.LegacyDialectInventories.V24InstructionNames.Length
                    + GEmuera.Core.Compatibility.LegacyDialectInventories.SnakeDeltaInstructionNames.Length,
                "snake 指令描述符数量 != v24 + snake 增量。");
            Assert(erafl.Plan.Dialect.Instructions.Count
                    == GEmuera.Core.Compatibility.LegacyDialectInventories.V24InstructionNames.Length
                    + GEmuera.Core.Compatibility.LegacyDialectInventories.EraFlDeltaInstructionNames.Length,
                "erafl 指令描述符数量 != v24 + erafl 增量。");
            Assert(erafl.Plan.Dialect.TryGetInstruction("SETANIMETIMER", out var eraflTimer)
                && eraflTimer.ModuleId == "game.erafl",
                "erafl 计划必须由 game.erafl 模块声明 SETANIMETIMER 指令。");
            Assert(!v24.Plan.Dialect.TryGetInstruction("CALLSTR", out _),
                "v24pure 计划不得声明 snake 专属指令 CALLSTR。");

            // 蛇系函数参数契约（重载差异名集，snake 模块声明）：仅 snake 会话激活。
            Assert(snake.UsesDialectFunctionContract("ABS")
                && snake.UsesDialectFunctionContract("SQRT")
                && snake.UsesDialectFunctionContract("UNCHECKED_ADD"),
                "snake 会话必须激活蛇系参数契约名集。");
            Assert(!v24.UsesDialectFunctionContract("ABS")
                && !v24.UsesDialectFunctionContract("SQRT"),
                "v24pure 会话不得激活蛇系参数契约。");
            Assert(!erafl.UsesDialectFunctionContract("ABS"),
                "erafl 会话继承 v24 参数契约（不激活蛇系名集）。");

            // v18 基线方言（emuera_v18_exported 双源取证）：独立闭包 {gemuera.v18}，
            // 表面 = v24 投影减 39 指令 + 110 函数的 v24 后增差集；无 quirk capability。
            LegacyCompatibilityProfile v18 = LegacyCompatibilityProfile.CreateForProfile("v18", true);
            Assert(v18.IsInstructionVisible("PRINT") && v18.IsFunctionVisible("ABS"),
                "v18 丢失基线指令/函数。");
            Assert(!v18.IsInstructionVisible("SETBGIMAGE") && !v18.IsInstructionVisible("PRINTN")
                && !v18.IsInstructionVisible("VARI") && !v18.IsInstructionVisible("VARS")
                && !v18.IsInstructionVisible("CALLSTR") && !v18.IsInstructionVisible("SKIPLOG"),
                "v18 泄漏了 v24 后增或 snake 专属指令。");
            Assert(!v18.IsFunctionVisible("GETVAR") && !v18.IsFunctionVisible("DT_CREATE")
                && !v18.IsFunctionVisible("XML_DOCUMENT") && !v18.IsFunctionVisible("SQL_CONNECT"),
                "v18 泄漏了 v24 后增或 snake 专属函数。");
            Assert(v18.Plan.Dialect.Instructions.Count == GEmuera.Core.Compatibility.LegacyDialectInventories.V18InstructionNames.Length
                && v18.Plan.Dialect.Functions.Count == GEmuera.Core.Compatibility.LegacyDialectInventories.V18Functions.Length,
                "v18 描述符数量与生成清单不一致。");
            Assert(v18.Plan.CapabilityIds.Count == 0, "v18 基线不应声明 quirk capability。");

            // eraBlue（碧蓝度假村）：v24 基座 + SETANIMETIMER 增量 + 外部插件 capability。
            // 血统：游戏自带 Emuera.NET 1824+v24+EMv18+EEv55 启动器；插件经 CALLSHARP 调用。
            LegacyCompatibilityProfile erablue = LegacyCompatibilityProfile.CreateForProfile("erablue", true);
            Assert(erablue.IsInstructionVisible("SETANIMETIMER") && erablue.IsInstructionVisible("CALLSHARP"),
                "erablue 必须提供 SETANIMETIMER 指令与 CALLSHARP（插件调用）基座指令。");
            Assert(!erablue.IsInstructionVisible("CALLSTR") && !erablue.IsFunctionVisible("SQL_CONNECT")
                && !erablue.IsFunctionVisible("ACOS"),
                "erablue 不得泄漏 snake 专属能力。");
            Assert(erablue.Plan.CapabilityIds.Count == 2
                && erablue.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.EraBlueCompatibilityModule.ExternalPluginCapability)
                && erablue.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.EraBlueCompatibilityModule.ContinueAfterStartupFaultCapability),
                "erablue 计划必须恰好声明外部插件 + 启动容错两个 capability。");
            Assert(erablue.ContinuesAfterStartupFault,
                "erablue 会话必须由 capability 账本派生启动容错为真（汉化 mod 依赖）。");
            Assert(!v24.ContinuesAfterStartupFault && snake.ContinuesAfterStartupFault,
                "启动容错判定：v24pure 假、snake 真（capability 同源）。");

            // era megaten（Emuera1824+v8.1 私改血统）：v24 基座、面零增量、仅启动容错 quirk。
            // 实测证据：VELVET_ROOM.ERB:2860 的 DITEMTYPE:ARG:Persona(LOCALS)（私改文法）
            // 在严格 v24 下致命退出，原生启动器容错继续。
            LegacyCompatibilityProfile megaten = LegacyCompatibilityProfile.CreateForProfile("megaten", true);
            Assert(megaten.ContinuesAfterStartupFault
                && megaten.Plan.CapabilityIds.Count == 1
                && megaten.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.MegatenCompatibilityModule.ContinueAfterStartupFaultCapability),
                "megaten 计划必须恰好声明启动容错 capability。");
            Assert(megaten.IsInstructionVisible("PRINT") && megaten.IsInstructionVisible("CALLSHARP"),
                "megaten 保持 v24 基座指令面。");
            Assert(!megaten.IsInstructionVisible("SETANIMETIMER") && !megaten.IsInstructionVisible("CALLSTR")
                && !megaten.IsFunctionVisible("SQL_CONNECT"),
                "megaten 面增量为零：不得泄漏 snake 系名字（游戏未使用）。");
            Assert(erablue.TryGetInstructionVariant("SETBGIMAGE", out var erablueBg)
                && erablueBg == LegacyInstructionVariant.SetBgImageV24,
                "erablue 的 SETBGIMAGE 必须继承 v24 基线变体。");
            Assert(erablue.Plan.Dialect.Instructions.Count == GEmuera.Core.Compatibility.LegacyDialectInventories.V24InstructionNames.Length + 1
                && erablue.Plan.Dialect.Functions.Count == GEmuera.Core.Compatibility.LegacyDialectInventories.V24Functions.Length,
                "erablue 描述符数量 != v24 + SETANIMETIMER 增量。");

            // 诊断提示（TryGetUnselectedModuleHint）：v24pure 下查询 snake 专属名字 → 归属 game.snake；
            // snake 会话查询其自身隐藏的函数形态 → 不提示。
            Assert(v24.TryGetUnselectedModuleHint("SETANIMETIMER", out string hintModule1) && hintModule1 == "game.snake",
                "v24pure 查询 SETANIMETIMER 应提示归属 game.snake。");
            Assert(v24.TryGetUnselectedModuleHint("SQL_CONNECT", out _),
                "v24pure 查询 SQL_CONNECT 应提示归属 snake 模块。");
            Assert(!snake.TryGetUnselectedModuleHint("SETANIMETIMER", out _),
                "snake 会话查询 SETANIMETIMER 不应提示（snake 已选中）。");
            Assert(!v24.TryGetUnselectedModuleHint("PRINT", out _),
                "v24pure 查询基线指令 PRINT 不应提示。");

            AssertPackProjection();

            Console.WriteLine("Legacy dialect surface smoke passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }


    // —— 兼容包投影用例（宿主接线增量）：手工句柄（surface 贡献：注册 SETANIMETIMER/
    // SQL_CONNECT、隐藏 CALLSHARP/EXISTVAR）组装进 v24pure 计划，Compose 的包差量回放
    // 应使包注册名在 v24 会话可见、包隐藏名从基线表面移除；无包路径不触发（上方全部
    // 既有用例即证明）。——
    private sealed class SmokeSurfaceContribution : ISurfaceContribution
    {
        public string ContributionId => "smoke.pack.surface";

        public void Apply(IInstructionSurfaceRegistry instructions, IFunctionSurfaceRegistry functions)
        {
            instructions.RegisterInstruction("SETANIMETIMER");
            functions.RegisterFunction("SQL_CONNECT", "Int64");
            instructions.HideInstruction("CALLSHARP");
            functions.HideFunction("EXISTVAR");
        }
    }

    private static void AssertPackProjection()
    {
        var manifest = CompatPackManifest.TryParse(
            "{\"packId\":\"smoke.pack\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,\"capabilities\":[\"startup.continue-after-fault.v1\"]}",
            out var parsed, out var manifestErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", manifestErrors));
        var handle = new CompatPackHandle(
            manifest, null!, "Z:/smoke-pack.dll", new string('a', 64), new string('a', 64),
            new ISurfaceContribution[] { new SmokeSurfaceContribution() },
            Array.Empty<ICapabilityContribution>(),
            Array.Empty<IInstructionVariantContribution>(),
            Array.Empty<IPolicyContribution>(),
            new CompatPackLoadContext("Z:/smoke-pack.dll"));

        CompatibilityPlan baseline = BuiltInDialectCatalog.CreateLegacySessionPlan("v24pure");
        if (!CompatPackPlanAssembler.TryAssemble(baseline, new[] { handle }, out CompatibilityPlan assembled, out var assemblyErrors))
            throw new InvalidOperationException("pack assembly failed: " + string.Join("; ", assemblyErrors));

        LegacyCompatibilityProfile packed = LegacyCompatibilityProfile.Create(assembled, true, new[] { "smoke.pack" });

        Assert(packed.IsInstructionVisible("SETANIMETIMER"), "包注册指令 SETANIMETIMER 在 v24 会话不可见。");
        Assert(packed.IsFunctionVisible("SQL_CONNECT"), "包注册函数 SQL_CONNECT 在 v24 会话不可见。");
        Assert(!packed.IsInstructionVisible("CALLSHARP"), "包隐藏指令 CALLSHARP 仍泄漏在 v24 会话。");
        Assert(!packed.IsFunctionVisible("EXISTVAR"), "包隐藏函数 EXISTVAR 仍泄漏在 v24 会话。");
        Assert(packed.Plan.CapabilityIds.Contains("startup.continue-after-fault.v1"), "包 capability 未进入会话账本。");
        Assert(packed.ContinuesAfterStartupFault, "包声明的启动容错 quirk 未在会话生效。");

        // 信任边界：同一组装 plan 在无白名单入口（历史严格校验）下必须拒绝——
        // 未知模块不会因为"看起来像包"而被放行。
        bool strictRejected = false;
        try { LegacyCompatibilityProfile.Create(assembled, true); }
        catch (InvalidOperationException) { strictRejected = true; }
        Assert(strictRejected, "无白名单入口接受了未分类包模块（信任边界泄漏）。");

        // 白名单撞内置方言模块必须抛（防御深度回归钉：保留名拒载在加载段，这里验证
        // 即便有人绕过 Host 直接传入坏白名单，Compose 也不会放行）。
        bool builtinWhitelistRejected = false;
        try { LegacyCompatibilityProfile.Create(assembled, true, new[] { "game.snake" }); }
        catch (InvalidOperationException) { builtinWhitelistRejected = true; }
        Assert(builtinWhitelistRejected, "白名单含内置方言模块未被拒绝。");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
