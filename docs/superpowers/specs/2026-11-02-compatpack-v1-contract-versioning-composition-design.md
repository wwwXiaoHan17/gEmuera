# CompatPack v1 契约版本轴、组合语义与发布门禁设计

> 状态：Draft for Owner Review
> 日期：2026-11-02
> 适用分支：`ai/compatpack-community-foundation`
> 关联文档：
> - `docs/designs/compat-pack-interface.md`
> - `docs/plans/2026-10-03-compatpack-contract-assembly-split-adr.md`
> - `docs/plans/2026-10-03-compatpack-v2-code-contributions-spec.md`
>
> 本文是设计 spec，不是实施计划。实施前必须另行使用 `writing-plans` 技能生成任务计划。

## 0. 决策摘要

| 编号 | 决策 |
|---|---|
| D-01 | 社区包是**可信本地代码**；ALC 只做程序集绑定隔离和故障隔离，不是安全沙箱。 |
| D-02 | 版本轴拆为 `contractAbi` + `engineApi` 两个独立的 `major.minor` 版本，不再用一个 `targetEngineApi` 混淆契约面和引擎行为面。 |
| D-03 | v1 契约承诺改为：**1.x 线内成员级二进制兼容；破坏性变更升 major**。v1.0 发布后，1.x 不删除、不重命名、不改变已有 public 类型/成员签名。 |
| D-04 | 包组合允许**依赖、显式覆盖、优先级**；拒绝隐式 last-wins，拒绝无声明冲突。 |
| D-05 | Android APK e2e、六游戏矩阵、真实 ALC 回收测试、旧包兼容夹具是 P-D 和社区包发布的硬门禁。 |
| D-06 | v24 基线语义不可被包直接覆盖；基线 handler 变更只走 `variantSelections` / `builtin:*` 变体通道。 |
| D-07 | capability 必须是**已接线的行为选择器**；只有名字没有消费者的 capability 不得进入可激活词汇表。 |
| D-08 | 表面贡献必须经过「符号双形态闭包」和「运行时精确闭包」校验；加载/组装失败不得静默回退全量注册表。 |

D-01 到 D-05 由 Owner 在本次澄清中确认；D-06 到 D-08 是为使 D-02/D-04 可执行而必须落实的基础不变量。

## 1. 范围与非目标

### 1.1 本文范围

1. `contractAbi` 与 `engineApi` 的定义、来源、比较规则和升级规则。
2. v1.x 成员级二进制兼容的范围、冻结规则和破坏性变更处理。
3. `compatpack.manifest.json` 的版本字段与组合字段。
4. 包依赖、冲突、显式覆盖、优先级和确定性解析。
5. 表面符号（指令/函数/变体）的闭包语义。
6. 加载期「校验一次、冻结 delta」的确定性要求。
7. P-D 与社区包发布硬门禁。

### 1.2 非目标

- 不把社区包变成不可信代码沙箱；不设计进程隔离或 OS 级权限模型。
- 不在本 spec 内完成六游戏自动内容探测或 launcher UI 重设计。
- 不在本 spec 内决定全部 v2 代码贡献接口；只约束 v2 不得破坏 v1 ABI。
- 不在本 spec 内实施 P-D 内置模块退役；退役必须等待第 9 节门禁全绿。
- 不改变 `baseProfileId` 的 v1 默认值 `v24pure`。

## 2. 现状问题

| 问题 | 现状 |
|---|---|
| 版本轴混淆 | `targetEngineApi` 取的是引擎内部 `ModuleApiVersion`，却承担契约面和引擎行为面的双重准入。 |
| 兼容承诺冲突 | ADR 声称 v2 minor + 旧包无限期兼容；v2 spec 又计划给 `EnginePolicyBinding` 增加位置参数，属于 breaking change。 |
| 没有组合图 | 当前 manifest 只有 `capabilities`、`surface`、`variantSelections`，没有 `requires` / `conflicts` / `overrides`。 |
| 没有覆盖语义 | 同名注册和变体异值只能整体拒载；无法表达「B 明确覆盖 A」。 |
| 表面双形态未闭环 | 指令名可能同时是函数投影；单独 hide/add 一种形态可能无效，甚至导致运行时回退全量注册表。 |
| 校验/组装双执行 | 校验期和组装期两次执行包贡献；首轮合法、次轮漂移可绕过 fail-closed。 |
| 哈希不完整 | 主 DLL 哈希不覆盖依赖 DLL；`baseSurfaceHash` 只覆盖名字/返回类型且为可选。 |

## 3. 术语

| 术语 | 定义 |
|---|---|
| **contractAbi** | CompatPack 作者编译面对应的 ABI 版本，描述 `Emuera.CompatPack` 的类型、成员、manifest schema 和加载契约。 |
| **engineApi** | 引擎行为/能力契约版本，描述 handler、capability 集合、表面语义和变体语义。 |
| **packVersion** | 包自身的语义化版本；只用于包发布和依赖范围，不承担引擎兼容判断。 |
| **manifest schema** | 内嵌 `compatpack.manifest.json` 的结构版本；与 `contractAbi` 分开演进。 |
| **capability** | 引擎实现库中已接线的行为选择器，例如 `math.times-clamp.v1`。 |
| **symbol** | 兼容包可改变的一个语言表面项：`instruction:NAME`、`function:NAME` 或 `variant:NAME`。 |
| **provider** | 当前为某个 symbol 提供最终状态（present/hidden/variant）的包。 |
| **override** | 一个包显式声明要替换另一个包对同一 symbol 的最终状态。 |
| **priority** | 仅用于显式 override 的确定性仲裁值；不是包加载顺序。 |
| **baseSurfaceHash** | 包声明所基于的基线表面快照哈希；用于在引擎表面漂移时拒绝过期的 add/hide 差量。 |
| **member-level binary compatibility** | 已编译 DLL 在不重新编译的情况下，仍能解析到原有类型、构造函数、方法和属性。 |

## 4. 版本轴设计

### 4.1 格式与当前值

两个版本都使用：

```text
^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$
```

当前值：

| 字段 | 当前值 | 来源 |
|---|---|---|
| `contractAbi` | `1.0` | `Emuera.CompatPack` 程序集 ABI 线；当前 `AssemblyVersion=1.0.0.0` |
| `engineApi` | `1.0` | 引擎行为/能力契约常量，由 Core 维护；不再复用 `DialectModuleDefinition.ModuleApiVersion` |
| `packVersion` | 各包自己的 `x.y.z` | 包的 `packVersion` 字段 |

### 4.2 `contractAbi` 语义

`contractAbi` 描述包作者编译时依赖的公开契约面。

- 对入口类包，若存在 `Emuera.CompatPack` AssemblyRef，其 major/minor 必须与 manifest 的 `contractAbi` 完全一致；旧 facade 包（`AssemblyRef=Emuera`）按 legacy 1.0 线处理；manifest-only 包没有 AssemblyRef，按 manifest 声明处理。

#### 允许在 1.x 内做的变更

- 新增 public 类型、接口、record 或重载；
- 新增与已有位置参数构造函数不冲突的构造函数或工厂方法；
- 新增不影响旧成员解析的属性/方法；
- 新增 manifest 字段，前提是旧 manifest 仍可解析并按旧语义运行。

#### 必须升 major 的变更

- 删除或重命名任何 public 类型/接口/枚举/record；
- 删除或修改已有构造函数、方法、属性、参数类型或返回值；
- 给已有 public 接口增加抽象成员（默认接口实现也需单独评审，默认不视为安全）；
- 改变 record 的 primary constructor 参数列表；
- 改变 enum 的底层值或已有成员顺序；
- 删除 `TypeForwardedTo` 目标或改变类型身份；
- 改变已有 manifest 字段的含义或必填性，导致旧包行为改变。

#### 1.x 兼容承诺的准确范围

1. **成员级二进制兼容**：已编译 v1 包在 1.x 引擎上仍可加载并解析到原本使用的 public 类型和成员。
2. **不保证行为兼容**：行为由 `engineApi`、`requiredCapabilities` 和 `baseSurfaceHash` 共同约束。
3. **不保证跨 major 兼容**：v2 包和 v1 包可以并存于不同 major 线，但不承诺 v1 包在 v2 上无 shim 运行。

### 4.3 `engineApi` 语义

`engineApi` 描述包依赖的引擎行为和能力集合。

#### 升级规则

- **major**：删除 capability、改变已有 capability 的语义、删除已有 surface、改变既有 handler 的语法/执行语义且无法用新 capability ID 表达。
- **minor**：新增 capability、新增可选 surface、新增向后兼容的 variant 通道。
- **patch**：不改变兼容语义的实现修复；不改变 `engineApi`。

#### 当前 v1 约束

- `engineApi` 的 minor 只允许新增，不允许改变已有 ID 的语义。
- 如果必须改变已有 capability 语义，必须要么升 major，要么新增 `.v2` 版本 ID 并保留旧 ID 的旧语义；不能静默改旧 ID。

### 4.4 比较规则

设：

- `p.contractAbi = cM.cm`
- `e.contractAbi = eM.em`
- `p.engineApi = aM.am`
- `e.engineApi = bM.bm`

加载要求：

```text
cM == eM
cm <= em
aM == bM
am <= bm
```

然后执行 capability 闭包、冲突和组合校验。

说明：

- contract minor 方向：包声明 `1.0` 可以在引擎 `1.2` 上加载；包声明 `1.2` 在引擎 `1.1` 上拒绝，因为包可能使用了 1.2 新增契约。
- engine minor 方向同理：包声明引擎 `1.3` 行为，引擎 `1.2` 缺少该增量时拒绝。
- minor 方向不能靠引擎更旧也能向下兼容来放宽；缺 API 就是缺 API。

### 4.5 `targetEngineApi` 迁移

在 v1 ABI 正式冻结前，保留一个受控迁移窗口：

1. **新格式**：必须提供 `contractAbi` 和 `engineApi`。
2. **旧格式**：只允许 `targetEngineApi: 1`，且不得同时出现 `contractAbi` 或 `engineApi`；内部映射为：
   - `contractAbi = 1.0`
   - `engineApi = 1.0`
   - 记录 `[LOAD] CompatPack legacy targetEngineApi mapped`。
3. **混合格式**：`targetEngineApi` 与任一新型字段同时出现时拒载。
4. **其他旧值**：`targetEngineApi != 1` 时拒载，提示按新双轴字段迁移。
5. **官方包**：`packs/gemuera.v18`、`packs/CommunityPackTemplate` 和所有仓库内测试夹具必须迁移到新字段；测试断言官方包不触发 legacy 迁移日志。
6. **移除时机**：v2 contract major 起删除旧字段；v1.x 期间保留接受能力。

manifest schema 的 `$id` 升到 `v1.1`，但 `contractAbi` 仍是 `1.0`；schema 修订和契约 ABI 是两个不同概念。

### 4.6 v2 示例：`EnginePolicyBinding` 不得改旧构造函数

v1.0 已冻结的一参形式：

```csharp
public sealed record EnginePolicyBinding(string CapabilityId);
```

v2 若需要实现载体，不能改为：

```csharp
// 破坏 v1 二进制兼容：删除了一参构造函数
public sealed record EnginePolicyBinding(
    string CapabilityId,
    ICompatPolicyAdapter Implementation);
```

允许的方式：

- 新增 `EnginePolicyBindingV2(string CapabilityId, ICompatPolicyAdapter Implementation)`；或
- 保留一参构造函数，新增可选属性/新工厂；或
- 新增独立接口/贡献类型，旧类型保持不变。

这一条应写入 API 基线测试，防止未来再次原地扩参。

### 4.7 版本比较示例

| 包 contractAbi | 引擎 contractAbi | 包 engineApi | 引擎 engineApi | 结果 |
|---|---|---|---|---|
| 1.0 | 1.2 | 1.0 | 1.3 | 通过（仍需 capability 闭包） |
| 1.2 | 1.1 | 1.0 | 1.3 | 拒载：contract minor 高于引擎 |
| 1.0 | 2.0 | 1.0 | 1.0 | 拒载：contract major 不同 |
| 1.0 | 1.1 | 1.3 | 1.2 | 拒载：engine minor 高于引擎 |
| 1.0 | 1.1 | 1.2 | 1.3 | 通过（需 capability 已接线） |

## 5. Manifest schema 变更

### 5.1 新格式示例

```jsonc
{
  "packId": "community.example",
  "packVersion": "1.0.0",
  "contractAbi": "1.0",
  "engineApi": "1.0",
  "baseProfileId": "v24pure",
  "baseSurfaceHash": "<hash>",
  "capabilities": ["math.times-clamp.v1"],
  "surface": {
    "addInstructions": ["SETANIMETIMER"],
    "addFunctions": [],
    "hideInstructions": [],
    "hideFunctions": []
  },
  "variantSelections": {
    "SETBGIMAGE": "builtin:snake"
  },
  "gameIdentity": {
    "gameCode": "20250628",
    "version": "305",
    "versionAccept": "exact"
  },
  "requires": {
    "packs": [
      { "packId": "community.shared", "versionRange": "[1.0.0,2.0.0)" }
    ]
  },
  "conflicts": {
    "packs": ["community.legacy"]
  },
  "overrides": [
    { "symbol": "instruction:SETANIMETIMER", "priority": 10 }
  ]
}
```

### 5.2 字段约束

| 字段 | 约束 |
|---|---|
| `contractAbi` | 新格式必填；`major.minor`。 |
| `engineApi` | 新格式必填；`major.minor`。 |
| `targetEngineApi` | 仅旧格式允许；value 必须为 `1`；与新型字段互斥。 |
| `requires.packs` | 可选数组；每项必须有 `packId`；`versionRange` 为精确 SemVer 或现有 `[min,max)` / `(min,max]` 区间。 |
| `conflicts.packs` | 可选数组；每项为 packId，表示任意版本冲突；冲突永远拒载，不被 priority 覆盖。 |
| `overrides` | 可选数组；`symbol` 为 `instruction:` / `function:` / `variant:`；`priority` 非负整数，默认 0。 |
| `baseProfileId` | v1 必须为 `v24pure`；其他值拒载；多基线支持需要新设计。 |
| `baseSurfaceHash` | 有非空 surface 或 `variantSelections` 时必填；纯 capability 包可省略。 |
| `capabilities` | 仍为包要求并激活的 capability 集合；必须闭包解析、必须为 Wired。 |
| `saveProfileId` | v1 仍显式拒载，直到存档路径真正消费该字段。 |
| `gameIdentity` | 语义不变；但 `versionAccept` 必须按第 5.3 节修正。 |

### 5.3 `gameIdentity.versionAccept` 修正

当前 `minor` 实现取版本第一段相等，实际是 major 语义：

- `CompatPackGameIdentityCheck.FirstSegmentEquals`
- 当前实现位置：`src/Core/Compatibility/Packs/CompatPackGameIdentity.cs`

v1 必须明确二选一：

1. 保持字段名 `minor`，改为真正的 `major.minor` 语义；或
2. 把字段重命名为 `major`，保持第一段相等语义。

本文建议采用 1，因为现有文档已把 `minor` 暴露给包作者；改名需要 ABI/schema 迁移。实现时必须同时补 `3.43` vs `3.50`、`3.43` vs `4.0`、单段版本 `305` 的测试。

### 5.4 `baseSurfaceHash` 的语义要求

现有算法只包含：

- v24 指令名；
- v24 函数名 + 返回类型。

这不足以表示精确基线。本设计要求 v1.1 的 `baseSurfaceHash` 至少覆盖：

- `engineApi`；
- `baseProfileId`；
- 指令名、指令 descriptor signature、completion mode；
- 函数名、函数 descriptor signature、返回类型；
- 指令到 METHOD 投影的条件；
- variant 可用映射（例如 `builtin:snake` x 指令名）；
- capability registry 的公开版本/哈希（不暴露实现细节）。

如果 v1 实现一时无法扩展完整签名，至少必须：

- 对任何带 surface/variant 的包强制要求 `baseSurfaceHash`；
- 在发布门禁中阻断 hash 不覆盖签名的已知风险；
- 不得继续接受 `packs/gemuera.v18` 和模板省略 hash。

## 6. 组合语义

### 6.1 依赖发现边界

依赖解析只发生在本次显式启用的包集合内：

- 用户/launcher 选择的路径列表；
- 以及由该列表声明的传递依赖。

不新增目录扫描、不自动下载、不从任意目录按 packId 搜索。缺失依赖时整组拒载：

```text
pack community.example requires community.shared [1.0.0,2.0.0), but it is not enabled.
```

如果 launcher 需要帮助用户补依赖，在 UI 层根据 manifest 元数据提示；引擎仍只接受显式路径集合。

### 6.2 组合解析顺序

1. **加载并解析 manifest**：所有包先完成结构校验。
2. **建立 packId 到 handle 的映射**：重复 packId 拒载。
3. **解析 `requires.packs`**：检查缺失、版本范围、循环依赖。
4. **解析 capability 闭包**：unknown、unwired、缺少 required、conflict 均拒载。
5. **拓扑排序**：依赖先于依赖者；同层按 `packId` Ordinal 排序。
6. **收集每个包的原始 symbol 状态**。
7. **解析显式 override/priority**：只对被覆盖的目标进行仲裁。
8. **执行符号双形态闭包**。
9. **生成最终 plan，并校验运行期精确闭包**。
10. **计算 resolved composition hash 和 plan hash**。

每一步失败都必须保留完整诊断并整体回退；不得保留半套 composition。

### 6.3 Capability 注册表

引擎必须维护显式 capability registry，而不是把所有 profile 的 `RequiredCapabilityIds` 求并集。

每个 capability 至少包含：

```csharp
public sealed record CapabilityDescriptor(
    string CapabilityId,
    CapabilityStatus Status,          // Wired | DiagnosticOnly
    IReadOnlyList<string> Requires,
    IReadOnlyList<string> Implies,
    IReadOnlyList<string> ConflictsWith,
    string ConsumerContractId,
    string EngineApiIntroducedIn);
```

规则：

- `Status != Wired` 的 capability 不允许进入包的激活集合。
- 所有 `Requires` 必须递归存在且 Wired。
- 所有 `Implies` 自动加入最终 capability 集合。
- `ConflictsWith` 任一出现即拒载。
- `ConsumerContractId` 必须指向真实的引擎消费者/策略实现，不能是空字符串。
- capability 的语义变化必须升 `engineApi` major 或新增新 ID；不得静默改变旧 ID。
- `SnakeCompatibilityCapabilities.RequiredCapabilityIds` 的全集推断应改为显式 capability 依赖关系；不能因为 19 项恰好都在就推断 `IsEnabled`。

### 6.4 表面符号模型

原始 add/hide 列表必须归一化为 symbol 状态，而不是直接在线性顺序中执行。

```text
SymbolKey = Instruction(name)
          | Function(name)
          | Variant(name)

SymbolState = Absent
            | Present(ownerPackId, descriptor)
            | Hidden(ownerPackId)
            | VariantSelection(ownerPackId, builtinName)
```

#### 双形态闭包规则

运行时 `FunctionIdentifier` 会把表达式函数投影为 METHOD 指令。因此：

1. `hideInstructions` 隐藏某指令时，如果同名项是 METHOD 投影函数，必须同时隐藏函数面，或由 loader 自动补齐同名 function hide。
2. `addInstructions` 只接受真实非 METHOD handler；如果名字只存在于函数面，拒载并提示改用 `addFunctions`。
3. `addFunctions` 不自动创建语句指令；如果同名指令不存在，METHOD 投影由引擎函数面负责。
4. `hideFunctions` 不移除真实独立指令；但是否同时影响 METHOD 投影由运行时函数可见性自然决定。
5. 最终 symbol 状态必须可解释为运行期投影结果；不能出现计划有、运行期无，或计划无、运行期有的静默差异。

#### 基线不可覆盖

- `instruction` / `function` 的 override 只能作用于其他包新增的 symbol；
- 修改 v24 基线 handler 只能通过 `variantSelections` + `builtin:*`；
- `variant` override 只能针对 v24 基线已注册 variant 的指令；
- `overrides` 不得把 engine baseline 的 hidden 状态改为 present 绕过 v24 语义。

### 6.5 Override 与 priority

`overrides` 采用目标 symbol + 优先级的显式声明。

规则：

1. 没有 `overrides` 声明时，两个包对同一 symbol 的最终状态不同时冲突，整组拒载。
2. 有 `overrides` 声明时，只有显式声明的包可以覆盖同一 symbol 的状态。
3. 多个 override 同时存在时，取 priority 唯一最高者。
4. priority 相同且均声明覆盖时整组拒载；不得按包顺序、路径顺序或加载顺序决定。
5. override 必须指向本次组合中真实存在的目标：
   - 另一包新增的 instruction/function；或
   - v24 基线中已注册的 variant 目标。
6. 不允许隐式覆盖 baseline instruction/function 的 handler。
7. 结果必须满足 symbol 双形态闭包和运行期精确闭包。

示例：

```jsonc
"overrides": [
  { "symbol": "instruction:SETANIMETIMER", "priority": 20 }
]
```

这表示该包要求覆盖同名 symbol 的最终状态；另一个包若也改同名 symbol 但没有显式 override，则冲突。

### 6.6 校验一次、冻结 delta

包的 `ISurfaceContribution` / capability / policy 代码可以在加载期执行，但只能执行一次。

加载器必须产出不可变的 `ValidatedPackDelta`：

```csharp
public sealed class ValidatedPackDelta
{
    public string PackId { get; }
    public string PackVersion { get; }
    public string PackSha256 { get; }
    public IReadOnlyList<string> CapabilityIds { get; }
    public IReadOnlyList<NormalizedSurfaceState> SurfaceStates { get; }
    public IReadOnlyDictionary<string, string> VariantSelections { get; }
    public string DeltaSemanticHash { get; }
}
```

组装器只读取 `ValidatedPackDelta`，不得再次调用包贡献对象。这样可以保证：

- 校验通过的内容和进入 plan 的内容同源；
- 非幂等 getter/Apply 不能在校验和组装之间漂移；
- plan hash 可以复现；
- 包代码的副作用被限制在加载阶段，且失败可诊断。

v2 若需要 per-session factory，也必须由 `ValidatedPackDelta` 持有工厂对象或工厂句柄；工厂实例不得在组装期重新创建并改变表面。

### 6.7 运行时精确闭包

组装后的 plan 与运行时投影必须满足精确关系：

```text
projectedRegistrySymbols == finalPlanSymbols
```

允许的条件符号必须显式记录为条件符号，例如：

- v24 `VARI` / `VARS` 在 scoped-variable 关闭时不可见；
- snake 主动排除的个别函数。

规则：

- 不再允许 `CompatibilityDescriptorRoute.TryCreateSessionView` 在发现 registry 超出 plan 时静默回退到全量 registry；
- 发现不一致时：
  1. 记录明确错误（含 symbol 名称、类别、owner、plan/profile）；
  2. 回退到无包基线计划；
  3. 不得让半套 plan 继续解析。
- `LegacyCompatibilityPlanConsumption.Validate` 必须同时检查 registry 包含于 plan 和 plan 的非条件项包含于 registry，而不只是单向包含。

## 7. 包完整性与哈希

### 7.1 包内容哈希

`PackSha256` 必须覆盖：

- 主程序集字节；
- 内嵌 manifest 原始字节；
- 所有运行时依赖 DLL 的字节；
- 依赖闭包中的每个程序集名称与哈希。

依赖必须从同一份不可变字节映射加载，不允许哈希时读一次、运行时按路径再读一次。

### 7.2 路径与符号链接

- 依赖解析不得逃逸包目录；
- 对 symlink/junction 的处理必须明确：v1 建议拒绝包目录之外的链接目标；
- 完整路径只写入调试日志；普通 `[LOAD]` 日志只记录 packId、版本、哈希和 basename。

### 7.3 类型加载失败

`Assembly.GetTypes()` 的 `ReflectionTypeLoadException` 不得被降级为零入口 = manifest-only。

规则：

- 完整枚举成功且确实零入口时，manifest-only 合法；
- 任一类型加载错误时整包拒载；
- 多个实现类仍按现有唯一入口规则拒载；
- 入口构造早于校验的副作用问题，必须在文档中明确为可信本地代码；加载器不声称沙箱。

### 7.4 空包/无效果包

一个没有任何有效 surface、capability、variant 或依赖贡献的包不得被当作正常包加载并产生伪差异哈希。

v1 决策：

- 没有任何有效 surface/capability/variant 的包直接拒载。
- `bundle` 类型不在 v1 范围；未来若引入，必须单独设计并提供独立测试。

## 8. v1 ABI 冻结与 API 基线

### 8.1 冻结对象

v1.0 发布时冻结：

- `src/EmueraCompatPack/**` 全部 public 类型与成员；
- `compatpack.manifest.schema.json` 的 v1.1 字段语义；
- `TargetFramework` 支持面（net8/net9）；
- `AssemblyName=Emuera.CompatPack`、`AssemblyVersion` major 线；
- `TypeForwardedTo` 列表和 facade 程序集别名。

### 8.2 允许的 1.x 变更

- 新增 public 类型；
- 新增独立接口；
- 新增方法/属性；
- 新增不会改变旧 manifest 行为的可选字段；
- 新增内置 variant 名称（需 engineApi minor，且旧包不受影响）。

### 8.3 禁止的 1.x 变更

- 删除或重命名任何已冻结成员；
- 给 record 增加位置参数；
- 给已有接口增加抽象成员；
- 修改已有字段的必填性/含义；
- 删除 `TypeForwardedTo`；
- 改变 pack manifest 的旧字段语义。

### 8.4 API 基线测试

必须加入 API 快照测试：

- 冻结 v1.0 的全部 public 类型、成员签名、构造函数参数；
- 常规 CI 对快照做 diff；
- 任何 diff 必须显式标注 major 或人工批准的新增（minor）；
- 该测试应能在不加载 Godot 的情况下运行。

### 8.5 `ICompatPack.Manifest`

v1 保留 `ICompatPack.Manifest` 以维持 ABI，但明确：

- 内嵌 manifest 是唯一事实源；
- 入口类返回的 `Manifest` 必须与内嵌 manifest 全字段相等；任一字段不一致即拒载；
- 加载器不得用入口类 Manifest 的字段覆盖内嵌 manifest；
- 必须添加入口 Manifest 与内嵌 Manifest 字段不一致时拒载的测试。

## 9. 发布硬门禁

以下门禁同时适用于：

- P-D 内置模块退役；
- 社区包正式发布；
- 任何声明 v1 兼容完成的对外版本。

### Gate 1：Android APK e2e

- 真实 Android 设备（记录型号、Android 版本、ABI）；
- 安装本分支 APK；
- 放入至少一个真实 pack（建议同时包含 manifest-only 和入口类包）；
- 启动到输入等待；
- 执行 Back 到 Restart；
- 同进程再次启动；
- 至少重复 2 次；
- 检查 `[LOAD]`、`contractAbi`、`engineApi`、plan hash、fallback 路径；
- 失败时确认用户可见状态和日志，不得静默回退到未声明 profile。

### Gate 2：六游戏矩阵

- 使用项目文档中的六游戏矩阵；
- 每个游戏使用正确的 profile / pack / 依赖组合；
- 无头运行到输入等待；
- 核对标题、首屏、关键输出；
- 无致命解析错误；
- 记录实际运行的 profile、baseProfile、packId、hash；
- APK 至少抽查每个游戏家族一个真实设备用例。

### Gate 3：真实 ALC 回收测试

- 用真实 `CompatPackLoader` 加载体；
- 用 `CompatPackSession` 持有并 Dispose；
- 对 ALC 和 pack 实例持有 `WeakReference`；
- 强制 GC 多轮，断言 `IsAlive == false`；
- 至少重复 3 轮；
- 检查 plan/session 静态字段无残留；
- 最好在 Android 真机重复一次。

### Gate 4：旧包兼容夹具

至少两类：

1. **v1 `Emuera.CompatPack` 包**：用固定契约 DLL 编译，验证 1.x 成员级兼容。
2. **旧 `AssemblyRef=Emuera` facade 包**：验证 `TypeForwardedTo` 通道和旧成员解析。

测试环境：

- 干净进程，facade 未预载时的加载行为；
- facade 预载后的加载行为；
- 失败时必须给出明确、可定位的 ABI 错误。

### Gate 5：版本与组合契约测试

- 双轴比较矩阵（第 4.7 节）；
- legacy `targetEngineApi: 1` 迁移；
- 非法混合字段拒载；
- `requires` 缺失/版本不满足/循环依赖拒载；
- conflicts 拒载且 priority 不能覆盖；
- override 唯一最高优先级生效；
- priority 相同冲突拒载；
- 输入顺序不影响 resolved plan hash；
- 符号双形态闭包；
- plan/registry 精确一致，禁止 fallback；
- 官方 v18 包和模板包含新版本字段与有效 `baseSurfaceHash`。

### Gate 6：治理与 CI

- 上述测试进入单一 CI/`Test-Governance` 入口；
- 保存命令、版本、测试计数和日志；
- 文档中的测试计数必须由实际运行结果生成或手写前复核；
- 没有 CI 的环境至少要有一键本地门禁脚本和可归档日志。

### Gate 7：P-D 解锁条件

P-D 内置模块退役必须等：

- Gate 1 到 Gate 6 全绿；
- 六游戏映射已从 spec 进入运行时代码；
- 每个待退役 profile 都有 v24pure + 包的逐名等价 golden；
- legacy-runner 三 profile 无头冒烟通过；
- Android 至少一个真实 APK 用例通过。

## 10. 兼容性迁移与文档同步

### 10.1 需要迁移的仓库对象

- `packs/gemuera.v18/compatpack.manifest.json`：`targetEngineApi` 迁移为 `contractAbi` + `engineApi`，补 `baseSurfaceHash`；
- `packs/CommunityPackTemplate/compatpack.manifest.json`：同上；
- `src/EmueraCompatPack/compatpack.manifest.schema.json`：新字段、legacy oneOf、组合字段；
- 所有测试夹具的 manifest；
- `docs/designs/compat-pack-interface.md`：把版本轴、ABI 承诺、组合语义同步到本 spec；
- `docs/plans/2026-10-03-compatpack-contract-assembly-split-adr.md`：改写无限期兼容为 1.x 成员级兼容；
- `docs/plans/2026-10-03-compatpack-v2-code-contributions-spec.md`：删除原地扩参方案，改为新类型/新重载；
- `packs/README.md`：修正安装目录、版本字段、依赖/覆盖说明。

### 10.2 过渡期行为

- 旧 manifest 的 `targetEngineApi: 1` 可加载，但每次记录 deprecation 日志；
- 新 manifest 不得触发 legacy 路径；
- v1.x 保留 legacy 接受；v2 major 删除；
- 任何官方包走 legacy 都视为发布门禁失败。

## 11. Definition of Done

满足以下全部条件，才能标记本设计实现完成：

- [ ] `contractAbi` + `engineApi` 双轴进入 manifest schema、parser、loader、rules、日志和测试。
- [ ] `targetEngineApi` legacy 映射按第 4.5 节工作，官方包全部迁移。
- [ ] v1.0 public API 基线快照测试存在，且 1.x 破坏性 diff 会让测试失败。
- [ ] `EnginePolicyBinding` 等 v2 变更不再修改 v1 位置参数；新能力通过新类型/重载承载。
- [ ] `requires` / `conflicts` / `overrides` / priority 按第 6 节解析。
- [ ] capability registry 有 Wired/Requires/Implies/ConflictsWith/Consumer，无消费者的 ID 不可激活。
- [ ] 表面符号双形态闭包完成，`hideInstructions:["ABS"]` 和 `addInstructions:["ACOS"]` 都有明确正确行为。
- [ ] 加载期一次执行贡献并冻结 `ValidatedPackDelta`，组装器不再二次调用。
- [ ] 运行期 plan/registry 精确校验，禁止静默回退全量 registry。
- [ ] 包哈希覆盖依赖闭包；依赖从同一字节图加载。
- [ ] 官方 v18 包和模板有有效 `baseSurfaceHash`，hash 算法覆盖签名/模式/variant/capability 版本。
- [ ] Android APK e2e、六游戏矩阵、ALC 回收、旧包夹具、组合测试全绿。
- [ ] P-D 内置模块退役在 Gate 1-7 全绿前保持锁定。

## 12. 风险与开放点

1. **`baseSurfaceHash` 扩展成本**：当前生成清单可能没有完整签名/重载信息，需要扩展 inventory generator；如果短期不能完成，必须在发布门禁中明确阻断，不得把仅名字 hash 当作安全等价。
2. **capability registry 迁移**：六个 profile 的 RequiredCapabilityIds 目前是隐式并集；迁移到显式 registry 时，必须逐项核对真实消费者，否则会把现有包误拒或误放行。
3. **legacy `targetEngineApi: 1` 的移除时间**：本文建议 v2 major 删除；如果 v1 公开发布前已有外部包，需要在 release note 中给出明确迁移期。
4. **`ICompatPack.Manifest` 双源**：保留 ABI 与消除歧义可能冲突；实施计划必须选择全字段相等校验或宿主唯一事实源其中一种。
5. **v1 ABI 冻结范围**：`Emuera.CompatPack` 的 public API 快照必须以实际冻结提交为准；冻结后不能再以只是结构调整为由改签名。
6. **真实包依赖来源**：本文禁止自动目录发现；如果 launcher 需要自动补依赖，只能在 UI 层发现并向引擎显式传入路径，不能破坏引擎信任边界。

## 13. 审阅结论记录（待 Owner）

请 Owner 审阅以下三点：

1. 第 4.5 节的 legacy `targetEngineApi: 1` 迁移窗口是否接受，还是 v1 发布前直接硬切双轴。
2. 第 5.4 节的 `baseSurfaceHash` 扩展是否作为 v1 发布的硬条件；本文建议是。
3. 第 6.3 节 capability registry 的最小实现是否接受无消费者不可激活的严格规则；本文建议是。
