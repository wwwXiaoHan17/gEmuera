# 兼容包三评审员交叉评审吸收：P0×1 + P1×4 + P2×8 修复与移动端启动器重设计

任务：三份同提示词独立评审（87/88/84，极差 4 分）对兼容包系列（PR #11–#17）的
全部发现收敛、核实与修复；用户裁定"修复和优化，移动端为主"，允许分 subagent。

## 成果

- **P0（评审全部漏掉，集成阶段发现）**：普通 App 路径包从未加载——EmueraMain 早绑定
  （早于兼容包系列存在）总在 `Program.Main` 之前绑定基线计划，而包加载只接在
  `boundPlan == null` 分支（PR #15 接线盲区）。修复：`Program.Main` 早绑定分支补做
  （清绑→加载→重绑顺序，无包 `HasEnabledPacks` 预判整段跳过，逐字节等价）。
  运行时探针证实：坏包路径打出 `[LOAD] CompatPack disabled (load rejected)`。
- **P0.5（P0 修复后暴露）**：包注册的函数名会被引擎投影为 METHOD 指令进解析器指令
  注册表，会话校验要求指令侧声明（内置清单双侧声明先例：snake 的 SQL_CONNECT）；
  组装器只折叠函数侧 → `Legacy instruction 'SQL_CONNECT' is not declared` 炸会话。
  修复：组装器把包函数名镜像声明进计划指令面（计划多出声明被容忍，VARI/VARS 先例）。
- P1×4：跨会话静态投影泄漏（早退/失败/解绑全量复位）；组装段回放 try/catch +
  Host 顶层兜底 + Compose 注入点降级；引擎 handler 对账（六清单并集基准）；启动器
  恢复选中即清空已存包选择（回填 + 提交防御双保险）。
- P2×8：多包回放归属、GameBase.csv SJIS 编码链、死契约 v1 拒载（用户裁定）、函数名
  comparer 语义钉住、hide 基线不对称 no-op、启动器"扫描候选+勾选"移动端重设计
  （exe 同级 compat_packs/ 与 /storage/emulated/0/emuera/packs/，48px 触控行，
  FirstWindow.cs 净减 23 行、新 UI 全部入 421 行 partial）。
- 门禁：Core 90/90（+12）、Facade 33/33、三冒烟全绿、宿主 0 错误、双运行时探针。

## 关键决策与 Why

1. **三 agent 分文件域并行 + 一 agent 串行**：B（启动器）/C（校验投影）文件域不相交
   可并行；A（Core 组装）与 C 共享 Rules/Assembler，必须等 C 完成——同文件 Edit 可
   并行但语义邻接不可。构建/测试全部收口主会话（避免 obj/bin 锁竞争）。
2. **死契约 v1 显式拒载而非补接线**：fail-closed 哲学——不接线的能力绝不假装支持；
   规则层两条拒载可逆（v2 删两条即恢复），公共编译面不动。
3. **handler 对账基准 = Core 生成清单六 profile 并集，不用 funcDic**：funcDic 静态
   构造器读 `Config.ICVariable`，校验期早于配置装载，提前触发会把比较器钉在默认值
   （ICVariable 行为回归）——静态注册表不可在配置装载前触碰。
4. **镜像声明不进差量哈希**：镜像是指令侧的派生可见性声明，非包声明；同输入派生
   确定性保证哈希稳定性不受影响。

## AI 表现复盘

- 有效：三评审员同提示词并行打分，收敛发现（3 条 P1 中 2 条双源命中）直接定位；
  单源 P1 主会话逐行核实后才采信（两条都属实）。
- 低效：评审员只审 diff 不审运行时调用链，全员漏掉 P0（env 死消费）——门禁烟测
  （SurfaceSmoke 直调内部 API）也覆盖不到 EmueraMain→Program.Main 真实时序。
- 教训 → 动作：**接线类任务的验收必须含"消费点真的被调到"的运行时探针**（本次
  `res://main.tscn` 直启 + env 注入 + 日志 grep 的探针法零成本抓出两个 P0 级缺陷，
  已沉淀为可复用手法）；agent 产出需过一次编译（两处 `>` 笔误、一处 LineEdit API
  误用、一处 MemberData 数组协变陷阱均为编译期/测试期才暴露）。
