# 2026-09-12 quirk 账本收编 + v18 基线方言（方言接口完善第四、五步）

## 任务目标与成果

backlog 第 3 项（解析器 policy 分支收编为 capability 驱动）与第 4 项（v18 模块）完成。方言接口的"模块形态"主干至此闭合：清单（生成器）、变体（模块声明）、契约（名集激活）、quirk（capability 账本）、基线族（v24/snake/erafl/v18 四 profile）全部模块化。

## 关键决策与 Why

1. **quirk 收编选"派生"而非"字符串直查"**：~15 处消费点保留类型化 policy 访问（`Snake.AllowsPrivateArguments`），但取值改为从 `plan.CapabilityIds` 逐属性派生（snake 7 项 + erafl 3 项 id，见 `SnakeCompatibilityCapabilities`）。理由：字符串直查丢失类型安全且热路径散布字符串比较；账本的价值在"声明即文档 + [LOAD] 可观测 + 映射穷尽门禁"，不在于改写调用形态。映射穷尽性由 SurfaceSmoke 反射断言把关（加 policy 属性不登记 id 即红）。
2. **v18 建模为独立基座**（`gemuera.v18`，不依赖 gemuera.v24）：取证证明 v18 是 v24 的严格子集（指令 266⊆305、函数 160⊆270、无独有名），但语义上它是"另一套基线"而非"v24 的增量"。会话表面 = v24 引擎投影减 149 个 v24 后增名；引擎未实现的 v18 名（若有）自然不存在于投影——清单忠实于引擎能力。
3. **第四处 profile 镜像的教训**：LegacyRunnerConfig.ValidateSupportedProfile/NormalizeProfile 硬编码三 profile 且 NormalizeProfile 会把未知名**静默降级为 v24pure**——这类"静默路由"比报错危险得多。修复为委托 FirstWindow.TryNormalizeCoreProfileName 单一事实源。

## AI 表现复盘

**有效**：quirk 断言入参错了一次（`ShouldSubmitBlankPointerStringInput(0,true)`——左键按设计不触发），30 秒从 Core 算法实现读出真语义修正，没有瞎改产品代码。v18 取证脚本一次跑通（正则先取样两条注册语法再全量）。

**低效**：v18 冒烟首跑 Failed，追了两层（次生 NRE 掩盖根因 → `_config` null → Load 抛错 → 第四处白名单）才发现是配置加载器自己的名单。若一开始 grep 全仓 `v24pure.*snake.*erafl` 模式就能列出全部镜像点一次改完。

## 教训 → 具体优化动作

1. **"消灭某名单"类任务的开工动作固定为全仓 grep 该名单的字面模式**（如 `v24pure.*snake`），列出全部镜像点一次处理。本轮四次教训（ps1/schema/LegacyRunnerConfig/还有 core-contracts 的断言）分三轮才发现。
2. **次生异常掩盖根因的排查口诀**：报告/日志路径上的 NRE 永远先怀疑"更早的异常让字段未初始化"——本例 `_config` 为 null 直指 Load 阶段抛错，而不是 Finish 逻辑有 bug。
3. **新增方言的标准接线清单**（下次直接照做）：FirstWindow 常量+TryNormalize → Program 枚举+三 switch → Core 模块+profile → 桥层模块+closure → 生成器+再生 → 三冒烟断言 → fixture 执行级冒烟。

## 后续路线（更新）

1. eraBlue 专属 profile（插件契约已通，缺方言面声明）。
2. v18 语义级差异（超出名单层面：文法/行为差异，需 v18 真机游戏差分测试）。
3. megaten（EM 家族）与 tw 画蛇添足（skia 家族）模块——取证方法已就绪。
4. 启动器 UI 的 v18 选项（当前仅 runner/诊断可达）。
