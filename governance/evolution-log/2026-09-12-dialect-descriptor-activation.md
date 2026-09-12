# 2026-09-12 方言描述符通道通电（方言接口完善第二步）

## 任务目标与成果

打通方言接口"半成品"的核心症结：描述符通道空转（BuiltInDialectCatalog 三模块空贡献、plan.Dialect.Instructions 恒空、CompatibilityDescriptorRoute 从未路由）。产出：引擎投影→生成清单→模块贡献→会话驱动路由的完整链路，外加 legacy-runner 工具链三处手工镜像的消灭。分支 ai/dialect-module-substitution 累计 7 个提交（含前一个工作包）。

## 关键决策与 Why

1. **清单用"生成数据"而非手写**——Core 模块贡献的 561/266+107/85+1 个名字由新工具 LegacyDialectInventoryGenerator 从引擎真实投影导出（哨兵锚定桥层名单），提交为 `LegacyDialectInventories.Generated.cs`。单一事实源仍是引擎行为，生成文件是可重生成的镜像；引擎/名单变化后重跑生成器，RuntimeSmoke 的漂移门禁保证不重跑就红。
2. **会话驱动路由（TryCreateSessionView）而非描述符驱动**——原路由语义"计划声明的每个名字必须在注册表中"会把条件可见性误判为错误：VARI/VARS 在 scoped-variable 关闭的会话不在注册表、但必须留在清单供开启的会话使用。反转后语义：会话注册表（已含方言变体）的每个名字必须被计划声明（漂移门禁），计划多出的名字按条件可见性容忍；漂移时记错并回退投影注册表，绝不静默截断解析面。
3. **键校验放宽（RequiredFunctionLookupKey）**——`陥落状態/陷落状态` 等 CJK 标识符同时出现在函数表和 METHOD 投影指令面，`^[A-Z][A-Z0-9_]*$` 的严格键校验会拒绝它们。两个注册器统一用 Unicode 字母键校验。

## AI 表现复盘

**有效**：
- 每一步先读代码再动手：路由激活前把 instructionDic 的填充时机（构造时即投影面）查清，避免了"描述符=增量会截断解析面"的灾难性错误设计。
- stash 快速二分定位"core-contracts 红"是 dev 既有故障而非本次回归（PRINTN 家族泄漏进 v24pure，core-contracts 抓的是真问题，留待单独修）。
- 探针法：往 SurfaceSmoke 临时塞两条 Console.WriteLine 断言（跑完即删），3 分钟锁定 PRINTN 可见性异常，避免了瞎猜。

**低效**：
- 又踩了 PowerShell 5.1 的坑：往 .ps1 里写了中文注释，无 BOM 文件被 ANSI 代码页解读后黏连吞掉相邻语句（`$generatedPath` 未定义）。上一条进化记录刚写过"写 JSON 用正斜杠"，这次教训升级为"**ps1 永远 ASCII-only**"。

## 教训 → 具体优化动作

1. **.ps1/.schema.json 等被 Windows PowerShell 5.1 消费的文件必须 ASCII-only**（中文注释写进 .md 或代码侧）。5.1 读无 BOM 文件用 ANSI 代码页，非 ASCII 字节序列会破坏相邻语句解析且报错位置完全误导（变量未定义≠语法错）。
2. **Core 契约遇到 CJK 标识符是常态不是例外**：era 系游戏的内置函数表就有 CJK 名（陥落状態）。任何"标识符合法性校验"默认用 Unicode 字母集，ASCII 严格版只用于确知纯枚举名的场景。
3. **core-contracts 工具处于失修状态**（dev 上就红：PRINTN/PRINTVN/PRINTSN/PRINTFORMN/PRINTFORMSN 未进 snake 模块 Declare 隐藏清单，v24pure 泄漏可见）。修复属可见性语义变更需单独验证——已在本记录留档，下一个工作包候选。
4. 生成器已就位后，**方言名单类改动的工作流**固定为：改引擎/桥层 → `dotnet run --project tools/dialect-inventory/LegacyDialectInventoryGenerator -- <repo-root>` → SurfaceSmoke/RuntimeSmoke 绿 → legacy-runner 冒烟。

## 后续路线（更新）

1. 修 core-contracts 既有红：PRINTN 家族收进 snake Declare 清单（需评估 v24pure 游戏误用风险）+ 该工具纳入常规门禁。
2. 函数侧 ~40 个 DialectFunctionContracts 包装从 Snake.IsEnabled 内部判断迁到模块声明。
3. 解析器 ~15 处 Snake.AllowsX/EraFl.IsX 分支收编为 capability 消费点。
4. v18 模块（emuera_v18_exported 源码机械枚举差异）。
