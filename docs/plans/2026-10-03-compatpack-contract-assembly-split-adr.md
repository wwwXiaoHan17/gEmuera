# ADR：CompatPack 契约程序集拆分与插件边界（2026-10-03）

状态：已实施（Task 10，分支 `ai/compatpack-community-foundation`）。
关联：`docs/designs/compat-pack-interface.md` §4/§10.1、Task 9 spec（`2026-10-03-compatpack-v2-code-contributions-spec.md`）、`docs/ERBAPI.md` §3。

## 1. 背景与问题

CompatPack 契约（`ICompatPack` / `CompatPackManifest` / `CompatPackVariants`，命名空间 `Emuera.Compatibility.Packs`）自 v1 起与上游插件 ABI（`IPluginMethod` / `PluginManifestAbstract` / `PluginMethodParameter`，命名空间 `MinorShift.Emuera.Runtime.Utils.PluginSystem`）共用同一个程序集——`src/EmueraFacade/Emuera.csproj`（`AssemblyName=Emuera`，`AssemblyVersion=1.824.0.0`）。该版本号是上游插件生态的 ABI 身份（外部插件按 `Emuera, Version=1.824.0.0` 强引用编译，.NET 绑定按简单名匹配）。

问题：包契约的任何演进（v2 贡献接口、未来 v3）都会牵动上游插件 ABI 程序集的版本/内容面；两条 ABI 线被一个版本号锁死。设计文档 §10.1 早已把"独立契约程序集"列为推荐落点。

## 2. 决定

### 2.1 新程序集命名

- **AssemblyName：`Emuera.CompatPack`**。理由：包作者在 AssemblyRef 里看到的名字应自证身份——"Emuera 家族的 CompatPack 契约线"；与历史契约名 `Emuera` 保持家族前缀（旧包作者迁移时认知连续）；不用 `gEmuera.` 前缀，因为契约类型命名空间本就是 `Emuera.Compatibility.Packs`（对包作者暴露的是 Emuera 面，不是宿主 gEmuera 面）。
- **工程落点：`src/EmueraCompatPack/EmueraCompatPack.csproj`**，`<AssemblyName>Emuera.CompatPack</AssemblyName>` 与目录/文件名一致可辨。遵守 AGENTS.md：无阶段代号、描述用途。
- **RootNamespace：`Emuera.Compatibility.Packs`**——与被迁移类型的命名空间一致；**类型命名空间一个字符都不改**（包程序集按类型全名发现入口类，改命名空间 = 全部现存包拒载）。
- 目标框架：`net8.0;net9.0`（无条件双目标，与 GEmuera.Core 同法；覆盖桌面 net8 与 Android 导出图 net9，避免条件 TFM 在引用图两侧错配 NU1201）。

### 2.2 版本策略

- 新契约程序集**独立 SemVer 线**：`AssemblyVersion=1.0.0.0`、`FileVersion=1.0.0`。v1 契约面（现有 14 个 public 类型）即 1.0；v2 贡献接线时按语义化版本升 minor——**不再牵动 facade 的 1.824**。
- facade 完全不动：`AssemblyName=Emuera`、`AssemblyVersion=1.824.0.0`、`FileVersion=1.824.0.0` 保持逐字节同值，上游插件 ABI 叙事不变。

### 2.3 TypeForwardedTo 策略（旧包兼容窗口）

facade 新增 `src/EmueraFacade/CompatPackContractForwarding.cs`（工程根，不放在已移除的 `Compatibility/` 目录下），对**全部 14 个迁移的 public 类型**声明 `[assembly: TypeForwardedTo(...)]`：

- `ICompatPack`、`ICompatPackContribution`、`ISurfaceContribution`、`IInstructionSurfaceRegistry`、`IFunctionSurfaceRegistry`、`ICapabilityContribution`（ICompatPack.cs）
- `CompatPackGameIdentity`、`CompatPackSurface`、`CompatPackManifest`（CompatPackManifest.cs）
- `IInstructionVariantContribution`、`InstructionVariantBinding`、`ICompatInstructionFactory`、`IPolicyContribution`、`EnginePolicyBinding`（CompatPackVariants.cs）

机制：拆分前编译的包（AssemblyRef = `Emuera`）在运行期把 `Emuera` 解析到宿主已加载的 facade 实例后，CLR 沿 TypeForwardedTo 链到 `Emuera.CompatPack`（宿主 Default ALC 已加载的同一实例），新旧两侧拿到**同一类型标识**。C# 编译器在编译期也会跟随 forward，所以引用 facade 的新代码自动产出 `Emuera.CompatPack` 的 AssemblyRef。

**兼容承诺：1.x 成员级二进制兼容**（TypeForwardedTo 是 CLR 元数据机制）——在 1.x 契约线内，拆分前编译的数据包与 v1 入口类包仍能加载并解析到原本使用的 public 类型/成员；不承诺行为兼容，也不承诺跨 major 兼容（语义见 `docs/superpowers/specs/2026-11-02-compatpack-v1-contract-versioning-composition-design.md` §4.2）。只有当契约线升 major、或未来改命名空间/删除类型时才需要重审。迁移期约定：新包一律直接引用 `Emuera.CompatPack`；`Emuera` 名下的契约类型面只作为历史加载面，不再新增。

### 2.4 ALC 绑定规则 ①（`GEmuera.Core.Compatibility.Packs.CompatPackLoadContext`）

拆分后规则 ① 变为两支（其余 ②③④ 不动）：

1. ①a `Emuera.CompatPack`（新契约程序集简单名）→ `typeof(ICompatPack).Assembly`（宿主 Default ALC 已加载实例）；
2. ①b `Emuera`/`emuera`（历史契约名，旧包）→ 在 `AssemblyLoadContext.Default.Assemblies` 中按简单名扫描 facade 实例返回（与 BCL 分支同法：只扫已加载，不在回调里发起绑定）；**未命中即抛 FileLoadException**（fail-closed——不允许 null 回落让默认解析去磁盘探测 `Emuera.dll`，那会重开信任边界缺口）。

不命中①的旧包在纯 Core 测试宿主（未加载 facade）里会得到明确拒载错误，这是预期行为：旧包本来就要求宿主提供 `Emuera`。

### 2.5 什么迁移、什么留下

| 项 | 去向 |
| --- | --- |
| `ICompatPack.cs`(+.uid)、`CompatPackManifest.cs`(+.uid)、`CompatPackVariants.cs`(+.uid) | `src/EmueraCompatPack/`（git mv，XML 文档注释随文件原样带走） |
| `compatpack.manifest.schema.json` | 随契约工程走（`None Include="*.json" Pack="false"`）：它是 manifest 结构的权威文档，语义上属于契约线；运行期无人按路径读它（grep 验证：仅文档引用），文档引用路径同步更新 |
| `IPluginMethod.cs`、`PluginManifestAbstract.cs`、`PluginMethodParameter.cs` | 留在 facade（上游插件 ABI） |
| facade `Compatibility/` 目录 | 拆分后删除（空目录不入 git） |

### 2.6 引用图变更（grep 验证后的逐项决定）

| 工程 | 拆分前 | 拆分后 | 依据 |
| --- | --- | --- | --- |
| `src/Core/GEmuera.Core.csproj` | → facade | → **Emuera.CompatPack**（替换） | Core 8 个文件只用契约类型，零插件 ABI 使用 |
| `gemuera-c#.csproj`（宿主） | → facade | → facade + **Emuera.CompatPack**（新增） | Scripts 用插件 ABI（Process.cs/Argument.cs/AgentLlmMethods.cs）也直名契约类型（CompatPackHost.cs:264 `CompatPackGameIdentity`）；同时必须把 `src\EmueraCompatPack\**` 加进宿主 glob 排除，防止新工程源码被默认 glob 收进宿主 |
| `tests/.../CompatPackContractOnlyFixture` | → facade | → **Emuera.CompatPack**（替换） | 夹具语义即"只引用契约程序集" |
| `tests/.../CompatPackHostBindingProbe` | → Core + facade | → Core + **Emuera.CompatPack** | 同上 |
| `tests/.../EmueraFacade.Tests` | → facade | → facade + **Emuera.CompatPack** | 保留 35 个契约行为测试原位全绿（facade 引用保留给 ABI 面） |
| `tools/dialect-inventory/LegacyDialectSurfaceSmoke` | → Core（契约经传递） | → Core + **Emuera.CompatPack**（显式化） | Program.cs 直名 `CompatPackManifest.TryParse` |
| `tools/core-contracts/CoreContractSmoke`、`tools/compat-pack/Generate-V18PackManifest` | → Core | 不变（契约经 Core 传递流入） | 只用 Core 类型或经传递面 |
| `packs/*`、`DataOnlyCompatPackFixture` | 无引用 | 不变 | 数据包无契约引用 |

### 2.7 回滚方案

单提交回滚即回到拆分前形态：git revert 拆分提交后，facade 重新含契约类型（forwarding 文件同被回滚），Core/宿主/测试引用回到 facade。唯一需要人工复核的是拆分后新编译的包（AssemblyRef=Emuera.CompatPack）：回滚后这些包将因 `Emuera.CompatPack` 缺席而拒载——回滚窗口内尚无外部新包，风险为零；今后出现社区包后，回滚必须连带评估包兼容（记录于此备查）。

### 2.8 已知问题与勤勉项结论

- **facade 停机路径可达性（Task 4 遗留核查项）**：`Scripts/EmueraMain.cs` `StopLegacySession()` 在 `facade != null` 时 `facade.DisposeAsync()` 后提前 return，看似跳过 `ClearCompatibilityPlan`；实测链路为 `DisposeAsync → LegacySessionBackend.StopAsync → StopLegacyBaselineAsync/StopCanarySessionAsync → Program.ClearCompatibilityPlan()/ResetSessionState()`，两者最终都调 `DisposeCompatPackSession()`（幂等，包 ALC 确定性卸载）。提前 return 跳过的只是无 facade 时的兜底二次清理，**不构成包 ALC 孤儿**。结论：无需修复，记录备查。
- **`Program.ConfigureCompatibilityPlan` 绑定点异常隔离**：从接收 `packSession` 到 `Volatile.Write` 存储之间只有 `plan.CanonicalHash` 读取（构造期已验证的 get-only 存储属性）与 `Volatile.Read/Write`（不抛），中间无可抛操作；包会话不可能在绑定途中被异常孤儿化。结论：无需加 guard，记录备查。
- 本 ADR 不实现任何 v2 宿主桥（Task 9 spec 的 `ICompatPolicyAdapter` 落点仅在此声明为契约程序集；接线是后续任务）。

### 2.9 门禁清单（全部通过才算完成）

1. `dotnet test tests/xUnitTest/EmueraPluginAbi.Tests/EmueraPluginAbi.Tests.csproj -c Release`（新，RED→GREEN 证据在任务报告）
2. `dotnet test tests/xUnitTest/GEmuera.Core.Tests/GEmuera.Core.Tests.csproj -c Release`（112/112）
3. `dotnet test tests/xUnitTest/EmueraFacade.Tests/EmueraFacade.Tests.csproj -c Release`（35/35）
4. `CompatPackHostBindingProbe` / `CompatPackContractOnlyFixture` 构建通过（夹具工程）
5. 三冒烟：LegacyDialectSurfaceSmoke / LegacyDialectRuntimeSmoke / CoreContractSmoke（`DOTNET_ROLL_FORWARD=Major`）
6. 宿主构建：`dotnet build gemuera-c#.csproj -c Release -nodeReuse:false -m:1` 0 error
7. Android export smoke：按计划为可选项，环境摩擦即放弃记「待 Android 实测」
