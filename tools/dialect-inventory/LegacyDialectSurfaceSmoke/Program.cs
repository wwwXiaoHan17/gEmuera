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

            // capability 账本：erafl 声明 markup 系能力，v24pure/snake 不声明。
            // 这是 capability id 的契约级消费（[LOAD] 日志与真机诊断依赖它）。
            Assert(erafl.Plan.CapabilityIds.Count > 0, "erafl 模块必须声明 capability 清单。");
            Assert(v24.Plan.CapabilityIds.Count == 0, "v24pure 不应声明 capability。");
            Assert(snake.Plan.CapabilityIds.Count == 0, "snake 不应声明 capability。");

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

            Console.WriteLine("Legacy dialect surface smoke passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
