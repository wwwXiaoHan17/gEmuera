[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [string]$ProjectRoot = (Get-Location).Path,
    [string]$ConfigPath,
    [Parameter(Mandatory = $true)][string]$GameRoot,
    [ValidateSet('v24pure', 'v18', 'megaten', 'erablue', 'erafl', 'snake')][string]$Profile = 'v24pure',
    [Parameter(Mandatory = $true)][string]$AlternateGameRoot,
    # 退役后 GEMUERA_COMPAT_PACKS 是进程级单包注入：base/alternate 必须解析到同一个 packId。
    # 默认两侧 v24pure 保持可执行；要跨方言请两侧传同一个退役 profile（如 snake/snake）。
    [ValidateSet('v24pure', 'v18', 'megaten', 'erablue', 'erafl', 'snake')][string]$AlternateProfile = 'v24pure',
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateRange(1, 10)][int]$RepeatCount = 1,
    [ValidateRange(2, 100)][int]$SwitchCount = 2,
    [ValidateRange(5, 900)][int]$TimeoutSeconds = 360,
    [switch]$SkipBuild,
    [switch]$UseDisplayServer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8NoBom = New-Object Text.UTF8Encoding($false)

if (-not $ConfigPath) {
    $ConfigPath = Join-Path $PSScriptRoot 'fixtures\session-isolation-inprocess-cross-aba.json'
}

function Write-CrossAbaJson {
    param($Value, [string]$Path)
    [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 30) + "`n"), $utf8NoBom)
}

function Get-CrossAbaRunEvidence {
    param(
        [string]$RunDirectory,
        [string]$ExpectedPrimaryProfile,
        [string]$ExpectedAlternateProfile,
        [bool]$AlternateSharesPrimarySource,
        [int]$ExpectedSwitchCount
    )

    $statePath = Join-Path $RunDirectory 'state.json'
    $timelinePath = Join-Path $RunDirectory 'timeline.json'
    $mutationPath = Join-Path $RunDirectory 'fixture-mutations.json'
    $cycleEvidencePath = Join-Path $RunDirectory 'in-process-session-cycle.json'
    foreach ($path in @($statePath, $timelinePath, $mutationPath, $cycleEvidencePath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "missing_cross_aba_evidence:$path"
        }
    }

    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    if (-not $state.runnerSuccess -or $state.exitReason -ne 'in_process_cross_aba_cycle_completed') {
        throw "cross_aba_cycle_not_completed:reason=$($state.exitReason)"
    }
    if ([int]$state.inProcessSessionSwitchCount -ne $ExpectedSwitchCount) {
        throw "cross_aba_state_switch_count_invalid:actual=$($state.inProcessSessionSwitchCount)"
    }

    $timeline = Get-Content -LiteralPath $timelinePath -Raw | ConvertFrom-Json
    $fingerprintEvents = @($timeline.events | Where-Object { $_.kind -eq 'in_process_session_wait_fingerprint' })
    $expectedSessionCount = $ExpectedSwitchCount + 1
    if ($fingerprintEvents.Count -ne $expectedSessionCount) {
        throw "cross_aba_fingerprint_count_invalid:actual=$($fingerprintEvents.Count)"
    }
    $fingerprints = @()
    foreach ($event in $fingerprintEvents) {
        $data = [string]$event.payload.data
        if ($data -notmatch 'profile=([^ ]+).*sha256=([0-9a-f]{64})') {
            throw "cross_aba_fingerprint_malformed:$data"
        }
        $fingerprints += [ordered]@{ profile = $Matches[1]; sha256 = $Matches[2] }
    }
    for ($index = 0; $index -lt $fingerprints.Count; $index++) {
        $expectedProfile = if (($index % 2) -eq 0) { $ExpectedPrimaryProfile } else { $ExpectedAlternateProfile }
        if ($fingerprints[$index].profile -ne $expectedProfile) {
            throw "cross_aba_profile_sequence_invalid:$($fingerprints | ConvertTo-Json -Compress)"
        }
        if ($index -ge 2 -and $fingerprints[$index].sha256 -ne $fingerprints[$index % 2].sha256) {
            throw "cross_aba_repeated_target_mismatch:index=$index expected=$($fingerprints[$index % 2].sha256) actual=$($fingerprints[$index].sha256)"
        }
    }

    $commitEvents = @($timeline.events | Where-Object { $_.kind -eq 'in_process_session_switch_committed' })
    if ($commitEvents.Count -ne $ExpectedSwitchCount) {
        throw "cross_aba_commit_count_invalid:actual=$($commitEvents.Count)"
    }
    $commitData = @($commitEvents | ForEach-Object { [string]$_.payload.data })
    for ($index = 0; $index -lt $commitData.Count; $index++) {
        $expectedGeneration = $index + 2
        $expectedProfile = if (($index % 2) -eq 0) { $ExpectedAlternateProfile } else { $ExpectedPrimaryProfile }
        if ($commitData[$index] -notmatch ('generation=' + $expectedGeneration + '.*profile=' + [regex]::Escape($expectedProfile))) {
            throw "cross_aba_commit_sequence_invalid:$($commitData -join ' | ')"
        }
    }

    $cycleEvidence = Get-Content -LiteralPath $cycleEvidencePath -Raw | ConvertFrom-Json
    if ([int]$cycleEvidence.switchCount -ne $ExpectedSwitchCount -or [int]$cycleEvidence.sessionCount -ne $expectedSessionCount) {
        throw "cross_aba_cycle_evidence_count_invalid:switches=$($cycleEvidence.switchCount) sessions=$($cycleEvidence.sessionCount)"
    }
    if (@($cycleEvidence.samples).Count -ne $expectedSessionCount) {
        throw "cross_aba_cycle_evidence_samples_invalid:actual=$(@($cycleEvidence.samples).Count)"
    }
    foreach ($sample in @($cycleEvidence.samples)) {
        if ([string]::IsNullOrWhiteSpace([string]$sample.semanticFingerprint) -or
            [long]$sample.managedBytes -lt 0 -or
            [long]$sample.privateBytes -lt 0 -or
            [long]$sample.workingSetBytes -lt 0 -or
            [int]$sample.handleCount -lt 0 -or
            [int]$sample.threadCount -lt 0) {
            throw "cross_aba_cycle_evidence_sample_invalid:ordinal=$($sample.ordinal)"
        }
    }

    $mutation = Get-Content -LiteralPath $mutationPath -Raw | ConvertFrom-Json
    if ([int]$mutation.changeCount -ne 0) {
        throw "cross_aba_primary_fixture_mutated:changeCount=$($mutation.changeCount)"
    }
    $alternateMutationChangeCount = 0
    if (-not $AlternateSharesPrimarySource) {
        $alternateMutationPath = Join-Path $RunDirectory 'alternate-fixture-mutations.json'
        if (-not (Test-Path -LiteralPath $alternateMutationPath -PathType Leaf)) {
            throw "missing_cross_aba_alternate_mutation_evidence:$alternateMutationPath"
        }
        $alternateMutation = Get-Content -LiteralPath $alternateMutationPath -Raw | ConvertFrom-Json
        $alternateMutationChangeCount = [int]$alternateMutation.changeCount
        if ($alternateMutationChangeCount -ne 0) {
            throw "cross_aba_alternate_fixture_mutated:changeCount=$alternateMutationChangeCount"
        }
    }

    return [ordered]@{
        fingerprints = $fingerprints
        switchCommits = $commitData
        lifecycleSamples = @($cycleEvidence.samples)
        primaryFixtureMutationChangeCount = [int]$mutation.changeCount
        alternateFixtureMutationChangeCount = $alternateMutationChangeCount
    }
}

try {
    if (($SwitchCount % 2) -ne 0) {
        throw 'cross-aba requires an even SwitchCount.'
    }
    $project = (Resolve-Path -LiteralPath $ProjectRoot).Path
    $game = (Resolve-Path -LiteralPath $GameRoot).Path
    $alternateGame = (Resolve-Path -LiteralPath $AlternateGameRoot).Path
    $fixtureConfig = (Resolve-Path -LiteralPath $ConfigPath).Path
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $output) {
        if (Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1) {
            throw "Output directory must be absent or empty: $output"
        }
    }
    [IO.Directory]::CreateDirectory($output) | Out-Null

    $config = Get-Content -LiteralPath $fixtureConfig -Raw | ConvertFrom-Json
    $config.gameRoot = $game
    $config.profile = $Profile
    $config.sessionIsolationMode = 'canary'
    $config.inProcessSessionCycle = 'cross-aba'
    $config.inProcessSessionSwitchCount = $SwitchCount
    $config.inProcessAlternateSession.gameRoot = $alternateGame
    $config.inProcessAlternateSession.profile = $AlternateProfile
    $configPathForRun = Join-Path $output 'cross-aba-runner-config.json'
    Write-CrossAbaJson -Value $config -Path $configPathForRun

    $runner = Join-Path $PSScriptRoot 'Invoke-LegacyRunner.ps1'
    $runOutput = Join-Path $output 'runs'
    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $runner,
        '-GodotPath', $GodotPath,
        '-ProjectRoot', $project,
        '-ConfigPath', $configPathForRun,
        '-OutputDirectory', $runOutput,
        '-RepeatCount', $RepeatCount,
        '-TimeoutSeconds', $TimeoutSeconds
    )
    if ($SkipBuild) { $arguments += '-SkipBuild' }
    if ($UseDisplayServer) { $arguments += '-UseDisplayServer' }
    & powershell @arguments
    $runnerExit = $LASTEXITCODE
    $runnerSummaryPath = Join-Path $runOutput 'summary.json'
    if ($runnerExit -ne 0 -or -not (Test-Path -LiteralPath $runnerSummaryPath -PathType Leaf)) {
        throw "cross_aba_runner_failed:exit=$runnerExit"
    }

    $runnerSummary = Get-Content -LiteralPath $runnerSummaryPath -Raw | ConvertFrom-Json
    if ($runnerSummary.result -ne 'PassedRunnerConsistency' -or -not $runnerSummary.semanticReportsConsistent) {
        throw "cross_aba_runner_inconsistent:result=$($runnerSummary.result)"
    }
    if (-not $runnerSummary.isolatedGameCopy -or $runnerSummary.sessionIsolationMode -ne 'canary' -or
        $runnerSummary.inProcessSessionCycle -ne 'cross-aba') {
        throw "cross_aba_runner_config_not_effective:mode=$($runnerSummary.sessionIsolationMode) cycle=$($runnerSummary.inProcessSessionCycle)"
    }
    if ([int]$runnerSummary.inProcessSessionSwitchCount -ne $SwitchCount) {
        throw "cross_aba_runner_switch_count_not_effective:actual=$($runnerSummary.inProcessSessionSwitchCount)"
    }
    if ($null -eq $runnerSummary.alternateSession -or $runnerSummary.alternateSession.baseProfile -ne $AlternateProfile) {
        throw ('cross_aba_runner_alternate_session_not_effective:requested=' + $AlternateProfile + ' reported=' + $runnerSummary.alternateSession.baseProfile)
    }

    $alternateSharesPrimarySource = [bool]$runnerSummary.alternateSession.sharesPrimaryFixtureSource
    $results = @()
    foreach ($run in @($runnerSummary.runs)) {
        $runDirectory = Join-Path $runOutput ('run-{0:D3}' -f [int]$run.run)
        # 指纹/提交事件记录的是引擎 profile（退役请求已翻译为 v24pure）。
        $evidence = Get-CrossAbaRunEvidence -RunDirectory $runDirectory `
            -ExpectedPrimaryProfile ([string]$runnerSummary.profile) `
            -ExpectedAlternateProfile ([string]$runnerSummary.alternateSession.engineProfile) `
            -AlternateSharesPrimarySource $alternateSharesPrimarySource -ExpectedSwitchCount $SwitchCount
        $results += [ordered]@{
            run = [int]$run.run
            semanticSha256 = [string]$run.semanticSha256
            fingerprints = $evidence.fingerprints
            switchCommits = $evidence.switchCommits
            lifecycleSamples = $evidence.lifecycleSamples
            primaryFixtureMutationChangeCount = $evidence.primaryFixtureMutationChangeCount
            alternateFixtureMutationChangeCount = $evidence.alternateFixtureMutationChangeCount
        }
    }

    $semanticHashes = @($results | ForEach-Object { [string]$_.semanticSha256 })
    $semanticEquivalent = $semanticHashes.Count -eq $RepeatCount -and
        @($semanticHashes | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -eq 0 -and
        @($semanticHashes | Select-Object -Unique).Count -eq 1
    $result = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M1-RUN-INPROCESS-CROSS-ABA-01'
        executionStatus = 'InProgress'
        gateStatus = 'Blocked'
        blockerCode = 'PreviousGate:M0'
        result = if ($semanticEquivalent) { 'PassedInProcessCrossAbaObservation' } else { 'SemanticDifferenceObserved' }
        comparisonScope = 'Runner-only A-to-B-to-A cross-configuration observation at first wait; validates pre-registered host game/profile bindings and A recovery, not full static isolation or rollout approval.'
        primaryGameRoot = $game.Replace('\', '/')
        primaryProfile = $Profile
        alternateGameRoot = $alternateGame.Replace('\', '/')
        alternateProfile = $AlternateProfile
        switchCount = $SwitchCount
        repeatCount = $RepeatCount
        semanticEquivalent = $semanticEquivalent
        preparedBy = [Environment]::UserName
        reviewedBy = ''
        approvedBy = ''
        gateDecision = 'NeedsEvidence'
        uncovered = @(
            'Late asynchronous completion, all static roots, and 100-switch leak evidence',
            'Parser/VM handler and typed-policy behavior consumption',
            'Android APK/device canary evidence',
            'M0 archive/signature and M1 gate approval'
        )
        runs = $results
    }
    Write-CrossAbaJson -Value $result -Path (Join-Path $output 'summary.json')
    $result | ConvertTo-Json -Depth 30
    if (-not $semanticEquivalent) { exit 2 }
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
