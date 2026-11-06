# pack.gemuera.erablue

- **packId**：`pack.gemuera.erablue`（目录/程序集/DLL 同名）
- **来源内置模块**：`game.erablue`（2026-11-07 退役，commit `2c24ec8`）
- **适用 profile / 游戏**：erablue；由 launcher profile→pack 映射选择
- **表面差量**：`addInstructions=["SETANIMETIMER"]`；1 个 Wired capability
- **清单来源**：手写；等价测试即 golden
- **构建**：`dotnet build packs/pack.gemuera.erablue/pack.gemuera.erablue.csproj -c Release`
- **等价门禁**：`FirstPartyPackEquivalenceTests`（SETANIMETIMER + capability）
- **部署**：staging `compat_packs/pack.gemuera.erablue/pack.gemuera.erablue.dll`
