# pack.gemuera.megaten

- **packId**：`pack.gemuera.megaten`（目录/程序集/DLL 同名）
- **来源内置模块**：`game.megaten`（2026-11-07 退役，commit `177743b`）
- **适用 profile / 游戏**：megaten（女神转生系）；由 launcher profile→pack 映射选择
- **表面差量**：零表面增量；声明 5 个 Wired capability（`parser.label-lookup-case.v1`、
  `parser.private-system-shadow.v1`、`parser.ref-out-name.v1`、`startup.continue-after-fault.v1`、
  `ui.html-rgb-zero-alpha.v1`）
- **清单来源**：手写；等价测试即 golden（通用生成器 deferred/P2）
- **构建**：`dotnet build packs/pack.gemuera.megaten/pack.gemuera.megaten.csproj -c Release`
- **等价门禁**：`FirstPartyPackEquivalenceTests`（零表面 + 5 Wired）
- **部署**：staging `compat_packs/pack.gemuera.megaten/pack.gemuera.megaten.dll`
