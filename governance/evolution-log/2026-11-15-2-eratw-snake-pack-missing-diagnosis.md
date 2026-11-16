# EraTW 日志大量 Lv2 解析告警诊断（2026-11-15）

前置：同日任务 [[2026-11-15-font-size-content-scale-api-separation]]。本任务为独立诊断，无文件改动。

## 1. 任务目标与成果

分析 `D:\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64\EraTW-Magic_DLC-update_base\emuera.log`
（998 行）中大量「解釈できない」告警的根因。
结论：**EraTW（画蛇添足版 = snake fork 衍生游戏）在 v24pure 基线、零兼容包会话下启动**，
402 条 Lv2 告警全部是 snake 包能力缺席所致；另有 ~20 条同根因（TOINT 实参/EXISTVAR 双参）未带
包归因文案；1 条 Lv1（#DIMF）独立待查。修复路径 = 包 DLL staging + 启动器选 snake profile/按游戏选包。

## 2. 关键决策与 Why（判定链）

1. **主因判定 = 包缺席而非引擎缺陷**：402 条告警的增强文案"属于 pack.gemuera.snake 模块的能力"
   由 gemuera 方言注册表生成——注册表认识这些名字，只是会话没加载包。
2. **游戏跑在 v24pure 的证据**：emuera.log 位于扫描根直属目录（exe 同级），不在 compat/ 路由下；
   `FirstWindow.ScanCompatibilityDirectory`（FirstWindow.cs:1245）只认 `compat/<profile>/<游戏>`；
   且 `TryResolveCompatibilityRouteProfile`（:1305）显式把 `compat/snake` 列为**保留 lane 跳过**——
   snake 不走目录路由，必须靠手动 profile / 按游戏选包 / GEMUERA_COMPAT_PACKS 三路之一注入，
   本次三路皆无（`ApplyCompatPackEnvironment` :805 无映射包 → 清空 env）。
3. **包即使选了也装不上（新发现）**：`assets/text/pack-profile-map.json` 的 relativePath 指向
   `compat_packs/pack.gemuera.snake/pack.gemuera.snake.dll`，但仓库与 exe 运行根均无 compat_packs/
   staging（grep 证实仅 `compat` 存在）→ `TryGetMappedPackPath`（:772）会报"未找到 snake 兼容包…
   本次将以 v24 基线启动"。开发树直跑缺 staging 步骤（导出管线有 release gate 检查，直跑没有）。
4. **排除项**：非字符串类告警（同名リソース Lv0 ×~110）为游戏 CSV 自身重复资源，良性；与方言无关。

## 3. 验证记录

- [静态] 日志统计：402/406 条「解釈できない」带 snake 包归因；非归因告警逐条看源码
  （`COMMON.ERB:606` `TOINT(回復量 * 0.7)` 实参——snake TOINT 对齐见 commit adc2e17；
  `Misc.ERB:694` `EXISTVAR(x,1)` 双参 = EM 语义；各 ×N 同型）。
- [静态] 代码对照：FirstWindow.cs :1245/:1305/:772/:805、PrototypeRuntimeNode.cs :85-125、
  assets/text/pack-profile-map.json（snake → compat_packs/pack.gemuera.snake/pack.gemuera.snake.dll）、
  `ls packs/`（源码包在）与 `ls compat_packs/`（staging 缺）。
- [未验证] 包就位后的实际回归（未执行 staging + 重启验证——本轮仅诊断，用户未要求修复）。

## 4. AI 表现复盘

- 有效：先统计告警分布（402/406 snake 归因）再定性，避免被 ~20 条无归因告警带偏；
  对非归因告警逐一开游戏源文件（COMMON.ERB:604-610）确认 TOINT 实参形态，而不是凭记忆断言。
- 低效：无返工；一轮 grep + 三处代码对照到位。
- 险些踩的坑：差点把"修复 = 移到 compat/snake/"写进结论——读 `TryResolveCompatibilityRouteProfile`
  后发现 snake 是保留 lane 会被扫描跳过，目录路由对 snake 无效，避免给出错误修复指引。

## 5. 教训 → 具体优化动作

1. （本轮不做，遗留 P1）开发树直跑缺 compat_packs staging：FirstPartyPackBootstrap/map 就位但 DLL 不在，
   等于所有依赖方言包的游戏在 dev 直跑下静默降级 v24。建议：导出/直跑管线补 staging 步骤或
   启动器在包缺失时把 TryGetMappedPackPath 的 warning 升级为醒目提示（目前只进 compatPackMappingWarning）。
2. （本轮不做，遗留 P2）"Core prototype unavailable" toast 链路：eraFL 冒烟实测弹过该 toast，
   PrototypeRuntimeNode.cs:84-86 注释自证已退役 profile 会 Resolve 失败——疑似 compat 路由把已退役
   profile 名未过 `ResolveEngineProfileIdForBinding`（FirstWindow.cs:755，专为翻译退役名而生）直传到
   PrototypeRuntimeNode.ProfileId。需核查赋值链。

## 6. 遗留登记

- P1 开发树直跑 compat_packs staging 缺失（§5-1，依赖方言包的游戏全量受影响）。
- P2 "Core prototype unavailable" toast 误导链路（§5-2）。
- P2 `#DIMF DYNAMIC 倍率`（魔法相关函数.ERB:3195，Lv1）独立解析告警，与包无关，需单独排查
  （可能前后文级联或全角字符，包就位后回归时一并看）。
- 无阻塞项：主修复路径明确（staging + 选包），等用户决定是否执行。

> 补遗（2026-11-16）：眼睛差分问题排查（推翻上文"修复路径待执行"的悬置状态）
>
> 用户报告"猫包绘（EagloV 包）立绘眼睛差分不显示"并附截图。排查结论：**截图来自旧坏包会话**；
> 重建包后整条眼睛差分管线实测健康。证据链：
> 1. [实测] 包数据完整：CATBURGER.csv 含角色 98 全部眼睛差分行（目_常目/单目/暗目/白目/闭目 + 红瞳变体，
>    行 876-884）；PIL 解码 `EagloV_98.webp`（1427×1500）确认眼睛裁切区域像素真实存在（37% 不透明，
>    与眉/口/鼻部件一致），CSV 坐标与图内容匹配。
> 2. [实测] 运行时探针（DEBUG 模式调试控制台，`Godot --path ... -- -DEBUG` 强制开启，
>    EmueraThread.cs:189）：`SPRITECREATED("EagloV98_目_常目")=1`，几何
>    `W=500 H=500 X=221 Y=111` 与 CSV dest 语义完全一致；身体部件对照 `X=118 Y=162` 亦正确。
> 3. [静态] 引擎链路逐环验证：懒加载索引/RealizeLazySprite（AppContents.cs:530-625）、
>    SPRITECREATED 懒实体化（:88-100）、SPRITEWIDTH/POSX 返回 DestBaseSize/Position
>    （Creator.Method.cs:2043-2056）、GDRAWSPRITE→DrawSpriteTo→ASpriteSingle.ApplyDestBaseCanvas
>    （CroppedImage.cs:88-101，偏移/缩放数学正确）、合成顺序体图→表情层
>    （COMPOSE_CORE.ERB:285-292）。均无缺陷。
> 4. [实测] 重建包后新会话：零「解釈できない」告警（旧会话 400+）、SQL 库正常打开
>    （qol_data.db-shm 等被触达）、toast 从 "profile 'snake' was not registered" 转为
>    "Core prototype ready | v24pure"。眼睛差分失效与坏包会话下 QOL/差し替え適用 状态链
>    （SQL 依赖）断裂相符。
> 遗留动作：用户以重建后的包实测眼睛差分；若仍复现，调试控制台探针套件
> （SPRITECREATED/SPRITEWIDTH/POSX + GCREATE/GDRAWSPRITE 隔离合成 + HTML_PRINT img 显示）
> 已建立，可直接下钻 GDRAWSPRITE 绘制像素级验证。
> 附带确认：诊断遗留 P2 "Core prototype unavailable" toast 为误导性过渡态（会话最终 ready），
> 不影响包会话；调试面板命令输入框在面板隐藏后仍持有键盘焦点（UI 缺陷，P3 登记）。

> 补遗 2（2026-11-16）：眼睛差分根因确认 + 引擎修复
>
> 用户调试控制台实测 `SPRITECREATED("EagloV62_目_常目动态")=1`，且立绘合成轨迹出现
> `EagloV_贫乳__服_通常无目_62 IS 1`（无目变体）——证明眼部动态接管生效、合成底图
> 刻意不烘焙眼睛，动画眼睛由特效层叠加，而叠加层未显示。逐层取证：
> 1. [实测] Godot `Image.LoadWebpFromBuffer` 对动画 WebP 解码失败（err=43）——
>    Godot --headless --script 探针：静态表解码 OK，`EagloV_62_目常目动态.webp` DECODE_FAIL。
>    PIL 确认该文件为 5 帧动画（眨眼循环 2800/120/200/120/4008ms）。
> 2. [实测] 同文件 Skia 解码完全正常（独立 SkiaSharp 2.88.8 探针：5 帧全 Success）。
> 3. [静态] gemuera 显示链逐环检查：懒加载索引/实体化/SPRITEWIDTH(DestBaseSize 语义)/
>    GDRAWSPRITE→ApplyDestBaseCanvas 偏移数学/EmueraImage 动画挂接（IsAvailable 放行
>    动画源）/Canvas overlay 感知——全部正确；断点即 SpriteManager 的解码占位。
> 4. [实测] 修复：`SpriteManager.LoadImageOrPlaceholder` webp 分支在 Godot 解码失败且
>    IsAnimatedWebp 时回退 `AnimatedWebpSpriteFrames.TryDecodeFirstFrameImage`（新增，
>    Skia 首帧同步解码），占位图 → 真实首帧。异步纹理路径（:1229）共用该入口，单点覆盖。
>    构建 0 错误，DLL 已更新。
> 5. [未验证] 修复后的实机显示回归（本机 DEBUG 实例标题编译被 3 实例并行拖慢，未走完）；
>    待用户重启实测。遗留疑点：状态页 bodygraph 装配（GET_BODY_SETS.ERB:161 只取
>    RESULTS:0 底图、未含特效层）与无目轨迹的对应关系待实机确认——若状态页走带眼
>    合成而仍无眼，需继续用调试控制台探针（已建立：SPRITECREATED/几何/HTML_PRINT img
>    三类）下钻。
> 附带发现（P3）：调试面板命令输入框在面板隐藏后仍持有键盘焦点吞游戏按键；
> DEBUG 模式下 ERB 编译期排队调试命令可能挂起引擎（两次复现，机制未查明）。

> 验证确认（2026-11-16，用户实测）：重启后猫包绘眼睛差分正常显示，修复生效。
> （"可以"确认；覆盖上文补遗 2 的 [未验证] 项。）
