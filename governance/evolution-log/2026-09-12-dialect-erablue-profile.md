# 2026-09-12 eraBlue 专属方言 profile（方言接口完善第六步）

## 任务目标与成果

erablue（碧蓝度假村）从 snake profile 独立为专属 `erablue` profile（v24 基座 + SETANIMETIMER + 插件 capability + 启动容错 quirk），并顺带修掉 runner 的 IntButton 等待判定缺口。A/B 验证：erablue/snake 双 profile 对同一真实游戏均 first_wait_reached 且控制台输出逐字一致。

## 关键决策与 Why

1. **血统取证先于建模**：游戏根目录的启动器 exe（Emuera.NET 1824+v24+EMv18+EEv55）+ 全库 ERB 扫描（仅 SETANIMETIMER 一条 snake 系指令、函数面零依赖、插件经 CALLSHARP 调用）把 erablue 定位为 v24(EM/EE) 血统——此前用 snake 是能跑但不真。
2. **启动容错 quirk 的证据链**：汉化 mod 有 v24 严格文法解释不了的行；游戏自带 emuera.config 明确 `解釈不可能な行があっても実行する:NO`——即它的原生启动器家族靠自身语义容错，不是靠配置。`startup.continue-after-fault.v1` 因此是方言真相而非 workaround。消费点随之方言无关化（读 profile capability，不再绑 Snake policy）。
3. **插件能力只声明不门控**：`plugin.external-assembly.v1` 入 erablue 账本（[LOAD] 可观测），但 PluginManager 加载保持宿主级——门控会让"任何 profile 跑任何游戏"退化，且六游戏矩阵的 snake 路径会回归。

## AI 表现复盘

**有效**：erablue 首轮 A/B 双失败的排查链高效——trace 时间戳（wait 出现在 +76.6s 却 480s 超时）一步证伪"加载慢"假设，直指判定缺口；display.raw 的致命退出文案（"エンターキーもしくはクリックで終了します"）直接锁定容错缺失。

**低效**：CJK 路径第三次坑（config JSON 无 BOM 被 PS5.1 按 GBK 读坏；identity 回读同类）。同一类编码坑三次分开踩，应当第一天就把"CJK 路径 + Windows 工具链"写成 checklist。

## 教训 → 具体优化动作

1. **CJK 路径 checklist（定型）**：涉及中文路径的 JSON 配置一律 `encoding='utf-8-sig'` 写（BOM）；被 PowerShell 5.1 读取的文件一律 ASCII-only 或带 BOM；`Get-Content -Raw` 无 BOM 必 mojibake。legacy-runner 的 identity 回读对 CJK 游戏路径是既有缺陷（-ExistingIdentityDirectory 不可用于 CJK 路径），待修。
2. **"等待判定"类谓词要一处定义**：IsWaitingInputSomething（runner）/IsWaitingValueSelection（面板）两套值-等待谓词差一个 IntButton 就能让真游戏永远超时。后续把值-等待谓词收敛到 uEmuera partial 单一定义（本例 runner 已改用面板的 IsWaitingValueSelection）。
3. **A/B 双 profile 对照是方言迁移的标准验收**：新 profile 与旧 profile 对同一游戏输出逐字一致 = 迁移零可观测差异的直接证据，比冒烟计数更强。

## 后续路线（更新）

1. megaten（EM 家族）与 tw 画蛇添足（skia 家族）模块——取证与接线套路已完全定型（本记录 + 前两份）。
2. v18 语义级差异、启动器 UI 的 v18/erablue 选项。
3. legacy-runner identity 回读的 CJK 修复。
