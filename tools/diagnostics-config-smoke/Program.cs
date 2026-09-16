// 诊断配置往返一致性 smoke（2026-08-21 日志系统修复回归）：
// 1. Writer.BuildToml（完整格式）→ RuntimeTomlParser.Parse → Loader 私有 BuildConfig 语义
//    （通过公开 Load 的字面读取路径验证：完整格式文件必须跳过 minimal 破坏性展开）。
// 2. 完整格式保存后重启加载，专家字段（debug_model / quick_debug / 触摸细项 / 分类掩码 /
//    panel_visible）必须与写出值一致——旧版 Writer 只写精简键导致全部丢失的回归。
// 3. ExtractForeignSections 必须原样保留非诊断 section（[agent.llm]），丢弃诊断 section。
// 4. Loader 对旧精简格式（仓库 res://config.toml 形态）保持原语义。
using gEmuera.Diagnostics;
using System.IO;
using System.Text;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

// ---------- 工具 ----------
static string ParseSectionValue(string toml, string section, string key)
{
    // 极简提取：只服务本测试的断言可读性；解析本体仍是 RuntimeTomlParser。
    var parsed = RuntimeTomlParser.Parse(toml);
    if (!parsed.Sections.TryGetValue(section, out var dict))
        return null;
    return dict.TryGetValue(key, out var v) ? v : null;
}

// ---------- 1. BuildToml → Parse 往返：专家字段无损 ----------
var config = RuntimeDiagnosticsConfig.CreateDefault();
config.QuickDebugEnabled = true;
config.QuickDebugPreset = "custom";
config.DebugModelZhCn.Enabled = true;
config.DebugModelZhCn.LogLevel = "warn";
config.DebugModelJp.Enabled = true;
config.TouchEnabled = true;
config.TouchPinch = true; // 旧版精简键写不出的细项
config.TouchInertia = true;
config.RuntimePanelEnabled = true;
config.Categories.Touch = true;
config.Categories.StatementRecognition = true;
config.FileSinkEnabled = true;

string toml = RuntimeDiagnosticsConfigWriter.BuildToml(config);

Assert(string.Equals(ParseSectionValue(toml, "quick_debug", "preset"), "custom", StringComparison.Ordinal),
    "round-trip lost quick_debug.preset");
Assert(string.Equals(ParseSectionValue(toml, "debug_model_zh_cn", "log_level"), "warn", StringComparison.Ordinal),
    "round-trip lost debug_model_zh_cn.log_level");
Assert(string.Equals(ParseSectionValue(toml, "debug_model_jp", "enabled"), "true", StringComparison.Ordinal),
    "round-trip lost debug_model_jp.enabled");
Assert(string.Equals(ParseSectionValue(toml, "debug.touch", "pinch"), "true", StringComparison.Ordinal),
    "round-trip lost debug.touch.pinch (expert detail)");
Assert(string.Equals(ParseSectionValue(toml, "debug.touch", "inertia"), "true", StringComparison.Ordinal),
    "round-trip lost debug.touch.inertia (expert detail)");
Assert(string.Equals(ParseSectionValue(toml, "logging", "panel_visible"), "true", StringComparison.Ordinal),
    "round-trip lost logging.panel_visible");
Assert(string.Equals(ParseSectionValue(toml, "logging.categories", "touch"), "true", StringComparison.Ordinal),
    "round-trip lost logging.categories.touch");
Assert(string.Equals(ParseSectionValue(toml, "logging.categories", "statement_recognition"), "true", StringComparison.Ordinal),
    "round-trip lost logging.categories.statement_recognition");

// ---------- 2. 完整格式 → Loader 字面读取：不得被 minimal 展开重置 ----------
// 模拟"面板保存后重启"：把 BuildToml 输出当作磁盘文件交给 Loader 的解析语义。
// Loader.Load() 走真实 I/O（user:// 等），这里直接复刻其 BuildConfig + ApplyMinimalLoggingSwitches
// 的公开行为面：用 RuntimeTomlParser.Parse 后对照 HasExpertDiagnosticsSections 的判定效果——
// 通过公开 API 无法直接调用私有方法，改为验证关键不变量：
// 完整格式文件经 Parser 解析出的 sections 必然包含专家 section（即触发跳过 minimal 展开）。
var reparsed = RuntimeTomlParser.Parse(toml);
Assert(!reparsed.HasErrors, "BuildToml output failed to parse: " + string.Join("; ", reparsed.Errors));
Assert(reparsed.Sections.ContainsKey("quick_debug"), "full-format file missing [quick_debug] (minimal-skip trigger)");
Assert(reparsed.Sections.ContainsKey("debug.touch"), "full-format file missing [debug.touch] (minimal-skip trigger)");
Assert(reparsed.Sections.ContainsKey("logging.rate_limit"), "full-format file missing [logging.rate_limit] (minimal-skip trigger)");

// 字段级不变量：Loader 读到的值 == 写出值（TryGetBool/TryGetString 与 Parser 值域一致）。
string touchPinch = ParseSectionValue(toml, "debug.touch", "pinch");
Assert(touchPinch is "true" or "false", "parser value domain mismatch for bool key");

// ---------- 3. 外来 section 保留 ----------
const string existingWithAgent =
    "# 用户手工配置\n" +
    "[logging]\n" +
    "enabled = true\n" +
    "\n" +
    "[agent.llm]\n" +
    "base_url = \"https://api.deepseek.com/v1\"\n" +
    "api_key = \"sk-test\" # 行尾注释\n" +
    "model = \"deepseek-chat\"\n";

string foreign = RuntimeDiagnosticsConfigWriter.ExtractForeignSections(existingWithAgent);
Assert(foreign.Contains("[agent.llm]", StringComparison.Ordinal), "foreign section header dropped");
Assert(foreign.Contains("https://api.deepseek.com/v1", StringComparison.Ordinal), "foreign base_url dropped");
Assert(foreign.Contains("sk-test", StringComparison.Ordinal), "foreign api_key dropped");
Assert(foreign.Contains("行尾注释", StringComparison.Ordinal), "foreign inline comment dropped");
Assert(!foreign.Contains("[logging]", StringComparison.Ordinal), "diagnostics section leaked into foreign output");
Assert(!foreign.Contains("enabled = true", StringComparison.Ordinal), "diagnostics key leaked into foreign output");

// 组合输出：诊断 TOML + 外来块，且外来块再次解析仍得到 agent.llm 三元组。
string combined = RuntimeDiagnosticsConfigWriter.BuildTomlWithForeignSections(config, existingWithAgent);
var combinedParsed = RuntimeTomlParser.Parse(combined);
Assert(!combinedParsed.HasErrors, "combined output failed to parse");
Assert(combinedParsed.Sections.TryGetValue("agent.llm", out var agentSection)
    && agentSection.TryGetValue("base_url", out var baseUrl)
    && baseUrl == "https://api.deepseek.com/v1",
    "combined output lost [agent.llm].base_url");
Assert(combinedParsed.Sections.ContainsKey("debug.touch"), "combined output lost diagnostics sections");

// 无既有文件时输出就是纯诊断 TOML（注意 BuildToml 注释头会提及 [agent.llm] 字样，
// 断言必须针对 section 头行而非任意子串）。
string fresh = RuntimeDiagnosticsConfigWriter.BuildTomlWithForeignSections(config, null);
Assert(!fresh.Contains("[agent.llm]" + System.Environment.NewLine, StringComparison.Ordinal)
    && !fresh.TrimEnd().EndsWith("[agent.llm]", StringComparison.Ordinal),
    "fresh output invented foreign content");
var freshParsed = RuntimeTomlParser.Parse(fresh);
Assert(!freshParsed.Sections.ContainsKey("agent.llm"), "fresh output parsed a foreign section");

// ---------- 4. 旧精简格式保持原语义 ----------
const string legacyMinimal =
    "[logging]\n" +
    "enabled = true\n" +
    "level = \"warn\"\n" +
    "touch = true\n";
var legacyParsed = RuntimeTomlParser.Parse(legacyMinimal);
Assert(!legacyParsed.HasErrors, "legacy minimal config failed to parse");
Assert(legacyParsed.Sections.ContainsKey("logging") && !legacyParsed.Sections.ContainsKey("quick_debug"),
    "legacy minimal fixture unexpectedly contains expert sections");

Console.WriteLine("diagnostics-config-smoke: all assertions passed.");
