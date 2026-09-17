# 2026-09-17 兼容包组装段 + DOD-3 端到端（接口定稿收官增量）

## 任务目标与成果

一句话：补齐设计文档 §11 DOD 第 2 项被推迟的「组装」段并完成第 3 项 hello-world 端到端——
CompatPackPlanAssembler 把包句柄折叠进 CompatibilityPlan（表面 ±差量、capability 账本并集、
模块闭包 +包合成快照、PackSha256 入哈希链），6 项端到端测试证明：显式启用→表面变化→
plan 哈希变化→禁用后与纯 v24 基线同实例同哈希（逐字节等价的 Core 层不变量入口）。
GEmuera.Core.Tests 52/52。

## 关键决策与 Why

1. **组装器独立文件而非扩 CompatibilityPlanBuilder**：builder 的贡献收集面向
   IDialectModule（编译期目录），包是运行期 ALC 产物；组装器直接构造
   DialectPlan/CompatibilityPlan（internal ctor 同程序集可访问），基线 plan 的表面字典
   ±差量出副本，不动 DialectRuntime.cs 既有逻辑零侵入。
2. **哈希两层链复刻 builder 同构**：dialectDelta（基线方言哈希 + pack=id|sha 有序对 +
   ±表面差量 + 变体选择）→ planEnvelope（profile + dialectHash + capability + save）。
   PackSha256 是一等输入：同包内容必同哈希、与包排列顺序无关（sorted），诊断可复现。
3. **空包集 = 原样返回基线实例**（同引用同哈希）——「未启用任何包的会话 = 纯 v24」
   从约定变成 API 行为；启用→卸载→空集重组装回到基线哈希有完整生命周期测试。
4. **跨包对账收窄到真冲突**：hide⊆基线 ∧ register∉基线（单包规则）传递性保证跨包
   hide/register 名字不可能相交，唯一真冲突是两包 register 同名——只查这一条 + null 句柄。
5. **变体选择/策略绑定入账本与哈希但不动投影**：引擎侧 handler 接线是宿主增量，
   组装器只产出计划数据（设计 §5.4 分层如实执行）。

## AI 表现复盘

- 有效：端到端测试直接复用「测试程序集兼任包」夹具（DOD-2 建立），零新增夹具成本；
  计数守恒断言（561+1-1=561）顺手证明了表面折叠的精确性。
- 低效：三处编译期小错（Dictionary foreach 类型笔误、TryGet 在 plan/dialect 层级写错、
  null 数组可空性警告）——都靠编译器第一轮抓出，但暴露写码时对既有 API 层级记忆不牢，
  应先 grep 层级再写调用。

## 教训 → 具体优化行动

- 调用既有 API 前先 `grep -n "成员名" 所在文件` 确认层级与签名，不凭记忆写。
- DOD-3 的「逐字节等价」在 Core 层的可证形式 = 空集组装返回 Same(基线) + 哈希相等；
  引擎级逐字节（会话表面哈希/门禁快照）待宿主接线增量补证。

## 验证记录

GEmuera.Core.Tests 52/52（46 旧 + 6 新）、EmueraFacade.Tests 33/33；Core `-t:Rebuild`
新增代码零警告（仅 KoujouPrefetchCache 存量）；Godot 无头构建 DLL 02:30:11 更新；
方言三冒烟全过；新 .uid 2 枚入库。

## result-review 返工记录（首轮 97 → 复评见后）

首轮 97/100 未过，两项必修与处置：
1. **变体选择跨包静默 last-wins**（评审变异实证：同启用集不同顺序 → 不同哈希，证伪
   类注释的顺序无关承诺与设计 §9 哈希稳定性）→ CollectVariantSelections 改为同键同值
   幂等、同键异值收集错误整体拒载。
2. **哈希组成无测试哨兵**（变异实测：去掉 PackSha256 或变体行，6/6 仍绿）→ Core 开
   `InternalsVisibleTo("GEmuera.Core.Tests")`，测试用手工句柄（manifest 经 public
   TryParse 构造——Emuera 程序集的 internal ctor 对测试不可见，这个跨程序集可见性
   边界值得记住）直接钉住：PackSha256 入哈希、变体行入哈希、双包 [A,B]vs[B,A] 同哈希。
另按评审建议在方法注释记明：包 saveProfileId v1 不参与组装，留宿主接线裁定。

复验：GEmuera.Core.Tests 56/56（含 4 条手工句柄哨兵）、EmueraFacade.Tests 33/33、
三冒烟全过、Core `-t:Rebuild` 新增代码零警告。
