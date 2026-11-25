# 过期第一方包自愈：清单预检 + 启动器改绑 + 释放器例外（2026-11-24）

> 上一份（同一会话的前一任务）：[`2026-11-24-snake-18.0-alignment.md`](2026-11-24-snake-18.0-alignment.md)。
> 本任务处理它重钉 `baseSurfaceHash` 之后暴露的问题：仓库外的旧包副本被引擎拒载、游戏静默回退基线。
> 用户指令链：「过期包自动改绑内置包 — 怎么做呢？」（引用批注）→「但是内置包我也用不了啊」→「自愈方案实现看看」。

## 1. 任务目标与成果

**现象与根因。** 把蛇版对齐到 Skiav18.0.0 时 `baseSurfaceHash` 从 `99a8763b…` 重钉为 `5310c6d8…`，
引擎随之更新，但启动器实际加载的是仓库外的旧包副本：旧包声明旧哈希，引擎按设计拒载并回退 v24 基线，
只在日志里留一条 Error，界面无感。eraTW 在基线下 `SQL_*` 指令全部无法解释，一轮 466 条 `警告Lv2`，
卡在启动阶段。两处副本来源不同：

- 桌面（编辑器模式）：`launcher.cfg [compat_packs]` 按游戏保存的是包的**绝对路径**，指向 Godot 编辑器
  同级目录下 11-15 的副本；合并顺序是 按游戏选择 > 外部环境变量 > 映射默认值，存过的旧路径永远压过内置包。
- Android：包目录里若是用户手动放的文件（解压 zip 覆盖过一次也算），释放器按「用户版本」处理
  （`kept-user`），永远不更新。

当天早些时候做过一次不改代码的处置（刷新编辑器同级副本、重建 zip），之后用户要求把自愈做进代码。

**成果（可验收）。** 三件事，缺一不可：

1. **只看清单的预检**（`src/Core/Compatibility/Packs/CompatPackManifestPeek.cs`）：直接从包程序集字节读出
   内嵌清单，不装载、不执行；按与规则层**同一个**头部判定给结论。预检不通过 = 真实加载必然拒载。
2. **启动器改绑**（`CompatPackStaleSelectionRebinder.cs` + `scripts/FirstWindow.CompatPackSelfHeal.cs`）：
   注入引擎之前，选中的第一方包若对当前引擎不可用、且 App 内置了同名可用的包、且不是同一个文件，
   改用内置包并把改绑写回 `launcher.cfg`；换不了的原样注入并在状态栏明示。
3. **释放器例外**（`CompatPackStalePackPolicy.cs` + `CompatPackBundleInstaller.cs`）：安装根里的用户文件
   若是「与内置载荷同 packId、对当前引擎必然被拒」的包，而被内置载荷可用，则替换；旧文件备份为
   `.<packId>.dll.stale-bak`。其余用户文件照旧保留。

顺带修正两处让自愈「发生了但用户看不见或被误导」的问题：状态栏原有一句「已选 N 项兼容包，但包要求 v24pure
基线——本次组合将被拒载并回退 snake」是方言退役前的旧逻辑（`snake` 现在会被翻译成 v24pure + 包），
与自愈提示并排出现时自相矛盾；启动早期完成的改绑在界面建好前就被后续计算清掉。

**改动清单**（开工基线 `git status --short` = 53 条，均为前一任务与其他会话的未提交改动，不计入本任务）：

- 新增源码：`src/Core/Compatibility/Packs/CompatPackEngineHeader.cs`、`CompatPackManifestPeek.cs`、
  `CompatPackStalePackPolicy.cs`、`CompatPackStaleSelectionRebinder.cs`、`scripts/FirstWindow.CompatPackSelfHeal.cs`。
- 修改源码：`src/Core/Compatibility/Packs/CompatPackRules.cs`（抽出头部判定）、`CompatPackBundleInstaller.cs`、
  `scripts/GodotHost/FirstPartyPackBootstrap.cs`、`scripts/FirstWindow.cs`（1840 → 1851，仅接线钩子与注释）。
- 新增测试：`tests/xUnitTest/GEmuera.Core.Tests/CompatPackManifestPeekTests.cs`、`CompatPackStalePackPolicyTests.cs`、
  `CompatPackStaleSelectionRebinderTests.cs`、夹具 `FirstPartyPackBytes.cs`；`CompatPackBundleInstallerTests.cs` 追加 7 个用例。
- 文档与治理：`packs/README.md`、`docs/designs/compat-pack-interface.md` §14.2、`docs/logging-convention.md` §2、
  `docs/compat-pack-release-gates.md`、`governance/backlog.md`、`governance/prompt-patterns/README.md`。
- 用户打开着的 Godot 编辑器为新 `.cs` 自动生成了 `.uid` 侧车（含 `src/Core` 与 `tests` 下的），随源文件一起提交即可。

## 2. 关键决策与 Why

- **预检与规则层共用一份判定，而不是各写一遍。** 把 `CompatPackRules.Validate` 里只依赖清单头部与引擎头部值的
  规则抽成 `CompatPackRules.CollectHeaderErrors`（保留名 / 基线档案 / `baseSurfaceHash` 的有无与对账 /
  双轴版本 / `saveProfileId`），`Validate` 原样采信它的错误文本，预检调同一个方法。这样两处判据不可能漂移；
  规则层原有全部用例不改一行仍然通过。
- **预检只保证一个方向。** 预检不通过 = 必然被拒；预检通过 ≠ 一定可加载（能力词汇、表面符号、handler 对账
  需要完整上下文与包代码）。因此预检只用来决定「要不要换掉」和「要不要提示」，最终仍以真实加载为准。
- **替换范围收得很窄。** 只有「同 packId + 必然被拒 + 内置载荷可用」才替换；目标读不出、是别的包、或仍然可用
  （哪怕字节不同，例如用户自己修过的版本）一律保留。备份写不出来就不替换，绝不先丢用户文件。
- **换不了的保留原路径。** 引擎对包集合是整体拒载，保留原路径等于「本次回退基线」，与状态栏文案一致；
  若改成悄悄丢掉这个包，其余包会被加载，文案就变成谎话。
- **同一文件既是选中项又是内置路径时不改绑**（安卓上被用户覆盖成旧版的那种）：换路径没有意义，
  交释放器在扫描之前处理。
- **内置表按清单里的 packId 查，不按文件名猜**；文件读不出来时才退而按文件名。社区包不在内置表里，永远只提示。
- **只有按游戏保存的选择会被写回**；外部环境变量与映射默认值不归启动器持久化。
- **提示按会话记忆。** 改绑常发生在 `ResolveStartupGamePath`（界面尚未建好），等状态栏计算时包已经正常，
  提示会被清空——按游戏记住本会话做过的改绑，只要改绑结果仍是当前选择就继续显示。

**被否决的替代方案**

- `launcher.cfg` 改存包名而不是绝对路径：更彻底，但要改存储格式并迁移旧配置，收益不抵风险。
- 用一次性 ALC 真实加载一遍来做精确预检：包代码会被执行两次，且与「包程序集随会话确定性回收」的生命周期
  约束相冲。
- 引擎侧在环境变量里接受候选列表做回退：改引擎的注入契约，复杂度更高。
- 给内置包另开一个永远最新的应用私有目录：多一处包位置，扫描根与界面都要跟着改。

**被推翻的中间结论**

- 「自愈日志没出现是因为用了 UI 类别」——换成加载类别后仍然没有；实际是显式指定类别的非 Error 日志在最小
  配置下不落盘，改成不指定类别 + `[LOAD]` 前缀后出现。
- 「日志里路径只取末两级/末三级即可分清副本」——一包一目录的布局下新旧副本的末三级往往同名，改回用既有的
  `DiagnosticLogRouter.RedactPath`。
- 「APK 没有目录条目会导致内置包列不出目录」——导出的 APK 里确实一个目录条目都没有，但模拟器实测释放正常。
- （更早）记忆里「正常 APK 约 130MB」——那是 arm64 + x86_64 双架构的模拟器包；arm64 单架构手机包 78–80MB。

**查过但确认不是缺陷的**

- 安卓内置包释放机制本身：覆盖安装新 APK 后 `installed=5`，五个包自动升到新版。
- 本次对齐新增的常数表在 APK 上可用：模拟器里用内置蛇版包跑夹具，输出与桌面逐项一致。
- 用户桌面在刷新副本之后的会话：Error 级记录为 0，没有拒载。

## 3. 验证记录

证据目录（仓库外）：`D:\gemuera-reports\compatpack-refresh-1\`（`tools\runs\f1-*`、`f2b-*`、`f3-*`、
`desktop-e2e-final\`、`release-gate.log`、`governance-test.log`）。

**单元测试**

- [实测] Core xUnit：534 通过 / 1 跳过（前一任务后为 478 / 1）；新增 56 个用例，覆盖预检、替换策略、改绑判定、
  释放器例外。其中 `CompatPackManifestPeekTests.PeekVerdict_AndEveryHeaderError_AreExactlyWhatTheRulesReport`
  对 10 种清单逐条比对「预检给出的头部错误 ⊆ 规则层输出」，`PeekVerdict_MatchesLoaderVerdict_ForFreshAndStaleBytes`
  用真实加载器对账新旧两种字节；规则层 `CompatPackRulesTests` 全部原样通过。
- [实测] 「过期包」夹具不是测试桩：把真实蛇版包内嵌清单里的哈希等长改成 64 个 0（清单是未压缩资源，
  PE 布局不变），得到一份按旧表面快照发布的同名包（`tests/xUnitTest/GEmuera.Core.Tests/FirstPartyPackBytes.cs`）。

**门禁（全部改动落位后）**

- [实测] `Test-CompatPackReleaseGate.ps1`：PASS（8 项检查，桌面命令 7/7）——staging 完整、Core 534/1、
  Facade 79、ABI 4、三冒烟、lifecycle 4/4。
- [实测] `Test-Governance.ps1`：`M3-M7 governance contract passed.`。
- [实测] `dotnet build gemuera-c#.csproj -c Debug`：0 错误（3 个警告均为既有）。

**模拟器执行级**（x86_64 AVD，API 37，16KB 页镜像；临时 arm64+x86_64 双架构 debug 包；**不是 arm64 真机**）

- [实测] 场景一「内置路径上的包被手动覆盖成旧版」：启动即 `[LOAD] FirstPartyPacks installed=0 skipped=4
  kept-user=0 replaced-stale=1 failed=0`；包文件哈希回到 `ebf5987e…`，旧文件在
  `.pack.gemuera.snake.dll.stale-bak`（`8d24365a…`）；随后进游戏 `[LOAD] CompatPack enabled:
  id=pack.gemuera.snake version=1.3.0`，控制台输出 `CTBL:3:4=18 … CONSTTABLE_OK`。
- [实测] 场景二「保存的选择指向别处的旧包」：启动后 `launcher.cfg` 里该游戏的选择被改成内置路径；日志
  `[WARN][Load] LOAD.WARN … ResolveStaleFirstPartyPacks | [LOAD] CompatPack 自愈：兼容包 pack.gemuera.snake.dll
  与当前引擎不匹配（表面哈希过期（引擎表面已变）），已改用内置包 pack.gemuera.snake.dll。`；状态栏
  「目录路由: 'snake' → snake；兼容包 pack.gemuera.snake.dll 与当前引擎不匹配（…），已改用内置包
  pack.gemuera.snake.dll。」，原先那句误导性的「本次组合将被拒载并回退 snake」不再出现；进游戏正常加载。
- [实测] 场景三「过期社区包，没有可替换的内置包」：选择未被改动；状态栏「…兼容包 community.template.dll
  与当前引擎不匹配（表面哈希过期（引擎表面已变）），本次将以 v24 基线启动，请更新该包。」；进游戏引擎给出
  同一条规则下的拒载（`community.template 的 baseSurfaceHash 与当前基线表面快照不一致（声明 0000…）`），
  游戏在基线下运行。

**桌面执行级**（编辑器模式，无头启动启动器场景，最终 Debug 程序集）

- [实测] 借用用户真实的 `launcher.cfg`：快照 → 把两处蛇版包选择临时指向旧副本 → 跑 30 秒 → 取证 → 还原。
  上次启动的游戏那一项被自动改回 `D:/gemuera/compat_packs/pack.gemuera.snake/pack.gemuera.snake.dll`
  （另一个未被选中的游戏保持不动，待选中时再自愈）；用户自己的日志配置下留有
  `WARN Load LOAD.WARN FirstWindow.CompatPackSelfHeal.cs | [LOAD] CompatPack 自愈：…`。
  事后 `settings.cfg` / `launcher.cfg` / `config.toml` 与快照逐字节一致，临时产生的运行日志已删除，
  用户的编辑器与游戏进程未受影响。

**环境障碍与打通**

- 模拟器只吃 x86_64，而导出配置只出 arm64：临时打开 x86_64、命令行导出到仓库外，导完按备份还原
  `export_presets.cfg` 并核对 SHA256，同时把被导出覆盖的 Gradle 产物换回用户原来那份（脚本
  `tools\finish_export.sh`）。导出进程在 `[DONE] export` 之后不退出，按命令行特征只结束自己那一个。
- 临时导出里的测试包在路径里带用户名等环境信息，属临时物，已删除。

[未验证] arm64 真机上的三种场景；首次自愈调用在真机上的耗时（模拟器 debug 包上从释放摘要到自愈日志约 0.8–1.2 秒）。

## 4. AI 表现复盘

**有效做法**

- 先复现再设计：用户的反驳「内置包我也用不了」没有靠争辩，而是在模拟器上把释放链路实测了一遍
  （`installed=5` 证明机制是好的，`kept-user=1` 复现出真正的坑），自愈方案的第 3 条正是由此确定。
- 三块核心逻辑各自先写测试再实现，宿主接线放在最后；接线出问题时核心逻辑不必回滚。
  触发点：`CompatPackRules.cs` 的重构 → 规则层原用例一行未改全部通过，说明抽取没有改变行为。

**低效与返工**

- 记错了 API：以为 `PEReader` 有 `GetManifestResource`，编译失败一次后才改成「定位资源目录 + 按偏移读长度前缀」。
  可复用动作：对不熟的 BCL API，先在参考程序集里搜一下成员名再写。
- 测试夹具连错三次：写目标文件前没建子目录（5 个用例同时失败）、拿文件名去比完整路径、静态字段与方法重名。
  可复用动作：夹具的「造场景」函数自己负责把目录建好；断言前先确认比的是同一种东西。
- 日志没出现时先换了一个想当然的原因（类别），为此多导了两轮测试包（每轮约 4 分钟）。可复用动作：
  可见性问题先用一次最小对照实验定位（同一处代码换一种写法各跑一次），不要连续改连续导。
- 门禁命令把项目根写成了 `D:\\gemuera`，被 shell 吃掉反斜杠变成 `D:gemuera`，两个门禁都报「项目根不存在」。
  可复用动作：给 PowerShell 脚本传路径一律用正斜杠。
- 又踩了一次 `MSYS_NO_PATHCONV=1` 的坑（设了之后把 `/d/…` 路径传给 adb 与 Python），以及模拟器「开始」按钮
  点在 y≥2368 不生效。可复用动作：带设备路径的 adb 命令才设这个变量；本机文件一律写 `D:/…`；
  点不动先换坐标再怀疑应用。
- 模拟器取证取早了两次（应用冷启动比预期慢，自愈在取证之后约 1 秒才完成），各重跑一次。
  可复用动作：以日志里的事件为准等条件，不用固定秒数。
- 日志路径描述改了两轮（末两级 → 末三级 → 改回既有脱敏函数），第二次改动本身就是一次回退。
  可复用动作：改一处「为了更规范」的细节前，先拿真实数据验证它确实更好。

## 5. 教训 → 具体优化动作

- (a) 已执行：加载前预检与规则层共用头部判定——`src/Core/Compatibility/Packs/CompatPackRules.cs`
  的 `CollectHeaderErrors`，由同源用例钉住。
- (a) 已执行：需要默认可见的日志不要显式指定类别——写进 `docs/logging-convention.md` §2 的追加说明；
  自愈日志与释放摘要已按此写法。
- (a) 已执行：状态栏按「引擎实际运行的档案」判断是否会被拒载——`scripts/FirstWindow.cs`
  `UpdateSelectedGameCompatibilityHint`。
- (a) 已执行：用户提示词新模式（引用批注追问 → 环境反驳 → 放行）记入
  `governance/prompt-patterns/README.md` 第 12 行。
- (b) 本轮不做：启动器原有几处显式指定类别的告警、调试浮层的 `Core prototype unavailable` 提示、
  `FirstWindow.cs` 的体量、真机验收与耗时度量——见下节与 `governance/backlog.md`
  「过期第一方包自愈（2026-11-24）」小节。

## 6. 遗留登记

- P0：无（本轮范围「预检 + 改绑 + 释放器例外 + 可见提示」已全部落地，桌面档门禁通过）。
- P1：**自愈的 Android 真机验收未做**。为什么不在本轮做：设备未连接；模拟器是 x86_64，不能替代 arm64 真机结论。
- P2：**首次自愈调用的耗时未在真机度量**（一次性的引擎头部计算）。为什么不在本轮做：没有真机数据，
  先看真机验收时是否可感知再决定要不要改。
- P2：**启动器原有告警在最小日志配置下不落盘**。为什么不在本轮做：不在范围内，且状态栏已有对应提示。
- P2：**调试浮层在退役档案下弹出 `Core prototype unavailable`**。为什么不在本轮做：与包加载无关的独立界面瑕疵。
- P2：**x86_64 模拟器上 `e_sqlite3` 延迟初始化的告警未追**。为什么不在本轮做：本轮没有在模拟器上跑 eraTW，也没有真机。
- P2：**`FirstWindow.cs` 净增 11 行**。为什么不在本轮做：接线所需的钩子无法再少，整体搬移属于纯移动拆分，
  应单独成一次改动。

以上均已登记到 `governance/backlog.md`。

> 补遗（2026-11-25）：提交前审查发现，选择来自环境变量或映射默认值时不会写回 `launcher.cfg`，
> 每次刷新状态栏都会把同一对路径再追加进会话记忆，提示和日志叠成重复句。按游戏保存的选择不受影响
> （第一次写回后不再改绑）。已在 `scripts/FirstWindow.CompatPackSelfHeal.cs` 按归一化后的旧路径+新路径去重，
> 同一对只记一次、只打一次日志。[静态] 对照 `ApplyCompatPackEnvironment` 的调用点
> （选游戏、切高级兼容、改档案、`SetSelectedGamePath`、`ResolveStartupGamePath`）。
> 未重跑门禁：判定仍在 Core，这次只改宿主侧记忆。
