# 2026-09-22 snake 兼容模组化 + snake 会话对齐参考源码（第二批）

## 任务

用户指令：将 snake 改成兼容模组（eraFL 模式），然后对齐 snake 参考源码
（`E:\MyCode\Era\emuera_lazyloading_selfmodified_version-develop-skiisharp`→实际路径无 `(2)` 后缀）。
严禁语义偏移，允许移动端专属优化。前置：PR #22（v24 对齐第一批）审核合并。

## PR #22 审核与返工（独立复审代理）

- 复审结论：**不可合并（P0×1）**——SafeArithmetic 被无门控重写为 v24 语义（unchecked/除零
  CodeEE），但 snake 参考存在同名同路径类且内容=被删旧实现（checked+告警+钳制；除零告警得 0），
  CHANGELOG v2.0.0 明文「SafeArithmetic 安全运算：溢出保护」。构成 snake/erafl 会话回归。
- 返工：SafeArithmetic 改 `Snake.IsEnabled || EraFl.IsEnabled` 双分支（snake 系恢复安全语义，
  v24 保持 emuera.em-master 原样）。验证：build 0 错误+三冒烟+三 fixtures 3/3 Passed。
- 同步修正 evolution-log 错误定性（原标"两参考一致"）、登记反射对比工具 extras 输出缺口（P2）。
- 复审 P2/P3 采纳情况：SP_INPUT 双检（本批修复）；OUT-after-REF 文案（本批修复）；DialectFunctionContracts
  英文文案（P3 登记）；div 递归继承部分属性（P3 登记）。
- **PR #22 已合并（b7acca7）**。

## 审计方法

1. 现状盘点：33 处 `Program.Compatibility.Snake` 引用分类（7 处具名 flag 消费 + 约 12 处粗粒度
   IsEnabled + 3 处合法 IsEnabled）。
2. 92 对文件归一化 diff（`Temp\v24align\snake-diffs\`，参考侧 Emuera/Runtime|UI ↔ 移植侧）。
3. 6 个领域并行审计代理：float 类型系统 / 输入指令族 / 解析词法声明 / 控制台 HTML /
   图形内容 / 加载器 CSV 数据。全部回收，共 ~40 项分级发现。

## 本批修复（已落地）

### A. snake 兼容模组化（结构，零行为差异）
- 新增 `src/Core/Compatibility/SnakeCompatibilityModule.cs`（ModuleId=game.snake，
  CreateDefinition/CreateProfile，与 erafl/megaten 同构）；BuiltInDialectCatalog 基线+profile
  注册改用模块类，删除内联 SnakePortTypeIds。
- 能力账本 7→17 项：新增 times-clamp / mask-alpha-channel / escape-e-two-char / throw-event /
  before-error-event / randomize-reseed / relation-without-mastername / out-keyword /
  variadic-strip / **float-system**。
- ISnakeCompatibilityPolicy +10 属性（含 UsesFloatTypeSystem），Disabled/Legacy 双实现同步；
  SurfaceSmoke 反射穷尽性断言自动把关。
- 11 处粗粒度 IsEnabled 门控改具名 flag；3 处保留 IsEnabled（VARI/VARS 文法族选择、
  snake 专用函数守卫、会话身份查询——属模块选择语义）。
- 运行期等价证明：v24pure/snake fixtures 语义哈希与 HEAD 基线逐字节一致
  （14a36f1ddde0 / d701c4042ef7）。

### B. float 类型系统门控（P0 泄漏 ×7，capability=type.float-system.v1）
v24pure 会话原可触达全部 snake 浮点入口，违反"未选择侧不变"铁律；eraFL 实测全库零浮点
使用（eraFL0.47 + fixture 扫描 0 文件），不随能力放行：
- 词法浮点字面量（LexicalAnalyzer：非蛇会话走整数路径，`.` 按 v24 语义报错）
- RESULTF 注册门控（VariableData）/ LOCALF+ARGF 注册门控（localvarTokenDic）
  → **返工**：LOCALF/ARGF 改为注册保留（ExecutionContext/LabelDictionary 的浮点支撑
  簿记无条件读取，注册门控触发 KeyNotFoundException 致 v24pure fixture 启动失败），
  可见性门控下沉到 IdentifierDictionary.GetVariableToken 的名字解析点；RESULTF 无
  引擎级读者，注册门控保持。
- ERB #DIMF/#REFF/#LOCALFSIZE/#FUNCTIONF（LogicalLineParser，非蛇落 default 警告忽略）
- ERH #DIMF/#REFF/#FUNCTIONF（HeaderFileLoader，非蛇落 default 致命 CodeEE）
- 保留字 REFF/VARIADIC（IdentifierDictionary nameDic，v24 参考均无此二保留字）
- 运算符提升表为派生泄漏（字面量+变量封堵后自然闭合）

### C. snake 会话语义漂移修复（P1/P2）
- **TOINT**：补浮点实参截断分支（snake 参考 Creator.Method 6149-6151）。
- **RAND**：max≤min 钳制为下界+一次告警（snake Skiav12.2；v24 保持 CodeEE）。
- **SPRITEANIMEADDFRAME**：`img == null && !img.IsCreated` 短路 NRE 修正为 `||`。
- **SPRITECREATE rect**：恢复参考 IntersectsWith 语义（完全不相交才抛；部分越界保留原 rect；
  负宽高透传——SrcRectangle 契约允许负值）；删除移植侧裁剪逻辑。
- **多边形画具默认**：pen/brush 未设置时黑 色（SKPaint 默认）而非 Config.ForeColor/透明擦除；
  GFILLRECTANGLE 无画刷用 Config.BackColor；空点表抛 CodeEE（参考 NRE 经 Creator 包装）。
- **SP_INPUT**：移除多余 checkArgumentType 调用（参考活动代码手工循环；v24pure 同受影响）。
- **RESULTF**：补 SetValueAll(double) 重载（VARSET RESULTF,1.5 落基类 CodeEE）。
- **RETURNF**：#FUNCTIONF + 整型表达式静默提升（snake 参考明注 auto-promote）。
- **TOSTRF**：解析期第 1 参数值值型/第 2 参字符串型检查（参考 ArgTypeList）。
- **GETVARF**：取值异常原样传播（不再吞成默认值）。
- **SETIMAGELAYER**：同深度多图层共存（参考 ImageLayerManager.SetLayer 纯 Add）。
- **HTML_PRINTC/PRINTC 换行判定**：整列基准（装不下整列即换行），参考 534-538。
- **deleteLine**：补 MaxLog-2 dummy 插入（消费打印缓冲）+ MaxLog 超额顶替（参考 GETDISPLAYLINE 修正段）。
- **OUT 关键字**：标量形状强制（Dimension=0/Lengths=[1]）；snake 会话移除 STATIC 名字位逃逸；
  OUT+REF 冲突文案对齐 CanNotSpecifiedWith。
- **VARIADIC**：不再剥相邻逗号（参考只删标识符）；零参数时跳过校验照常 label.Arg=args。
- **TRYCJUMPSTR/TRYCCALLSTR**：补入 nestStack 压栈表（缺失时 CATCH 必报"対応するTRYC系命令がありません"）。
- **GETCSVNOBY***：Ordinal 区分大小写 + 返回 NO 字段（参考逆序构建字典语义；重名两侧同归最小 No）。

### D. PRINTC v24 字符基准路径（P0 泄漏）
- v24pure 的 PRINTC/PRINTLC/PRINTRC/PRINTBUTTONC 原走 snake 像素分栏算法；恢复参考
  CreateTypeCString：SJIS 字节基准（LangManager.GetStrlenLang）补空格（右对齐 PrintCLength、
  左对齐 PrintCLength+1），像素测量仅用于超宽回删前导/尾随空格。
- snake 会话保持像素分栏（appendPrintCCell）；HTML_PRINTC 为 snake 专属指令不受影响。

### E. HTML 属性门控（P0 泄漏）
- font 的 render/edging/hinting/size/valign（snake 独有）与 div 的 margin/padding/border/radius
  + 移植私有 layout：非蛇会话（v24pure）按参考抛 CanNotInterpretAttributeName；
  eraFL 血统承 snake 保留可用（零回归取向，eraFL 无源码不可证伪）。bcolor/display 为 v24 既有，不门控。

### F. 加载顺序族（P1，v24pure 同受益）
- **getFiles 遍历顺序转正**：移植原为"子目录优先→文件"；参考（v24/snake 同款）为
  "当前目录文件（排序）→ 子目录（排序，递归）"。
- **EE `*#*` 目录机制**：名字含 '#' 的目录下 ERB 最先加载（覆盖声明次序优先）且不受 lazy
  表跳过约束（loadedFiles 短路在 lazy 检查之前）；Config 增加 GetFiles(dir, rootdir, pattern)
  3 参重载保持相对路径以 ERB 根为基准。

## Backlog（本批未修，按优先级）

1. **P1 INPUTMOUSEKEY 输入分发**（Godot UI 层）：键盘捕获全缺失（type=3 不可达，PressPrimitiveKey
   零调用）；非虚拟鼠标路径点击未走 primitive 协议（空白点击死等）；RESULT:2/3/5/6、RESULTS 写回
   缺失（EmueraThread.cs:278 只写 RESULT:1 且无条件）。证据：snake MainWindow 933-940/982-1032/
   1177、EmueraConsole 1351-1384。需 GUI 驱动验证，独立 PR。
2. **P1 TEXT_BGC 生效时机**：参考绘制时读全局（历史行追溯生效）；移植打印时快照（永久保留）。
   需渲染层重设计，独立 PR。
3. **P1 编码探测缺失**（F5）：参考逐文件 BOM→严格 UTF-8→回退 SJIS；移植恒 UTF-8，SJIS 游戏
   mojibake（v24pure/snake 双向缺口）。需 per-file 探测接 Godot 文件 API + SJIS fixture。
4. **P2 ERD preset 合并**（F8）：snake mergePresetErdFromErbDir 扫 *.erd 按槽位合并。
5. **P2 _rename.csv 解析细节**（F6）：严格 2 列（>2 列整行跳过）vs 移植静默取前两段；转义逗号
   保留 `\`；行内替换正则 `[[...]]` 逐 token vs 整字典 string.Replace。
6. **P2 SPRITECREATE 8/10 参运行时**：契约已放行但 CreateSpriteG 无 pos/destSize 重载
   （SPRITEPOSX/Y、SPRITEWIDTH/HEIGHT 全错）；SPRITECREATEFROMFILE 四点偏离（同名返回
   false→应 true、isRelative 丢弃、路径解析序、动图建静态）；SPRITECREATED 实体化语义。
7. **P2 CSV crop/delay 校验强加给 snake 会话**（snake 懒加载路径无校验）；负 dest_w 语义。
8. **P2 浮点数组维度超集**：移植支持 1D/2D/3D+ReferenceFloat 0-3D；snake 仅 Static/Private 1D
   +全局浮点 REF 标量。收窄会破坏既有超集依赖游戏，需先扫描实测使用面。
9. **P2 排序比较器/路径分隔符**（F9）：culture 敏感 Array.Sort vs OrdinalIgnoreCase；`\\` vs `/`
   （后者牵动 Godot/Android 路径约定，非纯对齐问题）。
10. **P2 解析警告只写文件不上控制台**（ParserMediator UsesParserDiagnostics 路径）：与参考
    PrintErrorButton 可点击警告漂移，Android 用户不可见；涉及启动期控制台洪泛风险评估。
11. **P3 杂项**：反射对比工具 extras 清单输出；DialectFunctionContracts 英文文案统一；
    CanSkip/MesSkip null 守卫（有意防御保留）；NF 滚动管理真机验证；CBProc（EE_Anchor 剪贴板）
    整体缺失；SET_SKIA_QUALITY 等状态化 no-op；StringMeasure 逐字符测量差异；
    LooksLikePreformattedAsciiArt 移植启发式；escaped parts 子系统未移植；VARI/VARS 代入
    优先于声明的跨 profile 有意偏离（既有注释）；ReadSingleIdentifier 全角空格抛错（gEmuera
    全 profile 自加，两参考均无）。

## 验证

- build 0 错误（Godot mono 回调）；三契约冒烟（Surface/Runtime/CoreContract）全绿；
  清单再生零差异；v24pure/snake/erafl fixtures 全部 Passed（语义哈希变化均为预期语义修正：
  PRINTC v24 字节基准、GETCSVNOBY、deleteLine、*#* 顺序等）。
- 模组化零差异证明：改造点提交（11f6d37）与 HEAD（c45226c）在同 fixture 下语义哈希逐字节一致。

## 教训

- **可见性门控要选对层**：变量名可见性≠注册。LOCALF/ARGF 被引擎簿记（默认尺寸/resize）
  无条件读取，注册层门控炸的是引擎自身；门控点应放在脚本名字解析处（本次返工实证）。
- **双参考核对必须覆盖"删除的旧实现"**：PR #22 的 SafeArithmetic 误判源于只验证了"新实现
  对 v24 正确"，未验证"被删实现对 snake 正确"。删除性改动要双向举证。
- **门控粒度**：粗粒度 IsEnabled 在"snake 会话=全能力"时与具名 flag 等价，但账本的可组合性
  是模组化的核心价值（未来拆 profile/兼容包复用）；SurfaceSmoke 穷尽性断言是廉价护栏。
- **eraFL 取向裁决模式**：无源码方言按"实测零使用→不随能力放行；有使用→保守放行"处理，
  裁决依据写进 capability 文档注释。
