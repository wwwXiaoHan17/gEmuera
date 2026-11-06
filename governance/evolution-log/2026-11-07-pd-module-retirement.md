# P-D 内置方言模块退役（2026-11-07）

## 1. 任务目标与成果

把引擎内置的 5 个方言（v18 / megaten / erablue / eraFL / snake）全部退役为第一方兼容包（`packs/pack.gemuera.<profile>/`），
同时补齐 launcher 映射/翻译、包表面契约（contractAbi 1.1 碰撞态）、第一方包分发链路（staging + 启动释放）、
legacy-runner 退役 profile 翻译与"不假绿"断言，并补齐指导文件/模板/pack README/AGENTS/release-gates/evolution 记录。

**可验收结果**：本地分支 `ai/pd-module-retirement`（基于 dev `a375a57`）9 个提交；v24 基线表面哈希保持
`950dbeb9028a8a2fc73ac3699411a05b8e367458219b40bd862066c8902888c9`；v24pure 561 指令 / 266 函数不变；
`profiles.generated.json` 仅 v24pure；桌面 release gate PASS（8 项检查 / 7 命令）；Core 314 passed / 1 skipped、
Facade 79、ABI 4；三冒烟全绿；governance PASS。**本分支不可发布**（真机/六游戏证据未采集）。

## 2. 关键决策与 Why

1. **冻结包校验表面宇宙**（`tools/dialect-inventory/retired-dialect-surface-universe.json`）：
   退役会删除生成器读取的模块类；若让已退役 profile 的 METHOD/capability/variant 词汇随删除收缩，
   `baseSurfaceHash` 与 `MethodProjectedInstructionNames` 会漂移，旧包/旧 v24 哈希失效。
   否决"每次生成都重算六 profile 并集"（会随退役不可逆）。落地：冻结工件只读，生成器正常模式 =
   live v24pure + 冻结退役数据，哈希钉扎每步回归。
2. **包表面契约正式扩展（contractAbi 1.1 的 `addInstructionCollisions`）**：
   snake 的 `BITMAP_CACHE_ENABLE` 需要"指令可见 + 函数隐藏"；用 hideFunctions 会被规则层双形态镜像
   反向隐藏指令。否决"把该名字从 hide 清单剔除"（函数会泄漏）与"宿主特判 packId"（不可维护）。
   `engineApi`/`baseSurfaceHash` 输入不变，旧 1.0 包继续加载。
3. **旧引擎绑定翻译点**：盘点确认 6 处，而非 spec 假设的 2 处；每处统一走 `CompatPackProfileMap`
   + `ResolveEngineProfileIdForBinding`，未翻译 id 直接抛而不是静默回退。
4. **分发链路**：一包一目录 + staging（`compat_packs/<packId>/<packId>.dll`，gitignored）+ 导出
   include_filter + 启动释放；installer 纯 Core 逻辑（SHA256/原子写/last-installed 记录/失败聚合），
   宿主只在权限解析后的包扫描前调用。否决"导出时把包放 exe 旁边"（APK 无 exe 旁写权限）。
5. **runner 不假绿**：退役 profile 一律翻译为 v24pure + 显式 `GEMUERA_COMPAT_PACKS`，并在每次 run
   后断言日志出现该 packId 的 `[LOAD] CompatPack enabled`；缺包/未加载即非零退出。
6. **交付方式**：OWNER 中途指示"完成记得上传 dev 分支"，覆盖最初"仅本地分支"的裁定；本轮完成后
   尝试 fast-forward 推送 `origin dev`（不 force），并以实际网络/凭据结果为准。

**被推翻的中间结论**：
- "包 hide 函数会自动只影响函数可见性"——实测被规则层双形态镜像/组合闭包拒绝，故有第 2 条契约扩展。
- "v24pure 的 BITMAP 指令本就隐藏"——实测 v24 指令面包含它（METHOD 投影），正确做法是
  collision Present + 基线声明隐藏 + 包暴露，而非新增指令。
- "legacy-runner 只需改 `SupportedProfiles`"——实际要处理 cross-aba 两种子、报告字段与假绿断言。

## 3. 验证记录

构建判定（AGENTS 口径：error CS/MSB = 0 且输出 DLL 更新）：

- [实测] `dotnet build gemuera-c#.csproj -c Release -nodeReuse:false -m:1` → 0 error（多轮：Task 7/8/9/10 后）。
- [实测] `GEmuera.Core.Tests` → 314 passed / 1 skipped（含新增 `CompatPackBundleInstallerTests` 13 用例、
  snake 等价用例、collision 规则用例）；`EmueraFacade.Tests` 79；`EmueraPluginAbi.Tests` 4。
- [实测] 三冒烟：SurfaceSmoke（v18/erafl/snake 真实 pack DLL 装配 + fallback 探针）、
  RuntimeSmoke（v24MergedInstructions=561; v24Functions=266）、CoreContractSmoke（`[exit 0]`，需
  `DOTNET_ROLL_FORWARD=Major`，本机 net8 运行时跑 net9.0 工具）。
- [实测] `powershell ... Test-CompatPackReleaseGate.ps1 -ProjectRoot .` → PASS（8 项检查；桌面命令 7/7；
  新增 staging 完整性静态检查）。
- [实测] `Test-Governance.ps1` → PASS（7 项检查；lifecycle 4/4）。
- [实测] `LegacyDialectInventoryGenerator -- .` → v24 561/266；生成物重跑零 diff（除允许的
  `profiles.generated.json` 收缩为仅 v24pure）；`LegacySurfaceHash` = `950dbeb9…` 钉扎通过。
- [实测] snake pack 装配：plan 指令 = v24 ∪ 109 delta（670），函数 = v24 ∪ 87 delta − 2 hidden（351）；
  capabilities 19；变体 FOR/SETBGIMAGE；碰撞名 BITMAP_CACHE_ENABLE 指令 present/函数 hidden。
- [静态] PowerShell 脚本（runner/gate/staging）parser 校验通过；runner 仅 ASCII（PS5.1 BOM-less 约束）。
- [静态] 全仓 grep：无 `CreateLegacySessionPlan("<退役 id>")` 残留、无内置 snake/erafl 类引用、无旧扁平映射路径。
- [未验证] Android 真机权限时序/释放、六游戏运行、`-RequireAndroid/-RequireSixGame` 证据、legacy-runner
  无头执行级（本机无 Godot 4.7 可执行文件）。桌面门禁 PASS 不替代真机 e2e。

## 4. AI 表现复盘

- **有效**：先抽"冻结表面宇宙"再删除模块，使哈希/词汇表不随退役漂移；每个退役步用真实 pack DLL
  进 SurfaceSmoke 做行为投影，而不是只信 manifest 文本。
  触发点：`tools/dialect-inventory/retired-dialect-surface-universe.json` + `AssertV18PackEquivalence`。
- **低效/返工（可复用动作）**：用 Node 模板字符串直接改 PowerShell/Bash 脚本导致反斜杠被转义吃掉
  （`assets\text` 变成 `assets<TAB>ext`、`'stdout.log'` 引号被打断），花了 2–3 次重写才恢复。
  后续动作：改脚本一律用 `tools.edit` 精确替换或 `[IO.File]` + 单引号 here-string，并在每次编辑后跑
  `Parser::ParseFile`；PS5.1 脚本保持 ASCII-only（runner 文件头已有明确注释）。
- **低效/返工**：把临时探针文件 `.tmp-old-snake.cs` 放在仓库根，被 csproj 默认 glob 收录，导致
  "命名空间已包含定义"的诡异 CS0101。动作：临时文件必须放 `reports/` 或系统临时目录并在收尾删除。
- **险些踩的坑**：`MethodProjectedInstructionNames` 冻结数量 345 含已退役 snake 的 CJK 名字；若坚持
  "运行时逐名相等"会在退役后误红。已改为"运行时 ⊆ 冻结集合 + 冻结计数哨兵"。

## 5. 教训 → 具体优化动作

- (a) 已执行：AGENTS.md「游戏适配铁律」改为"兼容包/生成清单"，并补 `/compat_packs/` staging 规则与
  runner 翻译/不假绿门禁表述（AGENTS.md §开发方式、§构建与验证、§文件管理规范 5）。
- (a) 已执行：release gate 增加 staging 完整性静态检查（`tools/compat-pack/Test-CompatPackReleaseGate.ps1`）。
- (a) 已执行：`Build-FirstPartyPacks.ps1` 以生产映射为唯一 staging 集合，缺包/形态错误 fail-closed。
- (b) 本轮不做：通用第一方清单生成器（非 v18 包仍人工写）→ 遗留 P2；跨会话不同第一方包的 cross-aba
  runner 场景 → 遗留 P2（当前显式报错，不假绿）。

## 6. 遗留登记

- P0：无（本轮范围 5 步退役 + 契约 + 分发 + runner + 文档已落地）。
- P1：Gate 1/2/7 真机与六游戏证据未采集（无设备/无 Godot 4.7/无游戏库；依赖发布流程）→ 分支不可发布。
- P1：legacy-runner 执行级验证缺失（同上环境限制）；静态/翻译/断言代码已落地。
- P2：通用第一方包清单生成器（成本 > 本轮收益，人工清单已有等价测试 golden）。
- P2：cross-aba 两个不同第一方包同时启用（进程级 env 限制；当前显式拒绝）。
- P2：旧 `DialectInventory.psm1`/DIA 快照链在本机预存在红（Task 1 已裁定 G3 用静态 v24 哈希钉扎替代）。
- P2：`git push origin dev` 依赖网络/凭据；若失败，交付说明记录未推送及重试命令。

## 7. 覆盖声明

本记录只统计本任务在 `ai/pd-module-retirement` 分支上的改动与亲自执行的命令输出；基线 worktree
之外其它会话的未提交改动不计入。本日志随 Task 11 文档提交入库，最终复核证据在 Task 12 追加。
