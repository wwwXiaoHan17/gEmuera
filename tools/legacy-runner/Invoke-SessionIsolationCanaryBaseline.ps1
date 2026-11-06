[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [string]$ProjectRoot = (Get-Location).Path,
    [string]$ConfigPath,
    [Parameter(Mandatory = $true)][string]$GameRoot,
    [ValidateSet('v24pure', 'v18', 'megaten', 'erablue', 'erafl', 'snake')][string]$Profile = 'v24pure',
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateRange(1, 10)][int]$RepeatCount = 1,
    [ValidateRange(5, 900)][int]$TimeoutSeconds = 360,
    [switch]$SkipBuild,
    [switch]$UseDisplayServer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8NoBom = New-Object Text.UTF8Encoding($false)

if (-not $ConfigPath) {
    $ConfigPath = Join-Path $PSScriptRoot 'fixtures\first-wait.json'
}

function Write-CanaryJson {
    param($Value, [string]$Path)
    [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 30) + "`n"), $utf8NoBom)
}

function Set-CanaryConfigProperty {
    param($Config, [string]$Name, $Value)
    $Config | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force
}

function Get-RunDiagnosticCanaryEvidence {
    param([string]$RunDirectory, [bool]$ExpectedCanary)
    $diagnosticsPath = Join-Path $RunDirectory 'diagnostics.json'
    if (-not (Test-Path -LiteralPath $diagnosticsPath -PathType Leaf)) {
        throw "missing_diagnostics:$diagnosticsPath"
    }
    $text = [IO.File]::ReadAllText($diagnosticsPath)
    $observed = $text.IndexOf('M1_SESSION_ISOLATION_CANARY', [StringComparison]::Ordinal) -ge 0
    if ($observed -ne $ExpectedCanary) {
        throw "unexpected_canary_diagnostic:expected=$ExpectedCanary observed=$observed run=$RunDirectory"
    }
    return $observed
}

try {
    $project = (Resolve-Path -LiteralPath $ProjectRoot).Path
    $game = (Resolve-Path -LiteralPath $GameRoot).Path
    $baseConfigPath = (Resolve-Path -LiteralPath $ConfigPath).Path
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $output) {
        if (Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1) {
            throw "Output directory must be absent or empty: $output"
        }
    }
    [IO.Directory]::CreateDirectory($output) | Out-Null

    $runner = Join-Path $PSScriptRoot 'Invoke-LegacyRunner.ps1'
    $sequence = @(
        [ordered]@{ label = 'baseline-a'; mode = 'baseline' },
        [ordered]@{ label = 'canary'; mode = 'canary' },
        [ordered]@{ label = 'baseline-b'; mode = 'baseline' }
    )
    $identityDirectory = ''
    $results = @()
    for ($index = 0; $index -lt $sequence.Count; $index++) {
        $entry = $sequence[$index]
        $config = Get-Content -LiteralPath $baseConfigPath -Raw | ConvertFrom-Json
        Set-CanaryConfigProperty $config 'gameRoot' $game
        Set-CanaryConfigProperty $config 'profile' $Profile
        Set-CanaryConfigProperty $config 'sessionIsolationMode' $entry.mode
        $configPath = Join-Path $output ("runner-" + $entry.label + '.json')
        Write-CanaryJson -Value $config -Path $configPath

        $runOutput = Join-Path $output $entry.label
        $arguments = @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $runner,
            '-GodotPath', $GodotPath,
            '-ProjectRoot', $project,
            '-ConfigPath', $configPath,
            '-GameRoot', $game,
            '-Profile', $Profile,
            '-OutputDirectory', $runOutput,
            '-RepeatCount', $RepeatCount,
            '-TimeoutSeconds', $TimeoutSeconds
        )
        if ($SkipBuild -or $index -gt 0) { $arguments += '-SkipBuild' }
        if ($UseDisplayServer) { $arguments += '-UseDisplayServer' }
        if ($identityDirectory) { $arguments += @('-ExistingIdentityDirectory', $identityDirectory) }
        & powershell @arguments
        $runnerExit = $LASTEXITCODE
        $summaryPath = Join-Path $runOutput 'summary.json'
        if ($runnerExit -ne 0 -or -not (Test-Path -LiteralPath $summaryPath -PathType Leaf)) {
            throw "session_isolation_sequence_failed:label=$($entry.label) exit=$runnerExit"
        }
        $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
        if ($summary.result -ne 'PassedRunnerConsistency' -or -not $summary.semanticReportsConsistent) {
            throw "session_isolation_sequence_inconsistent:label=$($entry.label) result=$($summary.result)"
        }
        if (-not $summary.isolatedGameCopy) {
            throw "session_isolation_requires_isolated_game_copy:label=$($entry.label)"
        }
        if ($summary.sessionIsolationMode -ne $entry.mode) {
            throw "session_isolation_mode_not_recorded:label=$($entry.label) expected=$($entry.mode) actual=$($summary.sessionIsolationMode)"
        }
        $runHashes = @($summary.runs | ForEach-Object { [string]$_.semanticSha256 })
        if ($runHashes.Count -ne $RepeatCount -or @($runHashes | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -ne 0) {
            throw "session_isolation_semantic_hash_missing:label=$($entry.label)"
        }
        $canaryObserved = @()
        $runtimeFixtureMutationChangeCounts = @()
        foreach ($run in @($summary.runs)) {
            $runDirectory = Join-Path $runOutput ('run-{0:D3}' -f [int]$run.run)
            $canaryObserved += Get-RunDiagnosticCanaryEvidence -RunDirectory $runDirectory -ExpectedCanary ($entry.mode -eq 'canary')
            $mutationPath = Join-Path $runDirectory 'fixture-mutations.json'
            if (-not (Test-Path -LiteralPath $mutationPath -PathType Leaf)) {
                throw "missing_fixture_mutation_report:label=$($entry.label) run=$($run.run)"
            }
            $mutation = Get-Content -LiteralPath $mutationPath -Raw | ConvertFrom-Json
            $runtimeFixtureMutationChangeCounts += [int]$mutation.changeCount
        }
        $results += [ordered]@{
            label = $entry.label
            mode = $entry.mode
            output = $runOutput.Replace('\', '/')
            repeatCount = $RepeatCount
            semanticSha256 = $runHashes[0]
            canaryDiagnosticObserved = @($canaryObserved | Where-Object { $_ }).Count -eq $RepeatCount
            runtimeFixtureMutationChangeCounts = $runtimeFixtureMutationChangeCounts
        }
        if ($index -eq 0) {
            $identityDirectory = Join-Path $runOutput 'identity'
        }
    }

    $semanticHashes = @($results | ForEach-Object { [string]$_.semanticSha256 })
    $semanticEquivalent = @($semanticHashes | Select-Object -Unique).Count -eq 1
    $result = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M1-RUN-CANARY-01'
        executionStatus = 'InProgress'
        gateStatus = 'Blocked'
        blockerCode = 'PreviousGate:M0'
        result = if ($semanticEquivalent) { 'PassedBaselineCanaryBaselineComparison' } else { 'SemanticDifferenceObserved' }
        comparisonScope = 'Independent-process startup/exit comparison only; not an in-process A/B/A session-switch or static-isolation proof.'
        gameRoot = $game.Replace('\', '/')
        profile = $Profile
        repeatCount = $RepeatCount
        semanticEquivalent = $semanticEquivalent
        preparedBy = [Environment]::UserName
        reviewedBy = ''
        approvedBy = ''
        gateDecision = 'NeedsEvidence'
        uncovered = @(
            'Real in-process A/B/A game switching and late completion evidence',
            'All mutable static isolation and 100-switch leak report',
            'Android APK/device canary evidence',
            'M0 archive/signature and M1 gate approval'
        )
        runs = $results
    }
    Write-CanaryJson -Value $result -Path (Join-Path $output 'summary.json')
    $result | ConvertTo-Json -Depth 30
    if (-not $semanticEquivalent) { exit 2 }
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
