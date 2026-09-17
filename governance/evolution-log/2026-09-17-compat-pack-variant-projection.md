# 2026-09-17 兼容包变体 handler 投影（builtin:* 真实替换引擎 handler）

## 任务目标与成果

一句话：包声明的 builtin:* 变体选择真实生效于引擎投影——BuiltinCompatPackVariants
注册表（builtin 名 × 指令 → LegacyInstructionVariant，与 FunctionIdentifier 的 handler
构造知识同文件对齐）+ Compose 第 4 参 variantSelections 注入（SubstituteInstruction）+
组合级校验（变体名 → 可作用指令集，未注册组合在校验段拒载）+ Host/Program 透传
（ActiveVariantSelections 与 ActivePackModuleIds 同生命周期，失败分支同清）。
Core.Tests 70/70（+组合校验 2 例）、SurfaceSmoke 变体断言真 enum、三冒烟全绿。

## 关键决策与 Why

1. **注册表是唯一事实源**：builtin 组合表只在引擎侧（LegacyCompatibilityModules.cs，
   紧邻 handler 构造知识）定义一次，Host 的校验上下文（组合级）与 Compose 的投影解析
   （TryResolve）都从它导出——校验与投影永不分叉。
2. **组合级校验放在加载段而非投影段**：builtin:snake 选 PRINT 这类"名字对但没注册
   handler"的组合在校验段拒载（降级不变量）；投影段 TryResolve 失败仍抛异常作防御
   深度（宿主绕过路径）。
3. **v1 不做 enum 开放化**：设计 §4 的 ICompatInstructionFactory（包自带变体）仍留白，
   builtin:* 先闭环——closed enum 的映射表化是开放化的前置（注册表值类型换成工厂
   即可平滑演进），不提前付接口面成本。

## AI 表现复盘

- 有效：注册表/校验/投影三层从同一张表派生，写完即一致；SurfaceSmoke 断言用真
  enum（SetBgImageSnake）而非字符串，投影断言与引擎行为零距离。
- 低效：三个编译/断言错误都是"改了一处忘配套"（元组键字典误用 StringComparer 构造、
  python 补丁残留旧行、smoke 用例漏传新增的第 4 参）——新增参数后调用点全量排查
  应该是本能动作，靠编译器/测试逐个抓出。

## 教训 → 具体优化行动

- 给方法加参数后立即 grep 全部调用点配套（本次第 4 参漏传 smoke 用例就是反例）。
- 元组键 Dictionary 不可用 StringComparer 构造（无此重载）；默认比较器对 string 元组
  即 Ordinal，语义等价。

## 验证记录

Core.Tests 70/70（+2 组合校验）、Facade.Tests 33/33；三冒烟全过（SurfaceSmoke 含变体
真 enum 断言）；Godot 无头构建 DLL 21:06:14 更新 0 错误；无新文件（无 .uid 事务）。

## result-review 返工记录（首轮 95 → 复评见后）

首轮 95/100 未过：主工程实测 2 条新增警告与初稿"零警告"申报矛盾——
LegacyCompatibilityModules.cs:70 CS8600（TryGetValue out 非空）与
LegacyCompatibilityProfile.cs:279 CS8632（惰性 `?`，同类问题 9bd765f 后复发）。
处置：前者 out 参数改可空；后者按评审推荐给 Profile 文件头启用 `#nullable enable`
归一两个编译上下文，并顺手修复启用后暴露的 3 处既有可空性（ctor 默认参/两处
TryGetValue/out 声明）。**增量构建再次掩盖警告**（普通 build 显示干净、-t:Rebuild
才现形第 3 处）——终验口径一律 `-t:Rebuild`。
复验：主工程 `-t:Rebuild` 新增代码零警告（仅剩 AgentLlmMethods/KoujouPrefetchCache
两处 dev 存量）；SurfaceSmoke/Core.Tests 70/70 复绿。
