[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-CanaryContract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

try {
    $configPath = Join-Path $ProjectRoot 'Scripts\LegacyRunner\LegacyRunnerConfig.cs'
    $hostPath = Join-Path $ProjectRoot 'Scripts\LegacyRunner\LegacyRunnerHost.cs'
    $runnerPath = Join-Path $ProjectRoot 'tools\legacy-runner\Invoke-LegacyRunner.ps1'
    $schemaPath = Join-Path $ProjectRoot 'tools\legacy-runner\legacy-runner-config.schema.json'
    $fixturePath = Join-Path $ProjectRoot 'tools\legacy-runner\fixtures\session-isolation-canary.json'
    $comparisonPath = Join-Path $ProjectRoot 'tools\legacy-runner\Invoke-SessionIsolationCanaryBaseline.ps1'
    foreach ($path in @($configPath, $hostPath, $runnerPath, $schemaPath, $fixturePath, $comparisonPath)) {
        Assert-CanaryContract (Test-Path -LiteralPath $path -PathType Leaf) "Session-isolation canary contract file is missing: $path"
    }

    $schema = Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json
    $modeProperty = $schema.properties.sessionIsolationMode
    Assert-CanaryContract ($null -ne $modeProperty) 'Runner schema does not expose sessionIsolationMode.'
    Assert-CanaryContract (@($modeProperty.enum).Count -eq 2) 'Session-isolation mode schema must have exactly two values.'
    foreach ($mode in @('baseline', 'canary')) {
        Assert-CanaryContract (@($modeProperty.enum) -contains $mode) "Session-isolation mode schema is missing '$mode'."
    }

    $configSource = [IO.File]::ReadAllText($configPath)
    Assert-CanaryContract ($configSource.Contains('SessionIsolationMode')) 'Runner config does not model session-isolation mode.'
    Assert-CanaryContract ($configSource.Contains('session_isolation_mode_must_be_baseline_or_canary')) 'Runner config does not fail fast for an unknown session-isolation mode.'
    Assert-CanaryContract ($configSource.Contains('SessionIsolationCanary')) 'Runner config does not expose the normalized canary selection.'

    $hostSource = [IO.File]::ReadAllText($hostPath)
    $overrideIndex = $hostSource.IndexOf('MigrationSessionIsolationEnabled = _config.SessionIsolationCanary;', [StringComparison]::Ordinal)
    $sceneIndex = $hostSource.IndexOf('mainScene.Instantiate()', [StringComparison]::Ordinal)
    Assert-CanaryContract ($overrideIndex -ge 0) 'Runner host does not apply the session-isolation override.'
    Assert-CanaryContract ($overrideIndex -lt $sceneIndex) 'Runner host applies the session-isolation override after main scene creation.'

    $runnerSource = [IO.File]::ReadAllText($runnerPath)
    Assert-CanaryContract ($runnerSource.Contains('sessionIsolationMode = $sessionIsolationMode')) 'Runner summary does not record the requested session-isolation mode.'

    $fixture = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
    Assert-CanaryContract ($fixture.sessionIsolationMode -eq 'canary') 'Canary fixture is not pinned to canary mode.'

    $comparisonSource = [IO.File]::ReadAllText($comparisonPath)
    foreach ($label in @('baseline-a', 'canary', 'baseline-b')) {
        Assert-CanaryContract ($comparisonSource.Contains($label)) "Comparison runner is missing sequence label '$label'."
    }
    Assert-CanaryContract ($comparisonSource.Contains(".IndexOf('M1_SESSION_ISOLATION_CANARY', [StringComparison]::Ordinal)")) 'Comparison runner does not use the PowerShell-compatible canary diagnostic lookup.'
    Assert-CanaryContract ($comparisonSource.Contains('summary.isolatedGameCopy')) 'Comparison runner does not reject a non-isolated game fixture.'
    Assert-CanaryContract ($comparisonSource.Contains('runtimeFixtureMutationChangeCounts')) 'Comparison runner does not retain isolated fixture mutation evidence.'
    Assert-CanaryContract ($comparisonSource.Contains('Independent-process startup/exit comparison only')) 'Comparison report does not state its limited evidence scope.'
    Assert-CanaryContract ($comparisonSource.Contains('PreviousGate:M0')) 'Comparison report does not preserve M1 previous-gate blocking.'

    Write-Output 'M1 session-isolation canary contract tests passed.'
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
