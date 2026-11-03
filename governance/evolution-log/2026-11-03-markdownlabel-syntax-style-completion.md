# 2026-11-03 MarkdownLabel 2.0：Markdown 语法与样式补全（第三方插件重构）

## 任务目标与成果

一句话可验收：把 addons/markdownlabel 从上游 1.4.0 升级为 gEmuera 定制 2.0.0，补全块引用（>）、GFM 表格、图片等缺失语法与样式，
并修复旧解析器的状态残留/换行/定位缺陷；判定口径 = tools/markdownlabel-tests/run_tests.gd 168/168 PASS（exit 0）
+ visual_demo 截图人工核对 + example.tscn/manual.md 实际渲染正常 + 独立 verifier 复审 92/100、P0/P1=0、P2 全部关闭（可交付）。

现象与根因（修复/回归类必写）：
- 现象：assets/text/manual.md 开头的 > 块引用在启动器"操作手册"页显示为字面 >；表格无表头/对齐/边框；图片不受控（自然尺寸会溢出 label 宽度）；
  fenced code 的语言标注（如 ```gdscript）不被识别；引用式链接/Setext 标题等缺失；同一节点多次更新后 header 锚点会累积重复（#a-1/#a-2）。
- 根因：旧实现是单文件 876 行、逐行"正则串烧"式解析（addons/markdownlabel/markdownlabel.gd 旧版）：
  1) 状态（_header_anchor_count、_header_anchor_paragraph、_checkbox_record）在 _convert_markdown 开头未重置；
  2) 锚点/checkbox 的"源行号/输出段落号"靠随手 += 的计数器推断，表格 _skip_line_break 的补丁会在表格后吞/多出换行；
  3) 每次循环内 RegEx.create_from_string（每行 ~10 次编译）、_escape_chars 每行 17 次 replacen；
  4) 图片/链接标题用"插入位置 = 常数 12 + 长度"硬算，标题/URL 一变就错位；
  5) 块级语法没有真正的 block 分类，>、表格、列表互相干扰，无法在不破坏旧行为的前提下扩展。

本任务改动（git status 可查）：
- 重写节点 addons/markdownlabel/markdownlabel.gd（450 行：公共 API + 19 个新样式/行为导出属性 + parser 集成 + 锚点/checkbox 回写修复 + 按需 set_process）。
- 新增 addons/markdownlabel/markdown_parser.gd（块级解析器：段落/标题/引用/列表/表格/代码/HR/引用定义；锚点与 checkbox 记录；约 1000 行）。
- 新增 addons/markdownlabel/markdown_inline.gd（内联转换器：转义/行内代码/图片/链接/自动链接/删除线/强调；约 950 行）。
- 新增 tools/markdownlabel-tests/{run_tests.gd, visual_demo.gd, README.md}（168 用例契约测试 + 窗口截图验证工具）。
- 文档与示例：README.md 重写、CHANGELOG.md 新增 2.0.0、example.md 新增、example.gd/example.tscn 更新、plugin.cfg version=2.0.0。
- 新增/生成的 .gd.uid 侧车：markdown_inline.gd.uid、markdown_parser.gd.uid（Godot --import 生成，属仓库惯例）。

## 关键决策与 Why

1. 三文件拆分（node / block parser / inline converter）而不是继续单文件：旧文件 876 行且语法相互耦合，
   拆开后 block 渲染可递归（引用内可放列表/表格/代码）、inline 可独立被表格单元格/列表项复用；用 preload 常量引用（不加 class_name，避免占用全局类名）。
   被否决：单文件增量补丁——无法解决"表格/列表在引用内递归"与"内联逐行无状态"两个结构问题。
2. 块引用用 [indent] + 逐行竖条模拟（RichTextLabel 没有 blockquote 标签）：实测 [indent] 跨行有效、嵌套 [indent] 可叠加；
   列表项文本行也加竖条（形如 "• │ item"），代码行用纯文本竖条（[code] 内容里不能放标签），表格/HR 只缩进不加竖条——这是 RichTextLabel 限制下的最接近 GitHub 的方案。
   被否决：用 [table] 双列模拟左侧色条（重、破坏表格嵌套）、用 [bgcolor] 模拟（只覆盖文本宽度、多行断裂）。
3. 表格样式只走 per-cell 属性（[cell border=/padding=/bg=]），不写 [table=... border=]：窗口探针实测 [table=2 border=#ff0000] 属非法 table 选项，
   会让后续表格布局严重错乱（列宽塌缩、文字逐字换行），而 [cell border/bg/padding] 在 4.7 渲染正常。斑马纹用"每个数据行输出单色 bg"而不是 bg=odd,even 双色：
   实测 RichTextLabel 的 odd/even 含表头行计数，有表头时双色会错位。
4. 图片 fit 只在"自然宽 > label 宽且 label 宽 > 0"时输出 width=100%：无条件 100% 会把小图标放大；同时把"是否含宽度敏感图片"上报给节点，
   resize 时只在这种情况下重新转换（避免每帧轮询/布局回环）。
5. 保留 1.4.0 兼容行为：raw BBCode 直写、#Title（无空格）标题、每源行一段（不做 CommonMark 软换行合并，避免破坏中文手册显式换行）；
   7 个以上 # 按段落、GFM delimiter 每格 >=1 个 -、无分隔行 pipe 表格继续兼容。
6. 版本门控按 Engine.get_version_info().hex：4.3 才有 [cell padding]/图片 %/em 尺寸，4.5 才有 [hr]，4.6 才有 [img alt=]；4.2 降级路径代码上保留（本机无法实跑 4.2，见验证记录）。
7. 治理升级：把"GDScript/独立 addon 的契约测试 + 窗口截图验证"写进 AGENTS.md 构建与验证（第 95 行）；把"插件/组件增强型"用户提示词模式写进 prompt-patterns（第 18 行）。

排除项（查过但确认非缺陷）：
- [table] 单元格对齐必须用单元格内 [center]/[right] 标签，不能用 [cell align=...]（4.7 实测无 align 选项；视觉探针确认包裹标签后对齐生效）。
- 表格内图片禁用自动 fit（in_table 上下文）：否则 width=100% 按 label 宽度算会溢出单元格；这是设计取舍，README 已记录。
- 4.7 下 @tool 节点在编辑器导入/场景加载均正常（--import exit 0，example.tscn 可 instantiate），无编辑器脚本报错。

## 验证记录

全部命令在 2026-11-03 本机执行，项目根 E:\MyCode\GodotCode\gEmuera-future，Godot 4.7-stable mono。
- [实测] 契约测试：`Godot --headless --path . --script res://tools/markdownlabel-tests/run_tests.gd` → PASS 176 / 176，RESULT: PASS，exit 0（stdout 干净、stderr 空；168 为初版，经 verifier 复审两轮返工后补至 176）。
- [实测] 视觉验证：`--script res://tools/markdownlabel-tests/visual_demo.gd --resolution 900x1200`（窗口模式）→ exit 0，PNG D:/gemuera-reports/markdownlabel-visual.png；
  人工核对：引用竖条+嵌套、表头加粗/边框/三列对齐/斑马纹、图片居中不超宽、代码/列表/标题/HR 正常、无 BBCode 泄漏。
- [实测] 实际用例渲染：用节点把 example.md（3109 字符）与 assets/text/manual.md（342 字符）离屏渲染成 PNG（D:/gemuera-reports/example_render.png、manual_render.png），
  manual.md 的 > 块引用正常显示竖条与缩进；example.tscn headless instantiate 成功，两个 RichTextLabel parsed=242 / 2352。
- [实测] 编辑器导入：`Godot --headless --path . --import` exit 0（无 GDScript 解析错误），生成 markdown_parser.gd.uid / markdown_inline.gd.uid。
- [实测] API 反射（docs-demo 探针）：19/19 新属性名字/类型/默认值精确、export groups 正确、setter 均触发 queue_update（走读更早的 probe_docs*.gd，输出 API CHECK OK）。
- [实测] README 内链：38 个 heading / 35 条 # 锚点 0 失效；代码围栏成对（docs-demo 2026-11-03 21:xx 报告）。
- [实测] 性能（min-of-8 单次转换，全部修复后的最终代码；脚本 D:/gemuera-reports/bench2.gd）：README.md 42.1KB → 36.08 ms；manual.md 342B → 0.48 ms；
  表格密集 16.9KB → 41.11 ms；引用密集 25.5KB → 47.52 ms；纯文本 40KB → 23.78 ms；粗体/行内代码 40KB → 54.10 ms。
  注：首轮测量受并发 teammate 抢占 CPU 影响曾达 60-200ms，最终数值为复测最小值；未修旧版同口径基线（旧版不支持这些语法且未插桩），故无新旧对比 [未验证]。
- [实测] 编辑器工具模式：`Godot --headless --path . --import` exit 0，无 SCRIPT ERROR/Parse Error；inline 移除多余 @tool 后工具模式仍可转换。import 生成 run_tests.gd.uid / visual_demo.gd.uid（随交付文件入库）。
- [静态] 版本门控（4.2/4.3/4.5/4.6）逻辑走读正确；本机仅 4.7 二进制，旧版本行为 [未验证]。
- [实测] 基线说明：开工时 git status 已有其他会话未提交改动（AGENTS.md、FILE_STANDARD.md、Scripts/EmueraContent.cs、assets/text/manual.md、governance/*、project.godot、src/Core/* 等；此为原始工作树状态）；
  本任务只改了 addons/markdownlabel/（9 个文件）与 tools/markdownlabel-tests/（5 个文件），未把基线改动计入成果。
  提交时另建干净 worktree（基于 origin/dev e847d0b）重新落盘本任务文件与治理增量，避免把基线未提交改动混入 dev。
- [未验证] 4.2 实机、Android APK 内渲染（本任务未导出 APK；手动页为静态文本，风险低但按项目口径仍需 APK 结论）。
- [待 verifier] 独立复审：见下「复审与返工」段。

## AI 表现复盘

有效做法：
- 先实测 Godot 4.7 BBCode 能力（引擎 docs 的 _sources RST + 最小窗口截图探针）再写规格：探针直接发现 [table=2 border=...] 会崩布局、[cell border/bg/padding]、[indent]、[img width=100%] 的可用性，
  避免把错误设计写进契约（D:/gemuera-reports/bbcode_visual*.png 系列与 markdownlabel-rewrite-spec.md）。
- 规格即契约 + write scope 分派：Lead 写 node/parser，inline-parser 写 markdown_inline.gd，test-harness 写 168 条契约测试，docs-demo 写文档/示例，
  各写各的文件（tools/addons 分目录），通过 send_message 对齐规格；测试先失败后转绿，返工点可定位到具体 suite。
- 视觉验收用窗口模式截图（root.get_texture().get_image().save_png）而非只看无头文本：表格列宽塌缩、竖条位置、任务符号字形这些只有截图能看出来。

低效/返工：
- 触发点：用 JS 模板字符串生成 GDScript 探针/源码时，反引号、\n 转义与变量拼写（lloise vs lloose）多次出错；后果：3 次 GDScript Parse Error 排查约 10 分钟，
  其中一次误以为是 Godot 4.7 解析器 bug。可复用动作：写 GDScript 一律用 write 工具或 pwsh here-string，写完先 --check-only 再运行；避免跨语言转义。
- 触发点：早期 probe 用 `Select-Object -First N` 截断 PowerShell 管道；后果：Godot 进程被 SIGPIPE/提前终止，exit=-1 被误判为程序崩溃，多花一轮排查。
  可复用动作：无头 Godot 长时间命令一律 `*> file` 重定向后读文件，不用管道截断。
- 触发点：test-harness 初版标签校验器只接受紧贴标签名的 =属性，把 [img alt="..." width=...]、[hr height=...] 判成 stray "["；后果：8 个假 FAIL（不是实现问题）。
  可复用动作：契约测试工具自身要有 self-check suite（该 harness 已有，且本轮由 test-harness 修好）；看到"全量失败但只涉及某类标签"先怀疑 validator。
- 触发点：首版自测 168/168 全绿后直接视为可交付；独立 verifier 用 30+ 组规格外输入抓到 2 条 P1（引用内 checkbox 行号、前导空行锚点偏移）。
  可复用动作：涉及“新语法 × 旧状态功能”（锚点/checkbox/滚动）必须由独立复审或额外 probe 覆盖，不能只信自己写的契约测试；本轮把两条 P1 + 4 条 P2 边界补成 8 条回归用例（168→176）。

## 教训 → 具体优化动作

- (a) 已执行：AGENTS.md「构建与验证」新增 GDScript/独立 addon 验证条目（第 95 行）：无头 --script 契约测试 + 窗口模式截图核对，参考 tools/markdownlabel-tests/。
- (a) 已执行：governance/prompt-patterns/README.md 新增模式 9「插件/组件增强型」+ 应对策略（第 18 行）。
- (a) 已执行：契约测试与视觉工具入库 tools/markdownlabel-tests/，后续改 addons/markdownlabel 必须两条命令都跑（run_tests.gd exit 0 + visual_demo 截图）；AGENTS.md 条目已指到该目录。
- (b) 本轮不做：把"Godot RichTextLabel 能力探针"做成通用工具/脚本模板——升级去向：暂不升级（RichTextLabel BBCode 能力已稳定，复用频率低，成本>收益；如后续再改富文本组件可把 D:/gemuera-reports 的 probe 脚本收编进 tools/）。

## 遗留登记

- P1 强调/嵌套语法按简化规则（从左到右贪心，复杂 CommonMark 嵌套会保持字面）：不在本轮做——完整 CommonMark 强调算法成本高，当前近似已满足文档与手册场景；触发条件：用户报告具体渲染差异。已写入 README limitations。
- P1 4.2/4.3/4.6 未实机验证（本机只有 4.7 二进制，只有静态门控）：不在本轮做——需额外下载旧版 Godot；触发条件：插件要对外发布或用户使用旧版 Godot。
- P2 块引用内列表项渲染为 "• │ item"（RichTextLabel 无块级 border，无法做到竖条在 bullet 之前）：不在本轮做——属渲染近似；触发条件：用户要求与 GitHub 像素级一致。
- P2 example.md 故意保留一个缺失图片演示，运行 example.tscn 会打印一次预期 push_warning（文档已注明）：不在本轮做——演示 alt 回退需要；触发条件：用户不希望示例 stderr 有 warning 时删该行。
- 交付方式（2026-11-03）：用户明确要求“提交到 dev 分支”。原始工作树含其他会话未提交改动，故从最新 origin/dev（e847d0b）新建干净 worktree `D:\gemuera-reports\gEmuera-markdownlabel`（分支 `ai/markdownlabel-2.0`）重新落盘本任务文件后提交；按用户指令推送 dev（覆盖 AGENTS 默认 PR 流程）。
- 已排除（不计本任务）：assets/text/manual.md 末尾一行空行删除——开工时 git status 已含该文件 M，本任务从未写 manual.md（verifier 复审 P2-8 已核实，保持基线原样）。

## 复审与返工

独立 verifier（fresh 上下文、只读 + reports/ 写权限）分三轮复审，报告在 reports/markdownlabel-review.md（git 忽略）：

- 首轮：80 / 100，P0=0、P1=2、P2=10。两条 P1 均为「新语法 × 旧功能」交叉缺陷：
  1) 块引用内 task checkbox 的 checkbox_records.line 记录成引用块内部行号（不是源文本行号），点击会勾不动或改错行；
  2) 文档以空行开头时 _trim_blank_lines 裁掉前导空行但未回退锚点 paragraph 计数，锚点整体偏移/越界。
- 返工 1（P1 修复）：parser 为 quote 递归增加 source_lines 原始行号数组并逐层透传，list item 记录原始行号；_trim_blank_lines 按删除的前导行数同步回退 anchors/_paragraph_count；
  另修 verifier 复审期间提示的 URL [ ] 注入（percent-encode）与表格按 1 个 paragraph 计数。
  复评：85 / 100，可交付，P1 全部关闭；test-harness 补 3 条回归（171 用例）。
- 返工 2（P2 定点修复）：表格分隔行列数与表头不匹配（不再渲染可见 --- 行，列数取表头）；行尾转义竖线 \| 保留；锚点后缀二次碰撞（#foo/#foo-1/#foo-1-1）；引用定义只接受 "T"/'T'/(T) 三种标题（非标准行按段落）；
  README 三处措辞（内联链接标题、warning 频次、升级表锚点 slug 变更）；heading 双重 inline 解析、HR 判定 4.5 门控、inline @tool 一致性、[img] 方括号路径注入防护。
  复评：92 / 100，P0=0、P1=0、P2 全部关闭（P2-8 manual.md 经核实为基线其他会话改动，不计本任务）；test-harness 再补 5 条回归 → run_tests.gd 176/176 PASS。
- verifier 明确排除/确认项：视觉截图修复前后 sha256 逐字节一致（020B9285…），无视觉回归；旧 1.4.0 公共 API 17 项逐项实测保留；example.tscn 可加载；addon 三文件无新 parse/运行错误。
- 被推翻/修正的中间结论：首版 168 用例自测全绿被 verifier 找出 2 条 P1（说明“契约测试全绿”不等于交付就绪，交叉点必须有独立 probe）；首版表格 odd/even 双色斑马纹设计被实测推翻改为单色（有表头时双色错位）。
