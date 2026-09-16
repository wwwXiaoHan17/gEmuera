[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-DisplayContract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

try {
    $configSchemaPath = Join-Path $ProjectRoot 'tools\legacy-runner\legacy-runner-config.schema.json'
    $displaySchemaPath = Join-Path $ProjectRoot 'tools\legacy-runner\legacy-display.schema.json'
    $hitSchemaPath = Join-Path $ProjectRoot 'tools\legacy-runner\legacy-hit-test.schema.json'
    $hostPath = Join-Path $ProjectRoot 'Scripts\LegacyRunner\LegacyRunnerHost.cs'
    $writerPath = Join-Path $ProjectRoot 'Scripts\LegacyRunner\LegacyRunnerReportWriter.cs'
    $observationPath = Join-Path $ProjectRoot 'Scripts\LegacyRunner\LegacyDisplayObservation.cs'
    $contentPath = Join-Path $ProjectRoot 'Scripts\EmueraContent.cs'
    $displayHarnessPath = Join-Path $ProjectRoot 'tools\legacy-runner\Invoke-LegacyDisplayBaseline.ps1'

    foreach ($path in @($configSchemaPath, $displaySchemaPath, $hitSchemaPath, $hostPath, $writerPath,
            $observationPath, $contentPath, $displayHarnessPath)) {
        Assert-DisplayContract (Test-Path -LiteralPath $path -PathType Leaf) "M0 display contract file is missing: $path"
    }

    $configSchema = Get-Content -LiteralPath $configSchemaPath -Raw | ConvertFrom-Json
    Assert-DisplayContract ($configSchema.properties.displayBackend.enum.Count -eq 2) 'Runner config must expose exactly two legacy display backends.'
    Assert-DisplayContract ($configSchema.properties.displayBackend.enum -contains 'controls') 'Runner config is missing the Controls backend.'
    Assert-DisplayContract ($configSchema.properties.displayBackend.enum -contains 'canvas') 'Runner config is missing the Canvas backend.'
    Assert-DisplayContract ($null -ne $configSchema.properties.captureScreenshot) 'Runner config does not explicitly gate screenshot capture.'
    Assert-DisplayContract ($null -ne $configSchema.properties.viewportWidth -and $null -ne $configSchema.properties.viewportHeight) 'Runner config does not fix viewport dimensions.'

    $displaySchema = Get-Content -LiteralPath $displaySchemaPath -Raw | ConvertFrom-Json
    foreach ($required in @('requestedBackend', 'effectiveBackend', 'backendEvidence', 'screenshot', 'featureCoverage', 'uncovered')) {
        Assert-DisplayContract ($displaySchema.required -contains $required) "Display schema does not require: $required"
    }
    Assert-DisplayContract ($displaySchema.properties.effectiveBackend.enum.Count -eq 2) 'Display report must preserve the effective Controls/Canvas identity.'
    foreach ($coverage in @('div', 'nestedDiv', 'src', 'srcb', 'dynamicMap', 'dataOnly', 'scrollIntent')) {
        Assert-DisplayContract ($displaySchema.properties.featureCoverage.required -contains $coverage) "Display coverage does not expose: $coverage"
    }
    Assert-DisplayContract ($displaySchema.properties.screenshot.required -contains 'sha256') 'Screenshot SHA-256 is not required.'
    Assert-DisplayContract ($displaySchema.properties.screenshot.required -contains 'width') 'Screenshot width is not required.'
    Assert-DisplayContract ($displaySchema.properties.screenshot.required -contains 'height') 'Screenshot height is not required.'

    $hitSchema = Get-Content -LiteralPath $hitSchemaPath -Raw | ConvertFrom-Json
    foreach ($required in @('backend', 'coordinateSpace', 'rect', 'value', 'generation', 'probe', 'matched')) {
        Assert-DisplayContract ($hitSchema.properties.hits.items.required -contains $required) "Hit-test schema does not require: $required"
    }

    $hostSource = [IO.File]::ReadAllText($hostPath)
    Assert-DisplayContract ($hostSource.Contains('RenderingServer.SignalName.FramePostDraw')) 'Screenshot is not synchronized after RenderingServer frame submission.'
    Assert-DisplayContract ($hostSource.Contains('CaptureScreenshot')) 'Runner host does not capture the M0 screenshot.'
    Assert-DisplayContract ($hostSource.Contains('EnsureLegacyViewportSize')) 'Runner host does not keep the configured viewport fixed after legacy resolution setup.'

    $contentSource = [IO.File]::ReadAllText($contentPath)
    Assert-DisplayContract ($contentSource.Contains('ConfigureLegacyRunnerDisplayBackend')) 'The explicit runner-only backend override is missing.'
    Assert-DisplayContract ($contentSource.Contains('CaptureLegacyDisplayObservation')) 'The read-only legacy display observation entry point is missing.'

    $writerSource = [IO.File]::ReadAllText($writerPath)
    Assert-DisplayContract ($writerSource.Contains('screenshots.json')) 'Runner does not write screenshots.json.'
    Assert-DisplayContract ($writerSource.Contains('hit-test.json')) 'Runner does not write hit-test.json.'
    Assert-DisplayContract ($writerSource.Contains('effectiveBackend')) 'Runner display report can lose the effective backend identity.'
    Assert-DisplayContract (-not $writerSource.Contains('CanonicalizeDisplayBackend')) 'Backend identity must never be removed by the normalizer.'

    $displayHarnessSource = [IO.File]::ReadAllText($displayHarnessPath)
    Assert-DisplayContract ($displayHarnessSource.Contains("'controls'")) 'Display harness does not create a Controls report.'
    Assert-DisplayContract ($displayHarnessSource.Contains("'canvas'")) 'Display harness does not create a Canvas report.'
    Assert-DisplayContract ($displayHarnessSource.Contains('effective_backend_mismatch')) 'Display harness does not reject one backend masquerading as the other.'

    Write-Output 'M0 legacy display contract tests passed.'
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
