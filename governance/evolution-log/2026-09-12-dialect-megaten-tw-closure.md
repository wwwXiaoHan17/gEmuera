# 2026-09-12 megaten 模块 + tw lineage 定论（方言版图收口）

## 任务目标与成果

用户版图六方言全部落定：v24/snake/erafl/v18/erablue/megaten 六个模块 + tw 的 lineage 定论（=snake，不建空壳）。megaten 以"v24 基座 + 启动容错 quirk + 面零增量"的薄模块落地，真实游戏实测 first_wait_reached。

## 关键决策与 Why

1. **megaten 面零增量是取证结论不是偷懒**：8396 个 ERB 扫描零方言外名使用；其私改启动器（Emuera1824+v8.1，无源码）的面不可知——不臆测裁剪 v24 基座（误伤风险大于收益），模块承载血统与 quirk 落点即可。生成器哨兵断言"megaten 增量必空"把这个结论锁进门禁。
2. **tw 不建模块**：4110 ERB 扫描显示同时用 snake 系名与 v24 后增名——面与 snake 完全重合（skia 启动器本就是 snake 参考系）。建一个 delta 为空的重复模块是纯官僚化；正确动作是 lineage 映射记录（本文件即账本）。**"为对称而建模块"是方言账本的反模式。**
3. **megaten 的 quirk 证据形态与 erablue 完全同构**：私改文法行 → 严格 v24 致命退出 → 原生启动器容错。`startup.continue-after-fault.v1` 三家共用（snake/erablue/megaten），验证了 quirk 账本设计的复用价值。

## AI 表现复盘

**有效**：megaten 首跑（v24pure）的 display.raw 直接给出致命退出文案与出错行（VELVET_ROOM.ERB:2860），quirk 判定零猜测。megaten/tw 双实测一次通过。

**低效**：`maxRuntimeMs: 900000` 超出 schema 上限 600000 导致一轮静默失败（Load 抛错又被报告写入器次生 NRE 掩盖——同款掩码问题第二次出现）。**LegacyRunnerHost 的 Fail 路径在 _config/_report 未初始化时会丢原始错误**，这个可观测性缺陷值得单独修。

## 教训 → 具体优化动作

1. **runner 配置数值先对 schema**：maxRuntimeMs/initialWaitTimeoutMs 上限均 600000；超限即 Load 抛错且错误被掩码。CJK 路径 + utf-8-sig + 数值在界内，三查过再跑。
2. **LegacyRunnerHost._Ready 的 catch 应 GD.PrintError 原始异常再 Fail**（当前 Load 阶段异常只进 AddError，而 _report 为 null 时彻底丢失）——留待小修。
3. **方言版图判定流程至此完全定型**：自带启动器 exe 定血统 → ERB 全库扫描定面 → v24pure 试跑定 quirk → 薄模块 + 哨兵锁结论 → 真实游戏实测。六方言全部走完此流程。

## 方言版图终态（2026-09-12）

| 方言 | 模块 | 面 | quirk | 实测 |
|---|---|---|---|---|
| v24 基线 | gemuera.v24 | 561/266 | — | ✔ fixture |
| snake 系（eraTW/画蛇添足） | game.snake | 668/349 | 7 quirk | ✔ 真实游戏（tw） |
| erafl | game.erafl | +SETANIMETIMER | markup×5+输入×3 | ✔ 真实游戏 |
| v18 老游戏 | gemuera.v18 | 417/160 | — | ✔ fixture |
| erablue | game.erablue | +SETANIMETIMER | 插件+容错 | ✔ 真实游戏 A/B |
| megaten | game.megaten | 零增量 | 容错 | ✔ 真实游戏 |
| tw 画蛇添足 | （=game.snake） | — | — | ✔ 真实游戏 |

## 后续路线

1. v18 语义级差异（文法/行为层，需真机差分）。
2. 启动器 UI 的 v18/erablue/megaten 选项与目录路由。
3. LegacyRunnerHost Fail 路径可观测性小修 + identity 回读 CJK 修复。

## 补遗（同日收尾）

1. **启动器 UI 接入**：高级兼容下拉增至六 profile（v18/erablue/megaten 可选）。compat 目录路由零改动即支持新方言——`DirectoryRouteProfileCatalog` 本就是 `BuiltInDialectCatalog.CreateLegacyProfileCatalog()`，新 profile 注册时路由自动生效（这是"单一事实源"设计的直接红利：UI 选项是唯一需要手工同步的点，目录路由不是）。
2. **LegacyRunnerHost Fail 掩码修复**：_Ready catch 先 `GD.PushError` 原始异常——Load 阶段异常曾两次被次生 NRE 完全掩盖。
3. **identity 回读 CJK 修复**：ps1 三处 `Get-Content` 补 `-Encoding UTF8`；erablue 真实游戏带 `-ExistingIdentityDirectory` 实测通过（上轮正是此场景失败）。CJK 路径 checklist 第四条补全："**读** JSON 也要 -Encoding UTF8，不只写"。

## 补遗二：吸收 origin/dev PR #5（524122 的 megaten 适配），双取证线合成（2026-09-12 晚）

**背景**：用户合并了 524122 的 eraMegaten 适配进 dev（09-07），与我的 megaten 模块（09-12）同 ModuleId 对撞。关键认知：**两版针对不同游戏版本与故障模式**——他测 eraMegaten 3.54 汉化版（Emuera.NET 0.2.6 宿主，IC 配置组合致 823 条警告拒跑→3 个大小写/遮蔽门控端口 + 13 处引擎门控）；我测 Var.157_3.43 安卓版（Emuera1824+v8.1 宿主，私改文法致命退出→启动容错 quirk）。**合成而非取舍**。

**合成方案**（merge 提交 4c9fd34，两条分支各一份）：
- Core 模块：他的 3 端口+SaveProfileId+defaultPorts + 我的 continue-after-fault capability，requiredCapabilityIds=[容错]；
- 接线：取本线单一事实源（LegacyRunnerConfig 委托 FirstWindow、ps1/schema 吃 profiles.generated.json）——他的硬编码第四名方案被生成清单覆盖；
- 桥层：并集（V18/EraBlue/账本 + IMegatenCompatibilityPolicy 全套；模块类取他的注入版）；
- render 分支同轮 merge origin/dev（零冲突）+ core-contracts PRINTN 前提修真最小子集。

**其他吸收**（随 merge 自然带入）：安卓导出四连修（SQLitePCLRaw 强制下载/assets-text 导出 SJIS 映射/compat 递归找根/.import keep）、LegacyRunnerHost 早期 Fail NRE 守卫（比本线 render 版更全，覆盖 config 未载路径）、DialectInventory.psm1 BOM（cp936 脆弱——本线踩过的同款坑）、AGENTS.md 游戏适配铁律（与本线方言方法论同向）。

**教训**：1) **同 ModuleId 对撞先问"是不是同一个游戏版本"**——不同版本的同一游戏家族可能需要互补 quirk 集，合并语义是并集不是二选一。2) 部分克隆（promisor）仓库的按需对象获取走仓库配置代理，命令行 -c 不继承——lazy fetch 失败先查 `git config http.proxy`。3) 用户报的代理端口可能与实际监听不符（7890→实际 7897），netstat 一查便知。
