# pack.gemuera.snake

- **packId**：`pack.gemuera.snake`（目录/程序集/DLL 同名；packVersion 1.3.0）
- **来源内置模块**：`game.snake`（2026-11-07 退役，commit `c865596`）
- **适用 profile / 游戏**：snake（Emuera-SKIA 蛇版，对齐到 Skiav18.0.0；eraTW 等）；由 launcher profile→pack 映射选择
- **表面差量**：最大差集——22 个非 METHOD 指令 + 93 个函数（METHOD 投影名由函数侧镜像；2026-11-14 对齐 Skiav17.2 增 6 个 AUDIO_*，packVersion 1.1.0；
  2026-11-23 对齐 Skiav18.0.0，表面无增减，packVersion 1.3.0）；
  `hideFunctions=["BITMAP_CACHE_ENABLE","SETANIMETIMER"]`；
  `addInstructionCollisions=["BITMAP_CACHE_ENABLE"]`（contractAbi 1.1，保留指令/隐藏函数）；
  22 个 Wired capability（v1.2.0 增 html.div-align-attr-ignore.v1；v1.3.0 增 declare.const-table.v1 与 declare.reject-float-charadata.v1）；
  `variantSelections={FOR,SETBGIMAGE}=builtin:snake`
- **Skiav18.0.0 对齐范围（2026-11-23）**：`declare.const-table.v1` = `#DIM`/`#DIMS`/`#DIMF` 的 `CONSTTABLE` 常数表声明
  （值与各维名称读自同名 CSV/ERD，名称下标不依赖 USE ERD；实现见 `scripts/Emuera/GameData/ConstArrayTable.cs`）；
  `declare.reject-float-charadata.v1` = `#DIMF CHARADATA` 在声明处报错。
- **未对齐的 Skiav18.0.0 内容**：`resources.pak` 单文件资源包（上游 17.4.0 引入、18.0 列为正式特性）——本引擎不读取该包，
  只以 `resources.pak` 分发资源的游戏会找不到图片与图片 CSV；eraWire（EWP）外部前端协议与本引擎无关；
  一维内置数组存档长度保底经审计不适用于本引擎的读档路径（登记于对齐日志）。
- **清单来源**：由冻结表面 `tools/dialect-inventory/retired-dialect-surface-universe.json` 计算生成
- **构建**：`dotnet build packs/pack.gemuera.snake/pack.gemuera.snake.csproj -c Release`
- **等价门禁**：`FirstPartyPackEquivalenceTests` + `LegacyDialectSurfaceSmoke`（变体/DFC/隐藏函数/capability）
- **部署**：staging `compat_packs/pack.gemuera.snake/pack.gemuera.snake.dll`
- **注意**：`陥落状態/陷落状态` 必须保持可见（eraTW 口上包依赖），禁止加入 hideFunctions
