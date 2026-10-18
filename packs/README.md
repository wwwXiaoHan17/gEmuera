# gEmuera 社区兼容包（CompatPack）开发指南

本目录是社区兼容包的唯一入口。一个兼容包 = 一个 .NET 类库程序集（.dll）+ 内嵌的
`compatpack.manifest.json` 清单，用于在不改动引擎的前提下声明对 v24 基线的差量
（指令/函数表面名单、行为 capability、内置变体选择）。

设计契约全文见 `docs/designs/compat-pack-interface.md`；清单 JSON Schema 见
`src/EmueraCompatPack/compatpack.manifest.schema.json`（契约程序集 `Emuera.CompatPack`，
2026-10-03 自 Emuera facade 拆分，旧包经 TypeForwardedTo 继续兼容）。
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

## 清单字段（compatpack.manifest.json）

必填（缺一即拒载）：

| 字段 | 形式 | 说明 |
| ---- | ---- | ---- |
| `packId` | `^[a-z0-9][a-z0-9.\-]*$` | 包唯一 id，全局冲突拒载；不得撞内置方言模块保留名 |
| `packVersion` | `x.y.z` | 语义化版本 |
| `targetEngineApi` | 正整数 | 目标引擎包 API 版本（当前为 1）；主版本不匹配拒载 |

常用可选字段（未知字段一律整体拒载，防止拼写错误静默失效）：

| 字段 | 说明 |
| ---- | ---- |
| `baseProfileId` | 包叠加的基线 profile，缺省 `v24pure`；v1 只支持 v24pure，与实际会话基线不一致拒载 |
| `surface.addInstructions` | 新增可见指令名（Trim+Upper；必须有真实引擎 handler，且不得与基线同名） |
| `surface.hideInstructions` | 隐藏的 v24 基线指令名（基线没有的名字拒载） |
| `surface.addFunctions` / `hideFunctions` | 表达式函数增删，规则同上；新增函数返回类型由引擎清单解析 |
| `capabilities` | 行为 capability id 清单（如 `math.times-clamp.v1`），只接受引擎已收录 id，未知 id 拒载 |
| `variantSelections` | 指令名 → `builtin:*` 内置变体；v1 不支持自带变体/策略贡献（携带即拒载） |
| `gameIdentity` | 绑定特定游戏（与 GameBase.csv 的 コード/バージョン 比对），不匹配拒载 |

不要声明 `saveProfileId`：v1 存档路径尚不消费该字段，携带即拒载。

## 构建方式

```bash
dotnet build packs/CommunityPackTemplate/CommunityPackTemplate.csproj -c Release
# 产物：packs/CommunityPackTemplate/bin/Release/net8.0/CommunityPackTemplate.dll
```

模板工程 target `net8.0`（Android 分发同样用该 dll，无需单独构建）。把工程复制改名后，
只需修改内嵌的 `compatpack.manifest.json` 与 `packId` 即可产出自己的包。

## 放入位置

把编译出的包 dll（含同目录依赖，若引用了第三方库）放到启动器扫描根的**顶层**：

- 桌面端：游戏根目录 `compat_packs/`（即 `<gameRoot>/compat_packs/<你的包>.dll`）。
- Android 端：`/storage/emulated/0/emuera/packs/`。

启动器仅扫描该目录顶层的 `*.dll`（不递归、不联网下载）；在启动器中勾选后，
所选路径经环境变量 `GEMUERA_COMPAT_PACKS`（分号分隔）传给引擎加载。

## 失败回退与日志

加载遵循 fail-closed 降级不变量：**任何一环失败（清单缺失/字段非法、未知 capability、
与基线对账冲突、gameIdentity 不匹配、targetEngineApi 不匹配……）= 整包拒载；一次
会话启用多包时全有或全无**——任一包失败，本次会话回退到纯 v24 基线启动，绝不半加载。

排查方法：

1. 启动器在包选择处会显示回退警告（选中包无效或基线非 v24pure）。
2. 拒载原因写入 `[LOAD]` 错误日志（含包路径前缀与全量错误列表）。开启
   `[logging] file_sink` 后查看 `user://gemuera_runtime_*.log`；也可在诊断面板
   导出诊断日志（`gemuera_*.log`）查看完整 ring buffer。
3. 自查清单合法性可对照 JSON Schema（见文首链接）；测试工程
   `GEmuera.Core.Tests/CommunityPackTemplateTests.cs` 演示了模板包与入口类包
   两条加载路径的端到端断言。
