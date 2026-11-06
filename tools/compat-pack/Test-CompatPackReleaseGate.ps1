# =============================================================================
# Test-CompatPackReleaseGate.ps1 — CompatPack v1 发布门禁（桌面档 + 证据档）
# =============================================================================
# 用途（docs/superpowers/specs/2026-11-02-compatpack-v1-contract-versioning-
# composition-design.md §9）：
#   - 桌面档（默认）：依次运行 Core/Facade/ABI xUnit、Surface/Runtime/
#     CoreContract 三冒烟与 CompatPack 生命周期静态守卫；任一失败立即 exit 1。
#   - 证据档（-RequireAndroid / -RequireSixGame）：先校验真实设备/六游戏证据，
#     证据不完整绝不进入桌面档，避免把"没跑"误报成"通过"。
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/compat-pack/Test-CompatPackReleaseGate.ps1 -ProjectRoot <项目根>
#   powershell ... -RequireAndroid -RequireSixGame [-AndroidEvidencePath <json>] [-SixGameEvidencePath <json>]
#
# 证据路径约定（相对路径按 -ProjectRoot 解析；也可传目录，脚本补默认文件名）：
#   reports/compatpack/android-evidence.json
#   reports/compatpack/six-game-evidence.json
# Android APK 约定：evidence JSON 的 apkPath（相对 evidence 目录），或同目录
# 恰好一个 *.apk。格式与采集步骤见 docs/compat-pack-release-gates.md。
#
# 退出码：0 = 全部 PASS；1 = 任一证据项或桌面命令 FAIL。
# 输出：统一 [PASS]/[FAIL] 行；失败时打印命令、退出码与输出尾部；最后一行
# 为 "=== 结果: PASS/FAIL ... ==="。
#
# 兼容性：Windows PowerShell 5.1（项目门禁宿主）。本文件必须保存为 UTF-8 BOM，
# 否则 5.1 会按系统 ANSI 代码页读取中文注释。
# =============================================================================

[CmdletBinding()]
param(
    [string]$ProjectRoot = '.',
    [string]$AndroidEvidencePath = '',
    [string]$SixGameEvidencePath = '',
    [switch]$RequireAndroid,
    [switch]$RequireSixGame,
    # 门禁自测：用提交在案的合成夹具（gate-self-test/）验证 Gate 1 判定逻辑本身，
    # 不跑桌面命令、不产生发布证据。governance 每次桌面门禁前自动调用。
    [switch]$SelfTest,
    # 仅自测内部使用：把夹具根指到临时副本（"缺证据必须 exit 1"自身自检用）。
    [string]$SelfTestFixtureRoot = '',
    # 仅自测内部使用：嵌套自检子进程跳过自身探针，避免无限递归。
    [switch]$SkipSelfTestProbe
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$script:CheckCount = 0
$script:FailureCount = 0
$script:DesktopPassCount = 0

# -----------------------------------------------------------------------------
# 通用辅助（避开内置别名/自动变量；所有文件读写显式 UTF-8）
# -----------------------------------------------------------------------------

function Write-GateCheck {
    param([string]$Name, [bool]$Passed, [string]$Detail)
    $script:CheckCount++
    if ($Passed) {
        Write-Output ("[PASS] {0}" -f $Name)
    }
    else {
        $script:FailureCount++
        Write-Output ("[FAIL] {0}" -f $Name)
    }
    if (-not [string]::IsNullOrWhiteSpace($Detail)) {
        Write-Output ("       {0}" -f $Detail)
    }
}

function Get-JsonField {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Test-NonEmptyText {
    param($Value)
    if (-not ($Value -is [string])) { return $false }
    return (-not [string]::IsNullOrWhiteSpace($Value))
}

function Format-JsonValue {
    param($Value)
    if ($null -eq $Value) { return '<missing/null>' }
    if ($Value -is [bool]) {
        if ($Value) { return 'true' }
        return 'false'
    }
    return [string]$Value
}

function Resolve-GatePath {
    param([string]$PathValue, [string]$BaseDirectory)
    if ([string]::IsNullOrWhiteSpace($PathValue)) { return $null }
    if ([IO.Path]::IsPathRooted($PathValue)) { return $PathValue }
    return [IO.Path]::Combine($BaseDirectory, $PathValue)
}

function Resolve-EvidenceJsonPath {
    param([string]$RawPath, [string]$DefaultRelativePath, [string]$DefaultFileName)
    if ([string]::IsNullOrWhiteSpace($RawPath)) {
        return [IO.Path]::Combine($ProjectRoot, $DefaultRelativePath)
    }
    $candidate = Resolve-GatePath -PathValue $RawPath -BaseDirectory $ProjectRoot
    if (Test-Path -LiteralPath $candidate -PathType Container) {
        return [IO.Path]::Combine($candidate, $DefaultFileName)
    }
    return $candidate
}

function Read-Utf8Text {
    param([string]$Path)
    return [IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

function Get-OutputSummary {
    param([string[]]$Lines)
    if ($null -eq $Lines -or $Lines.Count -eq 0) { return '(无输出)' }
    for ($i = $Lines.Count - 1; $i -ge 0; $i--) {
        $line = $Lines[$i].Trim()
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -match '(?i)(已通过!|已失败!|Passed!|Failed!|测试通过|smoke passed|检查全部通过| contract passed|全部通过)') {
            return $line
        }
    }
    for ($i = $Lines.Count - 1; $i -ge 0; $i--) {
        $line = $Lines[$i].Trim()
        if (-not [string]::IsNullOrWhiteSpace($line)) { return $line }
    }
    return '(无输出)'
}

function Write-CommandOutputTail {
    param([string[]]$Lines)
    if ($null -eq $Lines) { return }
    foreach ($line in @($Lines | Select-Object -Last 20)) {
        Write-Output ("       | {0}" -f $line)
    }
}

function Invoke-CapturedCommand {
    param([string]$FilePath, [string[]]$ArgumentList)
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $captured = New-Object System.Collections.Generic.List[string]
    $exitCode = 1
    try {
        & $FilePath @ArgumentList 2>&1 | ForEach-Object { [void]$captured.Add([string]$_) }
        $exitCode = $LASTEXITCODE
    }
    catch {
        [void]$captured.Add('[gate] 命令执行异常: ' + $_.Exception.Message)
        $exitCode = 1
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Lines    = $captured.ToArray()
    }
}

# -----------------------------------------------------------------------------
# Gate 1：Android evidence（真实 APK / 设备 / 三场景日志）
# -----------------------------------------------------------------------------

function Test-AndroidEvidence {
    param([string]$EvidencePath)

    Write-Output ''
    Write-Output '--- Gate 1: Android evidence ---'

    $evidenceExists = Test-Path -LiteralPath $EvidencePath -PathType Leaf
    Write-GateCheck -Name 'Android evidence JSON 存在' -Passed $evidenceExists -Detail $EvidencePath
    if (-not $evidenceExists) {
        Write-Output '       缺少: APK（apkSha256 + APK 文件）、设备信息（deviceModel/androidVersion/abi）、三场景日志（start/back-restart/second-start）、loadLogFound/fatalErrorFound'
        return
    }

    $evidence = $null
    try {
        $evidence = Read-Utf8Text -Path $EvidencePath | ConvertFrom-Json
    }
    catch {
        Write-GateCheck -Name 'Android evidence JSON 可解析' -Passed $false -Detail ('解析失败: ' + $_.Exception.Message)
        return
    }
    if ($null -eq $evidence) {
        Write-GateCheck -Name 'Android evidence JSON 可解析' -Passed $false -Detail 'JSON 为空'
        return
    }
    Write-GateCheck -Name 'Android evidence JSON 可解析' -Passed $true -Detail ''

    $evidenceDirectory = Split-Path -Parent $EvidencePath

    # ---- 设备信息 ----
    $deviceModel = Get-JsonField -Object $evidence -Name 'deviceModel'
    $androidVersion = Get-JsonField -Object $evidence -Name 'androidVersion'
    $abi = Get-JsonField -Object $evidence -Name 'abi'
    $missingDevice = @()
    if (-not (Test-NonEmptyText -Value $deviceModel)) { $missingDevice += 'deviceModel' }
    if (-not (Test-NonEmptyText -Value $androidVersion)) { $missingDevice += 'androidVersion' }
    if (-not (Test-NonEmptyText -Value $abi)) { $missingDevice += 'abi' }
    if ($missingDevice.Count -eq 0) {
        Write-GateCheck -Name '设备信息齐全（deviceModel/androidVersion/abi）' -Passed $true -Detail ("{0} / Android {1} / {2}" -f $deviceModel, $androidVersion, $abi)
    }
    else {
        Write-GateCheck -Name '设备信息齐全（deviceModel/androidVersion/abi）' -Passed $false -Detail ('缺少字段: ' + ($missingDevice -join ', '))
    }

    # ---- APK 定位 ----
    $apkRaw = Get-JsonField -Object $evidence -Name 'apkPath'
    $apkPath = $null
    $apkIssues = @()
    if (Test-NonEmptyText -Value $apkRaw) {
        $candidate = Resolve-GatePath -PathValue $apkRaw -BaseDirectory $evidenceDirectory
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $apkPath = $candidate
        }
        else {
            $apkIssues += ('apkPath 指向的文件不存在: ' + $candidate)
        }
    }
    else {
        $apkCandidates = @(Get-ChildItem -LiteralPath $evidenceDirectory -File -Filter '*.apk' -ErrorAction SilentlyContinue)
        if ($apkCandidates.Count -eq 1) {
            $apkPath = $apkCandidates[0].FullName
        }
        elseif ($apkCandidates.Count -eq 0) {
            $apkIssues += ('evidence 未提供 apkPath，且目录内没有 *.apk: ' + $evidenceDirectory)
        }
        else {
            $names = @($apkCandidates | ForEach-Object { $_.Name }) -join ', '
            $apkIssues += ('evidence 未提供 apkPath，且目录内存在多个 *.apk，无法唯一确定: ' + $names)
        }
    }
    if ($null -ne $apkPath) {
        Write-GateCheck -Name 'APK 文件存在' -Passed $true -Detail $apkPath
    }
    else {
        Write-GateCheck -Name 'APK 文件存在' -Passed $false -Detail ($apkIssues -join ' ; ')
    }

    # ---- APK SHA256 ----
    $expectedSha = [string](Get-JsonField -Object $evidence -Name 'apkSha256')
    if ([string]::IsNullOrWhiteSpace($expectedSha)) {
        Write-GateCheck -Name 'APK SHA256 匹配' -Passed $false -Detail '缺少 apkSha256'
    }
    elseif ($expectedSha -notmatch '^[0-9a-fA-F]{64}$') {
        Write-GateCheck -Name 'APK SHA256 匹配' -Passed $false -Detail ('apkSha256 不是 64 位十六进制: ' + $expectedSha)
    }
    elseif ($null -eq $apkPath) {
        Write-GateCheck -Name 'APK SHA256 匹配' -Passed $false -Detail '无 APK 文件可计算 SHA256'
    }
    else {
        try {
            $actualSha = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actualSha -eq $expectedSha.ToLowerInvariant()) {
                Write-GateCheck -Name 'APK SHA256 匹配' -Passed $true -Detail ('sha256=' + $actualSha)
            }
            else {
                Write-GateCheck -Name 'APK SHA256 匹配' -Passed $false -Detail ("evidence={0} actual={1}" -f $expectedSha.ToLowerInvariant(), $actualSha)
            }
        }
        catch {
            Write-GateCheck -Name 'APK SHA256 匹配' -Passed $false -Detail ('计算失败: ' + $_.Exception.Message)
        }
    }

    # ---- 三场景日志 ----
    $runsValue = Get-JsonField -Object $evidence -Name 'runs'
    $runs = @()
    if ($null -ne $runsValue) { $runs = @($runsValue) }
    $runIssues = @()
    $runLogs = @()
    $requiredScenarios = @('start', 'back-restart', 'second-start')
    $seenScenarios = @{}
    $runNumber = 0
    foreach ($run in $runs) {
        $runNumber++
        if ($null -eq $run) {
            $runIssues += ("第 {0} 条 run 为空" -f $runNumber)
            continue
        }
        $scenario = [string](Get-JsonField -Object $run -Name 'scenario')
        $logRaw = [string](Get-JsonField -Object $run -Name 'log')
        if ([string]::IsNullOrWhiteSpace($scenario)) {
            $runIssues += ("第 {0} 条 run 缺少 scenario" -f $runNumber)
            continue
        }
        $seenScenarios[$scenario.ToLowerInvariant()] = $true
        if ([string]::IsNullOrWhiteSpace($logRaw)) {
            $runIssues += ("场景 {0} 缺少 log 路径" -f $scenario)
            continue
        }
        $logPath = Resolve-GatePath -PathValue $logRaw -BaseDirectory $evidenceDirectory
        if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
            $runIssues += ("场景 {0} 日志不存在: {1}" -f $scenario, $logPath)
            continue
        }
        try {
            $logText = Read-Utf8Text -Path $logPath
        }
        catch {
            $runIssues += ("场景 {0} 日志读取失败: {1}" -f $scenario, $_.Exception.Message)
            continue
        }
        if ([string]::IsNullOrWhiteSpace($logText)) {
            $runIssues += ("场景 {0} 日志为空: {1}" -f $scenario, $logPath)
            continue
        }
        $runLogs += [pscustomobject]@{
            Scenario = $scenario
            Path     = $logPath
            Text     = $logText
        }
    }
    foreach ($scenario in $requiredScenarios) {
        if (-not $seenScenarios.ContainsKey($scenario)) {
            $runIssues += ("缺少场景 {0}（必须提供 start / back-restart / second-start 三场景）" -f $scenario)
        }
    }
    if ($runIssues.Count -eq 0) {
        Write-GateCheck -Name '三场景日志存在且非空' -Passed $true -Detail ("{0} 条 run（start/back-restart/second-start）" -f $runLogs.Count)
    }
    else {
        Write-GateCheck -Name '三场景日志存在且非空' -Passed $false -Detail ($runIssues -join ' ; ')
    }

    # ---- [LOAD] 标记 ----
    $loadIssues = @()
    foreach ($log in $runLogs) {
        if ($log.Text -notmatch '\[LOAD\]') {
            $loadIssues += ("场景 {0} 日志缺少 [LOAD]: {1}" -f $log.Scenario, $log.Path)
        }
    }
    if ($runLogs.Count -eq 0) {
        $loadIssues += '没有可检查的日志（三场景日志缺失）'
    }
    if ($loadIssues.Count -eq 0) {
        Write-GateCheck -Name '三场景日志均包含 [LOAD]' -Passed $true -Detail ''
    }
    else {
        Write-GateCheck -Name '三场景日志均包含 [LOAD]' -Passed $false -Detail ($loadIssues -join ' ; ')
    }

    # ---- plan 哈希正向校验（spec §9 Gate 1；final whole-branch review I2） ----
    # Process.Initialize 对每个已绑定计划都会打印 "[LOAD] CompatibilityPlan=<64 位小写 hex>"。
    # 每个场景都必须出现该行，且三份日志的哈希必须唯一、与 evidence.planHash 一致；否则
    # "没有 plan 哈希（或哈希对不上）"的日志会被误当成通过。
    $planHashPattern = '\[LOAD\]\s+CompatibilityPlan=(?<hash>[0-9a-f]{64})(?:\s|$)'
    $evidencePlanHash = [string](Get-JsonField -Object $evidence -Name 'planHash')
    $planIssues = @()
    if ([string]::IsNullOrWhiteSpace($evidencePlanHash)) {
        $planIssues += 'evidence 缺少 planHash（Gate 1 要求记录 plan 哈希）'
    }
    elseif ($evidencePlanHash -notmatch '^[0-9a-f]{64}$') {
        $planIssues += ('planHash 必须是 64 位小写十六进制: ' + $evidencePlanHash)
    }
    $observedPlanHashes = @{}
    foreach ($log in $runLogs) {
        $planMatches = [regex]::Matches($log.Text, $planHashPattern)
        if ($planMatches.Count -eq 0) {
            $planIssues += ("场景 {0} 日志缺少 [LOAD] CompatibilityPlan=<64 位小写 hex>: {1}" -f $log.Scenario, $log.Path)
            continue
        }
        foreach ($planMatch in $planMatches) {
            $observedPlanHashes[$planMatch.Groups['hash'].Value] = $true
        }
    }
    if ($runLogs.Count -eq 0) {
        $planIssues += '没有可检查 plan 哈希的日志（三场景日志缺失）'
    }
    if ($observedPlanHashes.Count -gt 1) {
        $planIssues += ('三场景日志的 plan 哈希不一致: ' + (($observedPlanHashes.Keys | Sort-Object) -join ', '))
    }
    if ((-not [string]::IsNullOrWhiteSpace($evidencePlanHash)) -and ($observedPlanHashes.Count -eq 1)) {
        $observedPlanHash = @($observedPlanHashes.Keys)[0]
        if (-not [string]::Equals($observedPlanHash, $evidencePlanHash, [StringComparison]::Ordinal)) {
            $planIssues += ("planHash 与日志不一致: evidence={0} log={1}" -f $evidencePlanHash, $observedPlanHash)
        }
    }
    if ($planIssues.Count -eq 0) {
        Write-GateCheck -Name '三场景日志均含 plan 哈希且与 evidence.planHash 一致' -Passed $true -Detail ('plan=' + $evidencePlanHash)
    }
    else {
        Write-GateCheck -Name '三场景日志均含 plan 哈希且与 evidence.planHash 一致' -Passed $false -Detail ($planIssues -join ' ; ')
    }

    # ---- 未处理失败：plan hash / ALC / unhandled / fatal / 包加载降级 / fallback 失败 ----
    # fallback 失败行必须单独拦截：它们同时包含 "fallback" 与 "no-pack baseline"，
    # 旧规则只校验目标基线和 fatal，会把 "fallback failed" 当成合法回退（review I1）。
    # 注意顺序：成功回退行是 "projection failed; fallback to no-pack baseline"，
    # 失败词在 fallback 之前，因此不会被下面"fallback 之后失败"的规则误伤。
    $forbiddenRules = @(
        [pscustomobject]@{ Label = '未处理 plan hash 失败'; Pattern = '(?i)(plan\s*hash|计划哈希)[^\r\n]{0,160}(fail|failed|mismatch|error|does not match|不匹配|不一致|失败)' },
        [pscustomobject]@{ Label = 'ALC 失败'; Pattern = '(?i)(assemblyloadcontext|\bALC\b)[^\r\n]{0,160}(fail|failed|error|exception|leak|not unload|未回收|失败|异常)' },
        [pscustomobject]@{ Label = '未处理异常'; Pattern = '(?i)unhandled\s+exception|未处理异常' },
        [pscustomobject]@{ Label = 'fatal 日志'; Pattern = '(?i)\bfatal(?:\s+error)?\b' },
        [pscustomobject]@{ Label = 'fallback 失败'; Pattern = '(?i)\bfallback\b[^\r\n]{0,160}(fail|failed|failure|error|exception|失败|异常|abort|aborted|terminat|终止)' },
        [pscustomobject]@{ Label = 'no-pack baseline 失败'; Pattern = '(?i)no-pack baseline[^\r\n]{0,160}(fail|failed|failure|error|exception|失败|异常|abort|aborted|terminat|终止)' },
        [pscustomobject]@{ Label = 'CompatPack 包加载降级'; Pattern = '(?i)\[LOAD\]\s+CompatPack\s+disabled' },
        # 非零错误计数（I2）："2 Errors" / "Failures: 1" / "Exceptions=3" 一律拦截；
        # "0 Errors" 不算失败（模式要求非零数字，避免误伤汇总行）。
        [pscustomobject]@{ Label = '非零错误计数'; Pattern = '(?i)(\b[1-9][0-9]*\s*(?:errors?|failures?|exceptions?)\b|(?:errors?|failures?|exceptions?)\s*[:=]\s*[1-9][0-9]*\b)' }
    )
    $violations = @()
    foreach ($log in $runLogs) {
        $lines = $log.Text -split '\r?\n'
        for ($i = 0; $i -lt $lines.Count; $i++) {
            foreach ($rule in $forbiddenRules) {
                if ([regex]::IsMatch($lines[$i], $rule.Pattern)) {
                    $snippet = $lines[$i].Trim()
                    if ($snippet.Length -gt 180) { $snippet = $snippet.Substring(0, 180) + '...' }
                    $violations += ("[{0}] {1}: {2}" -f $log.Scenario, $rule.Label, $snippet)
                }
            }
        }
    }
    if ($violations.Count -eq 0) {
        Write-GateCheck -Name '日志无未处理 plan hash / ALC / fatal / fallback 失败 / 包降级' -Passed $true -Detail ''
    }
    else {
        Write-GateCheck -Name '日志无未处理 plan hash / ALC / fatal / fallback 失败 / 包降级' -Passed $false -Detail ($violations -join ' ; ')
    }

    # ---- 至少一个真实 pack 成功加载（spec §9 Gate 1） ----
    # 只有 "[LOAD] CompatPack enabled: id=... contractAbi=... engineApi=..." 才算
    # 真实 pack 加载成功；baseline-only / disabled / 只有 [LOAD] 的日志不满足 Gate 1。
    $enabledPattern = '(?i)\[LOAD\]\s+CompatPack enabled:\s*(?<body>.+)$'
    $versionPattern = '^\d+\.\d+$'
    $enabledRecords = @()
    $enabledIssues = @()
    foreach ($log in $runLogs) {
        foreach ($line in ($log.Text -split '\r?\n')) {
            $enabledMatch = [regex]::Match($line, $enabledPattern)
            if (-not $enabledMatch.Success) { continue }
            $body = $enabledMatch.Groups['body'].Value
            $idMatch = [regex]::Match($body, '(?i)(?:^|\s)id=(?<value>\S+)')
            $contractMatch = [regex]::Match($body, '(?i)(?:^|\s)contractAbi=(?<value>\S+)')
            $engineMatch = [regex]::Match($body, '(?i)(?:^|\s)engineApi=(?<value>\S+)')
            $packId = ''
            if ($idMatch.Success) { $packId = $idMatch.Groups['value'].Value }
            $contractAbi = ''
            if ($contractMatch.Success) { $contractAbi = $contractMatch.Groups['value'].Value }
            $engineApi = ''
            if ($engineMatch.Success) { $engineApi = $engineMatch.Groups['value'].Value }
            $recordIssues = @()
            if ([string]::IsNullOrWhiteSpace($packId)) { $recordIssues += '缺少 id' }
            if ([string]::IsNullOrWhiteSpace($contractAbi)) { $recordIssues += '缺少 contractAbi' }
            elseif ($contractAbi -notmatch $versionPattern) { $recordIssues += ('contractAbi 非 major.minor: ' + $contractAbi) }
            if ([string]::IsNullOrWhiteSpace($engineApi)) { $recordIssues += '缺少 engineApi' }
            elseif ($engineApi -notmatch $versionPattern) { $recordIssues += ('engineApi 非 major.minor: ' + $engineApi) }
            $enabledRecords += [pscustomobject]@{
                Scenario    = $log.Scenario
                PackId      = $packId
                ContractAbi = $contractAbi
                EngineApi   = $engineApi
            }
            foreach ($recordIssue in $recordIssues) {
                $enabledIssues += ("场景 {0} enabled 行: {1}" -f $log.Scenario, $recordIssue)
            }
        }
    }
    if ($enabledRecords.Count -eq 0) {
        Write-GateCheck -Name '至少一个真实 pack 成功加载（[LOAD] CompatPack enabled）' -Passed $false -Detail '三场景日志均未出现 [LOAD] CompatPack enabled；纯 baseline / disabled / 只有 [LOAD] 的日志不能通过 Gate 1（spec §9 Gate 1 要求至少一个真实 pack）'
    }
    elseif ($enabledIssues.Count -gt 0) {
        Write-GateCheck -Name '至少一个真实 pack 成功加载（[LOAD] CompatPack enabled）' -Passed $false -Detail ($enabledIssues -join ' ; ')
    }
    else {
        $enabledDetail = @($enabledRecords | ForEach-Object { "id=$($_.PackId) contractAbi=$($_.ContractAbi) engineApi=$($_.EngineApi)" }) -join ' ; '
        Write-GateCheck -Name '至少一个真实 pack 成功加载（[LOAD] CompatPack enabled）' -Passed $true -Detail $enabledDetail
    }

    # ---- fallback：只接受“成功回退到无包基线” ----
    # 成功标记固定为 "[LOAD] CompatPack projection failed; fallback to no-pack baseline"。
    # 同一个日志在成功标记之后若再次出现 fail/error/exception/fatal/失败/异常/终止，
    # 说明回退后仍失败，必须 FAIL；"fallback failed" 行本身由 forbiddenRules 拦截。
    $fallbackMarkerPattern = '(?i)\[LOAD\]\s+CompatPack projection failed;\s*fallback to no-pack baseline'
    # I2：复数 Errors/Failures/Exceptions、terminated/aborted 与"错误/终止/中止"都必须拦截；
    # 前置零计数的行（"0 Errors"/"0 Failures"）不误伤。
    $postFallbackFailurePattern = '(?i)((?<!\b0\s)\bfail(?:ed|ure|ures)?\b|(?<!\b0\s)\berrors?\b|(?<!\b0\s)\bexceptions?\b|\bfatal\b|\bterminat(?:ed|ing|ion)\b|\babort(?:ed|ing|ion)\b|失败|异常|错误|终止|中止)'
    $fallbackIssues = @()
    $declaredFallbackTarget = [string](Get-JsonField -Object $evidence -Name 'fallbackTargetBaseline')
    $fallbackSeen = $false
    foreach ($log in $runLogs) {
        $lines = $log.Text -split '\r?\n'
        $fallbackLineCount = 0
        $markerIndex = -1
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -notmatch '(?i)\bfallback\b') { continue }
            $fallbackSeen = $true
            $fallbackLineCount++
            if ($markerIndex -lt 0 -and [regex]::IsMatch($lines[$i], $fallbackMarkerPattern)) {
                $markerIndex = $i
                continue
            }
            # 成功标记自身包含 no-pack baseline，不需要再查目标；其它 fallback 行必须
            # 写明目标基线，否则视为未声明的静默回退。
            $targetOk = [regex]::IsMatch($lines[$i], '(?i)\bfallback\b[^\r\n]*(no-pack baseline|no-pack composition|v24pure|pack\.gemuera\.|first-party pack)')
            if (-not $targetOk -and -not [string]::IsNullOrWhiteSpace($declaredFallbackTarget)) {
                $targetOk = $lines[$i].IndexOf($declaredFallbackTarget, [StringComparison]::OrdinalIgnoreCase) -ge 0
            }
            if (-not $targetOk) {
                $snippet = $lines[$i].Trim()
                if ($snippet.Length -gt 180) { $snippet = $snippet.Substring(0, 180) + '...' }
                $fallbackIssues += ("场景 {0} 出现 fallback，但日志未包含目标基线: {1}" -f $log.Scenario, $snippet)
            }
        }
        if ($fallbackLineCount -gt 0 -and $markerIndex -lt 0) {
            $fallbackIssues += ("场景 {0} 出现 fallback，但未找到成功回退标记（[LOAD] CompatPack projection failed; fallback to no-pack baseline）；fallback 失败/终止行不能算合法回退" -f $log.Scenario)
        }
        if ($markerIndex -ge 0) {
            for ($i = $markerIndex + 1; $i -lt $lines.Count; $i++) {
                # 再次出现成功标记不算失败（标记本身含 "projection failed"）。
                if ([regex]::IsMatch($lines[$i], $fallbackMarkerPattern)) { continue }
                if ([regex]::IsMatch($lines[$i], $postFallbackFailurePattern)) {
                    $snippet = $lines[$i].Trim()
                    if ($snippet.Length -gt 180) { $snippet = $snippet.Substring(0, 180) + '...' }
                    $fallbackIssues += ("场景 {0} 成功回退后仍出现失败/异常: {1}" -f $log.Scenario, $snippet)
                }
            }
        }
    }
    $fallbackUsed = Get-JsonField -Object $evidence -Name 'fallbackUsed'
    if (($fallbackUsed -is [bool]) -and $fallbackUsed -and (-not $fallbackSeen)) {
        $fallbackIssues += 'evidence 声明 fallbackUsed=true，但三场景日志均未出现 fallback'
    }
    if ($fallbackIssues.Count -eq 0) {
        Write-GateCheck -Name 'fallback 仅接受成功回退到无包基线（未发生 fallback 时自动通过）' -Passed $true -Detail ''
    }
    else {
        Write-GateCheck -Name 'fallback 仅接受成功回退到无包基线' -Passed $false -Detail ($fallbackIssues -join ' ; ')
    }

    # ---- evidence 汇总布尔 ----
    $loadLogFound = Get-JsonField -Object $evidence -Name 'loadLogFound'
    if (($loadLogFound -is [bool]) -and $loadLogFound) {
        Write-GateCheck -Name 'loadLogFound=true' -Passed $true -Detail ''
    }
    else {
        Write-GateCheck -Name 'loadLogFound=true' -Passed $false -Detail ('实际: ' + (Format-JsonValue -Value $loadLogFound) + '（必须是 JSON true）')
    }

    $fatalErrorFound = Get-JsonField -Object $evidence -Name 'fatalErrorFound'
    if (($fatalErrorFound -is [bool]) -and (-not $fatalErrorFound)) {
        Write-GateCheck -Name 'fatalErrorFound=false' -Passed $true -Detail ''
    }
    else {
        Write-GateCheck -Name 'fatalErrorFound=false' -Passed $false -Detail ('实际: ' + (Format-JsonValue -Value $fatalErrorFound) + '（必须是 JSON false）')
    }
}

# -----------------------------------------------------------------------------
# Gate 2：六游戏 evidence
# -----------------------------------------------------------------------------

function Test-SixGameEvidence {
    param([string]$EvidencePath)

    Write-Output ''
    Write-Output '--- Gate 2: 六游戏 evidence ---'

    $evidenceExists = Test-Path -LiteralPath $EvidencePath -PathType Leaf
    Write-GateCheck -Name '六游戏 evidence JSON 存在' -Passed $evidenceExists -Detail $EvidencePath
    if (-not $evidenceExists) {
        Write-Output '       缺少: 6 条记录，每条需 gameId/profile/baseProfile/packIds/result/keyOutput/logPath，且 result=PASS、logPath 文件存在'
        return
    }

    $evidence = $null
    try {
        $evidence = Read-Utf8Text -Path $EvidencePath | ConvertFrom-Json
    }
    catch {
        Write-GateCheck -Name '六游戏 evidence JSON 可解析' -Passed $false -Detail ('解析失败: ' + $_.Exception.Message)
        return
    }
    if ($null -eq $evidence) {
        Write-GateCheck -Name '六游戏 evidence JSON 可解析' -Passed $false -Detail 'JSON 为空'
        return
    }
    Write-GateCheck -Name '六游戏 evidence JSON 可解析' -Passed $true -Detail ''

    $records = @()
    if ($evidence -is [array]) {
        $records = @($evidence)
    }
    else {
        $gamesValue = Get-JsonField -Object $evidence -Name 'games'
        if ($null -eq $gamesValue) {
            Write-GateCheck -Name '六游戏 evidence 结构' -Passed $false -Detail '顶层必须是数组，或对象包含 games 数组'
            return
        }
        $records = @($gamesValue)
    }

    Write-GateCheck -Name '六游戏记录数 = 6' -Passed ($records.Count -eq 6) -Detail ('实际: {0}' -f $records.Count)

    $evidenceDirectory = Split-Path -Parent $EvidencePath
    $seenGameIds = @{}
    $index = 0
    foreach ($record in $records) {
        $index++
        if ($null -eq $record) {
            Write-GateCheck -Name ('六游戏[{0}/{1}] <null>' -f $index, $records.Count) -Passed $false -Detail '记录为 null（JSON 数组含空元素）'
            continue
        }
        $issues = @()
        $gameId = [string](Get-JsonField -Object $record -Name 'gameId')
        $gameProfile = [string](Get-JsonField -Object $record -Name 'profile')
        $baseProfileValue = [string](Get-JsonField -Object $record -Name 'baseProfile')
        $packIdsValue = Get-JsonField -Object $record -Name 'packIds'
        $resultValue = [string](Get-JsonField -Object $record -Name 'result')
        $keyOutput = [string](Get-JsonField -Object $record -Name 'keyOutput')
        $logPathRaw = [string](Get-JsonField -Object $record -Name 'logPath')

        foreach ($fieldName in @('gameId', 'profile', 'baseProfile', 'keyOutput', 'logPath', 'planHash')) {
            $fieldValue = Get-JsonField -Object $record -Name $fieldName
            if (-not (Test-NonEmptyText -Value $fieldValue)) {
                $issues += ('缺少/空字段 ' + $fieldName)
            }
        }

        $packIdsProperty = $record.PSObject.Properties['packIds']
        if ($null -eq $packIdsProperty) {
            $issues += '缺少字段 packIds'
        }
        elseif ($null -eq $packIdsValue) {
            # Windows PowerShell 5.1 的 ConvertFrom-Json 把 JSON [] 折叠成 $null；
            # 纯基线会话可以没有包，按空列表接受（字段存在性已单独检查）。
            $packIdsValue = @()
        }
        elseif ($packIdsValue -is [string]) {
            # PowerShell 5.1 会把单元素 JSON 数组折叠成字符串；按单包列表处理。
            if (-not (Test-NonEmptyText -Value $packIdsValue)) {
                $issues += 'packIds 含空值'
            }
        }
        elseif (-not ($packIdsValue -is [array])) {
            $issues += 'packIds 必须是数组'
        }
        else {
            foreach ($packId in @($packIdsValue)) {
                if (-not (Test-NonEmptyText -Value ([string]$packId))) {
                    $issues += 'packIds 含空值'
                }
            }
        }

        if ([string]::IsNullOrWhiteSpace($resultValue)) {
            $issues += '缺少/空字段 result'
        }
        elseif ($resultValue -cne 'PASS') {
            # 文档口径：只接受字面量大写 PASS；PowerShell 的 -ne 不区分大小写，
            # 必须用 -cne，否则 "pass" 会被误判为通过（review Minor #3）。
            $issues += ('result 必须为字面量大写 PASS（实际: {0}）' -f $resultValue)
        }

        $planHashRaw = [string](Get-JsonField -Object $record -Name 'planHash')
        if ((-not [string]::IsNullOrWhiteSpace($planHashRaw)) -and ($planHashRaw -notmatch '^[0-9a-f]{64}$')) {
            $issues += ('planHash 必须是 64 位小写十六进制: ' + $planHashRaw)
        }

        if (-not [string]::IsNullOrWhiteSpace($logPathRaw)) {
            $logPath = Resolve-GatePath -PathValue $logPathRaw -BaseDirectory $evidenceDirectory
            if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
                $issues += ('logPath 文件不存在: ' + $logPath)
            }
            else {
                $logText = $null
                try {
                    $logInfo = Get-Item -LiteralPath $logPath
                    if ($logInfo.Length -le 0) {
                        $issues += ('logPath 文件为空: ' + $logPath)
                    }
                    elseif ($planHashRaw -match '^[0-9a-f]{64}$') {
                        # M12：日志若记录了 plan 哈希，必须与 evidence 的 planHash 一致
                        #（spec §9 Gate 2 要求记录 hash；runner 日志没有该行时只做格式校验）。
                        $logText = Read-Utf8Text -Path $logPath
                        $gamePlanMatches = [regex]::Matches(
                            $logText, '\[LOAD\]\s+CompatibilityPlan=(?<hash>[0-9a-f]{64})(?:\s|$)')
                        if ($gamePlanMatches.Count -gt 0) {
                            $matchedPlan = @($gamePlanMatches | Where-Object {
                                [string]::Equals($_.Groups['hash'].Value, $planHashRaw, [StringComparison]::Ordinal)
                            })
                            if ($matchedPlan.Count -eq 0) {
                                $issues += ('planHash 与日志中的 CompatibilityPlan 不一致: ' + $planHashRaw)
                            }
                        }
                    }
                }
                catch {
                    $issues += ('logPath 无法读取: ' + $_.Exception.Message)
                }
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($gameId)) {
            $gameKey = $gameId.ToLowerInvariant()
            if ($seenGameIds.ContainsKey($gameKey)) {
                $issues += ('gameId 重复: ' + $gameId)
            }
            else {
                $seenGameIds[$gameKey] = $true
            }
        }

        $displayId = $gameId
        if ([string]::IsNullOrWhiteSpace($displayId)) { $displayId = '<无 gameId>' }
        $checkName = ('六游戏[{0}/{1}] {2}' -f $index, $records.Count, $displayId)
        if ($issues.Count -eq 0) {
            $packDisplay = @($packIdsValue) -join ','
            Write-GateCheck -Name $checkName -Passed $true -Detail ("profile={0} baseProfile={1} packIds=[{2}] result=PASS" -f $gameProfile, $baseProfileValue, $packDisplay)
        }
        else {
            Write-GateCheck -Name $checkName -Passed $false -Detail ($issues -join ' ; ')
        }
    }
}

# -----------------------------------------------------------------------------
# 门禁自测（-SelfTest）：用提交在案的合成夹具验证 Gate 1 判定逻辑本身
# -----------------------------------------------------------------------------
# 依据 spec §9 Gate 1 与 final whole-branch review I2：发布门禁是"发布硬门"，它的
# 判定逻辑本身必须有可重复的负向证据。夹具位于 tools/compat-pack/gate-self-test/
# （fake.apk + 每例一份 android-evidence.json 与日志），只验证脚本逻辑，**不是**发布证据。
# 覆盖：合法 PASS、0 Errors 不误伤、合法 fallback PASS、2 Errors、Failures: 1、
# terminated、aborted、缺 plan 哈希、fallback 后续失败。

function Invoke-GateSelfTest {
    param([string]$FixtureRootOverride = '')
    $fixtureRoot = Join-Path $PSScriptRoot 'gate-self-test'
    if (-not [string]::IsNullOrWhiteSpace($FixtureRootOverride)) {
        $fixtureRoot = $FixtureRootOverride
    }
    if (-not (Test-Path -LiteralPath $fixtureRoot -PathType Container)) {
        Write-Output ("[FAIL] 门禁自测夹具目录不存在: {0}" -f $fixtureRoot)
        # B（final fix review 新破坏 2）：夹具级失败必须让 -SelfTest 退出码为 1，不能只
        # 累加 FailureCount 后打印 PASS。SelfTestFailures 与 FailureCount 同时累加，
        # 主分支对两个计数统一判定（fail-closed）。
        $script:FailureCount++
        $script:SelfTestFailures++
        return
    }
    $cases = @(
        [pscustomobject]@{ Name = '合法 PASS'; Dir = 'pass'; ExpectPass = $true; ExpectLabel = '' },
        [pscustomobject]@{ Name = '0 Errors 不误伤'; Dir = 'pass-zero-errors'; ExpectPass = $true; ExpectLabel = '' },
        [pscustomobject]@{ Name = '合法 fallback PASS'; Dir = 'pass-fallback'; ExpectPass = $true; ExpectLabel = '' },
        [pscustomobject]@{ Name = '2 Errors 必须 FAIL'; Dir = 'fail-errors'; ExpectPass = $false; ExpectLabel = '非零错误计数' },
        [pscustomobject]@{ Name = 'Failures: 1 必须 FAIL'; Dir = 'fail-failures'; ExpectPass = $false; ExpectLabel = '非零错误计数' },
        [pscustomobject]@{ Name = 'terminated 必须 FAIL'; Dir = 'fail-terminated'; ExpectPass = $false; ExpectLabel = '成功回退后仍出现失败/异常' },
        [pscustomobject]@{ Name = 'aborted 必须 FAIL'; Dir = 'fail-aborted'; ExpectPass = $false; ExpectLabel = '成功回退后仍出现失败/异常' },
        [pscustomobject]@{ Name = '缺 plan 哈希必须 FAIL'; Dir = 'fail-no-plan-hash'; ExpectPass = $false; ExpectLabel = 'plan 哈希' },
        [pscustomobject]@{ Name = 'fallback 后续失败必须 FAIL'; Dir = 'fail-fallback-then-failure'; ExpectPass = $false; ExpectLabel = '成功回退后仍出现失败/异常' }
    )
    foreach ($case in $cases) {
        $evidencePath = Join-Path (Join-Path $fixtureRoot $case.Dir) 'android-evidence.json'
        if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) {
            Write-Output ("[FAIL] 自测夹具缺失: {0}" -f $evidencePath)
            # 同上：case 级夹具缺失也计入 SelfTestFailures，否则缺失证据会假绿。
            $script:FailureCount++
            $script:SelfTestFailures++
            continue
        }
        $failureBefore = $script:FailureCount
        $captured = @(Test-AndroidEvidence -EvidencePath $evidencePath)
        $actualFailed = ($script:FailureCount -gt $failureBefore)
        $expectedText = 'PASS'
        if (-not $case.ExpectPass) { $expectedText = 'FAIL' }
        $actualText = 'PASS'
        if ($actualFailed) { $actualText = 'FAIL' }
        $caseOk = ($actualFailed -eq (-not $case.ExpectPass))
        if ($caseOk -and (-not $case.ExpectPass) -and (-not [string]::IsNullOrWhiteSpace($case.ExpectLabel))) {
            $caseOk = (($captured -join "`n").IndexOf($case.ExpectLabel, [StringComparison]::Ordinal) -ge 0)
        }
        # 证据检查在"预期 FAIL"的用例里必然累加 FailureCount；自测的判据是"是否符合预期"，
        # 因此逐例回滚计数，另用 SelfTestFailures 统计不符合预期的用例。
        $script:FailureCount = $failureBefore
        if ($caseOk) {
            Write-Output ("[PASS] 自测: {0}（期望 {1}）" -f $case.Name, $expectedText)
        }
        else {
            $script:SelfTestFailures++
            Write-Output ("[FAIL] 自测: {0}（期望 {1}，实际 {2}）" -f $case.Name, $expectedText, $actualText)
            if (-not [string]::IsNullOrWhiteSpace($case.ExpectLabel)) {
                Write-Output ("       期望失败标签: {0}" -f $case.ExpectLabel)
            }
            foreach ($capturedLine in @($captured | Where-Object { $_ -match '^\[FAIL\]' })) {
                Write-Output ("       | {0}" -f $capturedLine)
            }
        }
    }
    if (-not $SkipSelfTestProbe) {
        Invoke-GateSelfTestMissingCaseProbe -FixtureRoot $fixtureRoot
    }
}

# -----------------------------------------------------------------------------
# 自测的 fail-closed 自检（B）：删掉一个 case 的 evidence 后，-SelfTest 必须 exit 1。
# 复制夹具到临时目录 → 子进程跑 -SelfTest（-SkipSelfTestProbe 防递归）→ 断言退出码为 1
# 且输出含"自测夹具缺失"。只在临时目录操作，不改动入库夹具，也不产生发布证据。
# -----------------------------------------------------------------------------
function Invoke-GateSelfTestMissingCaseProbe {
    param([string]$FixtureRoot)
    $probeParent = Join-Path ([System.IO.Path]::GetTempPath()) ('compatpack-selftest-missing-' + [Guid]::NewGuid().ToString('N'))
    $probeRoot = Join-Path $probeParent 'gate-self-test'
    $output = ''
    $exitCode = -1
    try {
        New-Item -ItemType Directory -Path $probeParent -Force | Out-Null
        Copy-Item -LiteralPath $FixtureRoot -Destination $probeRoot -Recurse -Force
        Remove-Item -LiteralPath (Join-Path (Join-Path $probeRoot 'fail-errors') 'android-evidence.json') -Force
        $scriptPath = $PSCommandPath
        if ([string]::IsNullOrWhiteSpace($scriptPath)) {
            $scriptPath = Join-Path $PSScriptRoot 'Test-CompatPackReleaseGate.ps1'
        }
        $output = (& powershell -NoProfile -ExecutionPolicy Bypass -File $scriptPath -SelfTest -SelfTestFixtureRoot $probeRoot -SkipSelfTestProbe 2>&1 | Out-String)
        $exitCode = $LASTEXITCODE
    }
    catch {
        $output = $_.Exception.Message
        $exitCode = -1
    }
    finally {
        Remove-Item -LiteralPath $probeParent -Recurse -Force -ErrorAction SilentlyContinue
    }
    $failClosed = ($exitCode -eq 1) -and ($output.IndexOf('自测夹具缺失', [StringComparison]::Ordinal) -ge 0) -and ($output.IndexOf('=== 结果: FAIL', [StringComparison]::Ordinal) -ge 0)
    if ($failClosed) {
        Write-Output '[PASS] 自测: case evidence 缺失 → exit 1（fail-closed 生效）'
    }
    else {
        $script:SelfTestFailures++
        Write-Output ("[FAIL] 自测: case evidence 缺失未按 fail-closed 退出（exit={0}）" -f $exitCode)
        foreach ($line in @($output -split "\r?\n" | Select-Object -First 20)) {
            Write-Output ("       | {0}" -f $line)
        }
    }
}

# -----------------------------------------------------------------------------
# 主流程
# -----------------------------------------------------------------------------

if ($SelfTest) {
    Write-Output '=== CompatPack 发布门禁自测（合成夹具，不是发布证据） ==='
    $script:SelfTestFailures = 0
    Invoke-GateSelfTest -FixtureRootOverride $SelfTestFixtureRoot
    Write-Output ''
    # B：case/夹具级失败同时累加 SelfTestFailures 与 FailureCount；两个计数任一非零都
    # 必须 exit 1（避免"打印 [FAIL] 却 PASS"的假绿）。
    if (($script:SelfTestFailures -gt 0) -or ($script:FailureCount -gt 0)) {
        Write-Output ("=== 结果: FAIL（门禁自测 {0} 项不符合预期，累计失败 {1}） ===" -f $script:SelfTestFailures, $script:FailureCount)
        exit 1
    }
    Write-Output '=== 结果: PASS（门禁自测全部符合预期） ==='
    exit 0
}

if (-not (Test-Path -LiteralPath $ProjectRoot -PathType Container)) {
    Write-Output ("[FAIL] 项目根不存在: {0}" -f $ProjectRoot)
    exit 1
}
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
Set-Location -LiteralPath $ProjectRoot

$modeParts = @('桌面门禁')
if ($RequireAndroid) { $modeParts += 'Android evidence' }
if ($RequireSixGame) { $modeParts += '六游戏 evidence' }

Write-Output '=== CompatPack v1 发布门禁 ==='
Write-Output ("项目根: {0}" -f $ProjectRoot)
Write-Output ('模式: ' + ($modeParts -join ' + '))

# 证据档先校验证据；失败不跑桌面门禁（避免耗时且绝不假绿）。
if ($RequireAndroid) {
    $androidEvidence = Resolve-EvidenceJsonPath -RawPath $AndroidEvidencePath -DefaultRelativePath 'reports\compatpack\android-evidence.json' -DefaultFileName 'android-evidence.json'
    Test-AndroidEvidence -EvidencePath $androidEvidence
}
if ($RequireSixGame) {
    $sixGameEvidence = Resolve-EvidenceJsonPath -RawPath $SixGameEvidencePath -DefaultRelativePath 'reports\compatpack\six-game-evidence.json' -DefaultFileName 'six-game-evidence.json'
    Test-SixGameEvidence -EvidencePath $sixGameEvidence
}
if ($script:FailureCount -gt 0) {
    Write-Output ''
    Write-Output ("=== 结果: FAIL（证据校验失败 {0} 项；桌面门禁未执行，避免假绿） ===" -f $script:FailureCount)
    exit 1
}

# ---- 第一方包 staging 完整性（执行设计 §6.1/§6.2：防止导出空包） ----
# 导出/发布前必须已跑 tools/compat-pack/Build-FirstPartyPacks.ps1；本检查逐包核对
# <ProjectRoot>/compat_packs/<packId>/<packId>.dll 存在、非空、一包一目录，且集合与
# assets/text/pack-profile-map.json 的第一方 packId 完全一致。staging 不入库，
# 缺失即 FAIL（这是有意的：桌面门禁通过不替代真机证据，也不允许静默导出无包 APK）。
if (-not $SelfTest) {
    $stagingRoot = Join-Path $ProjectRoot 'compat_packs'
    $mapPath = Join-Path $ProjectRoot 'assets/text/pack-profile-map.json'
    $stagingIssues = New-Object System.Collections.Generic.List[string]
    $stagingPackIds = @()
    if (-not (Test-Path -LiteralPath $mapPath -PathType Leaf)) {
        $stagingIssues.Add('缺少 profile 映射：' + $mapPath)
    }
    else {
        try {
            $map = Get-Content -LiteralPath $mapPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $stagingPackIds = @(@($map.profiles) |
                ForEach-Object { [string]$_.packId } |
                Where-Object { $_ -like 'pack.gemuera.*' } |
                Sort-Object -Unique)
        }
        catch {
            $stagingIssues.Add('profile 映射无法解析：' + $_.Exception.Message)
        }
    }
    if ($stagingPackIds.Count -eq 0 -and $stagingIssues.Count -eq 0) {
        $stagingIssues.Add('profile 映射未声明任何第一方包。')
    }
    if (-not (Test-Path -LiteralPath $stagingRoot -PathType Container)) {
        $stagingIssues.Add('staging 根不存在（先跑 tools/compat-pack/Build-FirstPartyPacks.ps1）：' + $stagingRoot)
    }
    else {
        $actualDirs = @(Get-ChildItem -LiteralPath $stagingRoot -Directory -ErrorAction SilentlyContinue |
            ForEach-Object { $_.Name } | Sort-Object)
        foreach ($packId in $stagingPackIds) {
            if ($actualDirs -notcontains $packId) {
                $stagingIssues.Add('staging 缺目录：' + $packId)
                continue
            }
            $dir = Join-Path $stagingRoot $packId
            $files = @(Get-ChildItem -LiteralPath $dir -File -ErrorAction SilentlyContinue)
            $expected = $packId + '.dll'
            if ($files.Count -ne 1 -or $files[0].Name -ne $expected) {
                $stagingIssues.Add('staging 目录形态错误：' + $packId)
                continue
            }
            if ($files[0].Length -le 0) {
                $stagingIssues.Add('staging DLL 为空：' + $packId)
            }
        }
        foreach ($dir in $actualDirs) {
            if ($stagingPackIds -notcontains $dir) {
                $stagingIssues.Add('staging 含映射外目录：' + $dir)
            }
        }
    }
    Write-GateCheck -Name '第一方包 staging 完整（compat_packs/<packId>/<packId>.dll）' -Passed ($stagingIssues.Count -eq 0) -Detail (($stagingIssues -join ' ; '))
}

# ---- 桌面门禁序列（顺序固定；任一失败立即停止） ----
$coreProject = Join-Path $ProjectRoot 'tests\xUnitTest\GEmuera.Core.Tests\GEmuera.Core.Tests.csproj'
$facadeProject = Join-Path $ProjectRoot 'tests\xUnitTest\EmueraFacade.Tests\EmueraFacade.Tests.csproj'
$abiProject = Join-Path $ProjectRoot 'tests\xUnitTest\EmueraPluginAbi.Tests\EmueraPluginAbi.Tests.csproj'
$surfaceSmoke = Join-Path $ProjectRoot 'tools\dialect-inventory\LegacyDialectSurfaceSmoke'
$runtimeSmoke = Join-Path $ProjectRoot 'tools\dialect-inventory\LegacyDialectRuntimeSmoke'
$coreContractSmoke = Join-Path $ProjectRoot 'tools\core-contracts\CoreContractSmoke.csproj'
$lifecycleGuard = Join-Path $ProjectRoot 'tools\compat-pack\Test-CompatPackLifecycle.ps1'

$desktopSteps = @(
    [pscustomobject]@{
        Name                     = 'Core xUnit'
        Display                  = 'dotnet test tests\xUnitTest\GEmuera.Core.Tests\GEmuera.Core.Tests.csproj -c Release'
        FilePath                 = 'dotnet'
        Arguments                = @('test', $coreProject, '-c', 'Release')
        RollForward              = ''
        AllowLatestMajorFallback = $false
    },
    [pscustomobject]@{
        Name                     = 'Facade xUnit'
        Display                  = 'dotnet test tests\xUnitTest\EmueraFacade.Tests\EmueraFacade.Tests.csproj -c Release'
        FilePath                 = 'dotnet'
        Arguments                = @('test', $facadeProject, '-c', 'Release')
        RollForward              = ''
        AllowLatestMajorFallback = $false
    },
    [pscustomobject]@{
        Name                     = 'Plugin ABI xUnit'
        Display                  = 'dotnet test tests\xUnitTest\EmueraPluginAbi.Tests\EmueraPluginAbi.Tests.csproj -c Release'
        FilePath                 = 'dotnet'
        Arguments                = @('test', $abiProject, '-c', 'Release')
        RollForward              = ''
        AllowLatestMajorFallback = $false
    },
    [pscustomobject]@{
        Name                     = 'LegacyDialectSurfaceSmoke'
        Display                  = 'dotnet run --project tools\dialect-inventory\LegacyDialectSurfaceSmoke -c Release'
        FilePath                 = 'dotnet'
        Arguments                = @('run', '--project', $surfaceSmoke, '-c', 'Release')
        RollForward              = ''
        AllowLatestMajorFallback = $false
    },
    [pscustomobject]@{
        Name                     = 'LegacyDialectRuntimeSmoke'
        Display                  = 'dotnet run --project tools\dialect-inventory\LegacyDialectRuntimeSmoke -c Release'
        FilePath                 = 'dotnet'
        Arguments                = @('run', '--project', $runtimeSmoke, '-c', 'Release')
        RollForward              = ''
        AllowLatestMajorFallback = $false
    },
    [pscustomobject]@{
        Name                     = 'CoreContractSmoke'
        Display                  = '$env:DOTNET_ROLL_FORWARD=''Major''; dotnet run --project tools\core-contracts\CoreContractSmoke.csproj -c Release'
        FilePath                 = 'dotnet'
        Arguments                = @('run', '--project', $coreContractSmoke, '-c', 'Release')
        RollForward              = 'Major'
        AllowLatestMajorFallback = $true
    },
    [pscustomobject]@{
        Name                     = 'CompatPackLifecycleGuard'
        Display                  = 'powershell -NoProfile -ExecutionPolicy Bypass -File tools\compat-pack\Test-CompatPackLifecycle.ps1 -ProjectRoot .'
        FilePath                 = 'powershell'
        Arguments                = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $lifecycleGuard, '-ProjectRoot', $ProjectRoot)
        RollForward              = ''
        AllowLatestMajorFallback = $false
    }
)

$previousRollForward = $env:DOTNET_ROLL_FORWARD
$desktopFailure = $false
foreach ($step in $desktopSteps) {
    Write-Output ''
    Write-Output ('[RUN ] ' + $step.Display)
    $started = Get-Date
    if (-not [string]::IsNullOrWhiteSpace($step.RollForward)) {
        $env:DOTNET_ROLL_FORWARD = $step.RollForward
    }

    $result = Invoke-CapturedCommand -FilePath $step.FilePath -ArgumentList $step.Arguments
    $note = ''
    if ($result.ExitCode -ne 0 -and $step.AllowLatestMajorFallback) {
        Write-Output '[WARN] DOTNET_ROLL_FORWARD=Major 执行失败，按本机运行时改用 LatestMajor 重试（若仍失败则记 FAIL）'
        $env:DOTNET_ROLL_FORWARD = 'LatestMajor'
        $retry = Invoke-CapturedCommand -FilePath $step.FilePath -ArgumentList $step.Arguments
        if ($retry.ExitCode -eq 0) {
            $note = 'Major 失败，LatestMajor 通过'
        }
        else {
            $note = 'Major 与 LatestMajor 均失败'
        }
        $result = $retry
    }
    if (-not [string]::IsNullOrWhiteSpace($step.RollForward)) {
        if ([string]::IsNullOrEmpty($previousRollForward)) {
            Remove-Item Env:\DOTNET_ROLL_FORWARD -ErrorAction SilentlyContinue
        }
        else {
            $env:DOTNET_ROLL_FORWARD = $previousRollForward
        }
    }

    $seconds = [int]((Get-Date) - $started).TotalSeconds
    $summary = Get-OutputSummary -Lines $result.Lines
    if ($result.ExitCode -eq 0) {
        $script:DesktopPassCount++
        $script:CheckCount++
        Write-Output ("[PASS] {0} (exit=0, {1}s)" -f $step.Name, $seconds)
        if (-not [string]::IsNullOrWhiteSpace($note)) {
            Write-Output ('       note: ' + $note)
        }
        Write-Output ('       summary: ' + $summary)
    }
    else {
        $script:FailureCount++
        $script:CheckCount++
        Write-Output ("[FAIL] {0} (exit={1}, {2}s)" -f $step.Name, $result.ExitCode, $seconds)
        Write-Output ('       command: ' + $step.Display)
        if (-not [string]::IsNullOrWhiteSpace($note)) {
            Write-Output ('       note: ' + $note)
        }
        Write-Output ('       summary: ' + $summary)
        Write-Output '       output tail:'
        Write-CommandOutputTail -Lines $result.Lines
        $desktopFailure = $true
        break
    }
}

Write-Output ''
if ($desktopFailure -or $script:FailureCount -gt 0) {
    Write-Output ("=== 结果: FAIL（{0} 项失败 / {1} 项检查；桌面命令 {2}/{3} 通过） ===" -f $script:FailureCount, $script:CheckCount, $script:DesktopPassCount, $desktopSteps.Count)
    exit 1
}

Write-Output ("=== 结果: PASS（{0} 项检查；桌面命令 {1}/{2} 通过） ===" -f $script:CheckCount, $script:DesktopPassCount, $desktopSteps.Count)
exit 0
