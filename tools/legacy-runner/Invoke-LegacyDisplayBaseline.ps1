[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [string]$ProjectRoot = (Get-Location).Path,
    [string]$ConfigPath,
    [Parameter(Mandatory = $true)][string]$GameRoot,
    [ValidateSet('v24pure', 'snake', 'erafl')][string]$Profile = 'v24pure',
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateRange(1, 10)][int]$RepeatCount = 3,
    [ValidateRange(5, 900)][int]$TimeoutSeconds = 360,
    [ValidateRange(320, 7680)][int]$ViewportWidth = 1280,
    [ValidateRange(240, 4320)][int]$ViewportHeight = 720,
    [switch]$Headless,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8NoBom = New-Object Text.UTF8Encoding($false)
if (-not $ConfigPath) {
    $ConfigPath = Join-Path $PSScriptRoot 'fixtures\first-wait.json'
}

function Write-DisplayJson {
    param($Value, [string]$Path)
    [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 30) + "`n"), $utf8NoBom)
}

function Set-ConfigProperty {
    param($Config, [string]$Name, $Value)
    $Config | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force
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
    $backendResults = @()
    $identityDirectory = ''
    $allScreenshotsCaptured = $true
    $allHitTestsCaptured = $true
    foreach ($backend in @('controls', 'canvas')) {
        $config = Get-Content -LiteralPath $baseConfigPath -Raw | ConvertFrom-Json
        Set-ConfigProperty $config 'gameRoot' $game
        Set-ConfigProperty $config 'profile' $Profile
        Set-ConfigProperty $config 'displayBackend' $backend
        Set-ConfigProperty $config 'captureScreenshot' $true
        Set-ConfigProperty $config 'viewportWidth' $ViewportWidth
        Set-ConfigProperty $config 'viewportHeight' $ViewportHeight
        Set-ConfigProperty $config 'displaySettleFrames' 2
        $configPath = Join-Path $output ("runner-$backend.json")
        Write-DisplayJson -Value $config -Path $configPath

        $backendOutput = Join-Path $output $backend
        $arguments = @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $runner,
            '-GodotPath', $GodotPath,
            '-ProjectRoot', $project,
            '-ConfigPath', $configPath,
            '-GameRoot', $game,
            '-Profile', $Profile,
            '-OutputDirectory', $backendOutput,
            '-RepeatCount', $RepeatCount,
            '-TimeoutSeconds', $TimeoutSeconds
        )
        if ($SkipBuild -or $backend -eq 'canvas') { $arguments += '-SkipBuild' }
        if (-not $Headless) { $arguments += '-UseDisplayServer' }
        if ($identityDirectory) { $arguments += @('-ExistingIdentityDirectory', $identityDirectory) }
        & powershell @arguments
        $runnerExit = $LASTEXITCODE
        if ($backend -eq 'controls') {
            $identityDirectory = Join-Path $backendOutput 'identity'
        }

        $backendSummary = Join-Path $backendOutput 'summary.json'
        if ($runnerExit -ne 0 -or -not (Test-Path -LiteralPath $backendSummary -PathType Leaf)) {
            throw "legacy_display_backend_failed:$backend exit=$runnerExit"
        }
        $summary = Get-Content -LiteralPath $backendSummary -Raw | ConvertFrom-Json
        $backendScreenshotStatuses = @()
        $backendHitStatuses = @()
        foreach ($run in $summary.runs) {
            $runOutput = Join-Path $backendOutput ('run-{0:D3}' -f [int]$run.run)
            $displayPath = Join-Path $runOutput 'display.json'
            $display = Get-Content -LiteralPath $displayPath -Raw | ConvertFrom-Json
            if ($display.effectiveBackend -ne $backend -or $display.requestedBackend -ne $backend) {
                throw "effective_backend_mismatch:requested=$backend actual=$($display.effectiveBackend)"
            }
            $screenshotReport = Get-Content -LiteralPath (Join-Path $runOutput 'screenshots.json') -Raw | ConvertFrom-Json
            $hitReport = Get-Content -LiteralPath (Join-Path $runOutput 'hit-test.json') -Raw | ConvertFrom-Json
            $backendScreenshotStatuses += [string]$screenshotReport.coverageStatus
            $backendHitStatuses += [string]$hitReport.coverageStatus
            if ($screenshotReport.coverageStatus -ne 'Captured') { $allScreenshotsCaptured = $false }
            if ($hitReport.coverageStatus -ne 'Captured') { $allHitTestsCaptured = $false }
        }
        $backendResults += [ordered]@{
            backend = $backend
            output = $backendOutput.Replace('\', '/')
            runnerResult = $summary.result
            semanticReportsConsistent = $summary.semanticReportsConsistent
            repeatCount = $summary.repeatCount
            screenshotStatuses = $backendScreenshotStatuses
            hitTestStatuses = $backendHitStatuses
        }
    }

    $result = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-DSP-01'
        executionStatus = 'InProgress'
        gateStatus = 'Blocked'
        blockerCode = 'EvidenceMissing'
        result = if ($allScreenshotsCaptured -and $allHitTestsCaptured) { 'CapturedSeparateLegacyBackends' } else { 'PartialDisplayEvidence' }
        gameRoot = $game.Replace('\', '/')
        profile = $Profile
        viewport = [ordered]@{ width = $ViewportWidth; height = $ViewportHeight }
        preparedBy = [Environment]::UserName
        reviewedBy = ''
        approvedBy = ''
        gateDecision = 'NeedsEvidence'
        compatibility = @('None: default-off observation only')
        uncovered = @(@(
            'Replay-specific div/srcb/dynamic-map/data-only paths not reached remain Uncovered in each display.json',
            $(if (-not $allScreenshotsCaptured) { 'One or more screenshots are Uncovered; headless output is not visual evidence' }),
            $(if (-not $allHitTestsCaptured) { 'One or more hit-test reports are Partial or Uncovered' }),
            'APK and Android device display evidence',
            'Artifact store archival and human review/approval signatures'
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
        backends = $backendResults
    }
    Write-DisplayJson -Value $result -Path (Join-Path $output 'summary.json')
    $result | ConvertTo-Json -Depth 30
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
