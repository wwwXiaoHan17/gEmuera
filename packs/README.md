# gEmuera 社区兼容包（CompatPack）开发指南

本目录是社区兼容包的唯一入口。一个兼容包 = 一个 .NET 类库程序集（.dll）+ 内嵌的
`compatpack.manifest.json` 清单，用于在不改动引擎的前提下声明对 v24 基线的差量
（指令/函数表面名单、行为 capability、内置变体选择）与组合约束。

设计契约全文见 `docs/designs/compat-pack-interface.md`；双轴版本、组合解析与语义级
`baseSurfaceHash` 的规范见
`docs/superpowers/specs/2026-11-02-compatpack-v1-contract-versioning-composition-design.md`。
清单 JSON Schema 见 `src/EmueraCompatPack/compatpack.manifest.schema.json`（契约程序集
`Emuera.CompatPack`，2026-10-03 自 Emuera facade 拆分，旧包经 TypeForwardedTo 继续兼容）。
可直接复制的模板工程：[`CommunityPackTemplate/`](CommunityPackTemplate/)。

## 目录约定

```text
packs/
└── CommunityPackTemplate/           # manifest-only 数据包模板（推荐起点）
	├── CommunityPackTemplate.csproj # 类库工程，内嵌清单
	├── compatpack.manifest.json     # 清单（资源名必须恰为 compatpack.manifest.json）
	└── CommunityPackTemplateMarker.cs  # 仅为定位程序集的 public 标记类（可选）
```

- **manifest-only 数据包（推荐）**：程序集只有内嵌清单、不含任何 `ICompatPack`
  实现类。表面/能力/变体声明全部来自清单，加载器自动合成贡献，包作者无需写 C#。
- **带入口类的包（少数场景）**：程序集内实现恰好一个 `ICompatPack`（+ 任意
  `ISurfaceContribution` / `ICapabilityContribution` 贡献），用于清单表达不了的
  动态名单。可参考测试夹具 `tests/xUnitTest/CompatPackContractOnlyFixture/`
  （只依赖契约程序集 `src/EmueraCompatPack/EmueraCompatPack.csproj` 即可实现入口，无需引擎核心）。
  入口类自报的 Manifest 必须与内嵌清单同源（packId/packVersion 一致），否则拒载。

**部署单元 = 包目录（硬约束，一包一目录）**：加载器把主程序集所在目录**顶层**的
`*.dll` 中、**被主程序集 AssemblyRef 实际引用（递归）**的那些当作该包的**依赖闭包**——
既是 ALC 私有依赖的唯一来源，也是 `PackSha256` 的哈希输入。目录顶层**未被引用**的 DLL
一律拒载（错误文案："包目录必须自包含且只含本包程序集"）：它们要么是另一个包的主 DLL、
要么是无关文件，混放会让同一包的哈希随邻居变化、并让未启用的包进入本包的 ALC 可见面。
因此一个包（含其私有依赖）必须独占一个目录，禁止把多个包的主 DLL 或无关 DLL 混放在
同一目录。依赖闭包按"主 DLL + 它真实引用的顶层 DLL + 递归"推导，框架/契约程序集不在
目录内时按 ALC 允许清单处理（见设计文档 §5.2）。

## 清单字段（compatpack.manifest.json）

必填（缺一即拒载）：

| 字段 | 形式 | 说明 |
| ---- | ---- | ---- |
| `packId` | `^[a-z0-9][a-z0-9.\-]*$` | 包唯一 id，全局冲突拒载；不得撞内置方言模块保留名 |
| `packVersion` | `x.y.z` | 语义化版本 |
| `contractAbi` | `major.minor` | Emuera.CompatPack 公开契约面 ABI；major 必须相等，包 minor 不得高于引擎 minor |
| `engineApi` | `major.minor` | 引擎行为/能力契约版本；major 必须相等，包 minor 不得高于引擎 minor |

版本字段二选一：新格式必须同时提供 `contractAbi` 与 `engineApi`；迁移窗口内仍接受
legacy `targetEngineApi: 1`（映射为 1.0/1.0），但官方包必须已完成迁移（不得再携带），
新包应直接使用双轴字段，且不得与双轴字段混用。

常用字段（`baseSurfaceHash` 在带 surface/variant 时必填，其余可选；未知字段一律整体拒载，防止拼写错误静默失效）：

| 字段 | 说明 |
| ---- | ---- |
| `baseProfileId` | 包叠加的基线 profile，缺省 `v24pure`；v1 只支持 v24pure，与实际会话基线不一致拒载 |
| `baseSurfaceHash` | 语义级基线表面快照（**恰好 64 位小写十六进制**，算法见设计 §12.9）。**带非空 `surface`（任一 add/hide 列表非空）或非空 `variantSelections` 的包必填**；纯 capability 包与空 surface 对象可省略。声明非空且与当前引擎基线哈希不一致即拒载；官方 v18 包与模板必须声明 |
| `surface.addInstructions` | 新增可见指令名（Trim+Upper；必须有真实引擎 handler，且不得与基线同名） |
| `surface.hideInstructions` | 隐藏的 v24 基线指令名（基线没有的名字拒载） |
| `surface.addFunctions` / `hideFunctions` | 表达式函数增删，规则同上；新增函数返回类型由引擎清单解析 |
| `capabilities` | 行为 capability id 清单（如 `math.times-clamp.v1`），只接受引擎已收录 id，未知 id 拒载 |
| `variantSelections` | 指令名 → `builtin:*` 内置变体；v1 不支持自带变体/策略贡献（携带即拒载） |
| `gameIdentity` | 绑定特定游戏（与 GameBase.csv 的 コード/バージョン 比对），不匹配拒载；`versionAccept=minor` 比较 `major.minor` 前两段，单段值退化为全串相等 |
| `requires.packs` | 依赖的其它包（`packId` + `versionRange`）；见下方组合语义 |
| `conflicts.packs` | 冲突包 id 清单；见下方组合语义 |
| `overrides` | 显式符号覆盖（`symbol` + 可选 `priority`，缺省 0）；见下方组合语义 |

不要声明 `saveProfileId`：v1 存档路径尚不消费该字段，携带即拒载。

### 组合语义（requires / conflicts / overrides）

- **requires.packs**：依赖解析只发生在"本次显式启用的包路径集合 + 其传递依赖"内，不做
  目录扫描或下载。缺失依赖、版本范围不满足、依赖环、重复 packId 均整组拒载；依赖先于
  依赖者做拓扑排序，同层按 packId Ordinal。
- **conflicts.packs**：清单声明的冲突包只要同时启用即整组拒载（`priority` 不能覆盖冲突）。
- **overrides**：`symbol` 形如 `instruction:<name>` / `function:<name>` /
  `variant:<name>`，`priority` 为非负整数、**可省略（缺省 0）**。只有多个包对同一符号产生**不同最终状态**时
  才需要 override；取 priority 唯一最高者，同 priority 并列拒载。override 目标必须是本次
  组合真实存在的符号，且不得覆盖 v24 基线 instruction/function handler（改既有语义必须走
  变体选择通道）；variant override 的目标指令必须已在 v24 基线内。
- 多包最终状态一致（都 Hidden / 变体同值）按同态合并，无需 override；没有任何符号/
  capability/变体贡献的包不得进入最终组合（v1 没有 bundle 概念）。

## 构建方式

```bash
dotnet build packs/CommunityPackTemplate/CommunityPackTemplate.csproj -c Release
# 产物：packs/CommunityPackTemplate/bin/Release/net8.0/CommunityPackTemplate.dll
```

模板工程 target `net8.0`（Android 分发同样用该 dll，无需单独构建）。把工程复制改名后，
只需修改内嵌的 `compatpack.manifest.json` 与 `packId` 即可产出自己的包。

## 放入位置

安装根（启动器扫描根）：

- 桌面端：**启动器 exe 同级的 `compat_packs/`**（编辑器模式额外扫描
  `res://compat_packs` 调试根）。注意不是"每个游戏根目录下的 compat_packs"。
- Android 端：`/storage/emulated/0/emuera/packs/`。

启动器扫描安装根**顶层**与**每个一级子目录**里的 `*.dll`（不递归更深的层级、不联网
下载）；在启动器中勾选后，所选路径经环境变量 `GEMUERA_COMPAT_PACKS`（分号分隔）传给
引擎加载。

**每包独立目录（唯一支持的部署形态）**：把每个包（主 DLL + 它引用的私有依赖）放进
安装根下的独立子目录 `<安装根>/<packId>/<你的包>.dll`。安装根顶层**只允许放一个包**的
DLL——两个包同处一个目录时，两个包都会因"目录内含未被引用的 DLL"被拒载（fail-closed，
不会静默挑一个哈希/依赖面）。也可用启动器的"手动路径/浏览"（或直接编辑 launcher.cfg 的
`[compat_packs]` 节）指向子目录里的主 DLL。

> 升级提示：旧的"把多个单文件包平铺在安装根"的部署会在加载期被拒载。请按
> `<安装根>/<packId>/` 分目录摆放；包内容哈希 `PackSha256` 只覆盖本包闭包，不再随
> 同目录的其它包变化。

## 版本、基线对账与失败回退

加载遵循 fail-closed 降级不变量：**任何一环失败（清单缺失/字段非法、未知 capability、
`contractAbi`/`engineApi` 不匹配、`baseSurfaceHash` 与当前基线不一致、依赖缺失/冲突、
gameIdentity 不匹配……）= 整包拒载；一次会话启用多包时全有或全无**——任一包失败，
本次会话回退到纯 v24 基线启动，绝不半加载。

- `baseSurfaceHash` 由 `LegacySurfaceHash.BuildV24SurfaceHashInput` 的同一输入构造计算
  （profile/engineApi/指令与函数描述符/METHOD 投影/内置变体/capability），官方 v18 包由
  `tools/compat-pack/Generate-V18PackManifest` 生成写入（禁止手写）；有表面差量或变体选择
  却省略该字段的包直接拒载，禁止把名字-only 的旧哈希当作安全等价（旧值需按新算法重算）。
- `PackSha256` 覆盖主程序集 + 内嵌清单原始字节 + **被引用依赖闭包**（文件名/长度/字节）；
  替换包目录里任一被引用的依赖 DLL 都会改变包内容哈希；未被引用的文件根本不会入哈希
  （它们会让整包拒载）。

排查方法：

1. 启动器在包选择处会显示回退警告（选中包无效或基线非 v24pure）。
2. 拒载原因写入 `[LOAD]` 错误日志（含包基名与全量错误列表）。开启
   `[logging] file_sink` 后查看 `user://gemuera_runtime_*.log`；也可在诊断面板
   导出诊断日志（`gemuera_*.log`）查看完整 ring buffer。
3. 自查清单合法性可对照 JSON Schema（见文首链接）；测试工程
   `GEmuera.Core.Tests/CommunityPackTemplateTests.cs` 演示了模板包与入口类包
   两条加载路径的端到端断言。
