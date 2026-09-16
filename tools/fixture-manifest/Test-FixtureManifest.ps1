[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$modulePath = Join-Path $ProjectRoot 'tools\fixture-manifest\FixtureManifest.psm1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-fixture-test-' + [Guid]::NewGuid().ToString('N'))
$utf8NoBom = New-Object Text.UTF8Encoding($false)

function Assert-FixtureContract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Write-TestText {
    param([string]$Path, [string]$Value)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Value, $utf8NoBom)
}

try {
    if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
        throw "Fixture manifest module is missing: $modulePath"
    }
    Import-Module $modulePath -Force

    $versionedCatalogPath = Join-Path $ProjectRoot 'Build\Fixtures\manifest.json'
    Assert-FixtureContract (Test-Path -LiteralPath $versionedCatalogPath -PathType Leaf) 'Versioned fixture catalog is missing.'
    $versionedCatalog = Get-Content -LiteralPath $versionedCatalogPath -Raw | ConvertFrom-Json
    foreach ($layer in @('upstream', 'legacy', 'game')) {
        Assert-FixtureContract (@($versionedCatalog.fixtures | Where-Object layer -eq $layer).Count -gt 0) "Versioned catalog layer is missing: $layer"
    }
    foreach ($fixture in @($versionedCatalog.fixtures | Where-Object { $_.authorization.status -ne 'Verified' })) {
        Assert-FixtureContract (-not $fixture.authorization.redistributionAllowed) "Unverified versioned fixture allows redistribution: $($fixture.id)"
    }
    $versionedResult = Invoke-FixtureManifest -CatalogPath $versionedCatalogPath `
        -OutputDirectory (Join-Path $testRoot 'versioned-output') -RootBindings @{} -ReportBindings @{}
    Assert-FixtureContract ($versionedResult.fixtures.Count -eq $versionedCatalog.fixtures.Count) 'Versioned catalog could not be resolved with missing bindings.'
    Assert-FixtureContract (@($versionedResult.fixtures | Where-Object availabilityStatus -eq 'Uncovered').Count -eq $versionedCatalog.fixtures.Count) 'Missing versioned bindings were not all Uncovered.'

    $rootA = Join-Path $testRoot 'root-a'
    $rootB = Join-Path $testRoot 'root-b'
    Write-TestText -Path (Join-Path $rootA 'b\two.txt') -Value 'two'
    Write-TestText -Path (Join-Path $rootA 'a\one.txt') -Value 'one'
    Write-TestText -Path (Join-Path $rootB 'a\one.txt') -Value 'one'
    Write-TestText -Path (Join-Path $rootB 'b\two.txt') -Value 'two'

    $manifestA = New-FixtureContentManifest -Label 'a' -Root $rootA
    $manifestB = New-FixtureContentManifest -Label 'b' -Root $rootB
    Assert-FixtureContract ($manifestA.canonicalSha256 -eq $manifestB.canonicalSha256) 'Canonical hash depends on enumeration or creation order.'
    Assert-FixtureContract ($manifestA.fileCount -eq 2) 'Content manifest file count is incorrect.'

    Write-TestText -Path (Join-Path $rootB 'b\two.txt') -Value 'changed'
    $changed = New-FixtureContentManifest -Label 'changed' -Root $rootB
    Assert-FixtureContract ($changed.canonicalSha256 -ne $manifestA.canonicalSha256) 'Content hash did not change after fixture bytes changed.'

    $reportRoot = Join-Path $testRoot 'legacy-report'
    Write-TestText -Path (Join-Path $reportRoot 'identity.json') -Value '{"schemaVersion":"1.0.0"}'
    $catalogPath = Join-Path $testRoot 'catalog.json'
    $catalog = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-FIX-01'
        fixtures = @(
            [ordered]@{
                id = 'UP-TEST-001'; layer = 'upstream'; category = 'baseline-runner'; profile = 'Upstream1808'; rootBinding = 'upstream'
                source = [ordered]@{ name = 'upstream-test'; origin = 'https://example.invalid/upstream'; revision = 'content-hash' }
                authorization = [ordered]@{ status = 'Verified'; origin = 'test license'; license = 'MIT'; evidence = 'root://LICENSE'; redistributionAllowed = $true }
                expectedReports = @([ordered]@{ kind = 'identity'; path = 'identity.json'; required = $true })
                expectedCoverage = @('minimal upstream behavior')
                notes = ''
            },
            [ordered]@{
                id = 'GE-TEST-001'; layer = 'legacy'; category = 'baseline-runner'; profile = 'GEmueraSnake'; rootBinding = 'legacy'
                source = [ordered]@{ name = 'legacy-test'; origin = 'local'; revision = 'content-hash' }
                authorization = [ordered]@{ status = 'Unverified'; origin = 'no license found'; license = 'Unresolved'; evidence = ''; redistributionAllowed = $false }
                expectedReports = @(
                    [ordered]@{ kind = 'identity'; path = 'identity.json'; required = $true },
                    [ordered]@{ kind = 'state'; path = 'state.json'; required = $true }
                )
                expectedCoverage = @('legacy startup')
                notes = ''
            },
            [ordered]@{
                id = 'SN-TEST-001'; layer = 'game'; category = 'representative-game'; profile = 'Snake'; rootBinding = 'game'
                source = [ordered]@{ name = 'missing-game'; origin = 'local'; revision = 'content-hash' }
                authorization = [ordered]@{ status = 'Unverified'; origin = 'unknown'; license = 'Unresolved'; evidence = ''; redistributionAllowed = $false }
                expectedReports = @([ordered]@{ kind = 'timeline'; path = 'timeline.json'; required = $true })
                expectedCoverage = @('game trace')
                notes = ''
            }
        )
    }
    Write-TestText -Path $catalogPath -Value (($catalog | ConvertTo-Json -Depth 20) + "`n")

    $output = Join-Path $testRoot 'output'
    $result = Invoke-FixtureManifest -CatalogPath $catalogPath -OutputDirectory $output `
        -RootBindings @{ upstream = $rootA; legacy = $rootB; game = (Join-Path $testRoot 'missing-game') } `
        -ReportBindings @{ legacy = $reportRoot }

    Assert-FixtureContract ($result.result -eq 'Partial') 'Missing fixture/report evidence was not reported as Partial.'
    Assert-FixtureContract ($result.executionStatus -eq 'InProgress') 'M0 execution status must remain InProgress.'
    Assert-FixtureContract ($result.gateStatus -eq 'Blocked' -and $result.blockerCode -eq 'EvidenceMissing') 'M0 gate status was incorrectly advanced.'
    Assert-FixtureContract ($result.fixtures.Count -eq 3) 'Fixture catalog entry count changed.'

    $upstream = @($result.fixtures | Where-Object id -eq 'UP-TEST-001')[0]
    $legacy = @($result.fixtures | Where-Object id -eq 'GE-TEST-001')[0]
    $game = @($result.fixtures | Where-Object id -eq 'SN-TEST-001')[0]
    Assert-FixtureContract ($upstream.availabilityStatus -eq 'Captured') 'Existing upstream root was not captured.'
    Assert-FixtureContract ($legacy.authorization.status -eq 'Unverified' -and -not $legacy.authorization.redistributionAllowed) 'Unverified authorization became redistributable.'
    Assert-FixtureContract ($game.availabilityStatus -eq 'Uncovered') 'Missing game fixture was not explicitly Uncovered.'
    Assert-FixtureContract ($game.uncovered.Count -gt 0) 'Missing fixture has no uncovered reason.'

    $identity = @($legacy.expectedReports | Where-Object kind -eq 'identity')[0]
    $state = @($legacy.expectedReports | Where-Object kind -eq 'state')[0]
    Assert-FixtureContract ($identity.status -eq 'Captured' -and $identity.sha256 -match '^[0-9a-f]{64}$') 'Existing expected report was not hashed.'
    Assert-FixtureContract ($state.status -eq 'Uncovered') 'Missing expected report was not explicitly Uncovered.'
    Assert-FixtureContract ((Test-Path -LiteralPath (Join-Path $output 'fixture-manifest.json') -PathType Leaf)) 'Fixture manifest report was not written.'
    Assert-FixtureContract ((Test-Path -LiteralPath (Join-Path $output 'UP-TEST-001.files.json') -PathType Leaf)) 'Captured fixture file manifest was not written.'
    Assert-FixtureContract ((Get-ChildItem -LiteralPath $rootA -File -Recurse).Count -eq 2) 'Fixture root was mutated during capture.'

    $unsafeOutputRejected = $false
    try {
        [void](Invoke-FixtureManifest -CatalogPath $catalogPath -OutputDirectory (Join-Path $rootA 'unsafe-output') `
            -RootBindings @{ upstream = $rootA } -ReportBindings @{})
    }
    catch {
        $unsafeOutputRejected = $_.Exception.Message -match 'must not be inside fixture root'
    }
    Assert-FixtureContract $unsafeOutputRejected 'Output inside a fixture root was not rejected.'

    Write-Output 'M0 fixture manifest contract tests passed.'
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
finally {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolvedTest = [IO.Path]::GetFullPath($testRoot).TrimEnd('\') + '\'
    if ($resolvedTest.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        $resolvedTest.Contains('gemuera-m0-fixture-test-') -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
