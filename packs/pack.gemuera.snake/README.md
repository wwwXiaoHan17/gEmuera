# pack.gemuera.snake

- **packId**：`pack.gemuera.snake`（目录/程序集/DLL 同名）
- **来源内置模块**：`game.snake`（2026-11-07 退役，commit `c865596`）
- **适用 profile / 游戏**：snake（Skiav13.0 系/eraTW 等）；由 launcher profile→pack 映射选择
- **表面差量**：最大差集——22 个非 METHOD 指令 + 87 个函数（87 个 METHOD 投影名由函数侧镜像）；
  `hideFunctions=["BITMAP_CACHE_ENABLE","SETANIMETIMER"]`；
  `addInstructionCollisions=["BITMAP_CACHE_ENABLE"]`（contractAbi 1.1，保留指令/隐藏函数）；
  19 个 Wired capability；`variantSelections={FOR,SETBGIMAGE}=builtin:snake`
- **清单来源**：由冻结表面 `tools/dialect-inventory/retired-dialect-surface-universe.json` 计算生成
- **构建**：`dotnet build packs/pack.gemuera.snake/pack.gemuera.snake.csproj -c Release`
- **等价门禁**：`FirstPartyPackEquivalenceTests` + `LegacyDialectSurfaceSmoke`（变体/DFC/隐藏函数/capability）
- **部署**：staging `compat_packs/pack.gemuera.snake/pack.gemuera.snake.dll`
- **注意**：`陥落状態/陷落状态` 必须保持可见（eraTW 口上包依赖），禁止加入 hideFunctions
