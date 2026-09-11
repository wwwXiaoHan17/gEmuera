# 2026-09-12 core-contracts 修真 + 蛇系函数契约模块化（方言接口完善第三步）

## 任务目标与成果

处理 backlog 第 1 项（core-contracts 既有红）与第 2 项（函数侧 DialectFunctionContracts 模块化）。核心反转：调查证明 PRINTN 家族"泄漏"是**误报**——core-contracts 的断言前提错误，不是桥层清单缺漏；真正的修复是改测试不是改清单。随后完成函数参数契约的选择权模块化。

## 关键决策与 Why

1. **"泄漏"判定必须回到参考源码逐名取证**：PRINTN/PRINTVN/PRINTSN/PRINTFORMN/PRINTFORMSN/SKIPLOG 在 emuera.em-master（v24 参考）与 snake 源的 BuiltInFunctionCode 枚举和 FunctionIdentifier 注册表**双双存在**——它们是 v24 基线指令（PRINTN 甚至注释着 vanilla 语义"改行をしないで入力待ち"）。gEmuera 里 `addSnakeCompatibilityFunctions()` 的命名误导了 core-contracts 的作者，snake 模块自己的注释早有警告："Handler class prefixes are not dialect ownership evidence"。**教训：方言归属争议永远 grep 两份参考源码的注册表，不信任本仓的函数命名或任何二手清单。**
2. **core-contracts 的两类过时用例分类修**：(a) 前提错误（PRINTN）→ 换真 snake 专属名 CALLSTR/TINPUTNF（v24 参考枚举零命中）并反向断言 PRINTN 可见；(b) 写在"空贡献世界"的用例（描述符驱动 Validate 抛错、空描述符路由基线）→ 按激活后新契约改写（会话驱动漂移门禁、条件可见性计数、TryCreateSessionView 正负用例）。
3. **函数契约模块化用"名集激活"而非逐名枚举**：21 个蛇系重载差异名进 snake 模块 Apply（`ActivateDialectFunctionContracts`），`DialectFunctionContracts` 只换数据源（`UsesDialectFunctionContract(name)` 替代 `Snake.IsEnabled` 全局布尔），checker 实现不动。与指令变体的绞杀者模式一致：选择权是模块数据，实现知识集中一处。

## AI 表现复盘

**有效**：动清单前先做证据评估（上一轮进化记录明确要求"先评估有无游戏依赖此泄漏"）——正因如此避免了把 v24 基线指令错误藏进 snake 清单的语义回归（那会让 v24pure 游戏用 PRINTN 时报"未声明"）。stash 二分法再次快速锁定"红是既有的"（上一轮已做过，这次直接引用结论省了一轮）。

**低效**：core-contracts 连修三轮才全绿（PRINTN 前提 ×2 处、Validate 旧语义、route 空基线、RESULT 不是函数）——应当第一轮就把文件里所有"空贡献世界"的用例 grep 出来一起改，而不是被断言逐个拦停。

## 教训 → 具体优化动作

1. **方言归属三步取证法**（写进本记录供所有后续方言任务引用）：名字在 emuera.em-master 的 BuiltInFunctionCode/FunctionIdentifier？→ 在 snake 源同两处？→ 都在=基线；仅在 snake=snake 专属；仅在 v24=v24 专属。禁止以本仓注册函数名或 handler 类名前缀推断归属。
2. **改测试文件前先全文 grep 同类过时前提**（"空贡献""Snake.IsEnabled""PRINTN"等关键词），一轮改完，避免逐断言返工。
3. **core-contracts 已入常规门禁**：AGENTS.md「构建与验证」新增方言/兼容层门禁集（三个契约冒烟 + 生成器再生 + 三 profile 执行级冒烟），后续会话首读即可见。

## 后续路线（更新）

1. 解析器 ~15 处 Snake.AllowsX/EraFl.IsX 分支收编为 capability 消费点（quirk ledger）。
2. v18 模块：emuera_v18_exported 源码机械枚举 v18↔v24 差异（三步取证法可直接脚本化）。
3. eraBlue 专属 profile（Phase C 插件线收尾后）。
