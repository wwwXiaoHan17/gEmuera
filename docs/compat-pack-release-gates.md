# CompatPack v1 发布门禁

> 适用范围：P-D 内置模块退役、社区包正式发布、任何对外声明 v1 兼容完成的版本。
> 设计依据：docs/superpowers/specs/2026-11-02-compatpack-v1-contract-versioning-composition-design.md §9。
> 实现入口：tools/compat-pack/Test-CompatPackReleaseGate.ps1（2026-11-03，Task 9）。
>
> **硬口径：Gate 1-7 全绿前，不退役内置模块、不发布社区包。桌面门禁绿不等于发布门禁绿；真机 e2e 仍是最终验收。**

## 1. 一键桌面门禁

日常开发和 CI 只跑桌面档，不因本机没有真机而阻塞：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\compat-pack\Test-CompatPackReleaseGate.ps1 -ProjectRoot .
```

脚本按固定顺序执行 7 条命令，任一失败立即停止并 exit 1；每条都记录命令、退出码、耗时和摘要：

| # | 命令 | 作用 |
| --- | --- | --- |
| 1 | dotnet test tests\xUnitTest\GEmuera.Core.Tests\GEmuera.Core.Tests.csproj -c Release | Core 契约/组合/ALC/旧包夹具测试 |
| 2 | dotnet test tests\xUnitTest\EmueraFacade.Tests\EmueraFacade.Tests.csproj -c Release | Facade 表面/入口测试 |
| 3 | dotnet test tests\xUnitTest\EmueraPluginAbi.Tests\EmueraPluginAbi.Tests.csproj -c Release | 插件 ABI/TypeForwardedTo 测试 |
| 4 | dotnet run --project tools\dialect-inventory\LegacyDialectSurfaceSmoke -c Release | 方言表面闭包冒烟 |
| 5 | dotnet run --project tools\dialect-inventory\LegacyDialectRuntimeSmoke -c Release | 方言运行期查找冒烟 |
| 6 | $env:DOTNET_ROLL_FORWARD='Major'; dotnet run --project tools\core-contracts\CoreContractSmoke.csproj -c Release | 核心契约冒烟；Major 失败时自动改用 LatestMajor 重试并在输出中标注 |
| 7 | powershell -NoProfile -ExecutionPolicy Bypass -File tools\compat-pack\Test-CompatPackLifecycle.ps1 -ProjectRoot . | CompatPack 生命周期 P0 静态守卫（4/4） |

输出约定：

- 每条检查打印 [PASS] 或 [FAIL]；失败时追加 command:、退出码、摘要与输出尾部（最多 20 行）。
- 最后一行固定为 === 结果: PASS ... === 或 === 结果: FAIL ... ===；退出码 0/1。

### 门禁自测（`-SelfTest`，2026-11-03 起）

发布门禁的判定逻辑本身有一组提交在案的合成夹具（`tools/compat-pack/gate-self-test/`：
fake.apk + 每例一份 android-evidence.json 与日志），验证"合法 PASS / 合规 fallback PASS /
2 Errors / Failures: 1 / terminated / aborted / 缺 plan 哈希 / fallback 后续失败"各自得到
预期结论（9 例）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\compat-pack\Test-CompatPackReleaseGate.ps1 -SelfTest
```

`tools/governance/Test-Governance.ps1` 在桌面档之前自动跑自测（自测模式只校验夹具，不跑
桌面命令，与桌面档无递归）。夹具与日志**不是**发布证据，不得进入发布归档。

### 2026-11-03 本机实测基线（final fix wave 后）

以下计数来自实际运行，仅用于当天回归对照；测试增删后以脚本输出为准，不要手抄过期数字。

| 项目 | 实测结果 |
| --- | --- |
| Core xUnit | 278 passed / 1 skipped / 0 failed（总 279） |
| Facade xUnit | 78 passed / 0 failed |
| Plugin ABI xUnit | 4 passed / 0 failed |
| 门禁自测（-SelfTest） | 9/9 用例符合预期（合法 PASS×3 + 预期 FAIL×6） |
| 三冒烟 | Surface / Runtime / CoreContract 全 PASS |
| lifecycle 守卫 | 4/4 检查通过 |
| 宿主构建 dotnet build gemuera-c#.csproj -nodeReuse:false -m:1 | 0 error / 4 个既有 warning（KoujouPrefetchCache CS8600 ×1、AgentLlmMethods CS8632 ×3） |
| 桌面档耗时 | 约 20–30 秒（.NET 10，Release 已还原） |

CoreContractSmoke 的 roll-forward：脚本先设 DOTNET_ROLL_FORWARD=Major；若本机运行时要求更高 roll-forward，命令失败后自动以 LatestMajor 重试。只有重试也失败才记 FAIL。2026-11-03 本机 Major 直接通过，没有触发回退。

## 2. 证据档（Android + 六游戏）

P-D 或社区包发布任务必须显式加开关并提供证据：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\compat-pack\Test-CompatPackReleaseGate.ps1 -ProjectRoot . -RequireAndroid -RequireSixGame -AndroidEvidencePath reports\compatpack\android-evidence.json -SixGameEvidencePath reports\compatpack\six-game-evidence.json
```

行为：
1. 先校验证据（APK/SHA256、设备、三场景日志 + plan 哈希、六游戏六条记录 + 每条 planHash），**证据不完整立即 exit 1，不进入桌面档**，避免"没跑"被误报成"通过"。
2. 证据通过后再跑第 1 节的 7 条桌面命令。
3. 任一证据项或桌面命令失败均为 exit 1；只有全部通过才 exit 0。

路径约定：
- 相对路径按 -ProjectRoot 解析；-AndroidEvidencePath / -SixGameEvidencePath 可以传 JSON 文件，也可以传目录（脚本自动补 android-evidence.json / six-game-evidence.json）。
- 缺省路径：reports/compatpack/android-evidence.json、reports/compatpack/six-game-evidence.json（reports/ 已被 .gitignore 排除，适合存档运行产物）。
- APK 定位：evidence 的 apkPath（相对 evidence 所在目录，也可绝对路径）；未给时要求 evidence 同目录**恰好一个** *.apk。多个 APK 会报错要求显式 apkPath。

## 3. Gate 1：Android APK e2e 证据

### 3.1 JSON 格式

```json
{
  "schemaVersion": "1.0.0",
  "commit": "<git rev-parse HEAD>",
  "capturedAt": "2026-11-03T12:00:00+08:00",
  "apkPath": "gemuera-release.apk",
  "apkSha256": "<64 位小写或大写十六进制>",
  "deviceModel": "<ro.product.model>",
  "androidVersion": "<ro.build.version.release>",
  "abi": "arm64-v8a",
  "planHash": "<64 位小写十六进制，日志 [LOAD] CompatibilityPlan= 的值>",
  "runs": [
    { "scenario": "start", "log": "logs/start.log" },
    { "scenario": "back-restart", "log": "logs/back-restart.log" },
    { "scenario": "second-start", "log": "logs/second-start.log" }
  ],
  "loadLogFound": true,
  "fatalErrorFound": false,
  "fallbackUsed": false,
  "fallbackTargetBaseline": "v24pure"
}
```

> Gate 1 的“真实 pack”要求以日志为准：三份日志中至少一份必须包含
> `[LOAD] CompatPack enabled: id=... version=... contractAbi=<major.minor> engineApi=<major.minor> ...`。
> 只有 `[LOAD]`、`[LOAD] CompatPack disabled (...)` 或纯 baseline 日志都不能通过 Gate 1（spec §9 Gate 1 要求放入至少一个真实 pack）。

字段说明：

| 字段 | 必需 | 校验 |
| --- | --- | --- |
| apkSha256 | 是 | 64 位十六进制，且与 APK 实际 SHA256 逐字匹配 |
| deviceModel / androidVersion / abi | 是 | 非空字符串；abi 建议 arm64-v8a |
| planHash | 是 | 64 位**小写**十六进制；三场景日志都必须出现 `[LOAD] CompatibilityPlan=<hash>` 且唯一值与它一致（Gate 1 的 plan 哈希正向证据） |
| runs | 是 | 必须覆盖 start / back-restart / second-start 三场景；每条 log 文件存在且非空 |
| loadLogFound | 是 | 必须是 JSON true |
| fatalErrorFound | 是 | 必须是 JSON false |
| apkPath | 否 | 缺省按同目录唯一 *.apk 解析 |
| fallbackUsed | 否 | 若为 true，日志中必须出现 fallback；fallback 必须是成功回退标记（见 3.2） |
| fallbackTargetBaseline | fallback 且日志未写明目标时必需 | 回退目标基线；日志同一行出现 no-pack baseline / no-pack composition / v24pure 等也算写明目标。成功回退标记自带 no-pack baseline，无需再填 |
| commit / capturedAt | 否 | 建议记录，便于归档追溯；脚本当前只做结构校验，不校验值 |

### 3.2 校验规则（缺一 exit 1）

- evidence JSON 存在且可解析。
- APK 文件存在；SHA256 与 apkSha256 匹配。
- 设备型号、Android 版本、ABI 非空。
- 三场景日志都存在、非空；每条日志都包含 `[LOAD]`。
- 三份日志中**至少一份**包含 `[LOAD] CompatPack enabled: id=... contractAbi=<major.minor> engineApi=<major.minor> ...`，且 contractAbi / engineApi 各自是 `major.minor`。只有 `[LOAD]`、纯 baseline 或 `[LOAD] CompatPack disabled (...)` 的日志不能通过 Gate 1。
- **plan 哈希正向校验**：每份日志都必须出现 `[LOAD] CompatibilityPlan=<64 位小写十六进制>`（Process.Initialize 对每个已绑定计划都会打印）；三份日志的哈希必须唯一且等于 evidence 的 `planHash`。缺行、格式不符或不一致均 FAIL。
- 日志中不得出现 `[LOAD] CompatPack disabled (...)`（load rejected / game identity mismatch / assembly rejected / unexpected failure 等包加载降级），也不得出现未处理的 plan hash 失败（plan hash ... fail/mismatch/does not match/不匹配/失败 等）、ALC 失败（ALC/AssemblyLoadContext ... fail/error/exception/未回收 等）、Unhandled exception 或 fatal 标记，以及**非零错误计数**（`2 Errors`、`Failures: 1`、`Exceptions=3` 等；`0 Errors` 不算失败）。
- `fallback` 只接受成功回退标记 `[LOAD] CompatPack projection failed; fallback to no-pack baseline`，且该行之后不得再出现 fail/failed/failure(s)/error(s)/exception(s)/fatal/terminated/aborted/失败/异常/错误/终止/中止（复数与终止态于 2026-11-03 补齐）；`... fallback failed`、`... no-pack baseline bind failed` 等失败/终止行直接判失败。出现 fallback 但找不到成功标记、或 evidence 声明 fallbackUsed=true 但日志无 fallback，也判失败。
- loadLogFound 必须为 true，fatalErrorFound 必须为 false。

> 守卫只能校验证据文本与已知失败模式，不能证明日志一定来自真机。发布评审仍须人工核对归档、设备信息与 APK 哈希。

### 3.3 采集步骤

1. 从待发布分支导出 APK，记录哈希：
   ```powershell
   Get-FileHash .\export\releases\gemuera.apk -Algorithm SHA256
   ```
2. 安装并准备设备：
   ```powershell
   adb install -r .\export\releases\gemuera.apk
   adb shell getprop ro.product.model
   adb shell getprop ro.build.version.release
   adb shell getprop ro.product.cpu.abi
   ```
   把游戏放到启动器扫描根（Android：/storage/emulated/0/emuera/），并把至少一个真实 pack 放到 /storage/emulated/0/emuera/packs/；Gate 1 要求日志出现 enabled 行，纯 baseline 启动不算通过。
3. 逐场景采集日志（三份都要保留原始文本）：
   - **start**：adb logcat -c 后从启动器冷启动游戏，等待输入等待，执行 adb logcat -d -v threadtime > logs\start.log。
   - **back-restart**：使用应用内 Back→Restart（或系统返回键退出后**不 force-stop 进程**立即重新进入），跑到输入等待，保存 logs\back-restart.log。
   - **second-start**：同一进程内再次启动同一游戏，跑到输入等待，保存 logs\second-start.log。
4. 若 adb logcat 中没有 [LOAD]，开启运行时诊断文件 sink（user://gemuera_runtime_*.log）并用 debug 包 run-as 或应用内诊断导出取回，写入对应 log 文件；不要用截图代替日志。
5. 人工检查每份日志：[LOAD]、[LOAD] CompatPack enabled 的 pack id/contractAbi/engineApi、`[LOAD] CompatibilityPlan=<planHash>`、fallback 路径；失败时确认用户可见状态，不得静默回退到未声明 profile。三份日志的 plan 哈希应与 evidence.planHash 相同（gate 会自动核对）。
6. 写 evidence JSON，运行：
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File tools\compat-pack\Test-CompatPackReleaseGate.ps1 -ProjectRoot . -RequireAndroid -AndroidEvidencePath reports\compatpack\android-evidence.json
   ```

## 4. Gate 2：六游戏矩阵证据

### 4.1 矩阵

矩阵沿 docs/designs/dialect-capability-strategy.md §5 与 docs/plans/2026-10-03-pd-module-retirement-spec.md §7.2。gameId 由证据作者选定并在六条记录内唯一；下表是建议值：

| 建议 gameId | 游戏 | 退役前 profile | P-D 后链路 | 所属退役步 |
| --- | --- | --- | --- | --- |
| eramaou | 魔王（伪 eramaou_EX，v18 等价能力） | v18 | v24pure + pack.gemuera.v18 | 步 1 |
| eramegaten | eraMegaten | megaten | v24pure + pack.gemuera.megaten | 步 2 |
| erablue | erablue resort | erablue | v24pure + pack.gemuera.erablue | 步 3 |
| erafl | eraFL0.47 | erafl | v24pure + pack.gemuera.erafl | 步 4 |
| eraocg2 | eraOCG2 | snake | v24pure + pack.gemuera.snake | 步 5 |
| eratohok | eratohok | snake | v24pure + pack.gemuera.snake | 步 5 |

语义：
- profile：本次会话实际使用的 profile。
- baseProfile：目标基线；P-D 后为 v24pure，P-D 前允许与 profile 相同。
- packIds：实际加载的包 id 列表；纯内置 profile 会话允许 []。P-D 发布证据应列出第一方包。
- result：只接受字面量大写 PASS；小写 pass 不接受。
- keyOutput：人工核对过的标题/首屏/关键输出（例如首行标题、首个输入等待）。
- logPath：该次无头运行的日志文件；必须存在且非空。

### 4.2 JSON 格式

```json
{
  "schemaVersion": "1.0.0",
  "games": [
    {
      "gameId": "eramaou",
      "profile": "v18",
      "baseProfile": "v24pure",
      "packIds": ["pack.gemuera.v18"],
      "planHash": "<64 位小写十六进制，该次运行的 plan 哈希>",
      "result": "PASS",
      "keyOutput": "标题 + 首屏 + 输入等待",
      "logPath": "six-game/logs/eramaou.log"
    }
  ]
}
```

以上示例只展示 1 条记录；实际文件必须恰好 6 条，缺一条即 FAIL。也接受顶层直接是 6 元素数组的写法。logPath 相对 evidence 文件所在目录解析（绝对路径也支持）。

### 4.3 校验规则（缺一 exit 1）

- JSON 可解析；顶层为数组或含 games 数组。
- 记录数恰好 6；gameId 不得重复。
- 每条记录包含非空 gameId / profile / baseProfile / keyOutput / logPath / planHash，且 result 严格等于字面量大写 PASS（小写 pass 不接受）。
- planHash 必须是 64 位小写十六进制；日志文件若包含 `[LOAD] CompatibilityPlan=` 行，其哈希必须与记录的 planHash 一致（runner 日志没有该行时只做格式校验）。
- packIds 字段必须存在；可以是 []（纯基线）或包 id 列表。Windows PowerShell 5.1 的 ConvertFrom-Json 会把单元素数组折叠成字符串、把 [] 折叠成 null，脚本已兼容这两种形态；不要因此写非数组 JSON。
- logPath 文件必须存在且非空。

### 4.4 运行与日志

入口为 tools/legacy-runner/Invoke-LegacyRunner.ps1（参数以该脚本为准）。每个游戏至少产出到输入等待的无头运行日志；示例（一行一条，-OutputDirectory 必须在项目外）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\legacy-runner\Invoke-LegacyRunner.ps1 -GodotPath "<Godot_v4.7-stable_mono_win64_console.exe>" -GameRoot "<游戏根>" -Profile v18 -OutputDirectory "D:\gemuera-reports\six-game\eramaou" -RepeatCount 1 -SkipBuild
```

日志要求：
- 保留 runner stdout/stderr 为 run.log，或把 diagnostics.json / state.json 作为 logPath；gate 只保证文件存在且非空，内容仍需人工核对标题/首屏/零致命解析错误。
- 建议日志中出现 [LOAD] 和实际 profile / baseProfile / packIds，便于与 JSON 对账。
- 不得把 -OutputDirectory 指到项目根内；不要在游戏目录写存档/日志（runner 默认隔离，除非显式 -AllowGameDirectoryWrites）。
- 当前机器没有六游戏真实证据时，带 -RequireSixGame 的调用必须 FAIL；这是预期行为，不得用空记录或 result=PASS 假绿。

## 5. Gate 3/4：ALC 回收与旧包兼容夹具

2026-11-03 本机实测（命令均带 -c Release）：

| 项 | 命令 | 实测 |
| --- | --- | --- |
| 真实 ALC 回收 | dotnet test tests\xUnitTest\GEmuera.Core.Tests\GEmuera.Core.Tests.csproj -c Release --filter FullyQualifiedName~CompatPackAlcReclamationTests | 1 passed / 0 failed |
| ALC 隔离允许清单 | ... --filter FullyQualifiedName~CompatPackAlcIsolationTests | 2 passed / 0 failed |
| 旧 facade 包 | ... --filter FullyQualifiedName~CompatPackLegacyFacadeTests | 3 passed / 0 failed |
| TypeForwardedTo / ABI 边界 | dotnet test tests\xUnitTest\EmueraPluginAbi.Tests\EmueraPluginAbi.Tests.csproj -c Release --filter FullyQualifiedName~PluginAbiBoundaryTests | 4 passed / 0 failed |

覆盖内容：
- ALC 回收用真实 loader/session 持有包，Dispose 后对 ALC 与入口实例做 WeakReference + 多轮 GC 断言，并检查静态字段残留。
- 旧包夹具覆盖 facade 预载/未预载、TypeForwardedTo 通道、失败时的可定位 ABI 错误与 baseSurfaceHash 一致性。
- Gate 3 的 Android 真机重复是 spec 的“最好”项：建议在同一 APK 上重复一次并人工归档结果；该项当前未编码进 Android evidence schema，gate 不校验，不计入自动门禁。

## 6. Gate 5/6：契约测试与治理入口

- Gate 5（版本/组合契约）已由 Core/Facade/ABI 测试与 Surface/Runtime 冒烟覆盖：双轴比较、legacy targetEngineApi 迁移、非法组合拒载、依赖/冲突/覆盖/优先级、plan hash 稳定性、符号双形态闭包、plan/registry 精确一致。
- Gate 6（治理与 CI）：tools/governance/Test-Governance.ps1 先做 work-package schema 往返，再跑 lifecycle 守卫，然后跑发布门禁自测（`-SelfTest`）与桌面档；任一失败 throw。日常 CI 不带 -RequireAndroid / -RequireSixGame；P-D/发布任务另行运行证据档并归档日志。
- 文档与报告中的测试计数必须来自实际运行；不要手写未实测数字。

## 7. P-D 解锁顺序（Gate 1-7）

**Gate 1-7 全绿前，P-D 内置模块退役与社区包发布均保持锁定。**

1. **Gate 6 桌面档先绿**：在待发布 commit 上跑 Test-CompatPackReleaseGate.ps1 -ProjectRoot . 与 Test-Governance.ps1，归档完整输出。
2. **Gate 1 Android APK e2e**：真实设备安装同一 APK，至少重复 2 次；保存三场景日志、设备信息、APK SHA256；带 -RequireAndroid 跑证据档。
3. **Gate 2 六游戏矩阵**：六个游戏按正确 profile / pack / 依赖组合无头跑到输入等待，关键输出核对为 PASS；带 -RequireSixGame 跑证据档。
4. **Gate 3 真实 ALC 回收**：桌面测试必跑；Android 真机重复一次为建议项（人工归档，gate 不校验，不作为自动门禁条件）。
5. **Gate 4 旧包兼容夹具**：v1 Emuera.CompatPack 包与旧 AssemblyRef=Emuera facade 包在干净/预载两类环境均通过。
6. **Gate 5 版本与组合契约**：桌面测试全绿；版本/组合契约用例无跳过、无过滤遗漏。Core 中 1 个 symlink 环境能力用例会按环境 Skip（见 §1 实测基线），不计入通过，也不属于 Gate 5 契约用例。
7. **Gate 7 才允许 P-D**：Gate 1-6 全绿；六游戏映射已进入运行时代码；每个待退役 profile 有 v24pure + 包 的逐名等价 golden；legacy-runner 三 profile 无头冒烟通过；Android 至少一个真实 APK 用例通过。满足后才按 docs/plans/2026-10-03-pd-module-retirement-spec.md 的步序退役。

P-D/发布用的证据档命令：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\compat-pack\Test-CompatPackReleaseGate.ps1 -ProjectRoot . -RequireAndroid -RequireSixGame -AndroidEvidencePath reports\compatpack\android-evidence.json -SixGameEvidencePath reports\compatpack\six-game-evidence.json
powershell -NoProfile -ExecutionPolicy Bypass -File tools\governance\Test-Governance.ps1
```

## 8. 归档与复现

建议在 reports/compatpack/（不入库）保留：

```text
reports/compatpack/
  android-evidence.json
  gemuera-release.apk
  logs/start.log
  logs/back-restart.log
  logs/second-start.log
  six-game-evidence.json
  six-game/logs/*.log
  gate-desktop.log
  gate-evidence.log
  governance.log
```

要求：
- evidence JSON 记录 commit（git rev-parse HEAD）与 capturedAt；APK/日志必须来自同一 commit，commit 变化后重采。
- 归档实际命令、退出码、测试计数、APK SHA256、设备信息；不要只留"全绿"结论。
- 发布前由评审抽查日志与设备信息；gate 通过不等于证据真实，人工核对仍是必要环节。
- 任何官方包走 legacy 路径都视为发布门禁失败（人工评审项；gate 当前不自动拦截 `[LOAD] CompatPack legacy targetEngineApi mapped` 行）。

## 9. 当前状态与已知限制（2026-11-03）

- 桌面门禁：PASS（7/7 命令；Core 278 passed / 1 skipped、Facade 78 passed、ABI 4 passed；三冒烟与 lifecycle 4/4 全绿；门禁自测 9/9）。
- Gate 1 现在强制 plan 哈希：三份日志都必须出现 `[LOAD] CompatibilityPlan=<64 位小写 hex>` 且与 evidence.planHash 一致；早于 2026-11-03 的旧证据需要按新 schema 补 planHash 后重采。
- 六游戏证据每条新增 `planHash`（必填，格式 + 与日志对账）。
- 门禁判定逻辑本身由提交在案的合成夹具（tools/compat-pack/gate-self-test/，9 例）与 governance 自动自测覆盖；夹具不是发布证据。
- 宿主构建：0 error / 4 个既有 warning。
- 本机**没有**真实 Android 设备与六游戏运行证据：-RequireAndroid / -RequireSixGame 会明确 FAIL（缺 APK/设备/日志/六游戏记录），不得假绿；这不代表产品失败，只代表发布验收尚未完成。
- 合成 self-test 夹具（fake.apk / 合成日志）只用于验证门禁逻辑，**不是**发布证据，不得放入发布归档。
- PowerShell 5.1 兼容性：脚本保存为 UTF-8 BOM；用 edit 等工具改动后若 BOM 丢失，中文会按 ANSI 解析并可能直接语法报错。提交前用字节检查前 3 字节为 EF BB BF。
- PowerShell 5.1 的 ConvertFrom-Json 会折叠单元素数组与空数组：本 gate 对 packIds 做了兼容；六游戏/games 的其它数组字段若未来新增，需同样处理。
- gate 只校验证据结构与已知失败模式，不执行 APK 安装、不自动跑六游戏；这些仍由发布任务执行并归档。
- Android Gate 1 以日志中的 `[LOAD] CompatPack enabled` 行作为“真实 pack 成功加载”的证据；gate 不验证 APK 内 pack 文件内容，发布评审仍须核对 pack id、版本轴与归档日志一致。
- fallback 只接受成功回退标记；fallback 失败/终止行、以及成功回退后再次出现 fail/error/exception/fatal 的日志均判失败。
- 六游戏 result 使用大小写敏感比较；`pass` / `Pass` 均不接受。
- fatal 是保留的阻断词：日志出现 non-fatal 之类措辞也可能被拦截；如属正常文案，应改日志或调整规则后再发布。
