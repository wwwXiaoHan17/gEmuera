# CommunityPackTemplate —— 社区兼容包起步模板

这是**可直接复制的最小 manifest-only 兼容包工程**：程序集只内嵌清单，没有 C# 入口类；
表面/能力/变体全部由 `compatpack.manifest.json` 声明（v1 推荐形态）。

## 复制与改名

1. 复制整个目录：`packs/CommunityPackTemplate/` → `packs/<你的 packId>/`
   （packId 形如 `community.r18.mymod`，只允许小写字母/数字/点/连字符；不允许下划线）。
2. 重命名 csproj：`CommunityPackTemplate.csproj` → `<你的 packId>.csproj`；
   把 `<AssemblyName>` 改成 packId（构建产物 DLL 名必须等于 packId，一包一目录）。
3. 重命名标记类文件（可选，仅用于测试定位程序集）：`CommunityPackTemplateMarker.cs` → `<PackName>Marker.cs`，
   命名空间同步改名（manifest-only 包不需要 `ICompatPack` 实现）。
4. 把 `compatpack.manifest.json` 的 `packId`/`packVersion` 改成你的值；
   `compatpack.manifest.json` 必须保持 **EmbeddedResource LogicalName = compatpack.manifest.json**。
5. 保持 `packs/<packId>/<packId>.csproj` 与 DLL 名一致；不要在同一目录混放其它包的 DLL。

## manifest 最小合法示例

```json
{
  "packId": "community.example.mymod",
  "packVersion": "1.0.0",
  "contractAbi": "1.0",
  "engineApi": "1.0",
  "baseProfileId": "v24pure",
  "baseSurfaceHash": "<64 位小写十六进制；带 surface/variant 时必填>",
  "capabilities": [],
  "surface": {
    "addInstructions": ["MY_INSTRUCTION"],
    "hideInstructions": [],
    "addFunctions": ["MY_FUNCTION"],
    "hideFunctions": [],
    "addInstructionCollisions": []
  },
  "variantSelections": {}
}
```

- 只改字段，不要新增未知字段（整包 fail-closed 拒载）。
- `surface.addInstructionCollisions` 仅用于 instruction/函数同名碰撞态（contractAbi 1.1）；
  名字必须同时出现在 `hideFunctions` 且是 v24 基线 METHOD 投影名。
- `baseSurfaceHash` 不要手写：按设计 §12.9 算法/官方生成器写出，改表面后必须重算。
- 可选字段示例（保持模板 manifest 合法才可保留，否则删掉）：
  `gameIdentity`（绑定游戏代码/版本）、`requires.packs`（依赖包）、
  `conflicts.packs`、`overrides`、`variantSelections`；字段语义见 `packs/README.md`。

## 构建

```bash
dotnet build packs/<你的 packId>/<你的 packId>.csproj -c Release
# 产物：packs/<你的 packId>/bin/Release/net8.0/<你的 packId>.dll
```

## 放置与验证

- 部署（一包一目录）：`<安装根>/<packId>/<packId>.dll`；桌面安装根 = exe 同级 `compat_packs/`，
  Android = `/storage/emulated/0/emuera/packs/`；启动器手动路径/浏览选择主 DLL。
- 验证：启动器勾选后看 `[LOAD] CompatPack enabled: id=...` 日志；拒载原因在 `[LOAD] CompatPack disabled`。
- 回归：`GEmuera.Core.Tests/CommunityPackTemplateTests.cs` 演示模板包加载路径；复制后可仿照写自己的测试。
- 不要在运行时下载/解压包；v1 只从显式路径集合加载，全有或全无。
