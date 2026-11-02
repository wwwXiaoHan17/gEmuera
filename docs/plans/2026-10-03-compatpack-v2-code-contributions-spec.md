# 兼容包 v2 代码贡献契约设计（Spike）

> **勘误（2026-10-03 契约程序集拆分后）**：本文成稿时契约尚未拆分，文中所有
> `src/EmueraFacade/Compatibility/Packs/` 路径引用（§1.1/§2.2/§4.1/§8.3 等，含
> 「契约程序集（EmueraFacade）」表述）均已过时：契约现位于独立契约程序集
> **`Emuera.CompatPack`（`src/EmueraCompatPack/`）**，由
> `docs/plans/2026-10-03-compatpack-contract-assembly-split-adr.md` 确立。阅读时把上述
> 路径与程序集归属一律读作 `src/EmueraCompatPack/`，其余设计不受影响。

> 状态：**Spike 设计稿，待 OWNER 评审**（2026-10-03）。本文只做设计，不含任何运行时改动——
> 对应提交只含本 spec，零生产代码变更；v1 的两条 fail-closed 拒载
> （`src/Core/Compatibility/Packs/CompatPackRules.cs:113-120` 策略贡献、`:137-144` 变体贡献）
> **保持原样不动**，仅在 §7 给出 v2 接线任务执行时的移除计划。
> 上游设计：`docs/designs/compat-pack-interface.md` §4（代码贡献接口）、§5.2-§5.4（加载管线）、
> §12.2（死契约 v1 显式拒载）、§12.5（targetEngineApi 语义）。
> 前置依赖：Task 4 会话所有权（`CompatPackSession`，设计 §5.4 末段）；Task 3 ALC 契约-only
> 允许清单硬化（`src/Core/Compatibility/Packs/CompatPackLoadContext.cs`）；Task 8 P-D 退役 spec
> （`docs/plans/2026-10-03-pd-module-retirement-spec.md`，本 spec 不与其冲突，见 §2.3）。
>
> **状态更新（2026-11-03，CompatPack v1 final fix wave）**：本文是 2026-10-03 的 v2 代码贡献
> design spike，其中"当前实现"的描述已部分被 v1 收尾覆盖——§1.4 的
> `EngineModuleApiVersion` / `targetEngineApi` 精确匹配已由双轴 `contractAbi`/`engineApi`
> 取代（legacy `targetEngineApi:1` 仅迁移期映射；见设计文档 §12.5/§12.10 与 2026-11-02
> v1 spec §4.5）；§1.6 的"组装器完全没有消费 `pack.Variants`"**仍然成立**（v1 继续拒载
> 变体/策略贡献），但 `manifest.variantSelections` 已进 plan 哈希与 handler 投影。
> 阅读实现现状时以 `docs/designs/compat-pack-interface.md` §12.10 为准；本文其余设计结论
> （v2 适配器接口、ALC/委托所有权、契约面新增）不受影响。

## 0. 术语与回答索引

- **pack / 包**：程序集即包 + 内嵌 `compatpack.manifest.json`；每包一个可回收 ALC
  （`CompatPackLoadContext`，isCollectible）。
- **代码贡献**：`IInstructionVariantContribution`（变体工厂）与 `IPolicyContribution`
  （策略绑定），声明于契约程序集 `src/EmueraFacade/Compatibility/Packs/CompatPackVariants.cs`
  （`:13-16` / `:37-40`），v1 为死契约（携带即拒载，fail-closed）。
- **投影**：引擎把会话表面物化为解析器指令注册表的过程，入口
  `FunctionIdentifier.GetInstructionNameDic(LegacyCompatibilityProfile)`
  （`Scripts/Emuera/GameProc/Function/FunctionIdentifier.cs:73-120`），变体替换点
  `CreateProfileInstruction`（`:129-149`）。

| Brief 五问 | 回答落点 |
| --- | --- |
| 1. `CreateInstruction()` 返回 `object` 如何收窄；`AbstractInstruction` 保持 internal 还是公开基类/适配器 | §3（推荐方案 B）+ §4.1 |
| 2. 工厂生命周期；`CompatPackSession` 持 handle，`FunctionIdentifier` 投影如何拿 factory map | §5 |
| 3. 新增 `EnginePolicyBindingV2` 实现载体（v1 记录不动；策略工厂接口、生命周期、异常/失败语义） | §6 |
| 4. v2 fail-closed 校验规则与版本门槛（`targetEngineApi` 递增策略） | §7 |
| 5. 测试方案：legacy-runner fixture 证明自定义 handler 可被 ERB 调用 + 禁用包逐字节回退 | §8 |

---

## 1. 现状快照（Step 1 记录，全部锚点已核对）

### 1.1 v1 契约面（`src/EmueraFacade/Compatibility/Packs/CompatPackVariants.cs`）

| 成员 | 锚点 | 语义 |
| --- | --- | --- |
| `IInstructionVariantContribution.Bindings` | `:13-16` | 变体贡献：指令名 → 变体工厂列表 |
| `InstructionVariantBinding(string, ICompatInstructionFactory)` | `:18-19` | 绑定记录；指令名由宿主按 Trim+Upper 规范化 |
| `ICompatInstructionFactory.CreateInstruction()` → `object` | `:25-28` | 契约程序集不见引擎内部类型，返回 `object` 由宿主桥收窄；**收窄失败 = 拒载（fail-closed），不做静默回退**（`:21-24` 注释） |
| `IPolicyContribution.Policies` | `:37-40` | 策略贡献：绑定列表 |
| `EnginePolicyBinding(string CapabilityId)` | `:42-43` | "包提供的实现标记（窄接口定稿时扩展实现载体）" |

### 1.2 v1 拒载路径（`src/Core/Compatibility/Packs/CompatPackRules.cs:84-142`，本 spike 不动）

- 策略贡献：`:113-120` 整包拒载（文案指明 v2 预留、指引 manifest 通道），结构校验照常执行
  （`:121-135`：`Policies` null、null 绑定、绑定未知 capability id 全部收集错误）。
- 变体贡献：`:137-144` 整包拒载（同上），结构校验照常执行（`:145-170`：`Bindings` null、
  null 绑定、空指令名、null 工厂、跨贡献重复绑定）。
- 两条拒载均为 2026-09-17 用户裁定（评审 P2-6），注释明确"v2 接线后移除此条恢复放行"；
  设计文档 §12.2 同步预告（`docs/designs/compat-pack-interface.md:210`）。

### 1.3 投影点与 `AbstractInstruction` 可见性

- `AbstractInstruction` 为 **internal abstract class**
  （`Scripts/Emuera/GameProc/Function/Instruction.cs:11-26`）：成员 `Flag`（`:14`）、
  `ArgBuilder`（`:16`）、`SetJumpTo`（`:17`）、`DoInstruction(ExpressionMediator, InstructionLine,
  ProcessState)`（`:18-19`）、`CreateArgument`（`:21-24`）。签名全部由引擎内部类型构成。
- 投影消费链：`IdentifierDictionary` 构造器取 `Program.Compatibility` 后调用
  `GetInstructionNameDic(compatibility)`（`Scripts/Emuera/GameData/IdentifierDictionary.cs:170-171`）。
- `GetInstructionNameDic` 按会话投影（`FunctionIdentifier.cs:73-120`）：结果缓存于静态
  `registrySurfaces`（键 = `RegistrySurfaceHash`，`:47`/`:79-83`/`:117`，**只增不减，无任何驱逐路径**）；
  逐名可见性判定后经 `CreateProfileInstruction`（`:98`）替换变体 handler。
- `CreateProfileInstruction`（`:129-149`）：枚举替换路径 = `LegacyInstructionVariant` switch
  （`:140-147`），构造引擎私有嵌套 handler（注释 `:139`："变体 handler 类是本类型的私有嵌套类，
  构造知识集中在此，不向模块侧泄漏"），产出 `new FunctionIdentifier(source.Name, source.Code,
  instruction)`（`:148`）。`FunctionIdentifier.Instruction` 字段类型即 `AbstractInstruction`
  （`:661`），构造器读取 `instruction.ArgBuilder` 与 `instruction.Flag`（`:635-636`）。
- 内置变体注册表：`LegacyInstructionVariant` 枚举 + `BuiltinCompatPackVariants.Map`
  （`Scripts/Emuera/Compatibility/LegacyCompatibilityModules.cs:28-38`/`:46-56`）；包 manifest
  `variantSelections` 的 `builtin:*` 选择在 `Compose` 投影期回放（`:210-222`，未注册即抛）。

### 1.4 会话与宿主管线（Task 4 落地）

- `CompatPackSession(CompatibilityPlan, IReadOnlyList<CompatPackHandle>)` 构造即接管句柄所有权，
  `Dispose` 幂等（`src/Core/Compatibility/Packs/CompatPackSession.cs:35-42`/`:67-73`）。
- `CompatPackHandle` 已分类持有 `Variants` / `Policies` 贡献列表与 `LoadContext`
  （`src/Core/Compatibility/Packs/CompatPackHandle.cs:43-46`）——v2 工厂/策略对象的宿主侧
  挂接点现成。
- 宿主绑定链：`CompatPackHost.ConfigureForLaunch` → `CompatPackLaunchResult(Plan, Session)`
  （`Scripts/Emuera/Compatibility/CompatPackHost.cs:178-235`，session 包装点 `:219`）→
  `Program.ConfigureCompatibilityPlan(plan, packSession)`（`Scripts/Emuera/Program.cs:353-416`，
  投影失败兜底 `:384-392`，session 绑定 `:405-414`）→ `LegacyCompatibilityProfile.Create(plan,
  scopedVariableInstructionsEnabled)`（`Scripts/Emuera/Compatibility/LegacyCompatibilityProfile.cs:280-287`）。
- 清理链：`ClearCompatibilityPlan()` / `ResetSessionState()` 先 `DisposeCompatPackSession()`
  再清 plan/profile（`Program.cs:421-440`/`:519-538`，Dispose 实现 `:447`）。
- ALC 允许清单（Task 3 硬化，`CompatPackLoadContext.cs:32-55`）：`Emuera/emuera` → 契约程序集
  （`:35-36`）；`netstandard/System.*/Microsoft.*` → 宿主已加载框架程序集（`:37-46`）；
  包目录 `<name>.dll` → 私有实例（`:47-49`）；其余一律 `FileLoadException`（`:50-54`）。
  **包不能看见 `GEmuera.Core` 或宿主内部程序集**（`:17-18` 注释，探针测试
  `tests/xUnitTest/GEmuera.Core.Tests/CompatPackAlcIsolationTests.cs` 钉住）。
- API 版本（**历史，2026-10-03 时点**）：`EngineModuleApiVersion = 1`（`CompatPackHost.cs:152`），
  清单 `targetEngineApi` 精确匹配拒载（`CompatPackRules.cs:52-56`）。**2026-11-02 起已被
  双轴取代**：`contractAbi`（公开契约面）+ `engineApi`（引擎行为/能力契约），各自
  major 相等且包 minor 不高于引擎 minor；legacy `targetEngineApi:1` 仅迁移期映射为 1.0/1.0
  （见 `src/EmueraCompatPack/CompatPackManifest.cs` 与 v1 spec §4.5）。

### 1.5 上游参照（emuera.em-master PluginSystem，仅作对比，不整体引入）

- `IPluginMethod`：`Name/Description/Execute(PluginMethodParameter[])`
  （`E:\MyCode\eraCode\emuera.em-master\Emuera\Runtime\Utils\PluginSystem\IPluginMethod.cs:3-10`）
  ——引擎无关的参数数组形态。
- `PluginManager`：单例神对象（`PluginManager.cs:20-38`）；`Assembly.LoadFrom` 装默认 ALC、
  无隔离无校验（`:290-292`）；按类型名发现 manifest（`:293-298`）；名字典解析方法
  （`:321-339`）；`SetParent(process, processState, expressionMediator)` 把引擎可变状态直接
  交给插件（`:341-348`）；公共变量包装字段几十个（`:439-503`）。
- 本宿主的 CALLSHARP 兼容通道复刻了该消费形态
  （`Scripts/Emuera/GameProc/Function/Instraction.Child.cs:826-860` 经
  `Scripts/Emuera/Runtime/Utils/PluginSystem/PluginManager.cs`）。
- 对比结论：v2 走"契约化窄接口 + 每调用编组上下文"，不引入神对象单例、不开放默认 ALC——
  见 §3.2 方案对比。

### 1.6 组装/哈希现状（v2 需增补的两处缺口，spike 发现）

- `CompatPackPlanAssembler.TryAssemble`（`src/Core/Compatibility/Packs/CompatPackPlanAssembler.cs:21-224`）
  折叠 manifest `variantSelections` 进哈希（`:317-318` 经 `ComputeDialectDeltaHash`
  `:295-320`），但**完全没有消费 `pack.Variants`**（全文无该属性引用；**2026-11-03 现状仍如此**：
  v1 继续按死契约拒载变体/策略贡献，只有 `manifest.variantSelections` 进 plan 哈希与 handler
  投影）——v2 接线必须把贡献绑定加进方言差量哈希（§7.2 规则 4），否则"变体差异不进 plan 哈希"，破坏设计 §9
  "包内容不变 → 会话计划哈希不变"的对偶（内容变 → 哈希必变）。
- `CompatPackSession` 只持有句柄与 plan（`CompatPackSession.cs:25-74`），无任何
  "句柄 → 引擎可用对象"的投影辅助——v2 需在宿主侧补 factory map 提取（§5.2）。

---

## 2. 两个候选方案（Brief Step 2）

> 背景约束：收窄目标类型必须被**宿主与包共享同一类型标识**。ALC 简单名匹配下，唯一既被
> 宿主加载又被包绑定的程序集是契约程序集（`CompatPackLoadContext.cs:35-36`，与包共享同一
> 类型标识的依据见该文件头注释 `:9-11`）；包目录私有副本的类型**不**与宿主统一（`:132` 规则 3
> 语义，设计 §5.2）。这是全部方案分叉的根。

### 2.1 方案 A：`InternalsVisibleTo` + 宿主桥程序集

形态：

1. 宿主新增强名签名的桥程序集（如 `GEmuera.PackBridge.dll`），主宿主程序集对它发放
   `InternalsVisibleTo`；
2. 桥公开抽象基类 `PackInstructionHost`，在桥内继承 internal `AbstractInstruction`
   （friend 编译允许桥内的公开类继承宿主 internal 基类），把 `DoInstruction` 等虚函数以
   公开面转发给包可重写成员；
3. ALC 允许清单把桥的简单名映射到宿主已加载桥实例（仿契约绑定 `:35-36` 新增一条特权绑定）；
4. 包引用桥、派生 `PackInstructionHost`；`CreateInstruction()` 返回值由宿主
   `is AbstractInstruction` 收窄（桥实例 IS-A AbstractInstruction，类型标识经桥共享统一）。

Trade-off：

| 维度 | 影响 |
| --- | --- |
| 版本影响 | 桥每引用一个引擎 internal 类型，引擎重构即可能破桥，桥必须与宿主锁版本发布；等效于把 `AbstractInstruction` 的成员面变成包公共 API——引擎内部可演进面被锁死（internal 面 ≫ 窄接口面） |
| 安全影响 | IVT 把宿主**全部** internal 面授予桥；桥代码虽为宿主自有，但桥的公开面（包可调用的一切）等于"带全套引擎访问权的第二个契约"，旁路信任边界"不向包暴露引擎内部可变静态"（设计 §6，`docs/designs/compat-pack-interface.md:155`）；ALC 允许清单新增特权绑定条目，`CompatPackAlcIsolationTests` 钉住的"宿主内部程序集对包不可见"语义需要开桥例外 |
| 工程影响 | 引入强名签名体系（当前主工程未走强名），发布物 +1，双程序集版本矩阵 |

### 2.2 方案 B（推荐）：契约程序集公开适配器接口

形态：

1. 契约程序集（EmueraFacade）声明引擎无关的适配器窄接口 `ICompatInstructionAdapter`
   （完整签名见 §4.1）；
2. 包实现该接口；`ICompatInstructionFactory.CreateInstruction()` **保持返回 `object` 不变**；
3. 宿主收窄 = `factoryResult as ICompatInstructionAdapter`；收窄失败 → 校验错误条目 → 整包
   拒载（沿用 `CompatPackVariants.cs:23-24` 已承诺的 fail-closed 语义）；
4. 宿主内部以私有 shim（`PackAdapterInstruction : AbstractInstruction`，FunctionIdentifier
   私有嵌套，沿用 `:139` "构造知识集中在此"纪律）包装 adapter，编组引擎类型 ↔ 契约原语；
   **`AbstractInstruction` 保持 internal 不动**。

Trade-off：

| 维度 | 影响 |
| --- | --- |
| 版本影响 | 契约面按窄接口增量演进（新成员 = 新接口/新方法，随 minor 只增不改，兑现设计 §9 的 v2 承诺）；引擎内部类型零暴露、可自由重构；代价 = 编组层维护 + 每个新能力域一个窄接口（§6.4 增量协议） |
| 安全影响 | **零 ALC 规则变更**（契约程序集已在允许清单 `:35-36`）；包可见面 = 契约 + BCL，与 v1.1 硬化后的边界完全一致；编组层只读暴露白名单数据 |
| upstream 对齐 | `IPluginMethod.Execute(PluginMethodParameter[])`（`IPluginMethod.cs:7-8`）即同类"引擎无关参数 + 引擎侧包装"形态；差异在本宿主**不用** PluginManager 神对象（无 `SetParent` 式引擎状态直交、无公共变量包装字段），上下文按调用传入（§4.1） |

### 2.3 为什么推荐 B、不选 A

1. **A 是 B 的全部成本再加 IVT 特权面**：A 同样需要一个新的共享类型标识程序集 + 新 ALC
   绑定规则（否则桥类型身份不统一、收窄必败），而它能做到的一切 B 都能做到；额外付出的
   是宿主 internal 面的编译期全量暴露与引擎演进自由度的损失。没有 B 做不到而 A 独有的能力。
2. **Task 3 的机制性收窄不该被重新打开**：2026-10-03 硬化把"包可见宿主内部"从约定变成
   机制（`FileLoadException` 拒载 + 探针测试钉住，`CompatPackLoadContext.cs:17-18`、设计
   §5.2/§12.6）；A 的桥绑定是允许清单上唯一一次"宿主内部程序集对包可见"的例外，方向相反。
3. **上游教训**：emuera.em-master 的插件面正是"internal 面直交"（`SetParent` 挂
   processState/expressionMediator、`Assembly.LoadFrom` 装默认 ALC，`PluginManager.cs:292/:341-348`），
   本项目的 ALC/校验/会话三段已远严于上游；v2 的目标只换"方法解析"为"契约化 handler"，
   不回退隔离。
4. **与 Task 8 P-D spec 无冲突**：P-D 退役 spec 的包形态（manifest-only 壳 + capability 声明，
   其 §4.1 步 2-4）不依赖代码贡献；本 spec 的 v2 接线是 snake 步（步 5）之后的可选增量，
   且其 §5.3 明确不删清单不含任何被本 spec 改变的对象。

> 评审注：若 OWNER 未来裁定"包必须能改自定义文法（ArgumentBuilder 等价物）"，B 仍可承载
> （契约面增加文法声明窄接口），A 依旧无额外收益——该增量记入 §4.3 的 v3 预留，不在 v2 范围。

---

## 3. 问题一：`object` 的收窄与 `AbstractInstruction` 可见性（裁定）

**裁定（推荐方案 B）**：

1. **`AbstractInstruction` 保持 internal**。其成员签名全部由引擎内部类型构成
   （`ExpressionMediator`/`InstructionLine`/`ProcessState`/`ArgumentBuilder`，
   `Instruction.cs:16-24`），公开它 = 公开这四个类型 = 信任边界失效。构造知识继续集中在
   `FunctionIdentifier` 私有嵌套（`:139` 纪律）。
2. **公开面 = 契约程序集的适配器接口**（§4.1 精确签名）。包实现接口，宿主 shim 实现桥接；
   收窄 = `as ICompatInstructionAdapter`，失败即拒载。
3. **`CreateInstruction()` 签名保持 `object` 不变**。理由：
   - 收窄点即校验点——`object` 让"收窄失败拒载"成为可测的运行时路径（契约注释
     `CompatPackVariants.cs:23-24` 已承诺该语义），强类型返回值只是把同类失败移到
     "返回 null"，fail-closed 路径并无消失；
   - v1 契约注释与设计文档两处（`CompatPackVariants.cs:22-24`、`compat-pack-interface.md:104/:117`）
     均按"object + 宿主桥收窄"定稿，spike 无推翻依据；
   - 拒载移除（v2 接线任务）与契约面新增（adapter 接口）由 `engineApi` 主版本递增
     一次性结算（§7.1；2026-11-02 起的双轴语义，原 `targetEngineApi`），不需要在同一破坏点上
     再改工厂签名。
   - 被否决的子选项：v2 直接把返回类型改为 `ICompatInstructionAdapter`——同为 API 主版本
     破坏，但与既定设计文档相抵且无附加收益。

---

## 4. 推荐方案精确签名与失败/回退语义（Brief Step 3）

### 4.1 契约面新增（v2.0 首批窄集，落点 `src/EmueraFacade/Compatibility/Packs/`）

```csharp
/// <summary>
/// v2 变体 handler 适配器（宿主收窄目标）。契约程序集不见引擎内部类型；
/// 包实现本接口，宿主把 CreateInstruction() 返回的 object 收窄为本接口，
/// 收窄失败 = 拒载（fail-closed），不做静默回退（沿 CompatPackVariants.cs:23-24）。
/// v2.0 仅支持"同文法换行为"（见接口注释）。
/// </summary>
public interface ICompatInstructionAdapter
{
    /// <summary>
    /// handler 行为。v2.0 仅支持同文法换行为：参数解析沿用被替换指令的引擎
    /// ArgumentBuilder，宿主把求值后的实参编组进 context；引擎对该指令名的
    /// 分类 Flag（IS_PRINT/FLOW_CONTROL 等）完全沿用被替换指令，适配器不声明
    /// Flag——消除"标志位漂移破坏 skip/wait/print 等引擎外行为"的整类风险。
    /// 自定义文法（ArgumentBuilder 等价物）为 v3 增量（§4.3）。
    /// </summary>
    void Execute(ICompatInstructionContext context);
}

/// <summary>
/// 执行期窄上下文（宿主实现，每次执行一个实例；成员随窄接口集合增量提炼，
/// 只增不改）。不暴露 ExpressionMediator/InstructionLine/ProcessState。
/// </summary>
public interface ICompatInstructionContext
{
    /// <summary>求值后的实参（编组自被替换指令的 Argument；只读）。</summary>
    IReadOnlyList<CompatValue> Arguments { get; }

    /// <summary>控制台输出窄面（Print/PrintError/NewLine/Flush 等既有 console 语义的子集）。</summary>
    ICompatConsoleBridge Console { get; }

    /// <summary>变量读写窄面：首批仅 RESULT/RESULTS 等引擎白名单成员（名单随增量提炼）。</summary>
    ICompatVariableBridge Variables { get; }
}

/// <summary>契约侧值类型：三态联合（引擎 IntegerType/DoubleType/StringType 的编组像）。</summary>
public readonly record struct CompatValue(CompatValueKind Kind, long Integer, double Real, string Text);
public enum CompatValueKind { Integer, Real, String }
```

宿主侧 shim（示意，落点 `FunctionIdentifier.cs` 私有嵌套，实际接线任务执行）：

```csharp
private sealed class PackAdapterInstruction : AbstractInstruction
{
    private readonly ICompatInstructionAdapter adapter;

    internal PackAdapterInstruction(ICompatInstructionAdapter adapter, AbstractInstruction source)
    {
        this.adapter = adapter;
        flag = source.Flag;            // 分类标志完全沿用被替换指令（同文法裁定）
        ArgBuilder = source.ArgBuilder; // 文法完全沿用被替换指令
    }

    public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
    {
        // 编组：func.Argument 求值 → CompatValue[]；Console/Variables 窄桥；
        // adapter.Execute(context)；包回调异常 → CodeEE（引擎既有行级错误路径），
        // 不穿透宿主帧逃逸 ERB 执行栈外（§4.2 失败语义 4）。
    }
}
```

### 4.2 失败/回退语义（逐条可测）

1. **加载期干跑收窄失败**（校验段，v2 接线时加入 `CompatPackRules` 变体分支）：对每个
   `InstructionVariantBinding` 调用 `binding.Factory.CreateInstruction()`，返回值
   `null` / 非 `ICompatInstructionAdapter` / 抛异常 → 收集错误条目（含包 id + 贡献 id + 指令名 +
   原因），**整包拒载**。包代码调用全程 try/catch（先例：表面 Apply 回放
   `CompatPackRules.cs:205-218`）。干跑实例即弃；工厂须为纯提供者（每次返回等价语义实例，
   写入契约注释）。
2. **投影期真实创建失败**（`CreateProfileInstruction` 新分支）：干跑已挡住全部结构失败，
   此处异常按防御处理——抛 `InvalidOperationException`，由 `Program.ConfigureCompatibilityPlan`
   既有投影兜底捕获降级为无包基线组合（`Program.cs:384-392`），并记
   `[LOAD] CompatPack projection failed` 错误日志。该路径在正常加载序中不可达（fail-at-load
   已保证），属纵深防御。
3. **不做静默回退**：收窄失败的包绝不"跳过该绑定、其余生效"——整包拒载是唯一失败出口
   （`CompatPackVariants.cs:23-24` 既有承诺）。
4. **运行期包回调异常**：shim 编组层捕获并翻译为 `CodeEE`（行级报错，复用引擎既有错误
   路径），与"加载器任何输入不允许异常逃逸"（`CompatPackPlanAssembler.cs:41-44` 注释纪律）
   构成加载/运行两段同构防线。
5. **会话边界**：adapter 实例存活 = 会话（§5.3）；会话 Dispose 后不得有任何宿主残留引用
   （§5.4 前置工作项）。

### 4.3 v3 预留（不在 v2 范围，仅登记方向）

- 自定义文法（ArgumentBuilder 等价契约接口）；
- 包注册**全新指令名**并自带 handler（需 `FunctionCode.__NULL__` 投影
  （先例 `FunctionIdentifier.cs:654` METHOD 投影用 `__NULL__`）+ "注册名必须与变体绑定配对"
  规则）；
- adapter 声明附加 Flag 位（需逐 Flag 宿主审计）。

---

## 5. 问题二：工厂生命周期与 factory map 流转（裁定）

### 5.1 实例语义

- 每个（会话, 绑定指令名）恰好**一个** adapter 实例：工厂在投影期被调用一次，实例由宿主
  shim 持有（`FunctionIdentifier.Instruction`，`:661`），与引擎内置 handler 的"每投影一实例"
  语义对齐（先例：`CreateProfileInstruction` 每次投影 `new REPEAT_Instruction(...)`，`:140-148`）。
- 工厂对象（`ICompatInstructionFactory` 实现）本身无状态、线程无关（投影发生在
  `GetInstructionNameDic` 的 `registrySurfaceGate` 锁内，`:80-119`）。

### 5.2 factory map 流转（plan 是纯数据，工厂对象走会话参数通道）

**裁定：随 `LegacyCompatibilityProfile` 构造参数下传，不引入 process-wide 静态。**
依据：Task 4 已裁定"投影不再读取 process-wide 静态"（`Program.cs:380-383` 注释），
v2 延续该纪律。

```text
CompatPackHost.ConfigureForLaunch
  └─ CompatPackSession(plan, handles)                    （CompatPackHost.cs:219，已有）
Program.ConfigureCompatibilityPlan(plan, session, scoped)（Program.cs:368）
  └─ session == null ? 空映射（零开销，无包路径逐字节等价）
     : PackFactoryMapExtractor.Extract(session.Handles)
       /* 遍历 handle.Variants（CompatPackHandle.cs:43），折叠为
          Dictionary<string /*Trim+Upper 指令名*/, ICompatInstructionFactory>；
          键冲突在正常加载序不可达（跨包冲突已在组装段 fail-at-load，见下注；
          本提取期碰撞仅作纵深防御）；
          提取失败 → 走既有投影失败兜底（Program.cs:384-392） */
  └─ LegacyCompatibilityProfile.Create(plan, scoped, packInstructionFactories)
       /* 现签名 LegacyCompatibilityProfile.cs:280-287 增加可选参数；
          无包调用点（CreateForProfile :291-296 等）不传 → 行为不变 */
  └─ FunctionIdentifier.GetInstructionNameDic(compatibility)   （FunctionIdentifier.cs:73）
       └─ CreateProfileInstruction 新分支：
            1. 既有 TryGetInstructionVariant 命中 → 引擎内置变体（:135-148，不变）；
            2. 未命中 → compatibility.TryGetPackInstructionFactory(name, out factory)
               → factory.CreateInstruction()（try/catch）→ as ICompatInstructionAdapter
               → PackAdapterInstruction(adapter, source.Instruction)
               → new FunctionIdentifier(source.Name, source.Code, shim)；
            3. 仍未命中 → 返回 source（不变）。
```

> **跨包绑定冲突的真相与对策（评审修正 2026-10-03）**：`CompatPackRules.Validate` 是
> **单清单签名**（`CompatPackRules.cs:15-19`），其 `:168-169` 去重字典作用域为单次调用 =
> **包内跨贡献去重**；跨包同名绑定（两个启用包各绑 `DRAWLINE`）**今天双双通过校验、
> 并未被拒载**。v2 的防线在组装段（§7.2 新增条目）：`CompatPackPlanAssembler.TryAssemble`
> 折叠 `pack.Variants` 绑定名时复用表面注册的"被多个包注册"错误模式（先例
> `CompatPackPlanAssembler.cs:128-129`/`:135-136`），错误条目指明**两个冲突包**；任一冲突
> → `TryAssemble` 返回 false → 宿主卸载全部句柄回退基线（`CompatPackHost.cs:204-209`，
> 整包集拒载语义）。本段 map 提取因此只承担纵深防御，不是冲突的唯一防线。

### 5.3 存活边界

- adapter 实例与工厂对象均属包 ALC；存活由 `CompatPackSession` 租约界定
  （`CompatPackSession.cs:67-73` 幂等 Dispose → `CompatPackHandle.Unload` →
  `CompatPackLoadContext.Unload`，`CompatPackHandle.cs:48-59`）。宿主不额外持有
  "session 之外"的工厂引用。

### 5.4 前置工作项（v2 接线任务必须先做，spike 发现的风险）

**投影静态缓存驱逐**：`registrySurfaces` 只增不减（`FunctionIdentifier.cs:47`/`:117`，
全文件无清除路径）。v1 无害（缓存只含引擎 handler 实例与名字串）；v2 起缓存条目将持有
包 ALC 实例——会话 Dispose（`Program.cs:447`）后同哈希重绑（同包字节 → 同 plan 哈希 →
同 `RegistrySurfaceHash`，`LegacyCompatibilityProfile.cs:175-177`）会命中**已卸载 ALC 的
死实例**。v2 接线任务第一步：`ClearCompatibilityPlan()`/`ResetSessionState()`
（`Program.cs:421-440`/`:519-538`）联动按当前 `Plan.CanonicalHash` 前缀驱逐
`registrySurfaces` 对应条目（无包基线条目亦可保守驱逐，代价仅为一次重投影）。
该项完成前，v2 变体接线不得合入。

---

## 6. 问题三：`EnginePolicyBinding` 实现载体补全（裁定：v2 新增类型）

### 6.1 现状

- 记录：`EnginePolicyBinding(string CapabilityId)`（`CompatPackVariants.cs:43`），注释自述
  "包提供的实现标记（窄接口定稿时扩展实现载体）"；`IPolicyContribution` 定位为
  "窄逃生舱：仅当 capability id 在引擎能力实现库中无内置实现时使用（社区新 quirk）"（`:30-36`）。
- 消费点两处：
  1. 规则校验：整包拒载（`CompatPackRules.cs:113-120`）+ 结构校验（`:121-135`，绑定未知
     capability id 收集错误）；
  2. 组装折叠：`CompatPackPlanAssembler.cs:86-105` 把绑定 id 并入 `plan.CapabilityIds`
     （设计 §12.2 称"组装器侧 capability 折叠循环保留为现成挂接点"，
     `compat-pack-interface.md:210`）——即 v1 已把策略绑定当作"capability 账本输入"。

### 6.2 载体补全（v2.0：新增类型/接口，不原地扩参）

**v1 记录保持不动**：`EnginePolicyBinding(string CapabilityId)` 继续是 v1 成员面
（1.x 成员级二进制兼容要求，见 `docs/superpowers/specs/2026-11-02-compatpack-v1-contract-versioning-composition-design.md`
§4.2/§4.6）。v2 用**新类型 + 新贡献接口**承载实现载体：

```csharp
/// <summary>v2 策略绑定：capability id → 包提供的窄接口实现。
/// 不修改 v1 EnginePolicyBinding —— 两类记录分属 v1/v2 契约线，不是同一概念的重复。</summary>
public sealed record EnginePolicyBindingV2(string CapabilityId, ICompatPolicyAdapter Implementation);

/// <summary>v2 策略贡献：仅当 capability id 在引擎能力实现库中无内置实现时使用。</summary>
public interface IPolicyContributionV2 : ICompatPackContribution
{
    IReadOnlyList<EnginePolicyBindingV2> Policies { get; }
}
```

- **禁止原地扩参**：给 `EnginePolicyBinding` 新增位置参数会编译期破坏已编译的 v1 包
  （record 的 primary constructor 参数列表属于 v1 冻结成员面）；v1 记录及其拒载/折叠路径
  原样保留，v2 接线新增 `IPolicyContributionV2` 消费分支。
- 被否决的备选：平行类型 `EnginePolicyImplementation`（不含 CapabilityId）——`...V2`
  记录自包含 id + 载体，与 v1 语义一一对应、迁移路径明确，且不需要额外查找表。

**`ICompatPolicyAdapter` 定义（契约程序集 `Emuera.Compatibility.Packs`，v2.0）**：

```csharp
/// <summary>
/// 策略适配器标记基接口：`EnginePolicyBindingV2.Implementation` 的统一收窄/校验目标。
/// v2.0 无成员——各能力域的窄子接口携带成员并扩展本标记
/// （首个试点：`ICompatFunctionContractPolicy : ICompatPolicyAdapter`，§6.4.4）。
/// </summary>
public interface ICompatPolicyAdapter
{
}
```

- **为何是空标记**：策略各域之间无公共成员可提炼（对比指令 handler 有统一的 `Execute`
  回调，§4.1），标记接口只承担两个职责——记录与校验有单一类型目标（`is` 收窄，与 §3 的
  指令收窄同构），以及记录签名随域增长保持稳定（新域 = 新子接口扩展标记，记录不动）；
  成员面（兼容性负担）全部落在子接口，只增不改承诺（§6.4.4）在子接口层面兑现。
- **capability id → 域接口映射的提供方（§6.4.2 校验输入）**：由**宿主校验上下文供给**——
  `CompatPackValidationContext` 新增 `PolicyDomainByCapabilityId`
  （`IReadOnlyDictionary<string, Type>`，值 = 该 capability 所属域窄接口的契约 `Type`），
  由 `CompatPackHost.BuildValidationContext` 填充。选择依据：沿"宿主是语义校验输入的
  唯一提供方"的既有纪律（先例：`CompatPackHost.cs:76-79` 显式注入 `BaselineSurfaceHash`
  的注释理由）；Core 规则侧以 `Type.IsInstanceOfType(Implementation)` 判定（契约接口的
  类型标识由宿主与包共享同一程序集实例，跨 ALC 判定有效，`CompatPackLoadContext.cs:35-36`）。
  新域接口接线时随该接口的独立提交同步登记映射（§6.4.4 增量协议的 (a)+(d) 项）。
- **映射无条目 = 拒载（fail-closed，防静默 no-op）**：绑定的 capability id 既无内置实现、
  又无已登记域接口 = 引擎尚无该 id 的消费面——放行会让绑定经组装折叠（§6.1 消费点 2）
  静默进 capability 账本而无任何行为，正是 v1 拒载条目防的"看起来生效的静默误导"
  （同类理由先例：saveProfileId 拒载注释 `CompatPackRules.cs:58-65`）。v2.0 映射初始
  内容 = 首个试点域的登记条目（试点是否采纳 `ICompatFunctionContractPolicy` 见 §10.5）。

**不引入策略工厂**：与指令 handler（每指令一实例、按会话创建）不同，策略实现默认无状态
（行为开关/纯函数），由包入口在 `Contributions` 构造时直接提供实例（与现有测试样本
`HelloPolicyContribution` 同构，`tests/xUnitTest/GEmuera.Core.Tests/HelloCompatPack.cs:58-66`）。
若未来某能力域需要按会话创建状态化策略，为**该域**窄接口增配工厂（域内增量，不进公共记录）。

### 6.3 引擎侧消费形态

- 引擎能力实现库（设计 §7 债表"算法进引擎能力实现库（内部键 = capability id）"，
  `compat-pack-interface.md:168`）按 id 解析：**内置实现优先，包贡献填充空缺**（逃生舱定位
  `CompatPackVariants.cs:30-31`）。
- 投影期把绑定折叠为 "capability id → `ICompatPolicyAdapter`" 映射，挂到
  `LegacyCompatibilityProfile`（与 §5.2 factory map 同一条参数传递路径，同一可选参数包）；
  会话 Dispose 随 ALC 卸载。

### 6.4 生命周期、异常/失败语义与增量协议

1. **生命周期**：实现实例随 `CompatPackHandle.Policies` 存活（`CompatPackHandle.cs:44`）；
   投影映射随 profile 存活；会话 Dispose 全部回收。
2. **校验期失败（全部错误条目、整包拒载）**：
   - `Implementation` 为 null / 未实现该 capability 所属域的窄接口（域期望由宿主供给的
     `PolicyDomainByCapabilityId` 映射给出，§6.2）→ 新增错误；映射无条目（该 capability
     在引擎侧尚无消费面）→ 同样拒载（§6.2，防静默 no-op）；
   - 绑定 capability id 拥有**内置引擎实现** → 拒载（防覆盖内置语义；内置判定基准见下）；
   - 绑定未知 capability id → 拒载（现有 `CompatPackRules.cs:133-134` 保留）。
   - **前置取证项**：现行 `KnownCapabilityIds` 是六 profile 声明的并集（`CompatPackHost.cs:61-63`），
     是"词汇表"而非"内置已实现表"（设计 §12.7 消费面分裂已知限制，
     `compat-pack-interface.md:215`）——v2 接线任务须把宿主校验输入拆为
     vocabulary / built-in-implemented 两集合（或补一张已实现表），否则"仅当无内置实现时使用"
     的逃生舱门槛不可判定。此项为策略接线的前置，与变体接线可分步落地。
3. **运行期异常**：按能力域由宿主编组层翻译（指令执行域 → `CodeEE`；解析域 → 解析警告），
   不穿透宿主帧；与 §4.2 同纪律。
4. **窄接口增量协议**（操作化设计文档 §4 "随加载器增量逐个提炼"，
   `compat-pack-interface.md:118`）：每个新域窄接口 = 一次独立提交，须同时交付
   (a) 契约接口 + (b) 宿主消费接线 + (c) 正反向测试 + (d) 契约注释登记成员只增不改承诺。
   **首个试点建议**：`ICompatFunctionContractPolicy`——对接既有 DialectFunctionContracts
   检查器挂点（`LegacyCompatibilityProfile.UsesDialectFunctionContract`，
   `LegacyCompatibilityProfile.cs:225-230` → `CheckArgumentType` 包装链），让包为自声明函数
   提供参数契约检查。理由：消费点已存在、参数面窄（函数名 + 实参值）、引擎无关。
   其余域（megaten 型 label 查找大小写、erafl 型显示历史等）随需求增量提炼，本文不定稿签名。

---

## 7. 问题四：v2 fail-closed 校验规则与版本门槛（裁定）

### 7.1 版本门槛：`contractAbi` + `engineApi` 双轴（历史：`targetEngineApi` 单轴）

> 2026-11-03 更新：`contractAbi` + `engineApi` 双轴已在 v1.1（2026-11-02 spec / Task 1-7）
> 落地；本节原 v2 门槛内容保留为设计记录，v2 接线沿用既有双轴比较，不再重复升级。

- v1.0（历史）现状：单整数精确匹配（`CompatPackRules.cs:52-56`；`EngineModuleApiVersion = 1`，
  `CompatPackHost.cs:152`）。设计 §9 明说："原『次版本 = 只增不改』承诺推迟到 v2
  （需 schema 升级为 major.minor 双段后再兑现）"（`compat-pack-interface.md:187`）。
- v2 规则：
  1. manifest 字段升级为 `"major.minor"` 字符串（如 `"2.0"`），`compatpack.manifest.schema.json`
     随 schemaVersion 升版；
  2. **major 精确相等**：不等即拒载，文案沿用"包需按新 API 重编译后分发"
     （`CompatPackRules.cs:54-55`）；
  3. **minor 单调**：引擎 minor ≥ 包 minor（引擎侧 minor 递增 = 纯增量承诺；包声明更高
     minor = 引擎缺该增量 → 拒载，文案"包声明的引擎 API 增量在当前引擎不存在"）；
  4. `CompatPackValidationContext.EngineModuleApiVersion`（int，`CompatPackRules.cs:52` 消费）
     随之升级为 (Major, Minor) 双字段——Core 侧类型变更与规则改写在同一接线任务。
- **主版本递增的破坏结算清单（v1 → v2 一次性）**：
  (a) 变体贡献放行 + adapter 必须实现（§3/§4）；
  (b) 新增 `EnginePolicyBindingV2` + `IPolicyContributionV2` 载体（§6.2；v1 `EnginePolicyBinding` 不动）；
  (c) manifest schema 双段化 + `baseSurfaceHash` 语义沿 §12.9（T7 已升级为语义级，v2 不重算）。
  三项同批结算，避免反复破坏；v1 包被 v2 宿主拒载即本门槛的预期行为。
- **契约程序集标识不变**：`Emuera` 简单名绑定规则（`CompatPackLoadContext.cs:35-36`）不动。
  ALC 简单名匹配下运行时版本仲裁不可行（2026-09-04 Phase C 实验结论，该文件头注释
  `:9-11`）——契约面主版本语义由 manifest 门槛承载，不靠程序集版本号。

### 7.2 校验规则变更清单（v2 接线任务执行 `CompatPackRules.Validate`；spike 不动）

| 动作 | 规则 | 锚点 |
| --- | --- | --- |
| **删除** | 策略贡献整包拒载条目 | `CompatPackRules.cs:113-120` |
| **删除** | 变体贡献整包拒载条目 | `CompatPackRules.cs:137-144` |
| 保留 | 全部结构校验（null/空名/null 工厂/重复绑定） | `:121-135`/`:145-170` |
| 保留 | manifest `variantSelections` 仅 `builtin:*` | `:181-202`（数据通道不开放代码值） |
| 保留 | 变体绑定与隐藏/表面注册的交叉对账 | `:261-274` |
| **新增** | 变体绑定作用域：绑定指令名 ∈ v24 基线（v2.0 不放行包自注册名配对，见 §4.3） | 对齐既有选择键基线约束 `:270-271` |
| **新增** | 干跑收窄：`CreateInstruction()` 返回 `is ICompatInstructionAdapter`，异常/null/异型 = 错误条目 | §4.2.1 |
| **新增** | 策略绑定：`Implementation` 非 null 且实现所属域接口；绑定的 capability id 无内置实现（前置取证 §6.4.2）；未知 id 沿用 `:133-134` | §6.4 |
| **新增** | 贡献变体绑定进方言差量哈希：`ComputeDialectDeltaHash` 增 `+variant=<指令名>\|<packId>` 行（现缺口见 §1.6） | `CompatPackPlanAssembler.cs:295-320` |
| **新增** | **跨包变体绑定同名冲突检测**：`CompatPackRules.Validate` 为单清单签名（`CompatPackRules.cs:15-19`），包内去重（`:168-169`）不覆盖跨包——跨包同名绑定今天双双通过校验（§5.2 修正注）。v2 在组装段 `TryAssemble` 折叠 `pack.Variants` 绑定名时复用表面注册的"被多个包注册"错误模式（先例 `CompatPackPlanAssembler.cs:128-129`/`:135-136`），错误条目**指明两个冲突包**；任一冲突 → `TryAssemble` 返回 false → 宿主卸载全部句柄回退基线（`CompatPackHost.cs:204-209`），fail-closed 与表面注册冲突同语义 | `CompatPackPlanAssembler.cs:126-143`（先例模式）；§5.2 修正注 |
| 保持 | fail-closed 不变量：错误全量收集不短路；包代码调用全程 try/catch；任何错误 → 整包拒载 → 宿主回退基线 + `[LOAD]` 日志 | `:24`/`:205-218`；`CompatPackHost.cs:190-234` |

> spike 期间 `:113-120`/`:137-144` 保持原样；上表是 v2 接线任务的执行计划，不是本提交的变更。

---

## 8. 问题五：测试方案（Brief 硬要求：legacy-runner fixture + 逐字节回退）

### 8.1 层级 1：xUnit（规则/组装/收窄，纯 C#）

落点 `tests/xUnitTest/GEmuera.Core.Tests/`（沿 `HelloCompatPack` 夹具模式，
`HelloCompatPack.cs:14-66`；正向夹具新增 v2 贡献类型，现注释"仅供拒载向测试"随之改写）：

- **规则正向**：携带合法变体贡献（干跑收窄通过）与合法策略绑定的包通过 `CompatPackRules.Validate`。
- **规则反向矩阵**（错误全量收集断言）：
  factory 返回 null / 返回非 adapter `object` / 工厂抛异常；绑定空名、重复名、基线外指令名；
  `Implementation` null / 实现了错误域接口；绑定拥有内置实现的 capability id。
- **组装/哈希**：变体包组装后 `ComputeDialectDeltaHash` 含 `+variant=` 行；同包换绑定 →
  plan 哈希必变；禁用包 → 哈希回到基线（既有不变量入口 `CompatPackPlanAssembler.cs:32-38`）。
- **会话提取**：`CompatPackSession.Handles` → factory map 提取（空 session = 空映射）。
- **ALC 回归**：现有 `CompatPackAlcIsolationTests` 全绿不动（方案 B 零 ALC 规则变更的直接
  证据）；新增"夹具包引用契约 adapter 接口 → 加载成功"探针（adapter 在契约程序集内，
  允许清单无需变更）。
- 投影级单测的落点取证项：`LegacyCompatibilityProfile` 为 internal（Scripts 程序集），
  投影替换断言是否可入 xUnit 取决于既有 InternalsVisibleTo 布局——接线任务先取证，
  不可入则以层级 2/3 为主证据。

### 8.2 层级 2：冒烟扩展

- `LegacyDialectSurfaceSmoke` 增补 v2 断言（先例：`AssertV18PackEquivalence`，
  `tools/dialect-inventory/LegacyDialectSurfaceSmoke/Program.cs:293-294`）：夹具包会话投影 =
  v24pure 基线 + 变体替换名落位；禁用包投影 ≡ 基线（名录零差异）。
- 三冒烟 + Core/Facade 全量 xUnit 沿既有门禁口径（本 spike 提交按原样全绿，见 §9）。

### 8.3 层级 3：legacy-runner 端到端 fixture（brief 硬要求）

**fixture 形态（变体替换既有基线指令，绕开新指令投影的 v3 限制）**：

- **夹具包工程**（新增，独立 csproj，不入主编译闭包，与 `tools/dialect-inventory/*` 同惯例）：
  `tools/legacy-runner/fixtures/CompatPackV2FixturePack/`——引用 `src/EmueraFacade` 契约工程；
  manifest：`contractAbi: "2.0"` + `engineApi: "2.0"`（双轴；legacy `targetEngineApi` 仅迁移窗口内接受值 1）、`baseProfileId: "v24pure"`；
  贡献 = `IInstructionVariantContribution` 绑定基线指令（选无副作用指令，如 `DRAWLINE`）→
  adapter（`Execute` = `Console.Print("PACK_V2_HANDLER_HIT")` 等确定性标记输出）。
- **夹具游戏根**：由 harness 脚本 `tools/legacy-runner/Invoke-CompatPackV2Fixture.ps1`
  （新增）生成**最小合成游戏根**（GameBase.csv + 调 `DRAWLINE` 的 ERB）到**项目外**目录
  （沿 `-OutputDirectory` 必须指向项目外的纪律，AGENTS.md 文件管理规范 7 与
  `docs/plans/2026-10-03-pd-module-retirement-spec.md` §7.2）。
- **双 golden 逐字节比对**：
  1. 无包跑（`GEMUERA_COMPAT_PACKS` 不注入）→ trace ≡ 预录 v24pure 基线 golden
     （**逐字节回退证明**：引擎 `DRAWLINE` 原样输出横线）；
  2. env 注入夹具包跑 → trace ≡ pack-on golden（标记输出行出现 = **自定义 handler 被 ERB
     调用**的直接证据）；
  3. 两条 golden 固化于 `tools/legacy-runner/fixtures/`（JSON，沿
     `session-isolation-canary.json` 惯例）。
- **会话隔离 ABAB**：复用 `Invoke-SessionIsolationInProcessAba.ps1` 模式
  （`tools/legacy-runner/`），序列 pack-on / pack-off / pack-on：第二次 pack-on 仍命中标记
  = §5.4 缓存驱逐工作项正确性的运行级证据（死实例/悬挂 ALC 会让该步失败）。

### 8.4 v1 拒载保持的确认口径

本 spike 提交后运行既有测试确认 v1 fail-closed 未动：
`CompatPackRulesTests` 的策略/变体拒载向用例全绿（对应 `CompatPackRules.cs:113-120`/`:137-144`
的两条拒载在 v2 接线任务才移除，§7.2）。

---

## 9. 本 spike 的验证基线（提交时实测，零生产代码变更）

```text
dotnet test tests/xUnitTest/GEmuera.Core.Tests/GEmuera.Core.Tests.csproj -c Release
dotnet test tests/xUnitTest/EmueraFacade.Tests/EmueraFacade.Tests.csproj -c Release
dotnet run --project tools/dialect-inventory/LegacyDialectSurfaceSmoke/LegacyDialectSurfaceSmoke.csproj -c Release
dotnet run --project tools/dialect-inventory/LegacyDialectRuntimeSmoke/LegacyDialectRuntimeSmoke.csproj -c Release
DOTNET_ROLL_FORWARD=Major dotnet run --project tools/core-contracts/CoreContractSmoke.csproj -c Release
```

## 10. 风险与开放问题（请 OWNER 评审时裁定）

1. **编组面宽度**（§4.1 `ICompatInstructionContext`）：首批成员集（Arguments/Console/Variables
   白名单）是否够用由首个真实用例校准；过窄会催生 adapter 之外的第二通道（禁止），过宽
   侵蚀窄接口纪律。增量协议（§6.4.4）是缓解，但首批成员集需 OWNER 认可。
2. **`KnownCapabilityIds` 拆分**（§6.4.2 前置取证）：vocabulary / built-in-implemented
   两集合的划分影响既有 capability 校验文案与 P-D 退役 spec G5 门禁（capability 词汇表冻结）
   的实现口径——拆分不得使词汇表收缩。
3. **投影缓存驱逐的波及面**（§5.4）：按 CanonicalHash 前缀驱逐可能把无包基线条目一并清掉
   （重投影代价），或做成精确双键（plan hash × scoped 标记）；接线任务择一并加回归测试。
4. **`DRAWLINE` 作为 fixture 替换目标的替代项**：若 OWNER 认为替换绘制类指令的 golden 太脆
   （输出列宽依赖终端宽度），可换成更纯的指令（如 `INITRAND` 类副作用指令）——fixture
   定稿时二选一。
5. **策略试点域**（§6.4.4）：`ICompatFunctionContractPolicy` 为建议值，若 P-D snake 步
   （其 §4.1 步 5，DFC 名单最重）先落地，也可换 snake 退役暴露的真实域。§6.2 的
   `PolicyDomainByCapabilityId` 映射表初始内容（宿主供给、随试点域登记）请一并裁定。

## 11. 评审裁定清单（OWNER 勾选即视为 spec 通过）

- [ ] §2.3 方案裁定：B（契约程序集公开适配器接口），A 不选
- [ ] §3.3 `CreateInstruction()` 保持返回 `object`（收窄点即校验点）
- [ ] §4.1 v2.0 窄接口首批成员集（同文法换行为；Flag/文法沿用被替换指令）
- [ ] §5.2 factory map 走 profile 构造参数（不引入 process-wide 静态）
- [ ] §5.4 投影缓存驱逐为 v2 接线前置工作项
- [ ] §6.2 新增 `EnginePolicyBindingV2` + `IPolicyContributionV2`（不改 v1 记录、不原地扩参）；不引入策略工厂
- [ ] §7.1 双轴门槛已由 v1.1 落地，v2 只保留历史记录、不重复升级；三项破坏一次性结算
- [ ] §7.2 校验规则增删清单（含贡献变体进 plan 哈希）
- [ ] §8 测试三层方案与 fixture 落点（`tools/legacy-runner/fixtures/CompatPackV2FixturePack/`）
- [ ] §10 开放问题 1-5 的处置方向
