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
        "EXIST_IN_CSV", "EXIST_IN_ERD",
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
            // Skiav13.0 预设名来源探针：snake 表面必须可见（v24/erafl 不泄漏，见上方排除清单与隔离断言）。
            Assert(snake.IsFunctionVisible("EXIST_IN_CSV") && snake.IsFunctionVisible("EXIST_IN_ERD"),
                "snake 会话丢失了 Skiav13.0 的预设名来源探针（EXIST_IN_CSV/EXIST_IN_ERD）。");
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
            Assert(!erafl.IsFunctionVisible("EXIST_IN_CSV") && !erafl.IsFunctionVisible("EXIST_IN_ERD"),
                "eraFL 接口泄漏了 snake 专属探针函数 EXIST_IN_CSV/EXIST_IN_ERD。");
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

            // capability 账本（quirk ledger）：snake 声明 19 项解析/调用/显示 quirk，erafl 声明
            // markup 系 + 3 项布尔 policy quirk，v24pure 不声明任何 quirk。
            // [LOAD] 日志输出的能力清单即此账本的运行期消费。
            foreach (string quirkId in GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.RequiredCapabilityIds)
            {
                Assert(snake.Plan.CapabilityIds.Contains(quirkId), $"snake 计划必须声明 quirk capability：{quirkId}。");
                Assert(!v24.Plan.CapabilityIds.Contains(quirkId), $"v24pure 不得声明 snake quirk：{quirkId}。");
                // 例外：OUT 关键字是"蛇系血统"共享能力（同名 id declare.out-keyword.v1），
                // erafl 模块也声明它（血统继承）；其余 snake quirk 不得出现在 erafl 计划。
                if (quirkId != GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.OutKeyword)
                    Assert(!erafl.Plan.CapabilityIds.Contains(quirkId), $"erafl 不得声明 snake quirk：{quirkId}。");
            }
            Assert(erafl.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.EraFlCompatibilityModule.DisplayExtendedHistoryBehavior)
                && erafl.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.EraFlCompatibilityModule.InputOmittedDefaultArgumentBehavior)
                && erafl.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.EraFlCompatibilityModule.PointerBlankStringBehavior),
                "erafl 计划必须声明三项布尔 policy quirk（display.extended-history / input.omitted-default-argument / input.pointer-blank-string）。");
            Assert(erafl.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.EraFlCompatibilityModule.FloatLiteralsBehavior)
                && erafl.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.EraFlCompatibilityModule.OutKeywordBehavior)
                && erafl.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.EraFlCompatibilityModule.SafeArithmeticGuardBehavior)
                && erafl.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.EraFlCompatibilityModule.ExtendedHtmlAttributesBehavior),
                "erafl 计划必须声明四项蛇系血继承 quirk（type.float-literals / declare.out-keyword / arith.safe-arithmetic-guard / markup.font-extended-attributes）。");
            Assert(erafl.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.EraFlCompatibilityModule.DataTableXmlLeadingBomBehavior),
                "erafl 计划必须声明 DT_FROMXML 前导 BOM 剥离 quirk（datatable.xml-leading-bom.v1）。");
            Assert(!v24.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.EraFlCompatibilityModule.DataTableXmlLeadingBomBehavior),
                "v24pure 不得声明 erafl 的前导 BOM 剥离 quirk（DT_FROMXML 共享路径必须保持零变化）。");
            Assert(!v24.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.EraFlCompatibilityModule.FloatLiteralsBehavior),
                "v24pure 不得声明 erafl 浮点字面量 quirk。");
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
            Assert(erafl.EraFl.AllowsFloatLiterals && erafl.EraFl.AllowsOutKeyword
                && erafl.EraFl.UsesSafeArithmeticGuard && erafl.EraFl.AllowsExtendedHtmlAttributes
                && erafl.EraFl.StripsLeadingBomFromDataTableXml,
                "erafl 会话的四项蛇系血继承 quirk 与前导 BOM 剥离必须由 capability 账本派生为真。");
            Assert(!v24.EraFl.UsesExtendedDisplayHistory && !v24.EraFl.IsOmittedDefaultArgument(','),
                "v24pure 会话不得激活 erafl quirk。");
            Assert(!v24.EraFl.AllowsFloatLiterals && !v24.EraFl.AllowsOutKeyword
                && !v24.EraFl.UsesSafeArithmeticGuard && !v24.EraFl.AllowsExtendedHtmlAttributes
                && !v24.EraFl.StripsLeadingBomFromDataTableXml,
                "v24pure 会话不得激活 erafl 蛇系血继承 quirk 与前导 BOM 剥离。");

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
				&& megaten.Plan.CapabilityIds.Count == 6
                && megaten.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.MegatenCompatibilityModule.ContinueAfterStartupFaultCapability)
                && megaten.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.MegatenCompatibilityModule.LabelLookupCaseBehavior)
                && megaten.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.MegatenCompatibilityModule.RefOutNameBehavior)
				&& megaten.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.MegatenCompatibilityModule.PrivateSystemShadowBehavior)
				&& megaten.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.MegatenCompatibilityModule.StandardColorNameBehavior)
				&& megaten.Plan.CapabilityIds.Contains(GEmuera.Core.Compatibility.MegatenCompatibilityModule.HtmlRgbZeroAlphaBehavior)
                && megaten.Megaten.UsesVariableCaseForFunctionLabelLookup
				&& megaten.Megaten.AllowsOutAsVariableNameAfterRefKeyword
				&& megaten.Megaten.AllowsPrivateSystemVariableShadowing
				&& megaten.Megaten.UsesStandardColorNameTable
				&& megaten.Megaten.TreatZeroAlphaRgbHexAsOpaque,
				"megaten 计划必须声明启动容错 + 五个 behavior capability，并激活对应 policy flag。");
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
            Assert(v24.TryGetUnselectedModuleHint("SETANIMETIMER", out string? hintModule1) && hintModule1 == "game.snake",
                "v24pure 查询 SETANIMETIMER 应提示归属 game.snake。");
            Assert(v24.TryGetUnselectedModuleHint("SQL_CONNECT", out _),
                "v24pure 查询 SQL_CONNECT 应提示归属 snake 模块。");
            Assert(!snake.TryGetUnselectedModuleHint("SETANIMETIMER", out _),
                "snake 会话查询 SETANIMETIMER 不应提示（snake 已选中）。");
            Assert(!v24.TryGetUnselectedModuleHint("PRINT", out _),
                "v24pure 查询基线指令 PRINT 不应提示。");

            AssertMethodProjectionInventoryMatchesRuntime();
            AssertPackProjection();
            AssertV18PackEquivalence();
            // 末尾：进程内 fallback 降级探针（会改写 Program 的全局计划绑定，必须在所有
            // 基于独立 profile 对象的断言之后执行）。
            AssertFallbackRebindStateEquivalence();

            Console.WriteLine("Legacy dialect surface smoke passed.");
            return 0;
        }
        catch (Exception exception)
        {
            // 反射调用的诊断：解出 TargetInvocationException 的真实内因，否则只剩
            // "Exception has been thrown by the target of an invocation." 无法定位。
            Exception actual = exception;
            while (actual is System.Reflection.TargetInvocationException && actual.InnerException != null)
                actual = actual.InnerException;
            Console.Error.WriteLine(actual);
            return 1;
        }
    }


    // —— 生成清单 ↔ 运行时对照：MethodProjectedInstructionNames 必须等于引擎指令注册表里
    //    FunctionIdentifier.Method != null 的名字并集（六 profile——funcDic 的 METHOD 条目
    //    是全局的、按函数可见性过滤；并集 = 任何会话可暴露的 METHOD 投影名。包规则用它做
    //    addFunctions 镜像，因此不能用 v24pure 单 profile 的子集，否则包暴露 snake 系函数
    //    时运行时会多出计划未声明的 METHOD 指令）。反射访问 internal 类型，不引入 IVT。——
    private static void AssertMethodProjectionInventoryMatchesRuntime()
    {
        System.Reflection.Assembly engine = typeof(MinorShift.Emuera.Program).Assembly;
        Type profileType = engine.GetType("MinorShift.Emuera.Compatibility.LegacyCompatibilityProfile")
            ?? throw new InvalidOperationException("LegacyCompatibilityProfile type not found in engine assembly.");
        Type identifierType = engine.GetType("MinorShift.Emuera.GameProc.Function.FunctionIdentifier")
            ?? throw new InvalidOperationException("FunctionIdentifier type not found in engine assembly.");
        System.Reflection.MethodInfo createProfile = profileType.GetMethod(
            "CreateForProfile",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string), typeof(bool) },
            modifiers: null)
            ?? throw new InvalidOperationException("LegacyCompatibilityProfile.CreateForProfile not found.");
        System.Reflection.MethodInfo getInstructionNameDic = identifierType.GetMethod(
            "GetInstructionNameDic",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            types: new[] { profileType },
            modifiers: null)
            ?? throw new InvalidOperationException("FunctionIdentifier.GetInstructionNameDic not found.");

        Type functionCreatorType = engine.GetType("MinorShift.Emuera.GameData.Function.FunctionMethodCreator")
            ?? throw new InvalidOperationException("FunctionMethodCreator type not found in engine assembly.");
        System.Reflection.MethodInfo getMethodList = functionCreatorType.GetMethod(
            "GetMethodList",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            types: new[] { profileType },
            modifiers: null)
            ?? throw new InvalidOperationException("FunctionMethodCreator.GetMethodList not found.");

        string[] declared = GEmuera.Core.Compatibility.LegacyDialectInventories.MethodProjectedInstructionNames;
        var declaredSet = new HashSet<string>(declared, StringComparer.Ordinal);
        var runtimeMethodProjected = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string profileId, bool scopedVariableInstructionsEnabled) in new[]
        {
            ("v24pure", true), ("snake", true), ("erafl", true),
            ("v18", true), ("erablue", true), ("megaten", true),
            // scoped-variable 关闭是第三类条件符号（VARI/VARS），单独跑一次闭包校验。
            ("v24pure", false),
        })
        {
            object profile = createProfile.Invoke(null, new object[] { profileId, scopedVariableInstructionsEnabled })
                ?? throw new InvalidOperationException("CreateForProfile returned null for " + profileId + ".");
            object registry = getInstructionNameDic.Invoke(null, new[] { profile })
                ?? throw new InvalidOperationException("GetInstructionNameDic returned null for " + profileId + ".");
            var registryInstructionNames = new List<string>();
            foreach (object entry in (System.Collections.IEnumerable)registry)
            {
                Type entryType = entry.GetType();
                string? name = entryType.GetProperty("Key")?.GetValue(entry) as string;
                object? identifier = entryType.GetProperty("Value")?.GetValue(entry);
                if (string.IsNullOrEmpty(name) || identifier is null)
                    continue;
                registryInstructionNames.Add(name!);
                object? method = identifier.GetType().GetProperty("Method")?.GetValue(identifier);
                if (method is not null)
                    runtimeMethodProjected.Add(name!);
            }

            // §6.7 运行时精确闭包：plan 与真实投影双向包含。条件符号来自会话 profile
            //（模块隐藏集 + scoped-variable 关闭时的 VARI/VARS），METHOD 投影名恒为条件项。
            LegacyCompatibilityProfile linkedProfile = LegacyCompatibilityProfile.CreateForProfile(
                profileId, scopedVariableInstructionsEnabled);
            object functionRegistry = getMethodList.Invoke(null, new[] { profile })
                ?? throw new InvalidOperationException("GetMethodList returned null for " + profileId + ".");
            var registryFunctionNames = new List<string>();
            foreach (object entry in (System.Collections.IEnumerable)functionRegistry)
            {
                string? name = entry.GetType().GetProperty("Key")?.GetValue(entry) as string;
                if (!string.IsNullOrEmpty(name))
                    registryFunctionNames.Add(name!);
            }
            var conditionalSymbols = new HashSet<CompatSymbolKey>(linkedProfile.ConditionallyHiddenSymbols);
            var consumption = LegacyCompatibilityPlanConsumption.Validate(
                linkedProfile.Plan,
                registryInstructionNames,
                registryFunctionNames,
                conditionalSymbols,
                declaredSet);
            Assert(consumption.PlanHash == linkedProfile.Plan.CanonicalHash,
                $"profile {profileId}（scoped={scopedVariableInstructionsEnabled}）的 plan 消费快照丢失了计划身份。");
        }

        Assert(runtimeMethodProjected.SetEquals(declared),
            "生成清单 MethodProjectedInstructionNames 与运行时 METHOD 投影不一致（重跑 LegacyDialectInventoryGenerator）："
            + "缺=[" + string.Join(',', declared.Except(runtimeMethodProjected).Order(StringComparer.Ordinal)) + "]"
            + " 多=[" + string.Join(',', runtimeMethodProjected.Except(declared).Order(StringComparer.Ordinal)) + "]。");
        Assert(declared.SequenceEqual(declared.Order(StringComparer.Ordinal)),
            "MethodProjectedInstructionNames 必须按 Ordinal 排序。");
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

    // —— 包计划 ↔ 运行时注册表精确闭包（§6.7）：用引擎真实的 LegacyCompatibilityProfile
    //    （反射构造，不经 IVT）投影包 plan，再由 Core 的 TryCreateSessionView 验证
    //    registry ⊆ plan。这是"双形态闭包在运行时投影上成立"的端到端哨兵。 ——
    private static void AssertRuntimePlanClosure(CompatibilityPlan plan, string label)
    {
        System.Reflection.Assembly engine = typeof(MinorShift.Emuera.Program).Assembly;
        Type profileType = engine.GetType("MinorShift.Emuera.Compatibility.LegacyCompatibilityProfile")
            ?? throw new InvalidOperationException("LegacyCompatibilityProfile type not found in engine assembly.");
        System.Reflection.MethodInfo create = profileType.GetMethod(
            "Create",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(CompatibilityPlan), typeof(bool) },
            modifiers: null)
            ?? throw new InvalidOperationException("LegacyCompatibilityProfile.Create(CompatibilityPlan, bool) not found.");
        object profile = create.Invoke(null, new object[] { plan, true })
            ?? throw new InvalidOperationException("LegacyCompatibilityProfile.Create returned null for " + label + ".");

        var instructions = RuntimeRegistryKeys(
            engine, "MinorShift.Emuera.GameProc.Function.FunctionIdentifier", "GetInstructionNameDic", profileType, profile);
        var functions = RuntimeRegistryKeys(
            engine, "MinorShift.Emuera.GameData.Function.FunctionMethodCreator", "GetMethodList", profileType, profile);

        Assert(
            CompatibilityDescriptorRoute<string, string>.TryCreateSessionView(
                plan,
                instructions,
                StringComparer.Ordinal,
                functions,
                StringComparer.Ordinal,
                out _),
            label + "：运行时投影注册表超出组装 plan（双形态闭包失败，运行时会触发无包基线回退）。");

        // 反向闭包（plan 的非条件项 ⊆ registry）与生产 ConsumeCompatibilityPlan 同口径：
        // 条件符号 = 引擎 profile 的隐藏集（反射读取，含包隐藏差量/内置排除项）。
        System.Reflection.PropertyInfo conditionalProperty = profileType.GetProperty(
            "ConditionallyHiddenSymbols",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("LegacyCompatibilityProfile.ConditionallyHiddenSymbols not found.");
        var conditionalSymbols = new HashSet<CompatSymbolKey>(
            (IEnumerable<CompatSymbolKey>)conditionalProperty.GetValue(profile)!);
        LegacyCompatibilityPlanConsumption.Validate(
            plan,
            instructions.Keys,
            functions.Keys,
            conditionalSymbols,
            new HashSet<string>(GEmuera.Core.Compatibility.LegacyDialectInventories.MethodProjectedInstructionNames, StringComparer.Ordinal));
    }

    private static Dictionary<string, string> RuntimeRegistryKeys(
        System.Reflection.Assembly engine,
        string typeName,
        string methodName,
        Type profileType,
        object profile)
    {
        object registry = RuntimeRegistryObject(engine, typeName, methodName, profileType, profile);
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in RegistryNames(registry))
            keys[name] = "handler";
        return keys;
    }

    private static object RuntimeRegistryObject(
        System.Reflection.Assembly engine,
        string typeName,
        string methodName,
        Type profileType,
        object profile)
    {
        Type registryType = engine.GetType(typeName)
            ?? throw new InvalidOperationException(typeName + " not found in engine assembly.");
        System.Reflection.MethodInfo method = registryType.GetMethod(
            methodName,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            types: new[] { profileType },
            modifiers: null)
            ?? throw new InvalidOperationException(typeName + "." + methodName + " not found.");
        return method.Invoke(null, new[] { profile })
            ?? throw new InvalidOperationException(typeName + "." + methodName + " returned null.");
    }

    private static List<string> RegistryNames(object registry)
    {
        var names = new List<string>();
        foreach (object entry in (System.Collections.IEnumerable)registry)
        {
            string? name = entry.GetType().GetProperty("Key")?.GetValue(entry) as string;
            if (!string.IsNullOrEmpty(name))
                names.Add(name!);
        }
        return names;
    }

    // —— 冒烟包夹具（包组装门禁与 fallback 降级探针共用）：snake 全部 capability +
    //    erafl display.extended-history + 表面贡献（注册 SETANIMETIMER/SQL_CONNECT、
    //    隐藏 CALLSHARP/EXISTVAR）+ SETBGIMAGE=builtin:snake 变体选择。 ——
    private static CompatPackHandle CreateSmokePackHandle()
    {
        string snakeCapabilities = string.Join(
            ",",
            GEmuera.Core.Compatibility.SnakeCompatibilityCapabilities.RequiredCapabilityIds
                .Select(id => "\"" + id + "\""));
        string smokeJson = "{\"packId\":\"smoke.pack\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"capabilities\":[" + snakeCapabilities + ",\"display.extended-history.v1\"],"
            + "\"variantSelections\":{\"SETBGIMAGE\":\"builtin:snake\"}}";
        var manifest = CompatPackManifest.TryParse(smokeJson, out var parsed, out var manifestErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", manifestErrors));
        // T5：手工句柄也必须携带冻结差量——先经规则层回放贡献一次并冻结，再构造句柄；
        // 组装器此后只读 delta，不会二次执行 SmokeSurfaceContribution。
        var pack = new ManualCompatPack(manifest, new ICompatPackContribution[] { new SmokeSurfaceContribution() });
        if (!CompatPackLoader.ValidatePackEntry(
                pack, manifest, PackValidationContext(), out _, out var validationErrors, out _, out var delta,
                packSha256: new string('a', 64)))
            throw new InvalidOperationException("smoke.pack validation failed: " + string.Join("; ", validationErrors));
        return new CompatPackHandle(
            manifest, pack, "Z:/smoke-pack.dll", new string('a', 64), delta!,
            Array.Empty<CompatPackDependencyInfo>(),
            new CompatPackLoadContext("Z:/smoke-pack.dll", new Dictionary<string, byte[]>()));
    }

    /// <summary>手工 ICompatPack（不经 ALC）：仅用于冒烟夹具直接把贡献交给规则层冻结差量。</summary>
    private sealed class ManualCompatPack : ICompatPack
    {
        public ManualCompatPack(CompatPackManifest manifest, IReadOnlyList<ICompatPackContribution> contributions)
        {
            Manifest = manifest;
            Contributions = contributions;
        }

        public CompatPackManifest Manifest { get; }
        public IReadOnlyList<ICompatPackContribution> Contributions { get; }
    }

    /// <summary>冒烟手工包/真实包的校验上下文（与宿主 BuildValidationContext 同口径的最小集）。</summary>
    private static CompatPackValidationContext PackValidationContext() => new(
        currentContractAbi: new CompatVersion(1, 0),
        currentEngineApi: new CompatVersion(1, 0),
        capabilities: GEmuera.Core.Compatibility.EngineCapabilityCatalog.All,
        knownBuiltinVariantNames: new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
        baselineInstructions: new HashSet<string>(
            GEmuera.Core.Compatibility.LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
        baselineFunctions: new HashSet<string>(
            GEmuera.Core.Compatibility.LegacyDialectInventories.V24Functions.Select(entry => entry.Name),
            StringComparer.Ordinal),
        methodProjectedInstructionNames: new HashSet<string>(
            GEmuera.Core.Compatibility.LegacyDialectInventories.MethodProjectedInstructionNames,
            StringComparer.Ordinal));

    private static CompatibilityPlan AssembleSmokePackPlan()
    {
        CompatibilityPlan baseline = BuiltInDialectCatalog.CreateLegacySessionPlan("v24pure");
        if (!CompatPackPlanAssembler.TryAssemble(
                baseline, new[] { CreateSmokePackHandle() }, PackValidationContext(),
                out CompatibilityPlan assembled, out var assemblyErrors))
            throw new InvalidOperationException("smoke.pack assembly failed: " + string.Join("; ", assemblyErrors));
        return assembled;
    }

    private static void AssertPackProjection()
    {
        CompatibilityPlan assembled = AssembleSmokePackPlan();

        LegacyCompatibilityProfile packed = LegacyCompatibilityProfile.Create(assembled, true);
        AssertRuntimePlanClosure(assembled, "smoke.pack（手工句柄）");

        Assert(packed.IsInstructionVisible("SETANIMETIMER"), "包注册指令 SETANIMETIMER 在 v24 会话不可见。");
        Assert(packed.IsFunctionVisible("SQL_CONNECT"), "包注册函数 SQL_CONNECT 在 v24 会话不可见。");
        Assert(!packed.IsInstructionVisible("CALLSHARP"), "包隐藏指令 CALLSHARP 仍泄漏在 v24 会话。");
        Assert(!packed.IsFunctionVisible("EXISTVAR"), "包隐藏函数 EXISTVAR 仍泄漏在 v24 会话。");
        Assert(packed.Plan.CapabilityIds.Contains("startup.continue-after-fault.v1"), "包 capability 未进入会话账本。");
        Assert(packed.ContinuesAfterStartupFault, "包声明的启动容错 quirk 未在会话生效。");
        // capability 激活与内置模块解耦的回归哨兵：v24pure + 包声明 snake/erafl quirk id
        // 也必须安装对应 policy 并让具体 flag 生效（否则 P-D 无法把内置模块退役为包）。
        Assert(packed.Snake.UsesTimesClamp, "包声明 snake capability 未在 v24pure 会话激活对应 policy。");
        Assert(packed.Snake.IsEnabled, "完整 snake capability 集未在 v24pure 会话激活 module 语义。");
        Assert(packed.UsesDialectFunctionContract("TOINT"), "完整 snake capability 集未激活 DFC 重载契约。");
        Assert(packed.EraFl.UsesExtendedDisplayHistory, "包声明 erafl capability 未在 v24pure 会话激活对应 policy。");

        // 信任边界：pack 模块白名单来自 plan 数据；plan 声明了额外模块却未把其列入
        // PackModuleIds 时必须拒绝——未知模块不会因为"看起来像包"而被放行。
        var missingMetadataDialect = new DialectPlan(
            assembled.Dialect.Modules,
            assembled.Dialect.Ports,
            assembled.Dialect.Instructions,
            assembled.Dialect.Functions,
            assembled.Dialect.CanonicalHash,
            packModuleIds: Array.Empty<string>(),
            variantSelections: assembled.Dialect.VariantSelections);
        var missingMetadataPlan = new CompatibilityPlan(
            assembled.ProfileId,
            missingMetadataDialect,
            assembled.CapabilityIds,
            assembled.SaveProfileId,
            assembled.CanonicalHash);
        bool strictRejected = false;
        try { LegacyCompatibilityProfile.Create(missingMetadataPlan, true); }
        catch (InvalidOperationException) { strictRejected = true; }
        Assert(strictRejected, "plan 缺少 PackModuleIds 时接受了未分类包模块（信任边界泄漏）。");

        // 防御深度：即便有人伪造 plan 把内置方言模块列进 PackModuleIds，Compose 也必须拒绝。
        var builtinWhitelistDialect = new DialectPlan(
            assembled.Dialect.Modules,
            assembled.Dialect.Ports,
            assembled.Dialect.Instructions,
            assembled.Dialect.Functions,
            assembled.Dialect.CanonicalHash,
            packModuleIds: new[] { "game.snake" },
            variantSelections: assembled.Dialect.VariantSelections);
        var builtinWhitelistPlan = new CompatibilityPlan(
            assembled.ProfileId,
            builtinWhitelistDialect,
            assembled.CapabilityIds,
            assembled.SaveProfileId,
            assembled.CanonicalHash);
        bool builtinWhitelistRejected = false;
        try { LegacyCompatibilityProfile.Create(builtinWhitelistPlan, true); }
        catch (InvalidOperationException) { builtinWhitelistRejected = true; }
        Assert(builtinWhitelistRejected, "plan 把内置方言模块列入 PackModuleIds 未被拒绝。");

        // 变体投影：包声明 builtin:snake 选 SETBGIMAGE → 投影注入后 TryGetInstructionVariant
        // 返回真 enum（FunctionIdentifier 将据此构造 SNAKE_SETBGIMAGE handler）。
        Assert(packed.TryGetInstructionVariant("SETBGIMAGE", out LegacyInstructionVariant packVariant)
            && packVariant == LegacyInstructionVariant.SetBgImageSnake,
            "包声明的 builtin:snake 变体未注入 SETBGIMAGE 投影。");

        // 归属诊断（包表面差量回放按描述符归属分组后）：包隐藏名（CALLSHARP）owner = 包
        //（在闭包内 → 不提示）；内置 snake 声明名（CALLSTR）owner 保持在 game.snake
        //（包回放不覆盖内置归属 → 提示不丢失）；包注册名（SQL_CONNECT）经 Expose 抹除
        // owner → 不提示。
        Assert(!packed.TryGetUnselectedModuleHint("CALLSHARP", out _),
            "包隐藏名的归属模块在本会话闭包内，不应提示。");
        Assert(packed.TryGetUnselectedModuleHint("CALLSTR", out string? packHintOwner) && packHintOwner == "game.snake",
            "v24pure+包 会话查询 snake 专属指令 CALLSTR 应仍提示归属 game.snake。");
        Assert(!packed.TryGetUnselectedModuleHint("SQL_CONNECT", out _),
            "包注册名已解除隐藏，不应提示。");

        AssertMultiPackSurfaceReplay();
    }

    // —— v18 第一方数据包等价门禁：packs/gemuera.v18 的内嵌清单由生成器从
    //    LegacyDialectInventories（唯一事实源）计算 v24−v18 差集（禁止手改），经真实
    //    包 DLL → CompatPackLoader → CompatPackPlanAssembler 组装进 v24pure 基线后：
    //    (1) 指令/函数键集合与内置 v18 计划逐名相等；(2) 投影为 LegacyCompatibilityProfile
    //    （ApplyPackSurface 从 plan−基线差量反推隐藏）后可见性仍逐名相等。
    //    清单漂移（生成清单再生成后未重跑生成器、或手改 JSON）在此处先红。——
    private static void AssertV18PackEquivalence()
    {
        // 经包工程的 public 标记类取真实 DLL 路径（MSBuild 保证已构建并复制到本输出目录）。
        string packPath = typeof(GemueraV18Pack.GemueraV18PackMarker).Assembly.Location;
        var context = new CompatPackValidationContext(
            currentContractAbi: new CompatVersion(1, 0),
            currentEngineApi: new CompatVersion(1, 0),
            capabilities: GEmuera.Core.Compatibility.EngineCapabilityCatalog.All,
            knownBuiltinVariantNames: new HashSet<string>(StringComparer.Ordinal),
            baselineInstructions: new HashSet<string>(
                GEmuera.Core.Compatibility.LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
            baselineFunctions: new HashSet<string>(
                GEmuera.Core.Compatibility.LegacyDialectInventories.V24Functions.Select(entry => entry.Name),
                StringComparer.Ordinal),
            // 宿主口径：METHOD 投影名并集参与双形态闭包（hide 函数会镜像隐藏同名 METHOD 指令）。
            methodProjectedInstructionNames: new HashSet<string>(
                GEmuera.Core.Compatibility.LegacyDialectInventories.MethodProjectedInstructionNames,
                StringComparer.Ordinal));
        if (!CompatPackLoader.TryLoad(packPath, context, out CompatPackHandle? handle, out var loadErrors))
            throw new InvalidOperationException("v18 第一方数据包加载失败：" + string.Join("; ", loadErrors));

        try
        {
            CompatibilityPlan baseline = BuiltInDialectCatalog.CreateLegacySessionPlan("v24pure");
            if (!CompatPackPlanAssembler.TryAssemble(
                    baseline, new[] { handle! }, context,
                    out CompatibilityPlan assembled, out var assemblyErrors))
                throw new InvalidOperationException("v18 第一方数据包组装失败：" + string.Join("; ", assemblyErrors));

            CompatibilityPlan builtinV18Plan = BuiltInDialectCatalog.CreateLegacySessionPlan("v18");
            Assert(builtinV18Plan.Dialect.Instructions.Keys.Order(StringComparer.Ordinal)
                    .SequenceEqual(assembled.Dialect.Instructions.Keys.Order(StringComparer.Ordinal)),
                "v24pure + v18 包的指令键集合 != 内置 v18 计划（清单漂移？重跑 Generate-V18PackManifest）。");
            Assert(builtinV18Plan.Dialect.Functions.Keys.Order(StringComparer.Ordinal)
                    .SequenceEqual(assembled.Dialect.Functions.Keys.Order(StringComparer.Ordinal)),
                "v24pure + v18 包的函数键集合 != 内置 v18 计划（清单漂移？重跑 Generate-V18PackManifest）。");

            LegacyCompatibilityProfile builtinV18 = LegacyCompatibilityProfile.Create(builtinV18Plan, true);
            LegacyCompatibilityProfile packed = LegacyCompatibilityProfile.Create(assembled, true);
            AssertRuntimePlanClosure(assembled, "packs/gemuera.v18（真实包 DLL）");

            // 投影后可见性逐名相等：universe 取两侧计划键的并集（含保留名/隐藏差量）。
            IEnumerable<string> instructionUniverse = builtinV18.Plan.Dialect.Instructions.Keys
                .Concat(packed.Plan.Dialect.Instructions.Keys)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);
            foreach (string name in instructionUniverse)
                Assert(builtinV18.IsInstructionVisible(name) == packed.IsInstructionVisible(name),
                    $"v18 包投影与内置 v18 可见性不一致（指令 {name}：builtin={builtinV18.IsInstructionVisible(name)} packed={packed.IsInstructionVisible(name)}）。");
            IEnumerable<string> functionUniverse = builtinV18.Plan.Dialect.Functions.Keys
                .Concat(packed.Plan.Dialect.Functions.Keys)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);
            foreach (string name in functionUniverse)
                Assert(builtinV18.IsFunctionVisible(name) == packed.IsFunctionVisible(name),
                    $"v18 包投影与内置 v18 可见性不一致（函数 {name}：builtin={builtinV18.IsFunctionVisible(name)} packed={packed.IsFunctionVisible(name)}）。");

            // 抽样哨兵（失败信息可读性）：基线保留，v24 后增与 snake 专属不泄漏。
            Assert(packed.IsInstructionVisible("PRINT") && packed.IsFunctionVisible("ABS"),
                "v18 包会话丢失基线指令/函数。");
            Assert(!packed.IsInstructionVisible("SETBGIMAGE") && !packed.IsFunctionVisible("GETVAR")
                && !packed.IsFunctionVisible("SQL_CONNECT"),
                "v18 包会话泄漏了 v24 后增或 snake 专属名字。");
        }
        finally
        {
            handle!.Unload();
        }
    }

    // —— 多包回放归属（分组语义）：注册项按各自 packId 归属回放——每包的注册名都
    //    可见、任一包的隐藏差量生效；此前按"每包重放全部包累计名单"实现。——
    private sealed class MultiPackSurfaceA : ISurfaceContribution
    {
        public string ContributionId => "smoke.multipack.a";
        public void Apply(IInstructionSurfaceRegistry instructions, IFunctionSurfaceRegistry functions)
        {
            instructions.RegisterInstruction("SETANIMETIMER");
            instructions.HideInstruction("CALLSHARP");
        }
    }

    private sealed class MultiPackSurfaceB : ISurfaceContribution
    {
        public string ContributionId => "smoke.multipack.b";
        public void Apply(IInstructionSurfaceRegistry instructions, IFunctionSurfaceRegistry functions)
            => instructions.RegisterInstruction("SETIMAGELAYER");
    }

    private static CompatPackHandle MultiPackHandle(string packId, char shaFill, ISurfaceContribution surface)
    {
        string json = "{\"packId\":\"" + packId + "\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1}";
        var manifest = CompatPackManifest.TryParse(json, out var parsed, out var manifestErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", manifestErrors));
        var pack = new ManualCompatPack(manifest, new ICompatPackContribution[] { surface });
        if (!CompatPackLoader.ValidatePackEntry(
                pack, manifest, PackValidationContext(), out _, out var validationErrors, out _, out var delta,
                packSha256: new string(shaFill, 64)))
            throw new InvalidOperationException("multi-pack validation failed: " + string.Join("; ", validationErrors));
        return new CompatPackHandle(
            manifest, pack, "Z:/" + packId + ".dll", new string(shaFill, 64), delta!,
            Array.Empty<CompatPackDependencyInfo>(),
            new CompatPackLoadContext("Z:/" + packId + ".dll", new Dictionary<string, byte[]>()));
    }

    private static void AssertMultiPackSurfaceReplay()
    {
        CompatPackHandle packA = MultiPackHandle("smoke.pack.a", 'a', new MultiPackSurfaceA());
        CompatPackHandle packB = MultiPackHandle("smoke.pack.b", 'b', new MultiPackSurfaceB());

        CompatibilityPlan baseline = BuiltInDialectCatalog.CreateLegacySessionPlan("v24pure");
        if (!CompatPackPlanAssembler.TryAssemble(
                baseline, new[] { packA, packB }, PackValidationContext(),
                out CompatibilityPlan assembled, out var assemblyErrors))
            throw new InvalidOperationException("multi-pack assembly failed: " + string.Join("; ", assemblyErrors));

        LegacyCompatibilityProfile multi = LegacyCompatibilityProfile.Create(assembled, true);

        Assert(multi.IsInstructionVisible("SETANIMETIMER"), "包 A 注册指令 SETANIMETIMER 在多包会话不可见。");
        Assert(multi.IsInstructionVisible("SETIMAGELAYER"), "包 B 注册指令 SETIMAGELAYER 在多包会话不可见。");
        Assert(!multi.IsInstructionVisible("CALLSHARP"), "包 A 隐藏指令 CALLSHARP 在多包会话仍泄漏。");
        // 多包隐藏差量不记录 owner（宁缺毋错）；包归属名恒不提示（包模块必在闭包内）。
        Assert(!multi.TryGetUnselectedModuleHint("CALLSHARP", out _),
            "多包会话的包隐藏名不应给出归属提示。");
    }

    // —— fallback 降级探针（Task 4 fix I1 + I3，进程内）：绑定 "v24pure + smoke.pack" 的
    //    组装计划后，用引擎真实 registry（FunctionIdentifier/FunctionMethodCreator）填充一个
    //    IdentifierDictionary 实例（RuntimeHelpers.GetUninitializedObject + 反射设字段），再反射
    //    调用 Program.TryFallbackToNoPackBaseline("v24pure", out plan) 与生产方法
    //    RebindProfileRegistries()，最后与"直接按无包基线重绑"的派生项逐键逐类型比较。
    //    覆盖 I1 三条症状（包函数残留 / 基线恢复名缺席 / snake 保留字残留）与 I3（返回 true、
    //    plan 身份与无包基线一致）。不构造 VariableData（依赖 CSV 装载）：varTokenDic/
    //    localvarTokenDic 用空字典，变量名用固定合成输入（两侧同输入）。
    //    放在冒烟末尾：会改写 Program 的全局计划绑定，不能污染前面基于独立 profile 的断言。 ——
    private static readonly string[] ProfileIndependentReservedNames =
    {
        "IS", "TO", "INT", "STR", "REFFUNC", "STATIC", "DYNAMIC", "GLOBAL", "PRIVATE",
        "SAVEDATA", "CHARADATA", "REF", "__DEBUG__", "__SKIP__", "_",
    };

    private static void AssertFallbackRebindStateEquivalence()
    {
        System.Reflection.Assembly engine = typeof(MinorShift.Emuera.Program).Assembly;
        Type programType = engine.GetType("MinorShift.Emuera.Program")
            ?? throw new InvalidOperationException("MinorShift.Emuera.Program type not found.");
        Type identifierType = engine.GetType("MinorShift.Emuera.IdentifierDictionary")
            ?? throw new InvalidOperationException("IdentifierDictionary type not found.");
        Type profileType = engine.GetType("MinorShift.Emuera.Compatibility.LegacyCompatibilityProfile")
            ?? throw new InvalidOperationException("LegacyCompatibilityProfile type not found.");

        const System.Reflection.BindingFlags anyStatic = System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        const System.Reflection.BindingFlags anyInstance = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        // IdentifierDictionary.DefinedNameType 的私有枚举序数（Reserved=1、SystemMethod=3）。
        const int reservedNameType = 1;
        const int systemMethodNameType = 3;

        System.Reflection.MethodInfo configure = programType.GetMethod(
            "ConfigureCompatibilityPlan",
            anyStatic,
            binder: null,
            types: new[] { typeof(CompatibilityPlan), typeof(bool) },
            modifiers: null)
            ?? throw new InvalidOperationException("Program.ConfigureCompatibilityPlan(CompatibilityPlan, bool) not found.");
        System.Reflection.MethodInfo fallback = programType.GetMethod(
            "TryFallbackToNoPackBaseline",
            anyStatic,
            binder: null,
            types: new[] { typeof(string), typeof(CompatibilityPlan).MakeByRefType() },
            modifiers: null)
            ?? throw new InvalidOperationException("Program.TryFallbackToNoPackBaseline not found.");
        System.Reflection.PropertyInfo currentProfileProperty = programType.GetProperty("Compatibility", anyStatic)
            ?? throw new InvalidOperationException("Program.Compatibility property not found.");
        System.Reflection.MethodInfo rebind = identifierType.GetMethod("RebindProfileRegistries", anyInstance)
            ?? throw new InvalidOperationException("IdentifierDictionary.RebindProfileRegistries not found.");
        System.Reflection.MethodInfo recompute = identifierType.GetMethod(
            "RecomputeProfileDerivedNames", anyInstance, binder: null, types: Type.EmptyTypes, modifiers: null)
            ?? throw new InvalidOperationException("IdentifierDictionary.RecomputeProfileDerivedNames() not found.");

        Type definedNameType = identifierType.GetNestedType(
                "DefinedNameType", System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("IdentifierDictionary.DefinedNameType not found.");
        Type nameDictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), definedNameType);

        // 1) 绑定带包计划，按包 profile 的真实注册表派生 nameDic（fallback 前状态）。
        CompatibilityPlan packedPlan = AssembleSmokePackPlan();
        configure.Invoke(null, new object[] { packedPlan, true });
        object packedProfile = currentProfileProperty.GetValue(null)
            ?? throw new InvalidOperationException("Program.Compatibility returned null after packed-plan bind.");
        object packedInstructions = RuntimeRegistryObject(
            engine, "MinorShift.Emuera.GameProc.Function.FunctionIdentifier", "GetInstructionNameDic", profileType, packedProfile);
        object packedFunctions = RuntimeRegistryObject(
            engine, "MinorShift.Emuera.GameData.Function.FunctionMethodCreator", "GetMethodList", profileType, packedProfile);

        object packedDictionary = NewIdentifierDictionaryProbe(
            identifierType, nameDictionaryType, definedNameType, packedInstructions, packedFunctions);
        // 构造期等价路径：按包 profile 派生一次（Rebind 之前的 nameDic 状态）。
        recompute.Invoke(packedDictionary, null);
        SortedDictionary<string, int> before = ReadNamedTypes(GetNameDictionary(identifierType, packedDictionary));

        // 探针前提 = 旧实现的失败面：包函数在表、包隐藏函数缺席、snake 保留字在场。
        Assert(before.TryGetValue("SQL_CONNECT", out int sqlNameType) && sqlNameType == systemMethodNameType,
            "探针前提失败：带包 profile 的 nameDic 未包含包新增函数 SQL_CONNECT（SystemMethod）。");
        Assert(!before.ContainsKey("EXISTVAR"),
            "探针前提失败：包隐藏函数 EXISTVAR 仍在带包 nameDic 中（smoke.pack 本应隐藏它）。");
        Assert(before.TryGetValue("REFF", out int reffNameType) && reffNameType == reservedNameType
                && before.ContainsKey("VARIADIC"),
            "探针前提失败：带包 profile 未登记 snake 保留字 REFF/VARIADIC（capability 未激活？）。");

        // 2) 生产降级路径：Program.TryFallbackToNoPackBaseline("v24pure", out plan)。
        object?[] fallbackArguments = { "v24pure", null };
        bool fellBack = (bool)(fallback.Invoke(null, fallbackArguments) ?? false);
        Assert(fellBack, "Program.TryFallbackToNoPackBaseline(\"v24pure\") 返回 false。");
        CompatibilityPlan baselinePlan = fallbackArguments[1] as CompatibilityPlan
            ?? throw new InvalidOperationException("TryFallbackToNoPackBaseline 未产出 out plan。");
        Assert(baselinePlan.ProfileId == "v24pure",
            "fallback 产出的 plan profile 不是 v24pure：" + baselinePlan.ProfileId);
        CompatibilityPlan expectedBaseline = BuiltInDialectCatalog.CreateLegacySessionPlan("v24pure");
        Assert(baselinePlan.CanonicalHash == expectedBaseline.CanonicalHash,
            "fallback 产出的 plan 与 BuiltInDialectCatalog.CreateLegacySessionPlan(\"v24pure\") 哈希不一致。");
        Assert(baselinePlan.Dialect.Instructions.Keys.Order(StringComparer.Ordinal)
                    .SequenceEqual(expectedBaseline.Dialect.Instructions.Keys.Order(StringComparer.Ordinal))
                && baselinePlan.Dialect.Functions.Keys.Order(StringComparer.Ordinal)
                    .SequenceEqual(expectedBaseline.Dialect.Functions.Keys.Order(StringComparer.Ordinal)),
            "fallback 产出的 plan 表面键集与无包基线不一致。");

        // 3) 生产方法 RebindProfileRegistries()：按新 profile 重建注册表并重算 nameDic 派生项。
        rebind.Invoke(packedDictionary, null);
        SortedDictionary<string, int> after = ReadNamedTypes(GetNameDictionary(identifierType, packedDictionary));

        // 显式症状回归放在全量比较之前：失败信息直指 review 三条，而不是只报字典差异。
        Assert(!after.ContainsKey("SQL_CONNECT"), "fallback 后 nameDic 仍残留包新增函数 SQL_CONNECT。");
        Assert(after.TryGetValue("EXISTVAR", out int existvarNameType) && existvarNameType == systemMethodNameType,
            "fallback 后 nameDic 未恢复基线可见函数 EXISTVAR（SystemMethod）。");
        Assert(!after.ContainsKey("REFF") && !after.ContainsKey("VARIADIC"),
            "fallback 后 nameDic 仍残留 snake 保留字 REFF/VARIADIC。");

        // 对照：空字典实例直接按基线重绑（同一 Program.Compatibility / 同一空 varToken 输入）。
        object freshDictionary = NewIdentifierDictionaryProbe(
            identifierType, nameDictionaryType, definedNameType, packedInstructions, packedFunctions);
        rebind.Invoke(freshDictionary, null);
        SortedDictionary<string, int> fresh = ReadNamedTypes(GetNameDictionary(identifierType, freshDictionary));

        Assert(after.Count == fresh.Count,
            "fallback 后 nameDic 派生项键数(" + after.Count + ") != 直接按无包基线重绑(" + fresh.Count + ")。");
        foreach (KeyValuePair<string, int> pair in fresh)
        {
            bool present = after.TryGetValue(pair.Key, out int actual);
            Assert(present && actual == pair.Value,
                "fallback 后 nameDic 派生项与直接按无包基线重绑不一致：" + pair.Key
                + " 期望类型=" + pair.Value + " 实际=" + (present ? actual.ToString() : "<missing>"));
        }
    }

    // 反射填充一个 IdentifierDictionary 实例（不跑构造器：VariableData 依赖 CSV 装载）。
    // 只设置 RebindProfileRegistries / RecomputeProfileDerivedNames 真正读取的字段。
    private static object NewIdentifierDictionaryProbe(
        Type identifierType,
        Type nameDictionaryType,
        Type definedNameType,
        object instructionRegistry,
        object functionRegistry)
    {
        object instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(identifierType);
        SetInstanceField(identifierType, instance, "nameDic",
            NewSeededNameDictionary(nameDictionaryType, definedNameType));
        SetInstanceField(identifierType, instance, "profileDerivedNames",
            Activator.CreateInstance(nameDictionaryType)
                ?? throw new InvalidOperationException("profileDerivedNames dictionary construction failed."));
        SetInstanceField(identifierType, instance, "varTokenDic",
            NewEmptyInstanceDictionary(identifierType, "varTokenDic"));
        SetInstanceField(identifierType, instance, "localvarTokenDic",
            NewEmptyInstanceDictionary(identifierType, "localvarTokenDic"));
        SetInstanceField(identifierType, instance, "instructionDic", instructionRegistry);
        SetInstanceField(identifierType, instance, "methodDic", functionRegistry);
        return instance;
    }

    private static void SetInstanceField(Type type, object instance, string fieldName, object value)
    {
        System.Reflection.FieldInfo field = type.GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(type.Name + "." + fieldName + " not found.");
        field.SetValue(instance, value);
    }

    private static object NewEmptyInstanceDictionary(Type type, string fieldName)
    {
        System.Reflection.FieldInfo field = type.GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(type.Name + "." + fieldName + " not found.");
        return Activator.CreateInstance(field.FieldType)
            ?? throw new InvalidOperationException(type.Name + "." + fieldName + " dictionary construction failed.");
    }

    private static object GetNameDictionary(Type identifierType, object identifierDictionary)
    {
        System.Reflection.FieldInfo field = identifierType.GetField(
                "nameDic",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("IdentifierDictionary.nameDic not found.");
        return field.GetValue(identifierDictionary)
            ?? throw new InvalidOperationException("IdentifierDictionary.nameDic is null.");
    }

    private static object NewSeededNameDictionary(Type nameDictionaryType, Type definedNameType)
    {
        object dictionary = Activator.CreateInstance(nameDictionaryType)
            ?? throw new InvalidOperationException("nameDic dictionary construction failed.");
        System.Reflection.MethodInfo add = nameDictionaryType.GetMethod(
                "Add", new[] { typeof(string), definedNameType })
            ?? throw new InvalidOperationException("Dictionary<string, DefinedNameType>.Add not found.");
        object reserved = Enum.ToObject(definedNameType, 1);
        foreach (string name in ProfileIndependentReservedNames)
            add.Invoke(dictionary, new[] { (object)name, reserved });
        return dictionary;
    }

    private static SortedDictionary<string, int> ReadNamedTypes(object nameDictionary)
    {
        var source = nameDictionary as System.Collections.IDictionary
            ?? throw new InvalidOperationException("nameDic is not a non-generic IDictionary.");
        var result = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in source)
            result[(string)entry.Key] = Convert.ToInt32(entry.Value);
        return result;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
