# 2026-09-12 渲染热路径三项优化（零可观测差异）

## 任务目标与成果

针对三大热路径（Canvas 全量重绘的逐字符网格绘制、合成路径 CPU/IO 税、500ms 跨线程等待）各落一刀：网格 run 批绘（26:1 调用压缩）、合成源 CPU pin（Android 专属）、GDRAWSTRING 等待忠实化（修静默丢文本缺陷）。分支 ai/render-hotpath-optimization（自 dev）。

## 关键决策与 Why

1. **批绘的零差异靠构造不靠测试**：批量条件四重把关——逐字符 advance 与格宽浮点精确相等、格点累计整型（半格限偶数字号）、非实心块元素、整段在 part 宽度内；再以 GetStringSize 整段复核（与 DrawString 同一布局引擎，堵 fallback 漂移）。任何一条不满足即回退逐字——默认字体 advance 非严格网格时自动全回退（实测如此），eraTW 的网格字体则 26:1 生效。**保守回退是特性不是缺陷**。
2. **像素 A/B 是零差异的门禁**：改动前先采集 baseline 截图（`-UseDisplayServer` + `captureScreenshot=true` + 专用箱线/块元素/全半角混合 fixture），改动后同配置重跑比对 sha256——逐字节相等才算过。语义级 display.json 不够（它不含像素）。
3. **等待忠实化而非调参**：500ms 超时丢文本是可观测差异（缺陷），不是性能权衡。ERB 语义同步、渲染组件随主帧消费、worker 是后台线程——无界等待语义正确且无死锁路径。

## AI 表现复盘

**有效**：先基线后改码的顺序避免了"改完没对照"的尴尬；探针分两档（20/1000）先证机制生效再证真实游戏比例，"零差异"没有被"没走批路径"的假绿欺骗。

**低效**：探针阈值第一次设 1000 对小 fixture 无输出，多跑一轮才意识到。探针阈值应按被测数据量预估。

## 教训 → 具体优化动作

1. **像素级零差异验证套路定型**：专用混合 fixture + UseDisplayServer 截图 + 改前/改后 sha256 比对，配语义冒烟双保险。后续一切渲染路径改动照此办理。
2. **W3（无界等待）无桌面冒烟覆盖**：gridtext/eraTW 不走 GDRAWSTRING；其正确性依据是代码审查（消费方随主帧、worker 后台线程）——**Android 首启实测时观察 G 系文本渲染**，若出现卡死优先查 EmueraTextRenderComponent 的场景生命周期。
3. 网格批绘的下一个台阶是**行级烘焙纹理**（40 次调用→40 次纹理绘制），但其像素等价在分数缩放下不可构造保证，需真机 DPI 矩阵验证后再动。

## 后续路线

1. 行级烘焙纹理（Pain 1 深水区，需缩放矩阵验证）。
2. SkiaSharp 合成后端（Pain 2 主刀，位级对齐测试先行——RoundTripChannel 怪癖是硬门槛）。
3. Android 首启验证 W2/W3 实效（合成源内存涨幅 + G 系文本渲染无卡死）。

## 补遗：ColorMatrix 替换路线双否决（同日）

1. **v24pure/snake fixture 补跑**：上一提交声明"无回归"但未实跑（惯性声明错误）——本日补跑双 Passed，提交声明与证据对齐。
2. **Skia SKColorFilter 否决**：位对齐工具 150,752 比较中 49,969 不匹配（alpha=0 强置全零 + 舍入差异）。零可观测差异约束下不可替换。
3. **gather/scatter SIMD 否决**：位级对齐成功达成过（零差异），但 1Mpx×30 实测 **0.94x 无收益**——负优化不合入。过程的两个副产品价值更高：
   - **ToByte 舍入语义确证 = banker's**（Godot Mathf.RoundToInt = Math.Round 默认 ToEven：165.5→166、90.5→90）。此前 away-from-zero 假设是错的，注释已固化防后人重蹈。
   - **float 向量判奇必须走整数域**：BitwiseAnd(floatVector, 1.0f) 是位模式与运算，不是数值判奇——中点全部失效的根因。
4. **方法论沉淀**：位对齐工具是"替换路线"的裁决设施（采样空间+双路比对+证据输出）；性能路线必须配 bench，"位级对齐成功"≠"值得合入"。

## 后续路线更新

- Pain 2 SIMD 复试前提：byte→float 向量转换 + 通道 deinterleave 全向量化（消掉 gather/scatter 标量段）。
- Pain 3 的 256 槽环形队列与 Pain 1 的行级烘焙仍留档（前者需会话协议重设计，后者需真机 DPI 矩阵）。

## 补遗二：环形队列实测裁决 + Android 首启验证清单（同日）

### 环形队列（Pain 3）实测裁决

精读修正审计印象：`EnqueueUI` 满槽行为是**倍增压容**（`GrowUiRingLocked`）而非丢弃/阻塞——不存在"溢出丢失"；消费侧已是**帧预算制批消费**（Android 128 条/9ms、桌面 96 条/7ms 每帧）；水位计数（pendingUiActions/pendingDisplayActions）与等待原语（WaitForUiFrameAfter/WaitForDisplayWorkDrained）均已存在。"256 槽"只是初容量。

补齐观测（零语义差异）：`uiQueueHighWaterMark`/`uiQueueGrowCount`（FlushUI 尾部更新、扩容处低频 Warning）+ `GetUiQueueStats()` + legacy-runner 结束时 `[UIQUEUE]` 落 godot.log。

**实测数据（真实游戏全流程到 first_wait）**：
- eraTW（snake，4110 ERB）：`highWaterMark=2 growCount=0 capacity=256`
- megaten（snake 面，8396 ERB、5522 行加载）：highWaterMark=2 growCount=0 capacity=256（注：本分支无方言线 profile 白名单，megaten 以 snake 面跑——队列水位与 profile 无关）

**裁决**：消费预算完全罩住生产，256 初容量在真实负载下水位个位数——**现状即最优，无需调容量/背压**。扩容路径保留为极端突发的正确性兜底（不丢语义），观测使其从静默变为可诊断（若未来某游戏触发 grow，Warning 会直接指认）。

### Android 首启验证清单（W2/W3，交真机执行）

**W2 合成源 CPU pin（120s 滚动窗豁免释放）**：
1. 用例：图形密集游戏的连续 G 系合成场景（eraTW 魔法使系/erablue 效果页）跑 ≥5 分钟；
2. 观察点：(a) 合成卡顿是否改善；(b) 整体 RSS/纹理内存涨幅——预期上限 ≈ 120s 窗内活跃合成源像素总量（正常 <30MB 量级），若异常增长或 OOM 即记录；
3. 紧急回退：`SpriteManager.IsRecentCompositionSource` 返回 false 即恢复原释放策略（改一行重出包）。

**W3 GDRAWSTRING 无界等待**：
1. 用例：任何调用 GDRAWSTRING 的游戏（erablue/汉化系）正常游玩 + 制造主线程重载（快速滚动+连续点击）；
2. 观察点：G 系文本是否完整渲染（修复点=原 500ms 超时静默丢文本）；worker 是否卡死（画面停但主线程响应）；
3. 卡死首查点：`EmueraTextRenderComponent` 场景生命周期——若组件随场景释放而 pending item 未完成，worker 会永挂于 `item.Completed.Wait(Timeout.Infinite)`；表现为 ERB 停跑、UI 仍动。此场景需改为"场景释放时完成或取消挂起项"，届时回调本条修正。
