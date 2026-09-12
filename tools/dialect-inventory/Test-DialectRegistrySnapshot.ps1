[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$modulePath = Join-Path $ProjectRoot 'tools\dialect-inventory\DialectInventory.psm1'
$catalogPath = Join-Path $ProjectRoot 'tools\dialect-inventory\dialect-classification.json'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-dia-snapshot-test-' + [Guid]::NewGuid().ToString('N'))

function Assert-SnapshotContract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

try {
    Import-Module $modulePath -Force
    $inventory = New-DialectInventory -ProjectRoot $ProjectRoot -ClassificationPath $catalogPath

    $baseModules = @('legacy.current.common', 'legacy.current.expression', 'gemuera.v24')
    $allModules = @('legacy.current.common', 'legacy.current.expression', 'gemuera.v24', 'game.snake')
    $v24Minimal = New-DialectRegistrySnapshot -Inventory $inventory -ProfileId 'v24-projection' `
        -SelectedModuleIds $baseModules -AvailableModuleIds $baseModules
    $v24FullCatalog = New-DialectRegistrySnapshot -Inventory $inventory -ProfileId 'v24-projection' `
        -SelectedModuleIds @('gemuera.v24', 'legacy.current.expression', 'legacy.current.common') -AvailableModuleIds $allModules
    $snake = New-DialectRegistrySnapshot -Inventory $inventory -ProfileId 'snake-projection' `
        -SelectedModuleIds $allModules -AvailableModuleIds $allModules

    Assert-SnapshotContract ($v24Minimal.canonicalHash -eq $v24FullCatalog.canonicalHash) 'Unselected Snake module changed the v24 snapshot hash.'
    Assert-SnapshotContract ($v24Minimal.canonicalHash -match '^[0-9a-f]{64}$') 'v24 snapshot hash is invalid.'
    Assert-SnapshotContract ($snake.canonicalHash -match '^[0-9a-f]{64}$' -and $snake.canonicalHash -ne $v24Minimal.canonicalHash) 'Snake snapshot hash is invalid or equal to v24.'
    Assert-SnapshotContract (@($v24Minimal.instructions | Where-Object publicKey -eq 'SKIPLOG').Count -eq 0) 'Snake-only instruction leaked into v24 projection.'
    Assert-SnapshotContract (@($v24Minimal.instructions | Where-Object publicKey -eq 'CALLSHARP').Count -eq 0) 'Snake candidate handler leaked into v24 projection.'
    Assert-SnapshotContract (@($snake.instructions | Where-Object publicKey -eq 'SKIPLOG').Count -eq 1) 'Snake instruction is missing from Snake projection.'
    Assert-SnapshotContract (@($snake.instructions | Where-Object publicKey -eq 'CALLSHARP').Count -eq 1) 'Snake candidate instruction is missing from Snake projection.'
    Assert-SnapshotContract (@($v24Minimal.instructions | Where-Object publicKey -eq 'PRINT').Count -eq 1) 'Common instruction is missing from v24 projection.'
    Assert-SnapshotContract (@($snake.instructions | Where-Object publicKey -eq 'PRINT').Count -eq 1) 'Common instruction is missing from Snake projection.'

    $snapshotHandler = @($v24Minimal.instructions | Where-Object publicKey -eq 'PRINT')[0].handler
    @($inventory.instructionRegistrations | Where-Object publicKey -eq 'PRINT')[0].handler = 'MUTATED_AFTER_SNAPSHOT'
    Assert-SnapshotContract (@($v24Minimal.instructions | Where-Object publicKey -eq 'PRINT')[0].handler -eq $snapshotHandler) 'Snapshot retained a mutable reference to inventory registration.'

    $duplicateInventory = [pscustomobject]@{
        canonicalHash = ('a' * 64)
        branchHits = @()
        instructionRegistrations = @(
            [pscustomobject]@{ publicKey='DUP'; registryKind='instruction'; handler='A'; currentGuard='none'; currentContribution='a'; targetModule='gemuera.v24'; provenanceWarning=''; sourceFile='a.cs'; sourceLine=1 },
            [pscustomobject]@{ publicKey='DUP'; registryKind='instruction'; handler='B'; currentGuard='none'; currentContribution='b'; targetModule='gemuera.v24'; provenanceWarning=''; sourceFile='b.cs'; sourceLine=2 }
        )
        expressionRegistrations = @()
    }
    $duplicateRejected = $false
    try {
        [void](New-DialectRegistrySnapshot -Inventory $duplicateInventory -ProfileId 'duplicate' `
            -SelectedModuleIds @('gemuera.v24') -AvailableModuleIds @('gemuera.v24'))
    }
    catch { $duplicateRejected = $_.Exception.Message -match 'Duplicate selected instruction key' }
    Assert-SnapshotContract $duplicateRejected 'Duplicate selected instruction key was not rejected.'

    $unselectedDuplicateInventory = [pscustomobject]@{
        canonicalHash = ('b' * 64)
        branchHits = @()
        instructionRegistrations = @(
            [pscustomobject]@{ publicKey='BASE'; registryKind='instruction'; handler='Base'; currentGuard='none'; currentContribution='base'; targetModule='gemuera.v24'; provenanceWarning=''; sourceFile='a.cs'; sourceLine=1 },
            [pscustomobject]@{ publicKey='DUP'; registryKind='instruction'; handler='SnakeA'; currentGuard='none'; currentContribution='snake'; targetModule='game.snake'; provenanceWarning=''; sourceFile='b.cs'; sourceLine=2 },
            [pscustomobject]@{ publicKey='DUP'; registryKind='instruction'; handler='SnakeB'; currentGuard='none'; currentContribution='snake'; targetModule='game.snake'; provenanceWarning=''; sourceFile='c.cs'; sourceLine=3 }
        )
        expressionRegistrations = @()
    }
    $unselectedDuplicate = New-DialectRegistrySnapshot -Inventory $unselectedDuplicateInventory -ProfileId 'base-only' `
        -SelectedModuleIds @('gemuera.v24') -AvailableModuleIds @('gemuera.v24', 'game.snake')
    Assert-SnapshotContract ($unselectedDuplicate.instructionCount -eq 1 -and $unselectedDuplicate.instructions[0].publicKey -eq 'BASE') 'Unselected duplicate module affected base snapshot.'

    $missingModuleRejected = $false
    try {
        [void](New-DialectRegistrySnapshot -Inventory $unselectedDuplicateInventory -ProfileId 'missing-module' `
            -SelectedModuleIds @('gemuera.v24', 'game.snake') -AvailableModuleIds @('gemuera.v24'))
    }
    catch { $missingModuleRejected = $_.Exception.Message -match 'Selected module is not available' }
    Assert-SnapshotContract $missingModuleRejected 'Unavailable selected module was not rejected.'

    $reportPath = Join-Path $testRoot 'registry-snapshots.json'
    $report = New-DialectRegistrySnapshotReport -Inventory (New-DialectInventory -ProjectRoot $ProjectRoot -ClassificationPath $catalogPath) -OutputPath $reportPath
    Assert-SnapshotContract ($report.workPackage -eq 'M0-DIA-02') 'Unexpected registry snapshot work package.'
    Assert-SnapshotContract ($report.executionStatus -eq 'InProgress' -and $report.gateStatus -eq 'Blocked' -and $report.blockerCode -eq 'EvidenceMissing') 'M0 gate status was incorrectly advanced.'
    Assert-SnapshotContract ($report.result -eq 'Partial') 'Test registry projection must not claim D2 completion.'
    Assert-SnapshotContract ($report.testProjectionInvariant.status -eq 'Passed') 'Unselected-module test projection invariant did not pass.'
    Assert-SnapshotContract ($report.currentRuntimeIsolation.status -eq 'Passed') 'Legacy parser/VM lookup is not bound to the frozen profile surface.'
    Assert-SnapshotContract ($report.profiles.v24.instructionCount -eq 303) "Unexpected runtime v24 instruction count: $($report.profiles.v24.instructionCount)."
    Assert-SnapshotContract ($report.profiles.v24.expressionFunctionCount -eq 266) "Unexpected runtime v24 expression function count: $($report.profiles.v24.expressionFunctionCount)."
    Assert-SnapshotContract ($report.profiles.snake.instructionCount -eq 326) "Unexpected runtime Snake instruction count: $($report.profiles.snake.instructionCount)."
    # 2026-09-07 对齐真实注册表值：erafl 方言清单提交（92ad2a7，snake 侧 +MGBGM/MGBGMSTOP 系）
    # 后未同步这两个断言，属存量红；349/85 与当前快照及 RuntimeSmoke 一致。
    Assert-SnapshotContract ($report.profiles.snake.expressionFunctionCount -eq 349) "Unexpected runtime Snake expression function count: $($report.profiles.snake.expressionFunctionCount)."
    Assert-SnapshotContract ($report.diff.snakeOnlyInstructionCount -eq 23) 'Unexpected Snake-only instruction diff count.'
    Assert-SnapshotContract ($report.diff.snakeOnlyExpressionFunctionCount -eq 85) 'Unexpected Snake-only expression function diff count.'
    Assert-SnapshotContract ((Test-Path -LiteralPath $reportPath -PathType Leaf)) 'Registry snapshot report was not written.'

    Write-Output 'M0 dialect registry snapshot contract tests passed.'
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
        $resolvedTest.Contains('gemuera-m0-dia-snapshot-test-') -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
