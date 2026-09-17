# 2026-09-17 兼容包宿主接线（引擎投影 / gameIdentity / [LOAD] 账本）

## 任务目标与成果

一句话：把包通道从 Core 数据面接进引擎会话——Compose 包差量回放（包注册名在引擎
会话可见、隐藏名移除）+ CompatPackHost（启用清单 GEMUERA_COMPAT_PACKS、capability
词汇表六 profile 并集、gameIdentity 与 GameBase.csv 比对、[LOAD] 账本、降级不变量）
+ Program.Main 首绑前组装接线。Core.Tests 67/67 + SurfaceSmoke 包投影用例（含信任
边界反例）+ 三冒烟全绿。

## 关键决策与 Why

1. **信任边界 = 显式包模块白名单**：首版 Compose 放宽（"未知模块当包"）立即被
   CoreContractSmoke 既有红线抓住（"未分类模块在内置闭包必须抛"）。正确形态是
   Compose/Create 增带白名单的 overload——只有 CompatPackHost.ActivePackModuleIds
   （宿主显式放行）内的模块走包回放，白名单外严格如初。SurfaceSmoke 补信任边界
   反例（同一组装 plan 走无白名单入口必须抛）。**既有契约测试当场抓住设计错误**
   ——门禁的价值又一次实证。
2. **包差量纯靠 plan 反推**：Compose 内「组装 plan − 同 profile 无包基线 plan」重建
   register/hide（描述符 ModuleId ∈ 包模块集合 / 基线有而 plan 无），不持有包句柄——
   Create/Compose 主签名零改动，无包路径零开销（六 profile 行为逐字节不变，全部既有
   用例未动即证）。
3. **降级不变量落地在 Host**：加载/身份比对/组装任一失败 → Error 日志 + 回退基线
   plan（游戏照跑）；gameIdentity 比对失败整包集合 UnloadAll（全有或全无）。
   v1 限制记入注释：CSV 按 UTF-8 读（shift-jis 游戏的身份声明包会按不匹配处理）。
4. **内置变体名录 v1 = {builtin:v24, builtin:snake}**：变体只入账本/哈希；handler
   替换投影留下一增量（LegacyInstructionVariant 开放注册表）。

## AI 表现复盘

- 有效：三处编译错误全是"命名空间解析陷阱"（ImplicitUsings 差异、命名空间内部
  `Emuera.` 前缀被解析为 `MinorShift.Emuera.`——C# 命名空间解析规则的坑），
  每处都在第一次构建即暴露，未流入后续阶段。
- 低效：Compose 首版设计直接放宽闭包校验，本应先问"既有红线为什么存在"——
  CoreContractSmoke 替我回答了。教训：**动严格校验前先 grep 断言该严格性的既有测试**。

## 教训 → 具体优化行动

- 修改 fail-closed 校验前先 `grep 工具目录` 找钉住该行为的契约测试，红线即设计约束。
- Scripts 工程 ImplicitUsings=false + 命名空间嵌套下的 `Emuera.` 引用一律
  `global::Emuera.` 前缀。

## 验证记录

Core.Tests 67/67（+11 身份比对）、Facade.Tests 33/33；Core `-t:Rebuild` 新增代码零
警告；Godot 无头构建 DLL 20:35:27 更新 0 错误；三冒烟全过（SurfaceSmoke 含包投影
正向用例 + 信任边界反例；CoreContractSmoke 既有红线原样通过）。.uid 侧车 3 枚入库。
