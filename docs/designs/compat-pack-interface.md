# 兼容包接口契约设计：v24 单基线 + 程序集即包（CompatPack）

> 状态：设计定稿待评审（2026-09-17）。本文只定义接口与边界，不含实施排期。
> 取代方向：方言"独立基线"旧方案（原 ai/dialect-independent-baselines，已废弃改名）。

## 0. 决策记录（用户已裁定）

| 分叉 | 裁定 | 含义 |
| --- | --- | --- |
| 方言战略 | STS2 模型 | 本体（引擎）只保留 v24 单基线；方言差异外置为可安装兼容包，社区可为新方言写包而不改引擎 |
| 包格式 | **程序集即包** | 包 = 一个 C# 程序集 + 内嵌清单；声明式数据（名单/capability）是程序集内的实现细节，不再是独立格式层 |
| 本轮范围 | 只出接口 | 五方言第一方包化**不定排期**，仅给阶段划分（§8） |
| examples 处置 | 复活为设计输入 | `docs/designs/agent-profiles/profile.schema.json` 的字段经验注入清单设计（§3.3） |
| 布局 | 先行提交待合并 | PR #10（export/ 工作区、tests 归一、根目录白名单 FILE_STANDARD §9）**已提交、尚未合并**；本文引用其引入的路径处已注明现路径 |

## 1. 术语：三类"扩展"严格区分

| 术语 | 是什么 | 通道 |
| --- | --- | --- |
| **CompatPack（兼容包）** | 适配某方言/游戏家族的程序集包，作用于引擎的方言面（指令/函数可见性、变体、capability、quirk 策略） | 本契约，新通道 |
| **Game（游戏）** | ERB/CSV 内容 | 启动器扫描根（compat/、/storage/emulated/0/emuera/），与包无关 |
| **Plugin（游戏侧插件）** | 游戏经 CALLSHARP 调用的 `Plugins/*.dll`（现有 `PluginManager` 通道，capability `plugin.external-assembly.v1`） | 既有通道，不动 |

CompatPack 与 Plugin 复用同一套 ALC 隔离技术（§5.2），但注册面、生命周期、信任语义完全独立。禁止混用：包不能注册 IPluginMethod，插件不能声明方言面。

## 2. 分层模型与核心不变量

```
gEmuera App（本体）
├─ 引擎核心：v24 基线 + 能力实现库（capability/quirk 的算法实现全部内置引擎，包只是"选择器"）
├─ CompatPack Loader（本契约：发现→隔离→校验→组装）
├─ 第一方包（dogfood：snake/erafl/erablue/megaten/v18 逐步外置，走与社区包同一接口）
└─ 社区包（用户显式安装并按游戏启用）
```

**核心不变量（游戏适配铁律的延续表述）**：未启用任何包的会话 = 纯 v24，与今日 v24pure 逐字节等价；任何门禁（三冒烟、legacy-runner、快照名录零差异）照旧适用。包的存在不改变"共享路径禁改、差异走模块"的约束——包就是模块的外置形态。

## 3. 包身份与清单（embedded manifest）

### 3.1 形式

程序集必须内嵌资源 `compatpack.manifest.json`（UTF-8，schema 校验失败拒载）。缺 manifest 的程序集不是包。

### 3.2 字段（v1 草案）

```jsonc
{
  "packId": "game.erafl",              // 沿用现有方言模块 id 风格；全局唯一，注册冲突拒载
  "packVersion": "1.0.0",              // semver
  "contractAbi": "1.0",                // Emuera.CompatPack 公开契约面 ABI（major.minor）；与引擎 major 相等且包 minor <= 引擎 minor
  "engineApi": "1.0",                  // 引擎行为/能力契约版本（major.minor）；比较规则见 2026-11-02 spec §4.4
  "baseSurfaceHash": "<64-hex>",        // 语义级基线表面快照；有非空 surface/variantSelections 时必填，声明非空且与当前引擎基线不一致即拒载（算法见 §12.9）
  "capabilities": ["input.pointer-button.v1", ...],  // 复用现有 capability id 词汇表；未知 id 拒载（fail-closed）
  "saveProfileId": "gemuera.erafl",    // 可选
  "variantSelections": { "SETBGIMAGE": "builtin:snake" },  // 内置变体选择；自带变体经 §4 贡献声明
  "gameIdentity": {                    // 可选：绑定特定游戏（agent-profiles 经验，见 3.3）
    "gameCode": "20250628", "version": "305", "versionAccept": "minor"
  }
}
```

`versionAccept=minor` 是真正的 major.minor 语义（spec §5.3）：双方都至少有
`major.minor` 两段时比较前两段（`3.43` 与 `3.43.7` 通过、`3.43` 与 `3.50` /
`4.0` 失败）；任一方只有单段时退化为全串相等（`305` 只与 `305` 相等）。
`exact` 始终全串相等。

### 3.3 agent-profiles schema 注入的经验（现路径 `examples/agent-profiles/profile.schema.json`；PR #10 合并后为 `docs/designs/agent-profiles/profile.schema.json`）

- **身份比对拒绝加载**：`gameIdentity` 声明后与 GameBase.csv 比对，不匹配拒绝加载并回退纯 v24（对应 schema 的 game 身份字段语义：加载时比对、不匹配拒绝并走原版降级）。
- **降级不变量**：包加载失败（任何原因）→ 回退纯 v24 + 明确日志，绝不"半加载"静默继续（对应 ProfileLoader outcome.Errors 全量报告模式）。
- **版本容忍语义**：`versionAccept` 精确/次级容忍二档起步，不做模糊匹配。

## 4. 程序集契约（C# 接口草案）

落点（2026-10-03 更新，见拆分 ADR `docs/plans/2026-10-03-compatpack-contract-assembly-split-adr.md`）：**独立契约程序集 `src/EmueraCompatPack`（AssemblyName=`Emuera.CompatPack`，独立 SemVer 线 1.0.0 起步，net8.0/net9.0 双目标），类型命名空间保持 `Emuera.Compatibility.Packs`**。v1 时契约曾与上游插件 ABI（IPluginMethod 等）同居 `src/EmueraFacade`（AssemblyName=Emuera, 1.824.0.0）；拆分后 facade 经 `TypeForwardedTo` 把全部契约类型链到 `Emuera.CompatPack` 同一实例——拆分前编译的旧包（AssemblyRef=Emuera）在 1.x 内按成员级二进制兼容继续可载。理由：包契约可独立语义版本演进，不再牵动上游插件 ABI 版本面；包作者编译面最小（只引契约程序集）。*备选：接口进 src/Core + 变体工厂弱类型桥（`Func<object>`）——不推荐，损失类型安全。*

```csharp
namespace Emuera.Compatibility.Packs;

/// <summary>一个兼容包程序集的入口。加载器按类型名发现唯一实现类。</summary>
public interface ICompatPack
{
    CompatPackManifest Manifest { get; }                      // 解析自内嵌资源
    IReadOnlyList<ICompatPackContribution> Contributions { get; }
}

public interface ICompatPackContribution { string ContributionId { get; } }

/// <summary>表面贡献（纯数据）：指令/函数名单注册。</summary>
public interface ISurfaceContribution : ICompatPackContribution
{
    void Apply(IInstructionSurfaceRegistry instructions, IFunctionSurfaceRegistry functions);
}

/// <summary>能力声明贡献：capability id 进会话账本；实现由引擎能力库解析。</summary>
public interface ICapabilityContribution : ICompatPackContribution
{
    IReadOnlyList<string> CapabilityIds { get; }
}

/// <summary>变体贡献（代码）：同名指令选不同 handler 文法。取代 closed enum
/// LegacyInstructionVariant（现 LegacyCompatibilityModules.cs:28-38）的开放注册。</summary>
public interface IInstructionVariantContribution : ICompatPackContribution
{
    IReadOnlyList<InstructionVariantBinding> Bindings { get; }
}
public sealed record InstructionVariantBinding(string InstructionName, ICompatInstructionFactory Factory);
public interface ICompatInstructionFactory
{
    object CreateInstruction();   // 宿主桥把 object 收窄为 AbstractInstruction（契约侧不见引擎内部类型）
}

/// <summary>策略贡献（窄逃生舱）：仅当 capability id 引擎无内置实现时需要（社区新 quirk）。</summary>
public interface IPolicyContribution : ICompatPackContribution
{
    IReadOnlyList<EnginePolicyBinding> Policies { get; }
}
```

设计约束：

- **名单数据优先走 manifest**（§3.2 的 add/hide/variantSelections），`ISurfaceContribution`/`IInstructionVariantContribution` 只为引擎没有的东西（新 handler 文法、新策略）存在——大多数包（megaten/erablue/v18 形态）只有 manifest，程序集是壳。
- 变体工厂返回 `object` 由宿主桥收窄（契约程序集不引用 `MinorShift.*`）；宿主侧收窄失败 = 拒载并报包作者错误。
- `EnginePolicyBinding` 的窄接口集合（≈ 现有 ISnake/IEraFl/IMegatenCompatibilityPolicy 的公共化）在接口定稿任务里逐个提炼，本文不展开签名。

## 5. 加载与激活管线

### 5.1 发现（显式，禁扫描）

per-game 启用配置（launcher 侧：游戏 → 包列表）给出**包文件的绝对/相对路径清单**。不做目录扫描、不做注册中心、不做远程下载。这是对 `DialectModuleCatalog` 现有原则（"never discovers assemblies"，`src/Core/Compatibility/DialectRuntime.cs:9-13`）的**有条件反转**：反转仅限"用户显式列名"，自动发现仍然禁止。

### 5.2 隔离（ALC）

每包一个 `CompatPackLoadContext : AssemblyLoadContext`（isCollectible: true，支持会话边界 Unload）。程序集绑定规则为**契约-only 允许清单**——fail-closed 机制而非约定（与宿主 `PluginLoadContext`（`Scripts/Emuera/Runtime/Utils/PluginSystem/PluginLoadContext.cs:8-21`）同源的姊妹实现，规则更严）：

1. `Emuera/emuera` → EmueraFacade 契约程序集（与包共享类型标识，简单名匹配绑定）；
2. `netstandard/System.*/Microsoft.*` → 宿主已加载框架程序集（不发起绑定；未命中由运行时默认解析接手，BCL 名字无宿主泄露面）；
3. 其余名字 → 加载器在进入 ALC 前一次性读入的**依赖字节图**（主 DLL + 它真实 AssemblyRef 递归引用的同目录顶层 DLL）按 `<name>.dll` 命中，即仅在该 ALC 内 `LoadFromStream`（包自带依赖的私有副本，与宿主实例类型不统一；哈希与装载共享同一份字节）；
4. 仍未命中 → **抛出 `FileLoadException`，不再返回 null 落回默认解析**（默认解析可见宿主全部已加载程序集，回落即泄露）。

宿主内部程序集（如 `GEmuera.Core`）对包不可见：早期 v1 的 `GEmuera.Core` 显式分支与"null 落回默认解析"已于 2026-10-03 硬化删除（`src/Core/Compatibility/Packs/CompatPackLoadContext.cs`）。实测行为（`tests/xUnitTest/GEmuera.Core.Tests/CompatPackAlcIsolationTests.cs`）：部署于空目录的宿主绑定探针（合法 `ICompatPack` 入口但构造器引用 `CompatibilityPlan`）在 `TryLoad` 的入口实例化期被拒载，错误含 `FileLoadException` 消息与被拒绝的程序集名，句柄为 null；契约-only 夹具（只引用契约程序集）加载成功，表面贡献正常折叠进 v24pure 会话计划（指令落位 + 哈希变化）。包确实需要 Core 数据类型时，必须经契约程序集暴露或另签契约，不允许 ALC 回落。

注意：现有 `PluginLoadContext` 是 internal 且服务游戏插件；CompatPack 用姊妹类，不复用实例。

### 5.3 校验（fail-closed 三原则 + 对账）

1. 未知 capability id / 缺 manifest / schema 不合 → **拒载**（含包 id 与错误定位文案）；
2. 表面名单与引擎注册表对账：包 add 的名字引擎注册表必须有 handler；包 hide 的名字必须在 v24 基线内；冲突 = 拒载（包是显式选择而非引擎内部投影，因此不走 `CompatibilityDescriptorRoute` 的"记错并回退"内部路径：规则层直接拒载整包）。运行期另有精确闭包守卫：`IdentifierDictionary.BindCompatibilityPlan` 经 `CompatibilityDescriptorRoute.TryCreateSessionView` 校验 registry ⊆ plan，`ParserMediator.ConsumeCompatibilityPlan` 校验 plan 的非条件符号 ⊆ registry；任一方向失败都由 `Process.TryProjectCompatibilityPlanWithFallback` 在同一个 fallback scope 内降级到无包基线（§6.7），连基线都失败才终止会话；
3. `contractAbi` / `engineApi` 双轴不匹配（任一方 major 不等，或包 minor 高于引擎 minor）→ 拒载并给包作者升级指引文案；`targetEngineApi:1` 仅作为迁移窗口的 legacy 输入（spec §4.5）。

### 5.4 组装与会话绑定

包贡献折叠进会话计划（等价今日 `BuiltInDialectCatalog.CreateLegacySessionPlan` 的产物类型 `CompatibilityPlan`，**plan 哈希链语义不变**，`DialectRuntime.cs:432-471`）；每个启用包的程序集哈希 + manifest 规范化内容进 plan 哈希（诊断可复现：同包内容同哈希）。会话生命周期遵守现状：一个 legacy 会话一个计划，`Program.ConfigureCompatibilityPlan` 拒绝换绑不同哈希（`Scripts/Emuera/Program.cs`）。

**会话持有 pack set 与确定性 Unload（2026-10-03 落地）**：`CompatPackHost.ConfigureForLaunch` 返回 `CompatPackLaunchResult(Plan, Session)`——成功路径把已加载句柄集合包装为 `CompatPackSession`（`src/Core/Compatibility/Packs/CompatPackSession.cs`，构造即接管句柄所有权，调用方不得再自行 UnloadAll），无包/失败路径 `Session` 为 null。`Program.ConfigureCompatibilityPlan(plan, packSession)` 把租约与计划同源绑定（`Program.CurrentCompatPackSession` 可观测）；`ClearCompatibilityPlan`/`ResetSessionState` 先 Dispose session（幂等：`Interlocked.Exchange` 保证对每个租约恰好一次 Unload，日志 `[LOAD] CompatPack session unloaded: count=N`）再清 plan/profile——Back/Restart/ERB 重启/退出路径无条件走到这里，包 ALC 的存活不再依赖 GC 猜测。配置装载后的同哈希重绑（null session）保留既有租约，避免启动中途卸载 ALC。`CompatPackHandle` 与 `CompatPackSet` 实现 `ICompatPackLease`（`CompatPackHandle.Unload`/`CompatPackSet.UnloadAll` 语义不变；集合侧接口方法 `Unload` = `UnloadAll`）。v2 代码贡献（ALC/委托存活）以此为所有权基座。

**组合解析（T6，2026-11-02 spec §6）**：多包启用时的依赖闭包（requires）、冲突（conflicts）、显式 override/priority 仲裁、拓扑排序与双形态闭包统一由 `CompatPackCompositionResolver` 执行；组装器只消费冻结的 `CompatPackCompositionResult`，不再自行合并包差量。失败语义（缺失依赖/版本不符/依赖环/无效果包/override 并列）一律整组拒载，规范见 spec §6.2。

## 6. 信任边界

- 用户显式按游戏启用；无全局生效、无静默安装。**2026-10-03 增补（env/路径/runner 通道硬化，E2-R3/R4）**："按游戏"语义已平台正确化——游戏键比较器 `CompatPackLauncherConfig.GameKeyComparer`（Windows=`OrdinalIgnoreCase`，Android/Linux=`Ordinal`）与 `NormalizeGameKey(root, caseSensitiveFilesystem)`：大小写敏感文件系统上仅差大小写的两个游戏根不再共用配置键（原实现无条件 `ToLowerInvariant` 会把 A 的包选择静默套到 B）；包路径比对统一走 `NormalizePackPath`（GetFullPath + 分隔符统一 + 去尾斜杠，大小写语义交给比较器）。启用清单注入统一走 `MergeSelection(stored, external)`：launcher 按游戏选择非空时优先，外部 `GEMUERA_COMPAT_PACKS` 不再永久早退遮蔽选择（无选择时仅作诊断输入透传，并存时状态提示行 + `UI.WARN` 日志同步警告）；runner/in-process switch 每次会话绑定经 `FirstWindow.ConfigureLegacyRunnerSession` 重算注入（无选择即清除），不沿用上一局清单。
- `[LOAD]` 账本记录：packId/packVersion/程序集 SHA256/contractAbi/engineApi（遵循 docs/logging-convention.md 文案规范）。
- 程序集在 ALC 内运行，可见面 = EmueraFacade 契约 + 框架库；不向包暴露引擎内部可变静态。
- 分发不在本契约范围：不做市场/自动更新/签名校验（本地文件 only）。若未来需要签名，挂接点是 5.3 校验段。

## 7. 与现有机制对账（迁移时消灭的债）

> 下表编号 H1/H2/M4 出自 2026-09-16 的方言设计会话人工评审（未作为独立文档归档入库）；编号含义以本表「现状」列的描述为准。

| 债 | 现状 | 包化后的终态 |
| --- | --- | --- |
| H1 Ports 通道装饰性（`plan.Dialect.Ports` 唯一消费者是诊断计数） | `BehaviorPortSnapshot` 声明无运行期消费 | manifest 即声明载体；Ports 通道**删除**或降级为 fixture 账本校验，二选一在接口定稿时裁定 |
| H2 闭包映射 4 处手写 | `expectedModuleClosures` + Program 三 switch | 被"游戏→包列表"启用配置取代，单一事实源 |
| M4 大小写规范化三套并存 | 指令 Upper / 函数原样 / 契约 Trim | manifest 名单规范化规则一次定死并写入 schema 校验（指令 Trim+Upper，函数跟随引擎注册表 comparer 语义） |
| 变体 closed enum | `LegacyInstructionVariant`（3 值+SharedTable） | 开放注册（§4），内置变体以 `"builtin:*"` 名字出现在 manifest variantSelections |
| 方言算法散在 Core 模块类 | `EraFlCompatibilityModule` 等含 GMap/恢复算法 | 算法进引擎能力实现库（内部键 = capability id），包只声明 id；Core 不再承载游戏专属启发式 |

## 8. 第一方包迁移（阶段划分，无排期）

- **P-A 试点**：erafl（算法型，验证策略贡献）+ megaten（纯声明型，验证 manifest-only 壳）双试点跑通加载→门禁全绿；
- **P-B**：snake（变体重度，验证开放注册）+ erablue；
- **P-C**：v18（纯数据壳，验证全量名单清单）；
- **P-D 引擎瘦身**：五方言模块（snake/erafl/erablue/megaten/v18）注册退役为包文件；v24 模块内化为引擎原生基线，Core 保留 v24 表面 + 能力实现库。

每阶段验收：六游戏启动矩阵全绿、方言快照名录零差异、三冒烟 + legacy-runner 门禁、result-review ≥98。

- **P-D 退役设计（spike，2026-10-03）**：`docs/plans/2026-10-03-pd-module-retirement-spec.md`——
  定义 profile→第一方包映射 schema（`packs/pack-profile-map.example.json`）与加载优先级、
  包缺失/版本不符/身份不符三类回退路径与用户文案、逐 profile 退役顺序与 red/green 门禁、
  `BuiltInDialectCatalog`/`LegacyCompatibilityModules`/`expectedModuleClosures` 删除清单。
  **退役执行以该 spec 通过 OWNER 评审为前提**，任何退役任务不得偏离其门禁矩阵。

## 9. 兼容性承诺

- 版本轴为 `contractAbi` + `engineApi` 双轴（`major.minor`）：两轴独立要求 major 相等且包 minor <= 引擎 minor；比较规则与迁移窗口见 `docs/superpowers/specs/2026-11-02-compatpack-v1-contract-versioning-composition-design.md` §4.4/§4.5。`targetEngineApi:1` 只在迁移窗口内被接受并映射为 1.0/1.0，官方包不得再携带。
- 契约程序集 `Emuera.CompatPack` 在 **1.x 内承诺成员级二进制兼容**：已编译 v1 包仍能解析到原本使用的 public 类型和成员；不承诺行为兼容，也不承诺跨 major 兼容（spec §4.2）。引擎内置变体名 `"builtin:*"` 属公共词汇表，改名视同主版本。
- plan 哈希稳定性：包内容不变 → 会话计划哈希不变（回归可比对）。
- 铁律表述更新：AGENTS.md 游戏适配铁律在 P-D 落地时改写为"差异必须以兼容包方式实现，禁止直接修改 v24 基线共享路径"。

## 10. 开放问题（需裁定，不阻塞本文评审）

1. ~~契约落点：EmueraFacade（推荐）vs src/Core 弱类型桥~~ **已裁定并落地：EmueraFacade**（§4 推荐项，PR #12 起实施）；**2026-10-03 起契约拆分至独立程序集 `Emuera.CompatPack`（src/EmueraCompatPack），facade 保留 TypeForwardedTo 兼容旧包**（§4 更新与拆分 ADR `docs/plans/2026-10-03-compatpack-contract-assembly-split-adr.md`）。
2. 包安装位置约定：候选 `compat/packs/<packId>/`（桌面）与 `/storage/emulated/0/emuera/packs/`（Android）；**不得**放 export/（那是导出工作区）。**v1 启动器实现取 exe 同级 `compat_packs/`（桌面，另加编辑器 `res://compat_packs` 调试根）与 `/storage/emulated/0/emuera/packs/`（Android）为扫描根**，只扫描顶层 `*.dll`（v1 不枚举一级子目录；一包一目录的主 DLL 须经启动器手动路径/浏览选择，子目录自动发现留待后续启动器增强）；**部署契约为一包一目录**（`<安装根>/<packId>/`，含私有依赖），加载器按"主 DLL + 真实 AssemblyRef 递归引用"推导闭包，目录内未被引用的 DLL 一律拒载（2026-11-03 I1 硬化）。与本条原候选的路径差异（compat_packs vs compat/packs）在第一方包分发定稿时统一。
3. ~~launcher UI 的按游戏包选择交互~~ **已落地**：扫描候选 + 勾选（CheckButton 48px 触控行）+ 手动路径合并 + 桌面 FileDialog；默认推荐映射（六游戏矩阵 profile→包）仍待第一方包迁移（P-A 起）。
4. v18 壳包是否携带全量清单于 manifest（417 指令/160 函数的单文件体积可接受性）。

## 11. 接口定稿任务的 Definition of Done（后续任务的验收，非本文）

- [x] EmueraFacade 落地 §4 接口 + manifest schema（JSON Schema 入库）+ 单元测试（PR #12）；
- [x] 加载器最小实现（发现/隔离/校验/组装）+ fail-closed 三原则各有测试（PR #13/#14）；
- [x] tests/ 内一个 hello-world 测试包端到端：显式启用 → 表面变化 → plan 哈希变化 → 禁用后回纯 v24 逐字节等价（PR #14/#15）；
- [ ] 信任边界人工评审（用户）——v1 已按 §12 勘误 3/4 加固（对账/兜底/编码链），评审项保留。

## 12. v1 落地勘误（2026-09-18，随 ai/compat-pack-review-hardening；三评审员交叉评审 + 集成修复）

> 以下为 §1-§11 定稿与 v1 实现之间经评审确认的偏差与加固，全部已随修复分支落地或如实标注：

1. **App 路径包加载修复（P0）**：EmueraMain 为 EmueraContent 显示默认值先行绑定基线计划（早于 legacy 工作线程），而 Program.Main 原只在"无绑定"分支调用 ConfigureForLaunch——普通 App 路径（桌面/Android）包从未真正加载，env 注入为死消费。修复：Program.Main 早绑定分支补做包加载，顺序为"清绑定（连带清投影静态）→ ConfigureForLaunch（重设投影）→ 绑定组装/回退计划"；无包（HasEnabledPacks 为假）整段跳过，与旧路径逐字节等价。已知角落：M1 canary 诊断路径下 facade 会话账本记录的哈希与实际绑定可能不一致（仅日志口径）；"canary + 包 + 启动失败后立即重试"会触发哈希防御大声报错（fail-loud，非静默污染）。
2. **死契约 v1 显式拒载**：`IInstructionVariantContribution` / `IPolicyContribution` 已发布为公共编译面但宿主零消费，v1 携带即拒载（文案指明 v2 预留），schema/注释同步改写；v2 接线时删除规则层两条拒载即可恢复（组装器侧 capability 折叠循环保留为现成挂接点）。
3. **对账与降级加固**：§5.3(2) 的引擎 handler 对账已落地，基准 = Core 生成清单的六 profile 并集（不可用 funcDic 静态构造器：其比较器初始化读 Config.ICVariable，校验期早于配置装载，提前触发会把比较器钉在默认值）；组装段回放全程 try/catch（异常→组装错误→拒载，不再依赖宿主顶层兜底）；Compose 投影注入点异常降级为"当前 profile 纯基线 + 无包白名单"而非炸启动；ConfigureForLaunch 顶层兜底 + 零包/失败/清理路径全量复位跨会话静态投影（计划解绑联动复位）。
4. **GameBase.csv 编码**：身份比对读取链 = BOM → 严格 UTF-8 → Shift-JIS 932（与 GodotHost GameContentProbe 同源；EraStreamReader 在本移植被钉为 UTF-8 故不可复用），SJIS 日文游戏的 gameIdentity 绑定不再必然失配。
5. **`targetEngineApi` v1 语义**：仅迁移窗口接受 `targetEngineApi:1` 并映射为 `contractAbi=1.0` / `engineApi=1.0`；官方包与新包必须使用双轴字段（见 §9 与 2026-11-02 spec §4.5）。
6. **ALC 生命周期 v1 现状**：成功路径包 ALC 保持到进程结束（§5.4"停机后 Unload"推迟到宿主接线深化；同进程重启会话会为新会话重载新 ALC，旧实例按 v1 策略滞留）。ALC 解析面原状（v1.1 时点）：未匹配名落回默认解析，包技术上可绑定宿主已加载程序集——信任边界 §6 的"可见面"是约定而非机制强制。**2026-10-03 勘误增补：该回落已硬化为机制**——允许清单（契约程序集 + BCL + 包目录依赖）之外的绑定一律 `FileLoadException` 拒载（fail-closed），`GEmuera.Core` 显式分支与默认解析回落均已删除；拒载错误含被拒绝的程序集名与最内层异常（`CompatPackLoader.DescribeException` 保留 TargetInvocationException 包裹下的 FileLoadException 诊断）。实测证据见 §5.2 与 `CompatPackAlcIsolationTests`。规则 3（包目录 `<name>.dll`）语义不变：包目录内的宿主程序集文件按"包自带依赖"加载为该 ALC 私有实例，类型不与宿主统一，不构成宿主内部可变静态的泄露通道。**2026-10-03 二次增补：本条的"ALC 滞留到进程结束"缓释已关闭**——成功加载的句柄集合现由会话绑定对象 `CompatPackSession` 持有（§5.4），会话 Stop/Restart/退出清理路径（`Program.ClearCompatibilityPlan`/`ResetSessionState`）先 Dispose session 再清计划绑定，对每个租约幂等恰好一次 Unload；同进程重启不再滞留旧 ALC。
7. **capability 词汇表与消费面（2026-11-03 勘误）**：capability 词汇表 = **`EngineCapabilityCatalog`**（显式登记 + 每项标注真实消费者契约的 41 个 id：32 Wired / 9 DiagnosticOnly），**不是**六 profile 名字并集（旧表述已废弃）。`DiagnosticOnly` 条目只作 profile 账本（`OrderedCapabilityIds` 保留但**不激活**、不得被包声明），包声明即拒载。Wired 条目的消费面分两类——策略类 capability（`ContinuesAfterStartupFault` 等）由 `Plan.CapabilityIds` 直读生效；方言模块类在对应模块 Apply 时激活——二者都由 catalog 的 `ConsumerContractId` 钉住真实消费点，同一 id 不存在"部分生效"。新增 capability 必须先登记 catalog + 消费者，再进 profile/包。
8. **启动器包选择（§10.3 落地）**：扫描候选 + 勾选 + 手动路径合并（存储格式不变）；恢复选中即回填编辑框（程序化 Select 不触发信号曾导致"启动即清空已存选择"）；相对路径在启动器侧按启动器根绝对化后才入存储/env。
9. **`baseSurfaceHash` 语义级精确对账（2026-11-03 升级，T7）**：字段从"生成清单名字/返回类型哈希"升级为**基线表面语义快照**——manifest 声明非空 `baseSurfaceHash` 且不等于当前基线表面哈希即拒载（错误文案 `baseSurfaceHash 与当前基线表面快照不一致`，附声明值与当前基线值）。**强制声明（2026-11-03 修复轮，spec §5.2/R7）**：带非空 `surface`（任一 add/hide 列表非空）或非空 `variantSelections` 的包若省略该字段，规则层直接拒载；纯 capability 包与空 surface 对象可省略（`V18PackManifestTests` / `CommunityPackTemplateTests` 另门禁官方包/模板 `^[0-9a-f]{64}$`，parser/schema 均要求恰好 64 位）。哈希由 `LegacySurfaceHash.ComputeV24SurfaceHash(SurfaceHashInput)`（`src/Core/Compatibility/Packs/LegacySurfaceHash.cs`）计算，输入由 `BuildV24SurfaceHashInput(...)` 统一构造：宿主 `CompatPackHost.BuildValidationContext` 显式传入 `CurrentEngineApi` 与引擎 `BuiltinCompatPackVariants.SupportedSelections()`；官方包生成器 `tools/compat-pack/Generate-V18PackManifest` 使用同一 Core 构造（缺省取 `LegacySurfaceHash.CanonicalBuiltinVariants`），两处禁止手写 hash，`LegacyDialectSurfaceSmoke` 断言两种构造逐项相等。

   **规范化文本（第三方可复算，`LegacySurfaceHash.BuildCanonicalText`）**：UTF-8、LF、无 BOM、每行以 \n 结尾；节顺序固定：

   ~~~text
   profile=v24pure
   engineApi=1.0
   instruction=<name>|<signature>|<completion>|<module>
   function=<name>|<signature>|<returnType>|<module>
   methodProjection=<name>
   variant=<instruction>|<variant>
   capability=<id>|<status>|<consumerContractId>|<introducedInEngineApi>
   ~~~

   指令/函数按名字键、METHOD 投影名按 Ordinal 排序、capability 按 id、variant 先按指令名再按变体名做 Ordinal 排序（不是整行排序）；不含绝对路径/时间戳/进程值。对文本取 SHA256 得小写十六进制 64 字符。**哈希规范 v1 修订（2026-11-03 修复轮）**：新增 `methodProjection=<name>` 段覆盖 spec §5.4 的"指令到 METHOD 投影的条件"；该修订属于 v1 内的语义补全，不是新版本轴，所有既有 `baseSurfaceHash` 必须按本规范重算。METHOD 投影名的唯一事实源是生成清单 `LegacyDialectInventories.MethodProjectedInstructionNames`（宿主 validation context、上下文缺省与生成器三处同源）。当前基线值 `950dbeb9028a8a2fc73ac3699411a05b8e367458219b40bd862066c8902888c9` 由 `LegacySurfaceHashTests.LegacySurfaceHash_V24_IsStable` 与官方包测试钉住；生成清单 / capability / 变体漂移时先红，必须重跑 `LegacyDialectInventoryGenerator` 与 `Generate-V18PackManifest` 后再发布/更新包声明。


### 12.10 2026-11-03 final fix wave（整支 final review 后的唯一修复轮）

1. **依赖闭包按真实 AssemblyRef 推导（I1）**：`CompatPackLoader` 只把主程序集 + 它递归引用到的同目录顶层 DLL 纳入依赖字节图/ALC/`PackSha256`；目录顶层未被引用的 DLL 一律拒载（"包目录必须自包含且只含本包程序集"）。部署契约 = 一包一目录；共享扫描根里的邻居不再污染哈希/ALC，不能借 ALC 解析未启用包。`packs/README` 与 §10.2 同步改写；v1 启动器候选扫描**只覆盖安装根顶层**，一包一目录的主 DLL 仍须经手动路径/浏览选择（与 README 的扫描边界说明一致）。
2. **§6.7 fallback 双向（I3）**：`Process.Initialize` 把 Bind 与 Consume 收进 `Process.TryProjectCompatibilityPlanWithFallback` 的同一 fallback scope（先 Bind 再 Consume；Consume 抛错同样降级），反向 mismatch 不再在回退路径之前终止会话；失败异常携带 plan hash / profile / symbol。`ParserMediator.ConsumeCompatibilityPlan` 改为显式接收 plan（不再读进程静态）。
3. **fallback 状态等价（M4/M5）**：`RebindProfileRegistries` 按 `UsesFloatTypeSystem` 裁剪 `varTokenDic`（RESULTF 等 capability 门控变量名不残留）；`ConfigureCompatibilityPlan` 的防守 catch 释放包 session、打同一条成功回退标记，并在 profile 创建成功后补 `[LOAD] CompatPack projection applied` 行。
4. **空描述符计划 fail-closed（M6）**：`IdentifierDictionary.BindCompatibilityPlan` 对零 descriptor 计划返回 false（不再静默回退全量 registry）；需要无限制查找的旧调用点必须显式 `UnbindCompatibilityPlan()`。
5. **单包自相矛盾与符号键（M7/M11）**：规则层拒载同一包对同一符号（含 METHOD 镜像形态）同时 add+hide；`CompatSymbolKey` 构造器与 `TryParse` 同一套归一/校验（拒绝 ':' 与内部空白），契约串恒可逆。
6. **门禁证据（I2/M12）**：Gate 1 正向要求三份日志都含 `[LOAD] CompatibilityPlan=<64 位小写 hex>` 且与 `evidence.planHash` 一致；负向模式补复数 Errors/Failures/Exceptions（非零计数）与 terminated/aborted；自测夹具 `tools/compat-pack/gate-self-test/` 由 `-SelfTest` 与 governance 自动跑（9 个证据用例 + 1 个"case evidence 缺失必须 exit 1"自检，共 10 项；夹具经目录级 `.gitignore` 例外入库）。六游戏证据新增每条 `planHash`（格式 + 与日志对账）。
7. **M8（capability Requires/Implies/ConflictsWith 入 baseSurfaceHash）park**：会再次改变 hash 规范并重算官方 hash，且当前 catalog 三个边集合生产数据为空；留待 engineApi minor 增强时连同 hash 规范标记一起做。

## 13. v1.1 实施增补（2026-10-02：数据包 + 会话所有权 + capability 激活解耦）

本节记录在 §12 基础上落地的第一批地基增补；目标是把 CompatPack 从"只有引擎内部模块
可用的表面选择器"推进为"社区可写数据包、且行为激活不依赖内置模块在场"的可持续地基。

### 13.1 manifest-only 数据包（无需 C# 入口）

- 程序集只内嵌 `compatpack.manifest.json`、不含任何 `ICompatPack` 实现类时，加载器按
  数据包处理（`ManifestOnlyCompatPack`）；表面/能力/变体全部来自 manifest。
- manifest 新增静态 `surface` 名单（纯数据，解析期规范化）：
  - `addInstructions` / `hideInstructions`（指令名 Trim+Upper）
  - `addFunctions` / `hideFunctions`（函数名保持原样；新增函数返回类型由引擎六 profile
    清单解析，包作者不再手填）
- 仍允许程序集携带 `ICompatPack` 实现类（动态/getter 场景）；但大多数社区包不需要写代码。
- schema 与 `CompatPackManifest` 同步扩展；未知成员/空白名/重复名依旧 fail-closed。

### 13.2 基线约束（baseProfileId）

- manifest 新增可选 `baseProfileId`，缺省 `v24pure`；加载校验要求它与当前会话基线一致，
  不一致整包拒载。这消除了"校验基准 v24、组装基准却是 snake/v18"的静默错叠。
- launcher 在已选 pack 且有效 profile 非 v24pure 时显示明确回退警告；pack 选择应始终
  以 v24pure 为基座。
- **未知 baseline profile fail-fast**：`CompatPackValidationContext` 在未显式传
  `baselineSurfaceHash` 时会按构造参数 `baselineProfileId` 现算基线快照；若该 id 不是
  `CompatibilityProfileCatalog` 已注册的 profile，构造期抛 `InvalidOperationException`
  （而不是拿到一条校验错误）。宿主/嵌入方必须使用真实 profile id 或显式传入
  `baselineSurfaceHash`；`CompatPackHost.ConfigureForLaunch` 的顶层兜底会把它降级回基线，
  不会炸启动。

### 13.3 saveProfileId v1 显式拒载（组装器已预留聚合语义）

- v1 legacy 存档路径尚不消费 `CompatibilityPlan.SaveProfileId`；按死契约纪律，manifest
  携带非空 `saveProfileId` 即拒载，避免"进 plan/哈希但存档格式不变"的静默误导。
- 组装器已实现 v2 预留的聚合语义：跨包同键同值幂等、异值拒载；有声明则覆盖基线
  save profile，并进入 plan envelope hash。v2 宿主接线存档路径后删除规则层拒载即可恢复。
- `baseSurfaceHash` 的提示语义已被 §12.9 的 v1 精确对账取代（2026-10-03）：声明非空且与
  当前 v24 基线表面快照不一致即拒载；2026-11-03 修复轮起，带非空 surface 或
  variantSelections 的包必须声明该字段（纯 capability 包可省略）。

### 13.4 投影数据 plan 内聚（删除 process-wide 静态）

- `DialectPlan` 新增 `PackModuleIds` 与 `VariantSelections` 两个具名字段；组装器把包模块
  白名单与内置变体选择固化进 plan。
- `LegacyCompatibilityProfile.Create(plan, scoped)` 只从 plan 重建投影；删除
  `CompatPackHost.ActivePackModuleIds` / `ActiveVariantSelections` / `ResetActiveSessionProjection`
  及其手动复位点。信任校验仍保留：plan 未声明的额外模块、或把内置模块列入
  PackModuleIds，Compose 仍拒载。
- 成功加载的 `CompatPackSet` 不再需要跨会话持有：v1 包不参与运行期执行，plan 已是
  纯数据；v2 代码贡献若落地，需由会话绑定对象持有 set 并确定性 Unload。

### 13.5 capability 激活与内置模块解耦（P-D 前提）

- 三个策略（snake/erafl/megaten）不再由各自 `module.Apply` 安装；`Compose` 统一从
  `plan.CapabilityIds` 构造 policy，`moduleSelected` 只决定 `IsEnabled` 的模块语义。
- 具体 flag 始终由 capability 派生。因此社区数据包在 v24pure 基线上声明 e.g.
  `math.times-clamp.v1` / `display.extended-history.v1` 即可激活对应行为，无需内置
  snake/erafl 模块在场。
- snake 的 DialectFunctionContract（DFC 重载差异名集）同样从 `module.Apply` 解耦：
  模块选中或完整 snake capability 集在场时，在 `Compose` 统一激活；完整 snake 数据包
  可在 v24pure 上获得与 snake profile 一致的 DFC 语义。
- megaten 三个门控行为补上 behavior capability id（`parser.label-lookup-case.v1`、
  `parser.ref-out-name.v1`、`parser.private-system-shadow.v1`），megaten profile 显式声明。
- 内置 profile 行为逐字节保持不变；`LegacyDialectSurfaceSmoke` 增加
  "v24pure + 包 capability 激活 policy flag" 哨兵回钉。

### 13.6 默认路径会话清理 P0 修复

- `EmueraMain.StopLegacySession` 不再用 `!backend.IsRunning` 提前返回：Back/Restart/ERB
  重启会先 `EmueraThread.End()`，旧逻辑会跳过 `GlobalStatic.Reset + ClearCompatibilityPlan`，
  导致 plan 残留、同进程下一次启动 hash 防御失败。
- `LegacySessionBackend.StopLegacyBaselineAsync` 对已停止线程幂等，现无条件调用。

### 13.7 本批验收

- GEmuera.Core.Tests：98（95 + 数据包加载/plan 元数据/save profile/capability 规则/数据包夹具）。
- EmueraFacade.Tests：35（33 + surface/baseProfile 解析）。
- LegacyDialectSurfaceSmoke / LegacyDialectRuntimeSmoke / CoreContractSmoke 全绿；
  `gemuera-c#.csproj` Release 构建 0 错误。
- 新增 `tests/xUnitTest/DataOnlyCompatPackFixture`：只有内嵌 manifest、无入口类的真实
  程序集，端到端证明 manifest-only 加载路径。
- **2026-10-03 增补（会话持有 pack set 与确定性 Unload，计划 E2-R2）**：`CompatPackSession`
  落地（§5.4）——`CompatPackHandle`/`CompatPackSet` 实现 `ICompatPackLease`；
  `ConfigureForLaunch` 返回 `CompatPackLaunchResult(Plan, Session)`；`Program` 持有租约并在
  `ClearCompatibilityPlan`/`ResetSessionState` 先 Dispose（幂等恰好一次 Unload）再清
  plan/profile。验收：GEmuera.Core.Tests 104（103 + `Dispose_UnloadsEachLeaseOnceIdempotently`）、
  EmueraFacade.Tests 35、三冒烟全绿、宿主 Release 构建 0 错误。
- **2026-10-03 增补（P0 静态回归守卫，计划批次 2 Task 11）**：新增
  `tools/compat-pack/Test-CompatPackLifecycle.ps1`（`-ProjectRoot <项目根>`，exit 0/1），
  静态断言 §13.6 的 P0 修复不回退：(1) `EmueraMain.StopLegacySession` 方法体内不得出现
  `IsRunning` 条件早退（注释/字符串字面量剥离后按大括号配对提取方法体再检查，方法内的
  P0 说明注释本身提到 `!backend.IsRunning` 不会误报）；(2) `LegacySessionBackend.StopLegacyBaselineAsync`
  必须调用 `ClearCompatibilityPlan`；(3) `CompatPackHost` 不得再现
  `ActivePackModuleIds`/`ActiveVariantSelections`/`ResetActiveSessionProjection`；
  (4) `DialectPlan` 必须声明 `PackModuleIds` 与 `VariantSelections`。验收命令：
  `powershell -NoProfile -ExecutionPolicy Bypass -File tools/compat-pack/Test-CompatPackLifecycle.ps1 -ProjectRoot <项目根>`，
  已接入 `tools/governance/Test-Governance.ps1`（其无参调用即执行本守卫）。

### 13.8 社区最小数据包示例

```jsonc
{
  "packId": "community.example",
  "packVersion": "1.0.0",
  "contractAbi": "1.0",
  "engineApi": "1.0",
  "baseProfileId": "v24pure",
  "baseSurfaceHash": "<按 §12.9 计算的 64 位小写十六进制>",
  "capabilities": ["math.times-clamp.v1", "display.extended-history.v1"],
  "surface": {
    "addInstructions": ["SETANIMETIMER"],
    "hideInstructions": ["CALLSHARP"],
    "addFunctions": ["SQL_CONNECT"],
    "hideFunctions": ["EXISTVAR"]
  },
  "variantSelections": { "SETBGIMAGE": "builtin:snake" }
}
```

- 该程序集不需要任何 C# 类型；把 JSON 以 `LogicalName=compatpack.manifest.json`
  内嵌进一个 `net8.0`/`net9.0-android` 类库即可。仓库内可直接复制的模板工程为
  `packs/CommunityPackTemplate/`（manifest-only，packId=community.template），社区
  入口文档为 `packs/README.md`；带最小 `ICompatPack` 入口类的对照夹具为
  `tests/xUnitTest/CompatPackContractOnlyFixture/`（packId=community.contract-fixture，
  只引用 Emuera 契约程序集，供 ALC 正向用例复用）。
- `baseProfileId` 必须与 launcher 实际会话基线一致（v1 只支持 v24pure）；否则整包拒载回退。
- `capabilities` 只接受引擎已收录 id；`surface` 名字必须命中引擎 handler/基线对账。
- 完整功能后续（自定义 handler/策略）仍需 v2 的 `IInstructionVariantContribution`/`IPolicyContribution`
  接线；v1 携带即拒载，不会静默 no-op。

### 13.9 未完成（下一批候选）

- P0 静态回归守卫（§13.7，2026-10-03）只覆盖文本级回归，**不能替代真机 e2e**：
  pack 游戏启动 → Back/Restart → 同进程二次启动（hash 防御）路径的 Android 真机实测仍开放。

- `IInstructionVariantContribution` / `IPolicyContribution` v2 运行时接线与会话持有
  pack set（code contribution 生命周期）。
- `baseSurfaceHash` 运行时校验端已完成（§12.9，2026-11-03 语义级：精确对账 + 仓内钉子测试）；
  官方 v18 包与社区模板的声明值由 `Generate-V18PackManifest`（或按 §12.9 复算）写入；
  仍开放：社区包发布端的通用 CI 生成流水线、`engineApi` 与 interpreter engine identity 统一。
- ALC allow-list 硬化（GEmuera.Core/默认解析回落）、跨契约程序集拆分。
  —— **硬化部分已于 2026-10-03 完成**（§5.2/§12.6：清单外绑定 FileLoadException 拒载）；
  跨契约程序集拆分仍开放。
- ~~launcher/runner 的 per-game env 通道去全局化、Android 路径大小写规范化~~ **已于 2026-10-03 完成**（E2-R3/R4）：`MergeSelection` 选择优先（外部 env 仅诊断输入 + UI 警告）、平台正确游戏键（`GameKeyComparer`/`NormalizeGameKey(caseSensitiveFilesystem)`/`NormalizePackPath`）、runner/in-process switch per-session 注入刷新——语义见 §6 增补；Android 真机路径行为**待 Android 实测**。
- 内置模块退役（P-D）的 profile/闭包迁移；本批只解除了 capability 激活的机制阻塞。
- 治理口径：`tools/dialect-inventory/*CompatibilityPack*` 是 M0 静态证据工具，其
  `BuiltInCompiledOnly`/`runtimeModuleLoading=NotImplemented` 描述的是当时证据边界，
  不是运行时 CompatPack 契约；运行时以本文件与 `compatpack.manifest.schema.json` 为准。

### 13.10 v18 第一方数据包与等价门禁（2026-10-03）

- `packs/gemuera.v18/`（manifest-only 数据包，packId=`pack.gemuera.v18`）内嵌的
  `compatpack.manifest.json` 是**生成物**，由 `tools/compat-pack/Generate-V18PackManifest`
  从生成清单 `LegacyDialectInventories`（唯一事实源）计算 v24−v18 差集：
  hideInstructions=144、hideFunctions=106、add* 恒为空（v18 ⊆ v24 取证成立；add* 非空时
  生成器以非零退出码失败并列出违规名）。闭包不变量由生成器自检：
  v24 指令 561−144=417=v18、v24 函数 266−106=160=v18。输出规范化（键 Ordinal 排序、
  LF、UTF-8 无 BOM、末尾单换行），重跑生成器即恢复，禁止手改。
- packId 说明：不能取 `gemuera.v18`——与内置方言模块 id 撞名（Compose 的包模块白名单
  禁止包含内置模块 id，加载校验的保留名对账同样拒载），故用 `pack.` 前缀。
- 等价门禁（两层）：
  1. `GEmuera.Core.Tests/V18PackManifestTests`：真实包 DLL → `CompatPackLoader` →
     `CompatPackPlanAssembler`（基线 v24pure），断言组装后 `Dialect.Instructions/Functions`
     键集合与内置 `CreateLegacySessionPlan("v18")` 逐名相等（排序数组 `Assert.Equal`）。
  2. `LegacyDialectSurfaceSmoke.AssertV18PackEquivalence()`（`AssertPackProjection()` 之后
     调用）：同链路再断言投影为 `LegacyCompatibilityProfile` 后（`ApplyPackSurface` 从
     plan−基线差量反推隐藏）的指令/函数可见性在两侧计划键并集上逐名相等。
- 历史差量对账（与 `LegacyV18CompatibilityModule` 手抄名单的交叉诊断，非事实源）：
  - 手抄名单为 39 指令 + 110 函数；生成清单差集为 144 指令 + 106 函数。指令侧多出的
    105 名是 v24 后增**函数**经 METHOD 投影进指令清单的同名条目（生成清单双侧声明），
    指令/函数两侧同 hide，行为等价。
  - 手抄函数名单多出的 4 名（`CHKGLOBALDATA`/`CHKVARDATA`/`FIND_VARDATA`/`GROTATE`）
    是 v24 参考源码有、gEmuera 引擎投影（生成清单）中根本不存在的名字：对引擎而言
    隐藏它们是 no-op，且包规则要求 hide ⊆ v24 基线（生成清单），故生成器不纳入。
    两会话的键集合恰因此逐名闭合，等价仍是精确断言、无任何容差放宽。
- 宿主 glob 提醒：`tools/compat-pack/**` 已加入 `gemuera-c#.csproj` 的 `Compile Remove`
  兜底（工具 Program.cs 的顶层语句若被宿主收录会以全局命名空间 `Program` 遮蔽
  `MinorShift.Emuera.Program`，表现为 CS0117）。
