[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path,
    [string]$CatalogPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$UpstreamRoot,
    [string]$LegacyRoot,
    [string]$GameRoot,
    [string]$EraFlRoot,
    [string]$UpstreamReportDirectory,
    [string]$LegacyReportDirectory,
    [string]$GameReportDirectory,
    [string]$EraFlReportDirectory,
    [switch]$RequireComplete
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
    $projectResolved = (Resolve-Path -LiteralPath $ProjectRoot).Path
    if (-not $CatalogPath) {
        $CatalogPath = Join-Path $projectResolved 'Build\Fixtures\manifest.json'
    }
    if (-not $LegacyRoot) {
        $LegacyRoot = $projectResolved
    }

    Import-Module (Join-Path $PSScriptRoot 'FixtureManifest.psm1') -Force
    $roots = @{}
    $reports = @{}
    foreach ($binding in @(
        @{ Key = 'upstream'; Root = $UpstreamRoot; Report = $UpstreamReportDirectory },
        @{ Key = 'legacy'; Root = $LegacyRoot; Report = $LegacyReportDirectory },
        @{ Key = 'game'; Root = $GameRoot; Report = $GameReportDirectory },
        @{ Key = 'erafl'; Root = $EraFlRoot; Report = $EraFlReportDirectory }
    )) {
        if (-not [string]::IsNullOrWhiteSpace([string]$binding.Root)) {
            $roots[$binding.Key] = [string]$binding.Root
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$binding.Report)) {
            $reports[$binding.Key] = [string]$binding.Report
        }
    }

    $result = Invoke-FixtureManifest -CatalogPath $CatalogPath -OutputDirectory $OutputDirectory `
        -RootBindings $roots -ReportBindings $reports
    $result | ConvertTo-Json -Depth 30
    if ($RequireComplete -and $result.result -ne 'ReadyForReview') {
        exit 2
    }
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
