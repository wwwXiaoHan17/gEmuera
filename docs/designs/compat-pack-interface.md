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
  "targetEngineApi": "1",              // 对应引擎 ModuleApiVersion；兼容判定见 §9
  "baseSurfaceHash": "<可选>",          // 声明基于哪个 v24 表面快照（生成清单哈希）；不匹配记警告不拒载（对齐提示用）
  "capabilities": ["input.pointer-button.v1", ...],  // 复用现有 capability id 词汇表；未知 id 拒载（fail-closed）
  "saveProfileId": "gemuera.erafl",    // 可选
  "variantSelections": { "SETBGIMAGE": "builtin:snake" },  // 内置变体选择；自带变体经 §4 贡献声明
  "gameIdentity": {                    // 可选：绑定特定游戏（agent-profiles 经验，见 3.3）
    "gameCode": "20250628", "version": "305", "versionAccept": "minor"
  }
}
```

### 3.3 agent-profiles schema 注入的经验（现路径 `examples/agent-profiles/profile.schema.json`；PR #10 合并后为 `docs/designs/agent-profiles/profile.schema.json`）

- **身份比对拒绝加载**：`gameIdentity` 声明后与 GameBase.csv 比对，不匹配拒绝加载并回退纯 v24（对应 schema 的 game 身份字段语义：加载时比对、不匹配拒绝并走原版降级）。
- **降级不变量**：包加载失败（任何原因）→ 回退纯 v24 + 明确日志，绝不"半加载"静默继续（对应 ProfileLoader outcome.Errors 全量报告模式）。
- **版本容忍语义**：`versionAccept` 精确/次级容忍二档起步，不做模糊匹配。

## 4. 程序集契约（C# 接口草案）

落点：**`src/EmueraFacade`（契约程序集，AssemblyName=Emuera，net8.0 桌面 / net9.0 android 条件目标）新增 `Compatibility.Packs` 命名空间**。理由：包作者编译面最小（只引契约程序集，不引宿主全集）；与现有插件契约（IPluginMethod 等）同一落点先例；避免 src/Core 引擎宿主类型。*备选：接口进 src/Core + 变体工厂弱类型桥（`Func<object>`）——不推荐，损失类型安全。*（此为设计决定点，标注待用户确认。）

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

每包一个 `CompatPackLoadContext : AssemblyLoadContext`（isCollectible: true，支持会话边界 Unload）。程序集绑定规则复用 `PluginLoadContext` 已实证的三条（`Scripts/Emuera/Runtime/Utils/PluginSystem/PluginLoadContext.cs:8-21`）：

1. `Emuera/emuera` → EmueraFacade 契约程序集（与包共享类型标识，简单名匹配绑定）；
2. `netstandard/System.*/Microsoft.*` → 宿主已加载框架程序集（不发起绑定，net 版本回落）；
3. 其余 → 包目录探测，null 落回默认解析。

注意：现有 `PluginLoadContext` 是 internal 且服务游戏插件；CompatPack 用同规则的姊妹类，不复用实例。

### 5.3 校验（fail-closed 三原则 + 对账）

1. 未知 capability id / 缺 manifest / schema 不合 → **拒载**（含包 id 与错误定位文案）；
2. 表面名单与引擎注册表对账：包 add 的名字引擎注册表必须有 handler；包 hide 的名字必须在 v24 基线内；冲突 = 拒载（沿用 `CompatibilityDescriptorRoute` 的"记错并回退"语义，但包场景升级为拒载整个包，因为包是显式选择而非引擎内部投影）；
3. `targetEngineApi` 主版本不匹配 → 拒载并给包作者升级指引文案。

### 5.4 组装与会话绑定

包贡献折叠进会话计划（等价今日 `BuiltInDialectCatalog.CreateLegacySessionPlan` 的产物类型 `CompatibilityPlan`，**plan 哈希链语义不变**，`DialectRuntime.cs:432-471`）；每个启用包的程序集哈希 + manifest 规范化内容进 plan 哈希（诊断可复现：同包内容同哈希）。会话生命周期遵守现状：一个 legacy 会话一个计划，`Program.ConfigureCompatibilityPlan` 拒绝换绑不同哈希（`Scripts/Emuera/Program.cs:325-341`）；停机后 Unload ALC 再允许下一局。

## 6. 信任边界

- 用户显式按游戏启用；无全局生效、无静默安装。
- `[LOAD]` 账本记录：packId/packVersion/程序集 SHA256/targetEngineApi（遵循 docs/logging-convention.md 文案规范）。
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

## 9. 兼容性承诺

- `targetEngineApi` 语义化：次版本 = 只增不改（旧包兼容）；主版本 = 破坏性（包须重编译）。引擎内置变体名 `"builtin:*"` 属公共词汇表，改名视同主版本。
- plan 哈希稳定性：包内容不变 → 会话计划哈希不变（回归可比对）。
- 铁律表述更新：AGENTS.md 游戏适配铁律在 P-D 落地时改写为"差异必须以兼容包方式实现，禁止直接修改 v24 基线共享路径"。

## 10. 开放问题（需裁定，不阻塞本文评审）

1. 契约落点：EmueraFacade（推荐）vs src/Core 弱类型桥。
2. 包安装位置约定：候选 `compat/packs/<packId>/`（桌面）与 `/storage/emulated/0/emuera/packs/`（Android）；**不得**放 export/（那是导出工作区）。
3. launcher UI 的按游戏包选择交互与默认推荐映射（六游戏矩阵的 profile→包对应关系迁移）。
4. v18 壳包是否携带全量清单于 manifest（417 指令/160 函数的单文件体积可接受性）。

## 11. 接口定稿任务的 Definition of Done（后续任务的验收，非本文）

- [ ] EmueraFacade 落地 §4 接口 + manifest schema（JSON Schema 入库）+ 单元测试；
- [ ] 加载器最小实现（发现/隔离/校验/组装）+ fail-closed 三原则各有测试；
- [ ] tests/ 内一个 hello-world 测试包端到端：显式启用 → 表面变化 → plan 哈希变化 → 禁用后回纯 v24 逐字节等价；
- [ ] 信任边界人工评审（用户）。
