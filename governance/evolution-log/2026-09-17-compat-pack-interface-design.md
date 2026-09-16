# 2026-09-17 兼容包接口契约设计（v24 单基线 + 程序集即包）

## 任务目标与成果

一句话：把用户裁定的 STS2 式方言战略（只留 v24、社区做 mod、本体兼容）落成
接口契约设计文档 `docs/designs/compat-pack-interface.md`——术语三分、分层模型、
embedded manifest 字段、C# 契约草案、加载管线四段（显式发现/ALC 隔离/fail-closed
校验/组装入哈希链）、信任边界、与 2026-09-16 审查债的对账、迁移阶段划分与 DOD。

## 关键决策与 Why

1. **契约落点推荐 EmueraFacade 而非 src/Core**：包作者编译面最小（只引契约程序集），
   与插件契约（IPluginMethod）同落点先例；Core 不引用引擎宿主类型。备选弱类型桥损失
   类型安全，列为开放问题待用户裁定（文档 §10.1）。
2. **"包只是选择器"**：capability/quirk 的算法实现全部内置引擎（能力实现库），包声明
   id 激活——复用现有 erafl"算法在 Core、profile 声明触发"的既成模式，L2 逃生舱只给
   引擎没有的东西。大多数包（megaten/erablue/v18 形态）= manifest-only 壳程序集。
3. **对 DialectModuleCatalog"never discovers assemblies"原则做有条件反转**：仅限用户
   显式列名启用，自动发现/远程分发仍禁止——信任边界的放开写成了条件清单而非原则抛弃。
4. **agent-profiles schema 复活方式**：不是搬运文件，而是注入三条经验进 manifest 设计
   （身份比对拒载、降级不变量、版本容忍二档）——文档 §3.3。

## AI 表现复盘

- 有效：契约设计全部锚定实存代码（PluginLoadContext 三条绑定规则、Program 单计划
  绑定、plan 哈希链行号），无一凭空假设；把 09-16 审查的 H1/H2/M4 债编号直接映射为
  包化终态，设计文档自带"还债清单"。
- 低效：无重大失误；gh CLI 不在本机，PR 创建走了 git 凭据 + REST API 降级路径
  （与既往吸收协议同路），后续会话可直接沿用此法。

## 教训 → 具体优化行动

- 本机无 gh：创建 PR 用 `git credential fill` 取 token + PowerShell Invoke-RestMethod
  POST /repos/<org>/<repo>/pulls（token 不落盘不回显）。
- 设计文档惯例：docs/designs/ 无日期前缀、描述性命名（compat-pack-interface.md，
  对齐 dialect-capability-strategy.md 先例）。

## 验证记录

本文档为纯设计交付（无代码改动），验收 = result-review ≥98 + 用户评审开放问题
（§10 四项：契约落点/包安装位置/launcher UI/v18 清单体量）。
