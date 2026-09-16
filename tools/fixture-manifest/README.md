# M0 Fixture Manifest

`M0-FIX-01` 把 upstream、legacy 和 game 三层 fixture 的来源、profile、授权证据、逐文件 hash 与期望报告固定成机器可读清单。工具只读外部目录；不会复制游戏、修改 fixture 或把本地绝对路径写入版本化 catalog。

版本化声明位于 `Build/Fixtures/manifest.json`。其中 `authorization.status=Verified` 只有在声明的许可证文件实际存在并被 hash 后才有效；`Unverified/MissingEvidence` 强制 `redistributionAllowed=false`。字节存在不代表授权可再分发。

## 本地生成

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/fixture-manifest/Invoke-FixtureManifest.ps1 `
  -ProjectRoot "E:\MyCode\GodotCode\gEmuera-future" `
  -UpstreamRoot "E:\XEmuera-master" `
  -GameRoot "E:\Godot_v4.6.2-stable_mono_win64\snake\EraTW-Magic_DLC-update_base" `
  -LegacyReportDirectory "<legacy-runner>\run-001" `
  -GameReportDirectory "<legacy-runner>\run-001" `
  -OutputDirectory "<artifact-output>"
```

未提供 `EraFlRoot`、upstream runner report 或其他条目时命令仍返回 0，并在 `fixture-manifest.json` 中明确记录 `Uncovered/Partial`。CI 需要完整证据时传 `-RequireComplete`，Partial 返回 exit 2；catalog/schema/路径错误返回 exit 1。

## 验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/fixture-manifest/Test-FixtureManifest.ps1 `
  -ProjectRoot "E:\MyCode\GodotCode\gEmuera-future"
```

逐文件清单按 ordinal relative path 排序，canonical 行为 UTF-8 路径长度前缀、路径、bytes 和 SHA-256。upstream/legacy 源码层应用 `M0-FIX-01-source-v1` 排除缓存、构建和本地 Agent 目录；game fixture 不套用源码排除规则。
