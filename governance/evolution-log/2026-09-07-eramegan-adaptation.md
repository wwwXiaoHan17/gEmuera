# eraMegaten 适配与方言模块返工（eramegan-adaptation）

## 任务目标与成果

让 eraMegaten 3.54 汉化版（code 666，erakanon 血统，9065 ERB/380 万行，宿主 Emuera.NET 0.2.6）在 gEmuera 上运行。**最终达成（eraFL 模式）**：新增 `megaten` 方言模块（game.megaten@1.0.0，闭包 {gemuera.v24, game.megaten}），三个窄策略端口门控全部行为差异；真游戏 megaten profile 无头 3/3 Passed（每轮约 4 分钟，三轮 semanticSha256 完全一致），emuera.log 零警告；v24pure/snake/erafl 未选择侧零变化（方言快照名录对比 569/675/678 逐项零差异 + 夹具回归逐字一致）。

## 关键决策与 Why（含一次违规返工）

1. **【违规与返工】**首版修复把三类行为差异（标签查询大小写对称化 ×11 处、#DIM REF OUT、私有 #DIM 遮蔽内置变量）直接改在 v24 基线共享路径上并提交（829da44，未推送）。仓库主人指出违规并援引 eraFL 先例；**reset 该提交**，全部返工为方言模块门控。教训升格为 AGENTS.md 硬规则（2026-09-07 起）：游戏适配必须走方言模块，禁止直接修改根基。返工要点：11 处查询点改为 `ICFunction || (ICVariable && Megaten.UsesVariableCaseForFunctionLabelLookup)`——Disabled 策略下布尔表达式与基线逐点等价。
2. **根因链**（配置翻转实验实锤）：游戏 emuera.config 的「大文字小文字の違いを無視する:YES」+「関数・属性については大文字小文字を無視しない:YES」组合下，定义侧按 ICVariable 转大写存储、查询侧按 ICFunction 不转——823 条「解釈できない」警告；又因「解釈不可能な行があっても実行する:NO」在加载后弹退出确认（无头下酷似卡死）。原版 1824+v24 同构存在该不对称；按游戏实际宿主 0.2.6 行为对齐（0.2.6 不受此组合影响）。
3. **megaten 模块设计**：纯声明模块（无算法端口、无注册表名变更——Declare 空实现），Apply 仅注入 3 个 flag 的策略（UsesVariableCaseForFunctionLabelLookup / AllowsOutAsVariableNameAfterRefKeyword / AllowsPrivateSystemVariableShadowing）。因此其方言表面与 v24pure 完全一致，快照名录零增量。
4. **实现代理纠偏**：任务书的 OUT 名字位公式会破坏基线 `#DIM DYNAMIC REF OUT` 行为，实现代理保留了基线侧 `!ret.Reference` 约束、以 `||` 追加 megaten 支路——规格缺陷被实现层拦截。
5. **附带澄清（非缺陷）**：裸 `RETURN` 强制 RESULT=0 与原版 1824 逐字节一致（RETURN_Instruction:3515-3519）；eraMegaten 用 `RETURN expr`/`RETURNF` 习语，不受影响。CALL 参数传递实测正常（被调方 ARG=5/ARG:0=5）。

## 排除项（有据）

懒加载开关无差异（对照实验逐字一致）；重复函数名优雅处理；ERH 变量队列干净（936 声明零冲突）；前向引用/子目录 ERH 机制正常（多组最小夹具全绿）；"SKILL3920 卡死"实为退出确认提示误判。

## 验证记录

- 构建：Godot 4.7 mono headless，dll 时间戳更新，0 CS 错误（返工版）。
- 门控证明夹具 gameC（仿真配置组合 + P1/P2/P3 探针）：v24pure 复现两条 Lv2 旧警告；megaten 全绿（B_EXPR=70、C_RESULTF=11、零警告）。
- v24pure 回归：probe2/dupA 与返工前基线逐字一致。
- 真游戏 megaten 3/3：全部 Passed，exitCode 0，三轮语义哈希一致（3b34c62d…）。
- 方言证据链：Invoke-DialectInventory/RegistrySnapshot 重生成（投影不变量 Passed、运行时隔离 Passed）；三既有 profile 名录新旧零差异；Test-DialectInventory 通过；Test-DialectRegistrySnapshot 修复 erafl 遗留的 347/83 过期断言（对齐真实值 349/85）后通过；DialectInventory.psm1 补 UTF-8 BOM 修复 cp936 下无法导入的环境脆弱性（FILE_STANDARD §6.2 记载项）。
- 顺手修复：LegacyRunnerHost 早期 Fail 的 NRE 掩码（_config null 守卫 + catch 带 reason）；Invoke-LegacyRunner.ps1 双处 profile 白名单加 megaten。
- 未验证：桌面 GUI 手动全流程、Android APK 真机（结论以 APK 实测为准，AGENTS.md）。

## 遗留登记

① megaten 会话尚无 SurfaceSmoke 专项断言（其表面与 v24pure 一致，无增量可断言；若未来 megaten 声明注册表名需同步）；② 快照生成器仍只枚举三 profile（megaten 表面零增量故未扩；扩展时参照 erafl 的 psm1 改动）；③ tools/legacy-runner 部分契约测试仍引用旧 Scripts\M0\* 路径（早前已登记）；④ runner MaxRuntimeMs 600s 上限对大游戏偏紧。

## AI 表现复盘

- **有效**：eraFL 方法论完整走通（根因实验→判别夹具→模块实现→未选择侧证明→3/3 门禁）；多模型分工（v4-flash 调查×2、glm-5.3-flash 实现×2、主会话综合/终审）；实现代理对规格缺陷的拦截。
- **低效/教训**：① 首版越过方言层直改基线（本记录核心教训，已升格为 AGENTS.md 铁律）；② 把"拒跑等按键"误判为死循环消耗多轮；③ 探针夹具用了非惯用语法（RESULT+裸RETURN）引发一轮虚惊——对照原版源码一锤定音。
