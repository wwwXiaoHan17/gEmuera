[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$modulePath = Join-Path $ProjectRoot 'tools\dialect-inventory\DialectInventory.psm1'
$catalogPath = Join-Path $ProjectRoot 'tools\dialect-inventory\dialect-classification.json'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-dia-test-' + [Guid]::NewGuid().ToString('N'))
$utf8NoBom = New-Object Text.UTF8Encoding($false)

function Assert-DialectContract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Write-TestText {
    param([string]$Path, [string]$Value)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Value, $utf8NoBom)
}

function Write-SyntheticProject {
    param([string]$Root, [bool]$ReverseOrder = $false, [bool]$DuplicateInstruction = $false)

    $program = @'
namespace MinorShift.Emuera {
    internal static class Program {
        public static EmueraCoreProfile CoreProfile { get; private set; }
        public static bool IsSnakeProfile => CoreProfile == EmueraCoreProfile.Snake;
    }
}
'@
    $duplicate = if ($DuplicateInstruction) { '            addFunction(FunctionCode.COMMON, new CommonInstruction());' } else { '' }
    $identifier = @"
namespace MinorShift.Emuera.GameProc.Function {
    internal sealed class FunctionIdentifier {
        private static void addV24CompatibilityFunctions() {
            addFunction(FunctionCode.V24_ONLY, new V24Instruction());
        }
        private static void addSnakeCompatibilityFunctions() {
            addFunction(FunctionCode.SNAKE_ONLY, new SNAKE_Instruction());
        }
        static FunctionIdentifier() {
            addFunction(FunctionCode.COMMON, new CommonInstruction());
$duplicate
            addV24CompatibilityFunctions();
            addSnakeCompatibilityFunctions();
        }
    }
}
"@
    $creator = @'
namespace MinorShift.Emuera.GameData.Function {
    internal static class FunctionMethodCreator {
        private static readonly object methodList = new object[] {
            ["COMMON_METHOD"] = new CommonMethod(),
            ["SNAKE_METHOD"] = new SnakeMethod()
        };
    }
}
'@

    $files = @(
        @{ Path = 'Scripts\Emuera\Program.cs'; Text = $program },
        @{ Path = 'Scripts\Emuera\GameProc\Function\FunctionIdentifier.cs'; Text = $identifier },
        @{ Path = 'Scripts\Emuera\GameData\Function\Creator.cs'; Text = $creator }
    )
    if ($ReverseOrder) { [array]::Reverse($files) }
    foreach ($file in $files) { Write-TestText -Path (Join-Path $Root $file.Path) -Value $file.Text }
}

try {
    if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
        throw "Dialect inventory module is missing: $modulePath"
    }
    if (-not (Test-Path -LiteralPath $catalogPath -PathType Leaf)) {
        throw "Dialect classification catalog is missing: $catalogPath"
    }
    Import-Module $modulePath -Force

    $actualOutput = Join-Path $testRoot 'actual.json'
    $actual = New-DialectInventory -ProjectRoot $ProjectRoot -ClassificationPath $catalogPath -OutputPath $actualOutput
    Assert-DialectContract ($actual.workPackage -eq 'M0-DIA-01') 'Unexpected dialect inventory work package.'
    Assert-DialectContract ($actual.executionStatus -eq 'InProgress') 'M0 execution status must remain InProgress.'
    Assert-DialectContract ($actual.gateStatus -eq 'Blocked' -and $actual.blockerCode -eq 'EvidenceMissing') 'M0 gate status was incorrectly advanced.'
    Assert-DialectContract ($actual.result -eq 'Partial') 'Static inventory must not claim behavioral completion.'
    Assert-DialectContract ($actual.unmappedHitCount -eq 0) 'Versioned classification catalog left branch hits unmapped.'
    Assert-DialectContract ($actual.branchHitCount -ge 60) 'Profile/setting branch inventory unexpectedly shrank.'
    Assert-DialectContract ($actual.instructionRegistrationCount -ge 250) 'Instruction registration inventory unexpectedly shrank.'
    Assert-DialectContract ($actual.expressionRegistrationCount -ge 300) 'Expression function inventory unexpectedly shrank.'
    Assert-DialectContract ($actual.canonicalHash -match '^[0-9a-f]{64}$') 'Canonical inventory hash is invalid.'
    Assert-DialectContract ((Test-Path -LiteralPath $actualOutput -PathType Leaf)) 'Dialect inventory report was not written.'

    $repeat = New-DialectInventory -ProjectRoot $ProjectRoot -ClassificationPath $catalogPath
    Assert-DialectContract ($repeat.canonicalHash -eq $actual.canonicalHash) 'Canonical hash changed between identical scans.'

    $syntheticCatalog = Join-Path $testRoot 'synthetic-classification.json'
    Write-TestText -Path $syntheticCatalog -Value @'
{
  "schemaVersion": "1.0.0",
  "workPackage": "M0-DIA-01",
  "classifications": [
    {
      "id": "synthetic-program-profile",
      "markerId": "profile.core-state",
      "fileRegex": "^scripts/Emuera/Program\\.cs$",
      "category": "profile-selection",
      "currentOwner": "Program",
      "intendedOwner": "candidate compatibility plan adapter",
      "targetModule": "session.selection",
      "behaviorKey": "dialect.selection.v1",
      "capabilityId": "",
      "fixtureId": "DIA-PLAN-SELECT-001",
      "notes": "synthetic"
    },
    {
      "id": "synthetic-program-snake",
      "markerId": "profile.is-snake",
      "fileRegex": "^scripts/Emuera/Program\\.cs$",
      "category": "profile-projection",
      "currentOwner": "Program",
      "intendedOwner": "candidate compatibility plan adapter",
      "targetModule": "session.selection",
      "behaviorKey": "dialect.selection.v1",
      "capabilityId": "",
      "fixtureId": "DIA-PLAN-SELECT-001",
      "notes": "synthetic"
    },
    {
      "id": "synthetic-v24-registry",
      "markerId": "registry.v24-method",
      "fileRegex": "^scripts/Emuera/GameProc/Function/FunctionIdentifier\\.cs$",
      "category": "instruction-registration",
      "currentOwner": "FunctionIdentifier",
      "intendedOwner": "DialectPlan instruction catalog",
      "targetModule": "gemuera.v24",
      "behaviorKey": "",
      "capabilityId": "instruction.registry.v1",
      "fixtureId": "DIA-REG-V24-001",
      "notes": "synthetic"
    },
    {
      "id": "synthetic-snake-registry",
      "markerId": "registry.snake-method",
      "fileRegex": "^scripts/Emuera/GameProc/Function/FunctionIdentifier\\.cs$",
      "category": "instruction-registration",
      "currentOwner": "FunctionIdentifier",
      "intendedOwner": "DialectPlan instruction catalog",
      "targetModule": "game.snake",
      "behaviorKey": "",
      "capabilityId": "instruction.registry.v1",
      "fixtureId": "DIA-REG-SNAKE-001",
      "notes": "synthetic"
    }
  ]
}
'@

    $rootA = Join-Path $testRoot 'root-a'
    $rootB = Join-Path $testRoot 'root-b'
    Write-SyntheticProject -Root $rootA
    Write-SyntheticProject -Root $rootB -ReverseOrder $true
    $inventoryA = New-DialectInventory -ProjectRoot $rootA -ClassificationPath $syntheticCatalog
    $inventoryB = New-DialectInventory -ProjectRoot $rootB -ClassificationPath $syntheticCatalog
    Assert-DialectContract ($inventoryA.canonicalHash -eq $inventoryB.canonicalHash) 'Canonical hash depends on file creation/enumeration order.'
    Assert-DialectContract ($inventoryA.instructionRegistrationCount -eq 3) 'Synthetic instruction registrations were not partitioned.'
    Assert-DialectContract ($inventoryA.expressionRegistrationCount -eq 2) "Synthetic expression registrations were not extracted: $($inventoryA.expressionRegistrationCount) [$(@($inventoryA.expressionRegistrations.publicKey) -join ',')]."
    Assert-DialectContract (@($inventoryA.instructionRegistrations | Where-Object targetModule -eq 'game.snake').Count -eq 1) 'Snake contribution was not attributed to its target module.'

    Write-TestText -Path (Join-Path $rootB 'Scripts\Emuera\GameData\Function\Creator.cs') -Value ((Get-Content -LiteralPath (Join-Path $rootB 'Scripts\Emuera\GameData\Function\Creator.cs') -Raw).Replace('CommonMethod', 'ChangedMethod'))
    $changed = New-DialectInventory -ProjectRoot $rootB -ClassificationPath $syntheticCatalog
    Assert-DialectContract ($changed.canonicalHash -ne $inventoryA.canonicalHash) 'Canonical hash did not change after source semantics changed.'

    $duplicateRoot = Join-Path $testRoot 'duplicate-root'
    Write-SyntheticProject -Root $duplicateRoot -DuplicateInstruction $true
    $duplicateRejected = $false
    try { [void](New-DialectInventory -ProjectRoot $duplicateRoot -ClassificationPath $syntheticCatalog) }
    catch { $duplicateRejected = $_.Exception.Message -match 'Duplicate instruction registration' }
    Assert-DialectContract $duplicateRejected 'Duplicate instruction key was not rejected.'

    $unmappedCatalog = Join-Path $testRoot 'unmapped-classification.json'
    Write-TestText -Path $unmappedCatalog -Value '{"schemaVersion":"1.0.0","workPackage":"M0-DIA-01","classifications":[]}'
    $unmappedRejected = $false
    try { [void](New-DialectInventory -ProjectRoot $rootA -ClassificationPath $unmappedCatalog) }
    catch { $unmappedRejected = $_.Exception.Message -match 'Unmapped dialect branch hit' }
    Assert-DialectContract $unmappedRejected 'Unmapped branch hit was not rejected.'

    Write-Output 'M0 dialect inventory contract tests passed.'
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
        $resolvedTest.Contains('gemuera-m0-dia-test-') -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
