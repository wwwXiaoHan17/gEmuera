# 2026-10-09 eraFL VARI 大小写归一修复（QUEST_57 nLOOP 误报）

## 任务目标与成果

eraFL0.49（erafl-master）加载时报 4 条 Lv2 警告
（`変数"nLOOP"はこの関数中では定義されていません` ×3 + `対応するFORの無いNEXT文です`，
QUEST_57_ゴブリンと雪国の狩人.ERB:3184-3187）。根因是
`ParseV24ScopedVariableDeclaration` 登记私有变量名时未按 `Config.ICVariable`
归一为大写，IGNORE CASE:YES 下 `VARI nLOOP` 登记 "nLOOP"、FOR 行查找侧
`GetVariableToken` 按 "NLOOP" 查表 → 误报。修复后 legacy-runner erafl 三重复跑
0 警告，游戏正常到标题画面。

## 关键决策与 Why

1. **条件判别用 `!Snake.IsEnabled || EraFl.IsEnabled`，弃用上一会话引入的
   `UsesDynamicScopedVariableArgumentBuilder` 派生属性**。
   - 上一会话把判别条件从 `!Snake.IsEnabled` 改成
     `!UsesDynamicScopedVariableArgumentBuilder || EraFl.IsEnabled`，其中
     `UsesDynamic => !AllowsScopedVariablePreRegistration`（capability 取反）。
     但 v24pure 会话的 snake policy 是 `LegacySnakeCompatibilityPolicy`（空
     capability 集，非 Disabled 实例）→ `AllowsScoped=false` →
     `UsesDynamic=true` → v24pure 被误切到运行期登记路径，RuntimeSmoke 立刻红。
   - 实测（RuntimeSmoke 临时 DBG）内置 erafl 会话 `Snake.IsEnabled=false`
     （血统≠模块选中），即 erafl 本来就走解析期登记路径——上一会话"eraFL
     需要在兼容层打开解析期登记"的诊断是错的，真正缺陷在大小写归一。
   - `|| EraFl.IsEnabled` 保留：对内置 profile 全部 no-op，只为社区包同时选中
     snake+erafl 模块时守住 eraFL 的 v24 系脚本风格。
2. **修复点放 `ParseV24ScopedVariableDeclaration` 的名字归一处**（与
   `TryCreateSnakeDynamic`、`#DIM/#REF` 声明路径一致），而不是查找侧放宽——
   查找侧大写归一是全局契约，不能动。
3. **RuntimeSmoke 增加 erafl profile 覆盖 + 混合大小写用例**
   （`VARI loopIdx = 7` 登记后须按 "LOOPIDX" 取到）。原用例全是大写名，
   大小写缺陷天然不可见；混合大小写用例让回归在 30 秒内暴露。

## AI 表现复盘

有效：
- 用 RuntimeSmoke 反射 harness 快速二分"源码行为 vs 运行时行为"，一轮就定位
  到 in-game 与 smoke 的差异点（大小写归一）。
- F# 脚本（dotnet fsi）直接复刻 DT_FROMXML 的 BOM 场景，5 分钟证实
  33ec97a 的 BOM 修复有效、用户 22:49 日志是陈旧构建产物，避免了一次
  不必要的返工。

低效：
- 一开始轻信上一会话注释里"eraFL 必须打开解析期登记"的诊断，按其思路
  修属性语义，浪费了 RuntimeSmoke 一轮红灯才发现条件组合被改坏。
  **教训：带"实证"注释的诊断也可能只实证了现象、没实证根因；动手前先跑
  门禁确认基线是否本来就红。**
- legacy-runner 首跑用 `-SkipBuild` 前的 harness 自建 build 因 NuGet SSL
  失败（NU1301），早知道直接 `-SkipBuild`（Godot DLL 已先行构建）。

## 教训 → 优化动作

1. RuntimeSmoke 已落 erafl + 混合大小写用例（本次完成）。
2. 新增 policy 布尔属性时，若它是某 capability 的"取补派生"，不要假设
   Disabled 实例与空 capability 实例取值一致——v24pure 是后者，语义会反。
   要么直接用 `IsEnabled` 级判别，要么在 SurfaceSmoke 里对派生属性加组合断言。
3. 诊断日志（如 `[ERAFL_COMPAT]`）走 GenericUtils → gemuera 日志文件，
   legacy-runner 报告目录里看不到；排查 runner 场景问题时直接去游戏目录
   或让 runner 收集 gemuera 日志。
