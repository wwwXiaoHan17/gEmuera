# 2026-09-23 eraFL 模组化 + TIMES 浮点倍率修复（eraFL0.47 真机实证驱动）

## 任务

用户指令：将 erafl 也改成模组，同时修复 eraFL0.47 附件日志暴露的问题。附件三份日志：
`emuera.log`（当日，2455 条 `TIMES ..., 1.20` 类 `'.'` 词法警告 + `@SET_BODYSIZE_BUST`
运行期 CodeEE 崩溃）、`emuera_startup_errors.log`（2026-05-18 陈旧）、
`gemuera_auto_20260923-205437.log`（当日诊断，崩溃栈定位 BODYSIZE.ERB:209）。

## 根因（上一批的误判返工）

PR #23 把浮点字面量词法门控为 snake 专属时，eraFL 取向考证只 grep 了浮点**变量名**
（#DIMF/RESULTF/LOCALF/ARGF，确为零使用），漏了浮点**字面量**——eraFL0.47 的 `TIMES X, 1.20`
依赖浮点字面量词法（源码 grep 口径 151 个 *.ERB 文件、2455 处）。门控后 erafl 会话 `'.'` 报警，
且 lazy loading 下函数首次调用才解析 → 新游戏角色创建流程在 `@SET_BODYSIZE_BUST`
内崩溃。

**双参考核实（本批核心修正）**：
- v24 参考 SP_TIMES（emuera.em-master ArgumentBuilder.cs:520-555）：倍率经
  `LexicalAnalyzer.ReadDouble(st)` 特殊扫描——v24 本就支持 `TIMES X, 1.5` 小数倍率
  （字面量专用，不接受表达式；解析失败 ArgIsNotRealNumber 警告 + 0.0 继续）。
  移植侧此前用"通用表达式解析 + 泄漏的浮点字面量"实现同一效果，属借道泄漏。
- snake 参考 SP_TIMES（skiasharp ArgumentBuilder.cs:563-590）：倍率为一般表达式项
  （依赖其浮点字面量词法）。
- 即：v24pure 的小数 TIMES 在 v24 参考有独立合法路径，不随浮点词法门控消失。

## 本批修复

### A. TIMES 倍率双路径（ArgumentBuilder SP_TIMES）
- snake（UsesFloatTypeSystem）/ erafl（AllowsFloatLiterals）会话：表达式项倍率
  （snake 参考原样）。
- 其余会话（v24pure）：`ReadDouble` 字面量扫描——v24 参考原样（仅字面量、失败 Lv1
  警告 + 0.0 继续、多余字符 Lv1 警告）。附带修正了移植侧此前"v24pure 接受表达式倍率"
  的超集偏差。

### B. eraFL 浮点字面量能力（type.float-literals.v1）
- 词法门控条件放宽为 `Snake.UsesFloatTypeSystem || EraFl.AllowsFloatLiterals`；
  浮点变量族（RESULTF/#DIMF/REFF/LOCALF 解析）仍 snake 专属（eraFL 实测零使用）。

### C. eraFL 模组化（与 snake 批同构）
- Core `EraFlCompatibilityModule` 账本 +4 能力：type.float-literals /
  declare.out-keyword（与 snake 同名共享 id，蛇系血统能力）/ arith.safe-arithmetic-guard /
  markup.font-extended-attributes。
- `IEraFlCompatibilityPolicy` +4 具名属性（AllowsFloatLiterals/AllowsOutKeyword/
  UsesSafeArithmeticGuard/AllowsExtendedHtmlAttributes），Disabled/Legacy 双实现同步。
- 三处粗粒度 `EraFl.IsEnabled` 消费点具名化：SafeArithmetic（UsesSafeArithmeticGuard）、
  OUT 关键字（AllowsOutKeyword）、HtmlManager font 扩展属性（AllowsExtendedHtmlAttributes）。
  保留 IsEnabled 的两处：`Program.IsEraFlProfile`（会话身份）、HtmlManager Html2PlainText
  选择性剥离（eraFL 独占行为，无共享问题，属模块选择语义）。
- SurfaceSmoke：erafl 新 4 项能力的账本声明+派生+v24pure 排除断言；snake↔erafl 共享
  id（OutKeyword）的互斥断言例外。CoreContractSmoke：erafl 能力序列钉扎更新（12 项）。

### D. startup 错误清单核证（5 月日志 vs 当日实测）
全部确认为**陈旧**（当前构建已修，当日 emuera.log 零出现）：
- XML_DOCUMENT/XML_RELEASE/XML_TOSTR 第 1 参 int 形态（当日 FL_INIT_LOADER 全程通过）；
- GSETFONT 2-4 参 / GDRAWTEXT 2|4 参 / GDRAWGWITHROTATE 3|5 参（现契约均覆盖 eraFL 用法，
  rotate 3 参还带 eraFL 中心枢轴默认）；
- MAP_GETKEYS 三参 (map, RefString1D, flag) 形态（Creator.Method.Map.cs:190 已支持）。
其余 5 月警告（変数未定義/FOR 無い NEXT/代入演算子'='/CASE 重複/FLOOR 函数名衝突）为
游戏自带 quirk，任何引擎都会警告，非引擎漂移。

## 验证

- **eraFL0.47 真机**（run-real-erafl047.json，输入 0×3 走新游戏+角色创建）：
  exitReason=inputs_consumed，scriptErrors=0，emuera.log `'.'` 警告 **0 条**（原 2455），
  越过原崩溃点 `@SET_BODYSIZE_BUST`。
- 三 fixtures（v24pure/snake/erafl）9/9 Passed，语义哈希=基线（14a36f1d/d701c404/e3f6667b）
  ——TIMES v24 路径变化对 fixture 零影响（fixture 无 TIMES 用法）。
- build 0 错误；三契约冒烟全绿（**复审返工后改在净提交树复跑取证**——初验曾借力工作树上
  并发会话未提交的 DescriptorRoute 修复跑绿 CoreContractSmoke，且其比较器回归断言被整文件
  add 误并入提交：净树必红。返工已把该断言从提交摘出、留在工作树归属其修复批次）。

## 复审与返工（result-review 92/100 → 返工后复验）

- **P0** 提交自洽性：并发会话对 tools/core-contracts/Program.cs 的未提交比较器断言
  被显式路径 add 混入 074b5fe（其依赖的 CompatibilityDescriptorRoute 修复仍在工作树）→
  净树 CoreContractSmoke 红。返工：amend 剔除断言块（完整版本保留回工作树），净树复跑
  三冒烟取证。
- **P1** 本日志验证陈述改按净提交树口径（见上）。
- **P2** IEraFlCompatibilityPolicy 缩进归位；SnakeCompatibilityCapabilities OutKeyword
  失效注释更新（共享 id + erafl 派生口径）；"152 文件"改为注明 grep 口径（151 个 *.ERB）。
- **流程教训**：显式路径 add 挡不住"同文件内他人 hunks"——提交前必须 git diff --cached
  逐 hunk 复核；门禁结论必须在提交树本身复现（stash 外来改动或 worktree），工作树绿灯
  不作数。

## 教训

- **"零使用"考证要覆盖能力族的全部入口形态**：浮点能力族=字面量+变量+声明语法+保留字，
  只 grep 变量名就断言"零使用"漏掉了 151 个 *.ERB 文件的字面量使用。真机日志是最终裁判。
- **借道泄漏的实现会让门控修复变成回归**：移植侧 v24pure 的 TIMES 小数此前"能用"是
  因为浮点词法泄漏；修门控时必须为每个会话找回参考里的合法路径（v24 的 ReadDouble），
  而不是简单收紧。
- **并发会话工作树纪律**：本批期间工作树出现他人未提交改动（EmueraContent tooltip/
  CompatibilityDescriptorRoute/compatpack schema）与用户手写 manual.md——提交一律显式
  路径 add，禁止 add -A。
