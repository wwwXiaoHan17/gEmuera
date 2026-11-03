# MarkdownLabel 测试与视觉验证工具

本目录是 **MarkdownLabel 重构（gEmuera 定制增强 fork）** 的回归测试契约，位于项目外协作规格
`D:/gemuera-reports/markdownlabel-rewrite-spec.md` 的 §10（测试与验收）。

- `run_tests.gd` — 无头断言测试（176 个用例，覆盖规格 §5/§6/§7/§8 全部可断言条目）。
- `visual_demo.gd` — 窗口模式视觉验证，把样例文档渲染成整窗截图。
- 本 README。

测试按**规格**写断言，不按当前实现写断言；用例失败可能意味着 addon 还没实现完。请不要为了让测试
变绿而放宽断言或修改 addon 源码——应当由实现方对齐规格，或在规格不明确处先和 Lead 确认。

## 前置条件

- Godot 4.7-stable mono（console 版），路径示例：
  `D:\OneDrive\桌面\Godot\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe`
- 项目根：`E:\MyCode\GodotCode\gEmuera-future`
- `run_tests.gd` 不需要 C# 构建、不需要渲染、不需要场景树。
- `visual_demo.gd` **必须窗口模式运行**（`--headless` 没有 viewport 纹理，拿不到截图）。
- 视觉截图输出目录 `D:\gemuera-reports\`（脚本会自动创建）。

## 运行命令

### 1) 无头断言测试

```powershell
& "D:\OneDrive\桌面\Godot\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe" `
  --headless --path "E:\MyCode\GodotCode\gEmuera-future" `
  --script "res://tools/markdownlabel-tests/run_tests.gd"
```

- 逐条打印 `PASS  <suite> :: <case>` / `FAIL  ...`，失败时附实际输出片段。
- 末尾打印 `PASS n / m` 与失败用例清单；全绿时输出 `PASS 176 / 176` + `RESULT: PASS`，退出码 0。
- **退出码 0 = 全过；1 = 有失败**（或 addon 脚本加载失败）。
- 若 addon 正在编辑导致 `.gd` 解析错误，runner 会打印
  `FAIL  bootstrap :: load addon script` 并以 1 退出，不会崩溃。

### 2) 视觉验证（窗口模式）

```powershell
& "D:\OneDrive\桌面\Godot\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe" `
  --path "E:\MyCode\GodotCode\gEmuera-future" `
  --script "res://tools/markdownlabel-tests/visual_demo.gd" --resolution 900x1200
```

- 运行时会短暂弹出窗口（约 1–2 秒），属正常现象。
- 输出：`D:\gemuera-reports\markdownlabel-visual.png`（900×1200 整窗截图），退出码 0。
- 样例覆盖：标题（ATX/Setext）、行内格式、块引用（单层 + 嵌套 + 引用内列表）、表格
  （表头加粗/底色、边框、三列对齐、斑马纹）、图片（居中 + 按 label 宽度自适应）、围栏代码块、
  无序/有序/任务列表、水平线。为便于肉眼检查，demo 脚本显式设置了表头底色、斑马纹、图片对齐、
  引用底色/竖条颜色和水平线颜色（都是规格 §5 的公开样式属性）。
- 判定（规格 §10 验收 3）：块引用有左侧竖条且缩进；表格有表头样式/边框/对齐；图片不超出 label 宽度；
  代码/列表/标题正常；截图中没有 BBCode 标签泄漏成可见文本。

## 测试覆盖（suite 分组）

| suite | 覆盖内容 |
| --- | --- |
| self-check | 校验器/语义辅助函数自身的正向 + 反向自检（防止测试工具自己坏了） |
| bootstrap | addon 脚本加载、实例化、公开方法、空文本/纯文本/CRLF/raw BBCode、`display_file()` + YAML frontmatter（frontmatter 存 `get_frontmatter()`、正文保留、`---` 不输出） |
| style properties | 规格 §5 全部新属性的存在性/类型/默认值；setter 必须触发 `queue_update()`；`_update()` 关闭逐帧轮询 |
| paragraphs | 段落换行、空行、行首空格、中英混排 |
| inline formatting | 转义、行内代码、粗体/斜体/删除线、内联链接与 hint、raw BBCode 保留 |
| code blocks | 围栏（info string/波浪线/未闭合/长闭合围栏）、缩进代码块、`[lb]`/`[rb]` 转义、代码块内 markdown 不解析；单行内容输出 `[code]content[/code]`，多行输出 `[code]l1\nl2[/code]`（内容前后不夹额外空行） |
| headings and anchors | ATX 1–6 级、标题标签顺序、结尾 `#`、Setext、`draw_horizontal_rule`、GitHub slug、重名 -1/-2、后缀二次碰撞仍唯一（`#foo`/`#foo-1`/`#foo-1-1`）、跨次转换锚点重置、Unicode slug、开头空行裁剪后的锚点偏移、表格块按 1 个 RichTextLabel paragraph 计数的锚点索引（不是 BBCode 输出行号） |
| horizontal rules | `---`/`***`/`___`、带空格写法、`hr_*` 属性（`hr_color` 输出 `color=#RRGGBBAA`，带 `#`）、Setext 与 hr 的区分 |
| block quotes | 单层/嵌套/懒续行/多段/标题/Setext/列表（引用内列表项也带竖条，task 列表的 checkbox 源行号与点击回写定位到原始 markdown 行）/代码（明文竖条）/表格（无竖条）/hr（无竖条）/文字颜色/斜体/背景色/包裹顺序 |
| lists | 无序/有序（任意起始数字）/嵌套（2–3 层）/混合嵌套/续行合并/松列表/列表结束、任务列表字符与 meta URL、`checkbox_records` 行号/标记/状态/文本/id 重置、`enable_checkbox_clicks=false`、`[X]`、自定义字符 |
| tables | GFM 分隔行规则（每格 `:?-+:?`：至少 1 个 `-`，`:--` 左对齐 / `--:` 右对齐 / `:-:` 居中对齐；用例覆盖 1 个与 3 个 `-` 两种写法）、表头/列数、cell 属性顺序（border→padding→bg）、`table_header_row`/`table_header_bold`/表头底色/奇偶行底色、三列对齐、`\|` 转义（含行尾转义竖线保留）、行内代码里的 `|`、分隔行比表头少列时列数取表头且分隔行不渲染、缺列补空/多列丢弃/空表头、无分隔行兼容、表格后接正文不吞行、单元格内联格式 |
| images | 本地图片/alt/tooltip、缺失回退 alt/关闭回退、按宽度自适应、显式尺寸扩展、`align` 覆盖、独占行对齐/行内不包裹/引用内不包裹、引用式/折叠式/快捷式图片、属性顺序 |
| links and autolinks | 内联链接、引用定义（行不输出/标题 hint 支持 `"…"`、`'…'`、`(…)` 三种写法/无引号尾巴不当作 title 而按普通段落保留/折叠/快捷/大小写不敏感/重复第一条优先）、角括号 URL/mailto、裸 URL/www 自动链接、尾标点、关闭自动链接、代码块与已有链接内不重复转换 |
| regressions | 同一节点两次转换输出/锚点/checkbox 记录完全一致、task checkbox 点击回写 `markdown_text` + 信号参数、样式属性变更影响下次转换、`_update()` 与 `_convert_markdown()` 一致、自定义 `_preprocess_line`/`_process_custom_syntax` 钩子、全类型文档合法 BBCode + 幂等 |

## 断言机制

1. **结构/子串断言**：`_has`/`_not_has`，用于标签顺序、属性字符串、meta URL 等输出格式稳定的地方。
2. **语义断言**：`_has_text` 把输出送 `RichTextLabel.parse_bbcode()` 再取 `get_parsed_text()`，
   自己 normalize（去掉 indent/ul 产生的 `\t` 与 `\r`），验证用户实际看到的文字。
3. **BBCode 标签栈校验**：`_assert_valid`（`_tag_errors`）扫描输出中的已知标签，检查：
   未知标签、未闭合、交叉闭合、`[table]` 的直接子标签只能是 `[cell]`、
   `[cell]` 内不允许块级标签（table/ul/ol/indent/hr/p）、表格内 cell 之外的散落文本。
4. **锚点/checkbox 记录读取**：优先读节点上的 `_header_anchor_paragraph` / `_checkbox_record`，
   兼容新命名 `anchors` / `checkbox_records`；都没有时回退到直接实例化
   `markdown_parser.gd` 读取 parser 字段。这样节点字段命名变化不会让测试失去意义。
5. **版本门控**：涉及 4.3+ 的 `[cell padding]`/`[img %]`、4.5+ 的 `[hr]`、4.6+ 的
   `[img alt]` 的断言按 `Engine.get_version_info().hex` 判断；当前用例在 4.7 全量执行。

## 新增用例

- 一个用例 = `_it("case name", func() -> void: ... )`，放在对应 `_suite_*()` 函数内；
  必须至少执行一次断言（否则 runner 会报 “no assertions executed” 防止运行时错误被静默吞掉）。
- 常用辅助：`_render(text, style)`（新建节点 → 设置样式 → 转换）、`_semantic`、
  `_has_text`、`_assert_valid`、`_first_table`/`_row_attrs`/`_row_contents`/`_attr_value`、
  `_checkboxes`/`_anchors`/`_checkbox_metas`、`_code_content`。
- 新增 suite 时在 `_initialize()` 里按顺序调用（失败时会打印 suite 名便于定位）。

## 当前状态

- 2026-11-03，Godot 4.7-stable mono：`PASS 176 / 176`，`RESULT: PASS`，退出码 0；
  stdout 无失败输出、stderr 为空。
- 视觉验证：`visual_demo.gd` 退出码 0，已生成并人工检查
  `D:\gemuera-reports\markdownlabel-visual.png`；块引用（含嵌套、引用内列表竖条、引用底色）、
  表格（表头加粗/底色、边框、三列对齐、斑马纹）、图片（居中且不超 label 宽度）、代码块、列表、
  标题、水平线均正常，无 BBCode 标签泄漏成可见文本。
- 无已知失败。首轮报告中提出的 §6.4 引用内列表竖条与 §6.8 重复引用定义行两个问题已由实现方修复；
  复审阶段补修的「引用内 task 列表源行号」「开头空行锚点偏移」「表格块锚点 paragraph 计数」也各补了
  1 条防复发用例（共 3 条）；verifier 复审 P2 的 4 类实现边界（表格分隔行少列、行尾转义 `\|`、
  锚点后缀二次碰撞、引用定义无引号尾巴不被当作 title）以及 `display_file()` + frontmatter 各补 1 条，
  全部通过，无已知失败。
