[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Write-Utf8File {
    param([string]$Path, [string]$Value)
    $parent = Split-Path -Parent $Path
    if ($parent) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
    [IO.File]::WriteAllText($Path, $Value, (New-Object Text.UTF8Encoding($false)))
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-id-' + [Guid]::NewGuid().ToString('N'))
$projectRoot = Join-Path $testRoot 'project'
$gameRoot = Join-Path $testRoot 'game'
$reportsRoot = Join-Path $testRoot 'reports'
$runner = Join-Path $PSScriptRoot 'Invoke-BaselineIdentity.ps1'

try {
    Write-Utf8File -Path (Join-Path $projectRoot 'project.godot') -Value '[application]'
    Write-Utf8File -Path (Join-Path $projectRoot 'gemuera-c#.csproj') -Value '<Project Sdk="Godot.NET.Sdk/4.7.0"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>'
    Write-Utf8File -Path (Join-Path $projectRoot 'config.toml') -Value '[logging]'
    Write-Utf8File -Path (Join-Path $projectRoot 'scripts/main.cs') -Value 'class Main {}'
    Write-Utf8File -Path (Join-Path $projectRoot 'Resources/sample.txt') -Value 'resource'
    Write-Utf8File -Path (Join-Path $projectRoot 'Fonts/sample.ttf') -Value 'font'
    Write-Utf8File -Path (Join-Path $projectRoot '.godot/cache.bin') -Value 'ignored-a'
    Write-Utf8File -Path (Join-Path $gameRoot 'ERB/main.erb') -Value '@SYSTEM_TITLE'
    Write-Utf8File -Path (Join-Path $gameRoot '.godot/game-owned.bin') -Value 'game-byte-source'

    $reportA = Join-Path $reportsRoot 'a'
    $reportB = Join-Path $reportsRoot 'b'
    & powershell -NoProfile -ExecutionPolicy Bypass -File $runner -ProjectRoot $projectRoot -OutputDirectory $reportA -GameRoot $gameRoot | Out-Null
    Assert-True ($LASTEXITCODE -eq 0) 'First identity run failed.'
    Write-Utf8File -Path (Join-Path $projectRoot '.godot/cache.bin') -Value 'ignored-b'
    & powershell -NoProfile -ExecutionPolicy Bypass -File $runner -ProjectRoot $projectRoot -OutputDirectory $reportB -GameRoot $gameRoot | Out-Null
    Assert-True ($LASTEXITCODE -eq 0) 'Second identity run failed.'

    $identityA = Get-Content -LiteralPath (Join-Path $reportA 'identity.json') -Raw | ConvertFrom-Json
    $identityB = Get-Content -LiteralPath (Join-Path $reportB 'identity.json') -Raw | ConvertFrom-Json
    Assert-True ($identityA.schemaVersion -eq '1.0.0') 'Unexpected schema version.'
    Assert-True ($identityA.workPackage -eq 'M0-ID-01') 'Unexpected work package.'
    Assert-True ($identityA.status -eq 'Partial') 'Missing optional evidence must produce Partial.'
    Assert-True ($identityA.source.manifest.canonicalSha256 -eq $identityB.source.manifest.canonicalSha256) 'Excluded cache changed the source tree identity.'
    Assert-True ($identityA.game.fileCount -eq 2) 'Game identity must not apply source-tree exclusions.'

    Write-Utf8File -Path (Join-Path $projectRoot 'scripts/main.cs') -Value 'class Main { int Version = 2; }'
    $reportC = Join-Path $reportsRoot 'c'
    & powershell -NoProfile -ExecutionPolicy Bypass -File $runner -ProjectRoot $projectRoot -OutputDirectory $reportC -GameRoot $gameRoot | Out-Null
    Assert-True ($LASTEXITCODE -eq 0) 'Third identity run failed.'
    $identityC = Get-Content -LiteralPath (Join-Path $reportC 'identity.json') -Raw | ConvertFrom-Json
    Assert-True ($identityA.source.manifest.canonicalSha256 -ne $identityC.source.manifest.canonicalSha256) 'Source mutation did not change the source tree identity.'

    $missingArtifact = Join-Path $testRoot 'missing.apk'
    $previousErrorPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $runner -ProjectRoot $projectRoot -OutputDirectory (Join-Path $reportsRoot 'failure') -ArtifactPath $missingArtifact 2>$null | Out-Null
        $missingArtifactExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorPreference
    }
    Assert-True ($missingArtifactExitCode -ne 0) 'An explicitly missing artifact must return a non-zero exit code.'

    Write-Output 'BaselineIdentity regression tests passed.'
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolvedTest = [IO.Path]::GetFullPath($testRoot).TrimEnd('\') + '\'
    if ($resolvedTest.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
