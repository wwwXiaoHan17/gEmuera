using System;
using System.Collections.Generic;

namespace MinorShift.Emuera.Compatibility;

/// <summary>
/// snake 方言的静态表面名集（P-D 步 5 从已退役的 snake 内置模块迁出）。
/// SnakeOnlyInstructionNames/SnakeOnlyFunctionNames 是 v24pure 基线必须隐藏的 snake/port 专有面
///（Compose 在模块 Declare 后统一声明，包/模块再在 Apply 阶段暴露自己声明的名字）；
/// DivergentFunctionContractNames 驱动蛇系函数参数契约；SnakeExcludedFunctionNames 是
/// snake 会话 Apply 阶段必须隐藏的 v24 基线函数（不含 陥落状態/陷落状態）。
/// </summary>
internal static class SnakeLegacySurfaceNames
{
    internal static readonly IReadOnlyCollection<string> SnakeOnlyInstructionNames = Array.AsReadOnly(new[]
    {
        "BITMAP_CACHE_ENABLE", "CALLSTR", "CLEARIMAGELAYER", "CLEARIMAGELAYER_ALL",
        "HTML_PRINTC", "HTML_PRINTLC", "JUMPSTR", "SET_SKIA_QUALITY", "SET_TEXT_DRAWING_MODE",
        "SETANIMETIMER", "SETIMAGELAYER", "SETIMAGELAYERL", "STRICT_FONT_FALLBACK",
        "TEXT_BGC_OFF", "TEXT_BGC_ON", "TINPUTNF", "TINPUTSNF", "TONEINPUTNF",
        "TONEINPUTSNF", "TRYCALLSTR", "TRYCCALLSTR", "TRYCJUMPSTR", "TRYJUMPSTR",
    });

    internal static readonly IReadOnlyCollection<string> SnakeOnlyFunctionNames = Array.AsReadOnly(new[]
    {
        "ACOS", "ARGLEN", "ASIN", "ATAN", "BGMCONTROL", "BITGET", "BITINDEXOFFIRST",
        "BITSET", "BITTOGGLE", "CBGSETCIMG", "CEIL", "COS", "DISABLE_INPUT_MACRO",
        "DT_CELL_GETF", "DT_CELL_SETF", "ENABLE_INPUT_MACRO", "EVAL", "EVALF", "EVALS",
        "EXISTSIMAGELAYER", "EXIST_IN_CSV", "EXIST_IN_ERD", "FLOOR", "GCLEARLOWALPHA", "GDRAWPOLYGON", "GDRAWPOLYGONADDPOINT",
        "GDRAWPOLYGONCLEARPOINT", "GDRAWSTRING", "GET_SKIA_QUALITY", "GET_TEXT_DRAWING_MODE",
        "GETANIMETIMER", "GETARGCOUNT", "GETCSVNOBYCALLNAME", "GETCSVNOBYMASTERNAME",
        "GETCSVNOBYNAME", "GETCSVNOBYNICKNAME", "GETLINEY", "GETMETHF", "GETPLATFORM",
        "GETSOUNDORBGMINFO", "GETVARF", "GFILLPOLYGON", "G_POLYGON_DRAW",
        "G_POLYGON_FILL", "G_POLYGON_POINT_ADD", "G_POLYGON_POINT_CLEAR", "GROTATE", "ISPLAYINGBGM",
        "ISPLAYINGSOUND", "MAP_FINDKEY", "MAP_FROMSTRING", "MAP_MERGE", "MAP_REMOVEIF",
        "MAP_TOSTRING", "MAP_VALUES", "MATCHALL", "MATCHALLEX", "MOUSEBUTTON", "ROUND",
        "SEQUENCEINPUT", "SIN", "SOUNDCONTROL", "SPRITECREATEFROMFILE", "SQL_CONNECT",
        "SQL_CONNECTION_OPEN", "SQL_DISCONNECT", "SQL_ESCAPE", "SQL_EXECUTE_NONQUERY",
        "SQL_EXECUTE_READER", "SQL_EXECUTE_SCALAR_FLOAT", "SQL_EXECUTE_SCALAR_LONG",
        "SQL_EXECUTE_SCALAR_STRING", "SQL_EXPORT_DT_XML", "SQL_EXPORT_MAP_XML", "SQL_IMPORT_DT_XML",
        "SQL_IMPORT_MAP_XML", "SQL_IMPORT_XML_CUSTOM", "SQL_P_EXECUTE_NONQUERY", "SQL_P_EXECUTE_READER",
        "SQL_P_EXECUTE_SCALAR_FLOAT", "SQL_P_EXECUTE_SCALAR_LONG", "SQL_P_EXECUTE_SCALAR_STRING",
        "SQL_READER_CLOSE", "SQL_READER_GET_FLOAT", "SQL_READER_GET_LONG", "SQL_READER_GET_STRING",
        "SQL_READER_ISNULL", "SQL_READER_READ", "STRFORMCHECK", "TAN", "TOFLOAT", "TOSTRF",
        "UNCHECKED_ADD", "UNCHECKED_MUL", "UNCHECKED_NEG", "UNCHECKED_SUB", "陥落状態", "陷落状态",
    });

    internal static readonly IReadOnlyCollection<string> DivergentFunctionContractNames = Array.AsReadOnly(new[]
    {
        "ABS", "ARGLEN", "CBGSETSPRITE", "CBRT", "EXISTVAR", "EXPONENT",
        "GETVAR", "GETVARS", "LIMIT", "LOG", "LOG10", "POWER", "SIGN",
        "SPRITECREATE", "SPRITECREATEFROMFILE", "SQRT", "TOINT",
        "UNCHECKED_ADD", "UNCHECKED_MUL", "UNCHECKED_NEG", "UNCHECKED_SUB",
    });

    // NOTE: 陥落状態/陷落状态 不在本排除清单 — eraTW 系口上包在多个角色 ERB 中直接调用它，
    // 定义只存在于个别口上包（如さとり包）里；隐藏后这些口上会在运行时抛无法解释的标识符错误。
    internal static readonly IReadOnlyCollection<string> SnakeExcludedFunctionNames = Array.AsReadOnly(new[]
    {
        "BITMAP_CACHE_ENABLE", "CBGSETCIMG", "DT_CELL_SETF", "GCLEARLOWALPHA",
        "GDRAWPOLYGON", "GDRAWPOLYGONADDPOINT", "GDRAWPOLYGONCLEARPOINT", "GDRAWSTRING", "GROTATE",
        "GETARGCOUNT", "GFILLPOLYGON", "MOUSEBUTTON", "SETANIMETIMER",
    });
}
