[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [string]$ProjectRoot = (Get-Location).Path,
    [string]$ConfigPath,
    [Parameter(Mandatory = $true)][string]$GameRoot,
    [ValidateSet('v24pure', 'v18', 'megaten', 'erablue', 'erafl', 'snake')][string]$Profile = 'v24pure',
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateRange(1, 10)][int]$RepeatCount = 3,
    [ValidateRange(5, 900)][int]$TimeoutSeconds = 360,
    [switch]$SkipBuild,
    [switch]$UseDisplayServer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8NoBom = New-Object Text.UTF8Encoding($false)

if (-not $ConfigPath) {
    $ConfigPath = Join-Path $PSScriptRoot 'fixtures\session-isolation-inprocess-aba.json'
}

function Write-InProcessJson {
    param($Value, [string]$Path)
    [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 30) + "`n"), $utf8NoBom)
}

function Get-InProcessRunEvidence {
    param([string]$RunDirectory)

    $statePath = Join-Path $RunDirectory 'state.json'
    $timelinePath = Join-Path $RunDirectory 'timeline.json'
    $mutationPath = Join-Path $RunDirectory 'fixture-mutations.json'
    foreach ($path in @($statePath, $timelinePath, $mutationPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "missing_in_process_evidence:$path"
        }
    }

    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    if (-not $state.runnerSuccess -or $state.exitReason -ne 'in_process_aba_cycle_completed') {
        throw "in_process_cycle_not_completed:reason=$($state.exitReason)"
    }

    $timeline = Get-Content -LiteralPath $timelinePath -Raw | ConvertFrom-Json
    $fingerprintEvents = @($timeline.events | Where-Object { $_.kind -eq 'in_process_session_wait_fingerprint' })
    if ($fingerprintEvents.Count -ne 3) {
        throw "in_process_fingerprint_count_invalid:actual=$($fingerprintEvents.Count)"
    }
    $fingerprintHashes = @()
    foreach ($event in $fingerprintEvents) {
        $data = [string]$event.payload.data
        if ($data -notmatch 'sha256=([0-9a-f]{64})') {
            throw "in_process_fingerprint_malformed:$data"
        }
        $fingerprintHashes += $Matches[1]
    }
    if (@($fingerprintHashes | Select-Object -Unique).Count -ne 1) {
        throw "in_process_fingerprint_mismatch:$($fingerprintHashes -join ',')"
    }

    $commitEvents = @($timeline.events | Where-Object { $_.kind -eq 'in_process_session_switch_committed' })
    if ($commitEvents.Count -ne 2) {
        throw "in_process_commit_count_invalid:actual=$($commitEvents.Count)"
    }
    $commitData = @($commitEvents | ForEach-Object { [string]$_.payload.data })
    if ($commitData[0].IndexOf('generation=2', [StringComparison]::Ordinal) -lt 0 -or
        $commitData[1].IndexOf('generation=3', [StringComparison]::Ordinal) -lt 0) {
        throw "in_process_generation_sequence_invalid:$($commitData -join ' | ')"
    }

    $mutation = Get-Content -LiteralPath $mutationPath -Raw | ConvertFrom-Json
    if ([int]$mutation.changeCount -ne 0) {
        throw "in_process_fixture_mutated:changeCount=$($mutation.changeCount)"
    }

    return [ordered]@{
        fingerprints = $fingerprintHashes
        switchCommits = $commitData
        runtimeFixtureMutationChangeCount = [int]$mutation.changeCount
    }
}

try {
    $project = (Resolve-Path -LiteralPath $ProjectRoot).Path
    $game = (Resolve-Path -LiteralPath $GameRoot).Path
    $config = (Resolve-Path -LiteralPath $ConfigPath).Path
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $output) {
        if (Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1) {
            throw "Output directory must be absent or empty: $output"
        }
    }
    [IO.Directory]::CreateDirectory($output) | Out-Null

    $runner = Join-Path $PSScriptRoot 'Invoke-LegacyRunner.ps1'
    $runOutput = Join-Path $output 'runs'
    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $runner,
        '-GodotPath', $GodotPath,
        '-ProjectRoot', $project,
        '-ConfigPath', $config,
        '-GameRoot', $game,
        '-Profile', $Profile,
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
        throw "in_process_runner_failed:exit=$runnerExit"
    }

    $runnerSummary = Get-Content -LiteralPath $runnerSummaryPath -Raw | ConvertFrom-Json
    if ($runnerSummary.result -ne 'PassedRunnerConsistency' -or -not $runnerSummary.semanticReportsConsistent) {
        throw "in_process_runner_inconsistent:result=$($runnerSummary.result)"
    }
    if (-not $runnerSummary.isolatedGameCopy) {
        throw 'in_process_runner_requires_isolated_game_copy'
    }
    if ($runnerSummary.sessionIsolationMode -ne 'canary' -or $runnerSummary.inProcessSessionCycle -ne 'aba') {
        throw "in_process_runner_config_not_effective:mode=$($runnerSummary.sessionIsolationMode) cycle=$($runnerSummary.inProcessSessionCycle)"
    }

    $results = @()
    foreach ($run in @($runnerSummary.runs)) {
        $runDirectory = Join-Path $runOutput ('run-{0:D3}' -f [int]$run.run)
        $evidence = Get-InProcessRunEvidence -RunDirectory $runDirectory
        $results += [ordered]@{
            run = [int]$run.run
            semanticSha256 = [string]$run.semanticSha256
            fingerprints = $evidence.fingerprints
            switchCommits = $evidence.switchCommits
            runtimeFixtureMutationChangeCount = $evidence.runtimeFixtureMutationChangeCount
        }
    }

    $semanticHashes = @($results | ForEach-Object { [string]$_.semanticSha256 })
    $semanticEquivalent = $semanticHashes.Count -eq $RepeatCount -and
        @($semanticHashes | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -eq 0 -and
        @($semanticHashes | Select-Object -Unique).Count -eq 1
    $result = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M1-RUN-INPROCESS-ABA-01'
        executionStatus = 'InProgress'
        gateStatus = 'Blocked'
        blockerCode = 'PreviousGate:M0'
        result = if ($semanticEquivalent) { 'PassedInProcessAbaObservation' } else { 'SemanticDifferenceObserved' }
        comparisonScope = 'Same configured game restart only; verifies two in-process LegacySessionFacade switches at first wait, not different-game/profile isolation or stale-completion coverage.'
        gameRoot = $game.Replace('\', '/')
        profile = $Profile
        repeatCount = $RepeatCount
        semanticEquivalent = $semanticEquivalent
        preparedBy = [Environment]::UserName
        reviewedBy = ''
        approvedBy = ''
        gateDecision = 'NeedsEvidence'
        uncovered = @(
            'Different-game/profile A-to-B-to-A switching and configuration isolation',
            'Late asynchronous completion, all static roots, and 100-switch leak evidence',
            'Parser/VM handler and typed-policy behavior consumption',
            'Android APK/device canary evidence',
            'M0 archive/signature and M1 gate approval'
        )
        runs = $results
    }
    Write-InProcessJson -Value $result -Path (Join-Path $output 'summary.json')
    $result | ConvertTo-Json -Depth 30
    if (-not $semanticEquivalent) { exit 2 }
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
