# 2026-09-22 v24 核心对齐参考源码（第一批）

## 任务

将 v24 核心解释器（`Scripts/Emuera/`）与参考源码 `E:\MyCode\Era\emuera.em-master`（1.824+v24+EMv18+EEv55）做语义对齐，严禁语义偏移；移动端专属优化保留。共享代码的改动同时以 snake 参考（`emuera_lazyloading_selfmodified_version-develop-skiasharp`）交叉核对，保证 snake 会话语义不偏离 snake 参考。

## 方法

1. **注册表级**：`LegacyDialectReflectionDiff`（上游两参考 DLL 反射 vs 当前三 profile）——v24 侧 7 个数学函数声明漂移，snake 侧零错配。
2. **算法级**：85+7 对核心文件做规范化 diff（剥 using/注释/空行/大括号/trerror 引用），分 13 域并行审计（Parser/Loader/Expression/Variable×2/Process/Instraction/Argument/ConstantData/Console×2/Html/Utils-Config/Creator/Content），共报约 130 项候选漂移。
3. **snake 交叉核对**：对 42 项计划修复逐项查 snake 参考同位置行为，产出"可无门控直修 / 需 Snake.IsEnabled 门控 / 结构性放弃"三分类。关键结论：TIMES 溢出钳制、`\e` 转义、mask Alpha 通道、RANDOMIZE 重播种 newRand、BEFORE_ERROR/THROW 事件、float 类型系统、VARIADIC 剥除、OUT 关键字为 **snake 侧有意行为**，必须门控；`;^;`、ONEINPUT 系截断解除、INPUTANY 旗标、EE INPUT 三参扩展、REPEAT COUNT 检查、PRINT_SPACE 两段校验、ARRAYREMOVE num<=0、deleteLine 下溢补偿、警告级别着色等两参考一致，可直修。

## 本批修复（已落地，构建+三冒烟+反射对比全绿）

### 语义恢复（两参考一致，无门控直修）
- **SafeArithmetic 重写**：整型 +−* 溢出恢复 unchecked 回绕（原为 checked+钳制+控制台告警）；/ % 除零恢复 CodeEE 致命错误（原为告警+返回 0 继续跑）；单目负号对 long.MinValue 打系统行后原值返回。TIMES/++/--/变量 PlusValue 全部连带归位。
- **TIMES**：恢复参考 unchecked 回绕 + decimal 溢出 `(long)(double)d` 路径（v24 分支；snake 分支保留钳制+告警）。
- **INPUT 家族端到端恢复 EM 三参扩展**（Def/Mouse/CanSkip）：新增 `SpInputsArgument`（含 eraFL 指针元数据透传）；SP_INPUT/SP_INPUTS builder 恢复参考解析（INPUTS form 串以 Comma 结束、后随 1-2 整数参）；INPUT/INPUTS/ONEINPUT(S)/BINPUT(S)/ONEBINPUT(S)/TINPUT(S) 执行侧恢复 MouseInput 与 CanSkip+MesSkip 默认值直通；ONEINPUT 系默认值截断/负数作废（解析期+执行期）全部解除。
- **INPUTANY**：flag 恢复 `EXTENDED`（SKIPDISP 下照常等待）。
- **ARRAYREMOVE**：第 3 参恢复透传（num<=0 = 删到末尾）；string 分支补 start 越界检查。
- **REPEAT**：恢复 COUNT 变量禁用时的解析期 CodeEE。
- **PRINT_SPACE/PRINT_RECT**：恢复两段式个数校验（>maxArg 警告放行；非 1 非 4 才错误）。
- **`;^;` 行中标记**：SkipWhiteSpace 恢复（`;!;`/`;^;` 均跳过）。
- **EMUERA_VERSION**："1.824.0.0"（原空串）；错误尾串改用版本文本（原 Program.ExeName=null）。
- **relationDic**：GETNUM 的 CALLNAME 映射恢复；Mastername 注册（snake 门控跳过——snake 参考走独立 map）。
- **chara CSV**：ISASSI/助手 行恢复静默忽略。
- **GameBase**：版本不足警告打印 CSV 要求的 targetVersoin（原打印引擎自身版本）。
- **ErbLoader**：系统函数带参数告警恢复 level2+isError；ERB 打开失败恢复整体加载失败（noError=false）；parseLabel 的 VARIADIC 剥除加 Snake 门控（v24 视为普通标识符）。
- **LabelDictionary.AddFilename**：重载文件 FileIndex 恢复"已载文件总数"。
- **deleteLine**：恢复下溢补偿（列表删空未删够时 `lineNo=0; logicalLineCount-=差值`）。
- **PrintWarning/PrintErrorButton**：恢复按级别着色（Lv0-2 淡黄、Lv3+ 红；结构体副本避免参考侧污染默认样式的副作用）。
- **TINPUT 剩余时间**：固定一位小数 `0.0`。
- **FLOWINPUT**：setWaitInput 恢复 `req.MouseInput = flowinput`（空白右/中键提交）。
- **IdentifierDictionary**：VarKeys 恢复全部变量名（ENUMVAR 系可见系统变量）；CheckUserVarName SystemMethod 恢复 level2；CheckUserPrivateVarName SystemMethod 恢复 level2+不注册。
- **ERD**：isUserDefined 恢复未定义键抛 CodeEE（NotDefinedErdKey 文案）；TryKeywordToInteger 移除吞异常 catch。
- **RANDOMIZE**：UseNewRandom=true 时 v24 会话恢复警告+跳过；VariableEvaluator.Randomize 的 newRand 重播种限 snake 会话。
- **setConfig**：GETCONFIG 白名单外已定义项恢复按值返回（YES/NO/数字/字符串）。
- **HTML**：HTML_TAGSPLIT 恢复"任意 `<`…下一 `>` 原始切分"；HTML_TOTEXT 恢复正则全剥（eraFL 会话保留选择性剥离）；未闭合 clearbutton 不再报错；div 递归继承 clearbutton 状态；div 后不再强制 LineHead=false；shape param 恢复 MixedNum 直传（消除 px→percent→px 往返舍入）；img 负高度恢复 FlipY 翻转。
- **Content**：CSV 裁剪矩形恢复父图越界拒绝；dest_w/dest_h 负值恢复绝对值放大语义（ResolveDestSize）；GDRAWGWITHMASK mask 通道 v24 恢复 B 通道/snake 恢复纯 Alpha（原 R 通道+alpha 覆盖两不沾）；CSV 第 2 列不再 Trim；ALS 仅在同名 CSV 存在时加载；VarExt 九个 HashSet 恢复区分大小写；csv 目录缺失恢复静默。
- **杂项**：ReadMap/存档 Map 字典恢复 Ordinal（区分大小写，含 RuntimeDataStore 外层三字典）；StringStream.Find 恢复 Ordinal；TOOLTIP_SETFONT/SETFONTSIZE/CUSTOM/FORMAT/IMG 恢复 `EXTENDED`（无 METHOD_SAFE）；DT_COLUMN_OPTIONS 成功路径不再写 RESULT=1；UPDATECHECK 空白判定恢复 `null||""`。

### 方言投影层
- **DialectFunctionContracts**：ABS/CBRT/EXPONENT/LOG/LOG10/POWER/SIGN 的 v24 分支补 Wrap 固定 Int 声明（原透传 snake 双态实现致声明漂移）；新增 RAND/MAX/MIN 的 v24 契约（第 1 参不可省略、全 Integer，形状保持 custom 与参考 argumentTypeArrayEx 对齐）。反射对比 v24/snake 双侧全部归零。
- **FunctionMethod.CheckArgumentType**：补 argumentTypeArray==null 防护（参考同构 else-if）。
- **BEFORE_ERROR/BEFORE_THROW**：事件名注册与两处调用点（异常路径/THROW）加 Snake.IsEnabled 门控；v24 会话中这两个名字恢复为普通可 CALL 函数、错误即停。
- **REF/OUT 关键字**：`Out=true` 从 REF 移到 OUT（v24/snake 两参考一致：REF 只置 Reference）；OUT 关键字本身加 Snake/EraFl 门控（v24 会话 OUT 落 default 作变量名）。

## Backlog（本批未修，按优先级）

### P1（语义影响大，需专项 PR）
1. **float 类型系统整体未门控**（snake 独有）：词法浮点字面量（LexicalAnalyzer case '0'-'9' 遇 `.` 扫 double + LiteralFloatWord）、#DIMF/#REF/#REFS/#REFF/#FUNCTIONF/#LOCALFSIZE（LogicalLineParser + HeaderFileLoader 的 case）、RESULTF/LOCALF/ARGF/REFF 无条件注册进 varTokenDic、StrForm `{}` 接受 Float、REFF 保留字、函数参数 FLOAT。建议以 `Snake.IsEnabled || EraFl.IsEnabled` 统一收口（如 eraFL 也需要），v24pure 下全部走参考报错路径。第二个未门控入口已确认：RESULTF 等变量注册（ExpressionParser 域审计 G6）。
2. **ErbLoader 加载顺序**：`*#*` 目录优先加载（B-D1）与 getFiles"先当前目录后子目录"+Ordinal 排序（B-D2）未恢复；影响 FileIndex→同名标签/事件顺序。需实现两段式加载并跑六游戏矩阵验证。
3. **置き字（_rename.csv）解析三分歧**（B-D3）：多逗号行/值中转义逗号/key 侧转义逗号与参考 `(?<!\\),` 分割+两段注册不同。ParserMediator.LoadEraExRenameFile。
4. **ICVariable 变量名大小写折叠**（E-#3）：参考在默认 IgnoreCase=true 下变量名恒区分大小写（IdentifierDictionary 的折叠被参考刻意注释）；当前全链折叠。影响面大，需用户裁定口径 + 矩阵验证。
5. **SP_INPUTS 之外的 raw 参数预剥空白**（A-D4）：LogicalLineParser 非打印指令的参数流被预 SkipWhiteSpace（参考保留原文），SETCOLORBYNAME/PRINTPLAIN 等带前导空格参数语义漂移。修法：ShouldPreserveRawPrintArgument 按 builder 类型判定或移除预剥。
6. **EncodingHandler 缺失**（J-D2）：SJIS 无 BOM 文件全线按 UTF-8 读（参考 BOM→严格 UTF8→CP932 探测）。EraStreamReader.Open/Preload/EraDataReader/LOADTEXT 四入口。移动端需 CodePages provider 可用性验证。

### P2（中等工作量）
7. ERH `#FUNCTION/#DIMF/#REF...` 接受面（B-D4/D5）：v24pure 应走参考 NotImplCodeEE/default 报错；现无条件实现。
8. UseNewRandom 的加载期告警块（B-D8，snake 门控）；INITRAND/DUMPRAND 同。
9. VARI/VARS：UseScopedVariableInstruction 默认 true（参考 false）；VARI/VARS 指令 v24pure 应按参考默认关闭。
10. ERD 重复索引告警（H-#18）与 isDefinedErd 恢复（H-O1，默认关配置零影响）。
11. Ctrl+Z 钩子（F-D7）：saveGameWaitInput/endCallSaveInfo/loadGameWaitInput 三处 OnSavePrepare/OnSave/OnLoad 缺失 + CtrlZ 配置门控（参考默认 false）。
12. runScriptProc 解析致命错误降级（F-D2）："が変数のように使われています"类错误 Warn+continue 未门控（参考 throw）。
13. SparseArray 越界容错（D-D1）：1D 越界读返回默认值/写 overflow（参考 CodeEE）；需评估移动端内存策略与错误语义的折中或门控。
14. LOCAL/ARGS 生命周期（D-D3）：参考按函数标签持久+递归共享；当前每调用新开零数组。方向性架构差异，需专项设计。
15. PRINTC/PRINTBUTTONC（I1-DRIFT-1）：参考 SJIS 字节 padding 真空格+Flush 期折行；当前像素分栏+ConsoleSpacePart。文本产物（ToString/日志 padding、2000 字符缓冲）与折行时机不同。snake 参考也是像素制（结构不同），建议至少恢复文本产物层。
16. div 按钮输入可达性（I1-DRIFT-5）：键入匹配应限"顶层+绝对 div（免 generation 门控）"，相对 div 不可交互（参考）；当前递归全 div+统一门控。
17. GDRAWTEXT 默认字体/画刷（L-D7：100px+前景色 vs FontSize+透明）与 RESULT:1/2 宽高回填（L-D8）。
18. GCREATEFROMFILE 路径解析次序（L-D6：ContentDir 优先）。
19. SPRITEDISPOSEALL 0 的 CSV 原件恢复语义（L-D10）。
20. HotkeyState 键名表扩充（I2-DRIFT-10：OEM/小键盘/锁定键）。
21. div 头标签日志文本格式（I1-DRIFT-7：depth/display 属性、px/em 输出、bcolor 折叠）。
22. 按钮焦点底色（I1-DRIFT-4：Gray/(50,50,50)/黄色豁免，opt-in 配置）。
23. 未闭合 `<clearbutton>` 等 HTML 宽松化组（I2-DRIFT-13：height 必填/嵌套 div 报错/display 扩展）——确认归属方言后门控。
24. `;` 注释行"复活"（L-D3）与 CUTIN 兜底（L-D4）：确认归属（snake/eraFL）后门控。
25. ERD .als 注入（H-#17，Skiav8.0）与别名键 Trim/重复告警差异（#30 子项，snake 门控）。
26. CSTR 第三字段参考崩溃 vs 当前置空（H-#9）、CVAR2D @2 非对称（H-#16）：登记为有意纠偏。
27. 输入面板 UI 小缺口：Godot clear_richText 空实现（F-ADAPT-3 附注）。
28. getStBar/CreateBar 等 GDI→Godot 已核对等价项无需动作；LazyLoading 的 SpriteDispose 不清 lazyImageDictionary（snake 专属小疵，一行修复）。
29. **IsFunctionMethod vs IsCurrentFunctionMethod**（F-D5）：当前"修复"了参考 quirk（方法体内 CALL 的 RETURN 语义）。需裁定是否回归参考行为。
30. **RollbackToState 抹掉 #FUNCTION 异常输出**（F-D4）：错误行函数名/调用栈显示调用方。BEFORE_ERROR 已门控后仍需快照方案。

### P3（登记）
31. ASCII art nobr 启发式（I2-DRIFT-3）：无参考对应、未门控，需确认目标游戏后门控或移除。
32. 字体测量忽略字体族/样式（I2-DRIFT-11）；Srcm 图片映射输入协议（I2-OMIT-17）；EscapedParts 按钮计数（I2-OMIT-18）；div radius 圆角（I2-OMIT-19）；GIF/TIFF 解码（L-O2）；ReloadResource（L-O1/I1-OMIT-5）；ClipboardProcessor（I1-OMIT-1）；Ctrl+Z 回退重放 LoadSilent（I1-OMIT-3）。
33. 配置默认值：SearchSubdirectory/SystemSaveInBinary true→false（J-D5，需矩阵验证+用户裁定）；Ctrl_Z_Enabled 门控恢复（J-D8）。
34. 文本存档 string 2D/3D 写读不自洽（J-D3）。
35. LangManager 字宽基准（J-D9）：代码页字节 vs 固定网格分类（零宽/箱线差异），需签核归类 ADAPT 或按 useLanguage 走参考算法。
36. megaten/erablue/v18 专属 profile 未在本批交叉核对范围内，后续批次补。

## 证据

- 构建：`dotnet build gemuera-c#.csproj` 0 错误。
- 契约：LegacyDialectSurfaceSmoke / LegacyDialectRuntimeSmoke / CoreContractSmoke 全部通过。
- 清单：LegacyDialectInventoryGenerator 再生，生成物与 HEAD 零差异（指令/函数名字集未变）。
- 反射对比：LegacyDialectReflectionDiff——v24 301 指令/266 函数 0 错配（修复前 7 函数声明错配）；snake 0 错配。
- 运行级（legacy-runner，成功判据 first_wait_reached，各 3 连跑）：
  - canonical fixtures：v24pure（E:\tmp\dialect-fixtures\v24）3/3、erafl 3/3、snake 3/3 全绿；
  - 真实游戏：snake×EraTW-Magic_DLC 3/3 绿。
  - 非矩阵游戏观察（不属于本任务门禁）：eraAkumaMaid0.305 与 eraFL0.47 在 v24pure/erafl 会话因既有"解釈できない行"（`Call TaiTaChara_K7`/`goto MEGANE_TALK` 等行）触发严格加载退出（提示需兼容选项"解釈不可能な行があっても実行する"）。已做 HEAD 基线对照（暂存本 PR 改动重跑）：**同样失败、日志同样含 TaiTaChara 无效行**——属既有行为，非本 PR 回归。

## 复审返工记录（result-review 92 分 → 修复清单）

- P1-1 GameBase targetVersoin：补上漏做的修复（打印 CSV 要求版本，参考文案 `Ver.{0}` 无空格）。
- P2-3 SP_INPUTS 尾逗号（`INPUTS "x",`）：恢复参考行为返回 null 交上游报错。
- P2-4 EE_INPUT 跳过路径的 `arg.Def != null`/`arg.Mouse != null` 防御：参考侧此处为 NRE 崩溃，属已记录的有意偏离（崩溃路径不复制，正常路径逐语义一致）。
- P2-5 RuntimeDataStore.cs 去除误加的 UTF-8 BOM。
- P2-6 REPEAT 禁用报错改用参考文案（"COUNTが使用禁止変数になっているため、REPEATは使用できません"）。
- P2-7 规模描述修正：代码改动 33 文件（不含生成物与文档）。

## 教训

- **两个参考会话的共享代码必须双参考核对**：本批三次返工（TIMES、Mastername、`\e`）都是只看 v24 参考导致的误判——snake 参考在这些点上有意分叉。后续对齐任务应在动手前先跑交叉核对表。
- **审计 agent 结论需抽查验证**：13 域审计约 130 项发现，抽查 10+ 项全部属实，但行号/细节仍有偏差，逐项修复前以源码为准。
- **"参考侧注释掉的代码被复活"是最高频漂移形态**（ONEINPUT 截断、INPUTANY 旗标、TINPUT 截位、INPUTS EoL）——移植早期版本而非参考当前版本。
- **审计域发现 → 修复落地要有勾销清单**：GameBase targetVersoin 一项"已定位未落地"直到复审才暴露；后续大清单任务逐项打勾。
- **python 批量改文件注意编码**：utf-8-sig 写入会加 BOM、newline='' 会动行尾，改完用 git diff --stat 复核。
