# 2026-09-17 兼容包加载器最小实现（接口定稿第二增量：DOD-2）

## 任务目标与成果

一句话：按 docs/designs/compat-pack-interface.md §11 DOD 第 2 项落地加载器四段——
显式发现（禁扫描）/ CompatPackLoadContext ALC 隔离（isCollectible，姊妹 PluginLoadContext
三条绑定规则）/ fail-closed 语义校验（CompatPackRules：未知 capability、API 版本、
变体对账、表面对账）/ 哈希固化（PackSha256=文件‖清单字节，供 DOD-3 并入 plan 哈希链）；
新增 19 项测试（加载器全链路 11 + 规则直测 8），GEmuera.Core.Tests 39/39。

## 关键决策与 Why

1. **加载器落 src/Core**（而非 Scripts 宿主）：ALC/校验/哈希全是纯 BCL 逻辑，Core 持有
   capability 词汇表与 v24 基线清单（对账数据源），且 xUnit 可直测；Core 因此新增对
   契约程序集 EmueraFacade 的 ProjectReference（零依赖方向：EmueraFacade 仍无任何引用）。
2. **测试程序集兼任真实包样本**：GEmuera.Core.Tests 内嵌清单（LogicalName 规范资源名）
   + HelloCompatPack 实现契约入口，加载器经独立 ALC 加载测试程序集自身文件——无需在
   仓库里放编译产物 fixture，全链路（发现→隔离→解析→入口→校验→哈希）都是真程序集。
   GetTypes 走 ReflectionTypeLoadException 容错（测试类混载 xunit 不阻断包发现，与
   PluginManager.getLoadableTypes 同款）。
3. **规则与加载分离**：CompatPackRules.Validate 是纯函数（清单+贡献+校验上下文→错误集），
   结构类负路径（重复贡献 id/重复绑定/变体选择冲突/Apply 抛异常）用手工对象直测，
   不依赖程序集加载——两种测试形态互补，负路径不用污染夹具。
4. **对账范围如实分层**：本增量做 Core 数据可支撑的对账（hide ⊆ v24 基线、register ∉
   基线、builtin 变体 ∈ 名录、非 builtin 选择 v1 拒绝）；引擎注册表级对账（add 名单须有
   真实 handler）与 gameIdentity 比对留 DOD-3 宿主接线（§5.3 原文如此分层）。
5. **TryLoadSet 全有或全无**：任一包失败整体回退纯 v24（降级不变量），失败时立即回收
   已加载包的 ALC，不留半套集合。

## AI 表现复盘

- 有效：夹具"测试集兼任包"一次跑通（ALC 自加载 + 容错 GetTypes）；fail-closed 三原则
  各有正反向用例；测试构造器内置基线前置事实断言（防生成清单漂移后假绿/假红）。
- 低效：一次测试文件里写了两组残句（HashSet 泛型参数笔误 ×2）靠编译抓出；CS8600 定位
  两次——第一次修错对象（以为在 manifest 赋值，实际在 FindSinglePackEntry 返回值），
  教训：**警告定位先 sed -n 看真实行，不要按记忆猜行号**。

## 教训 → 具体优化行动

- 警告/错误定位流程固化：`grep warning` → `sed -n 'Np'` 看原文 → 再修。
- Core 存量警告在案：KoujouPrefetchCache.cs:29 CS8600（dev 基线即有，非本增量引入，
  零警告口径=「不新增」；顺手修属无关改动，留给该文件下次触碰时处理）。

## 验证记录

dotnet test：GEmuera.Core.Tests 39/39、EmueraFacade.Tests 33/33；Core `-t:Rebuild`
（net8+net9）新增代码零警告（仅剩上述存量 1 条×2 TFM）；Godot 无头构建 DLL 01:34:26
更新、0 编译错误；方言三冒烟全过；新 .cs 的 .uid 侧车 9 枚随提交入库。
