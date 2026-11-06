# pack.gemuera.erafl

- **packId**：`pack.gemuera.erafl`（目录/程序集/DLL 同名）
- **来源内置模块**：`game.erafl`（2026-11-07 退役，commit `d1a3171`）
- **适用 profile / 游戏**：eraFL（eraFL 系游戏）；由 launcher profile→pack 映射选择
- **表面差量**：`addInstructions=["SETANIMETIMER"]`；10 个 Wired capability；
  GMap/XML/算法库仍保留在引擎（不在包内）
- **清单来源**：手写；等价测试即 golden
- **构建**：`dotnet build packs/pack.gemuera.erafl/pack.gemuera.erafl.csproj -c Release`
- **等价门禁**：`FirstPartyPackEquivalenceTests` + `LegacyDialectSurfaceSmoke`（packed erafl 行为投影）
- **部署**：staging `compat_packs/pack.gemuera.erafl/pack.gemuera.erafl.dll`
