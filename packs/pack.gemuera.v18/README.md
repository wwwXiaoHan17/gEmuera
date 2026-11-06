# pack.gemuera.v18

- **packId**：`pack.gemuera.v18`（目录/程序集/DLL 同名）
- **来源内置模块**：`gemuera.v18`（2026-11-07 退役，commit `8072448`）
- **适用 profile / 游戏**：v18（emuera1824+v18 血统游戏）；由 launcher profile→pack 映射选择
- **表面差量**：纯隐藏——v24 后增指令/函数不进入 v18 会话；无 capability、无变体
- **清单来源**：`tools/compat-pack/Generate-V18PackManifest` 生成后 golden 固化（禁止手写）
- **构建**：`dotnet build packs/pack.gemuera.v18/pack.gemuera.v18.csproj -c Release`
- **等价门禁**：`FirstPartyPackEquivalenceTests` + `LegacyDialectSurfaceSmoke`（packed v18 行为投影）
- **部署**：staging `compat_packs/pack.gemuera.v18/pack.gemuera.v18.dll`，启动释放到安装根一包一目录
- **已知限制**：v1 包通道不能表达 `saveProfileId`/`defaultPorts`（由宿主映射保留）
