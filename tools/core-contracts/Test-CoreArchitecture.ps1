[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-CoreArchitecture {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$coreRoot = Join-Path $ProjectRoot 'src\Core'
$coreProject = Join-Path $coreRoot 'GEmuera.Core.csproj'
Assert-CoreArchitecture (Test-Path -LiteralPath $coreProject -PathType Leaf) 'Core project is missing.'

$sourceFiles = @(
    Get-ChildItem -LiteralPath $coreRoot -Filter '*.cs' -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' }
)
Assert-CoreArchitecture ($sourceFiles.Count -ge 3) 'Core source inventory unexpectedly shrank.'

foreach ($sourceFile in $sourceFiles) {
    $text = Get-Content -LiteralPath $sourceFile.FullName -Raw -Encoding utf8
    Assert-CoreArchitecture ($text -notmatch '(?m)^\s*(?:global\s+)?using\s+Godot(?:\.[A-Za-z0-9_]+)*\s*;') "Core source references Godot: $($sourceFile.FullName)"
    Assert-CoreArchitecture ($text -notmatch '\bGodot(?:Sharp)?\.(Node|Resource|Texture2D|Image|Variant|Callable|Object|GodotObject)\b') "Core source exposes Godot type: $($sourceFile.FullName)"
    Assert-CoreArchitecture ($text -notmatch '\[Signal\]|EmitSignal\s*\(') "Core source uses Godot signal API: $($sourceFile.FullName)"
}

$projectText = Get-Content -LiteralPath $coreProject -Raw -Encoding utf8
Assert-CoreArchitecture ($projectText -match '<TargetFrameworks>net8\.0;net9\.0</TargetFrameworks>') 'Core Godot-host and validation target frameworks are not explicit.'
Assert-CoreArchitecture ($projectText -notmatch 'Godot\.NET\.Sdk|GodotSharp|ProjectReference') 'Core project gained a Godot or legacy project dependency.'

$legacyProfilePath = Join-Path $ProjectRoot 'Scripts\Emuera\Compatibility\LegacyCompatibilityProfile.cs'
Assert-CoreArchitecture (Test-Path -LiteralPath $legacyProfilePath -PathType Leaf) 'Legacy compatibility policy projection is missing.'
$legacyModulesPath = Join-Path $ProjectRoot 'Scripts\Emuera\Compatibility\LegacyCompatibilityModules.cs'
Assert-CoreArchitecture (Test-Path -LiteralPath $legacyModulesPath -PathType Leaf) 'Legacy compatibility module catalog is missing.'
$legacyProfileText = Get-Content -LiteralPath $legacyProfilePath -Raw -Encoding utf8
$legacyModulesText = Get-Content -LiteralPath $legacyModulesPath -Raw -Encoding utf8
Assert-CoreArchitecture ($legacyProfileText -notmatch 'SnakeOnly(?:Instruction|Function)Names') 'Profile projection owns a dialect key list instead of a module.'
Assert-CoreArchitecture ($legacyModulesText -match 'selectedModules\.SetEquals\(expectedModules\)') 'Legacy profile module closure is not exact.'
Assert-CoreArchitecture ($legacyModulesText -match 'LegacyV24CompatibilityModule') 'v24 bridge module is not declared.'
# P-D 退役后 snake/erafl 内置桥接模块已删除；其静态表面名集迁到独立文件（兼容包加载链使用）。
$snakeLegacySurfacePath = Join-Path $ProjectRoot 'Scripts\Emuera\Compatibility\SnakeLegacySurfaceNames.cs'
Assert-CoreArchitecture (Test-Path -LiteralPath $snakeLegacySurfacePath -PathType Leaf) 'Snake legacy surface names file is missing.'
$snakeLegacySurfaceText = Get-Content -LiteralPath $snakeLegacySurfacePath -Raw -Encoding utf8
Assert-CoreArchitecture ($snakeLegacySurfaceText -match 'SnakeOnlyInstructionNames' -and $snakeLegacySurfaceText -match 'SnakeExcludedFunctionNames') 'Snake legacy surface names are not declared.'

$instructionRegistryPath = Join-Path $ProjectRoot 'Scripts\Emuera\GameProc\Function\FunctionIdentifier.cs'
$functionRegistryPath = Join-Path $ProjectRoot 'Scripts\Emuera\GameData\Function\Creator.cs'
$erbLoaderPath = Join-Path $ProjectRoot 'Scripts\Emuera\GameProc\ErbLoader.cs'
$processStatePath = Join-Path $ProjectRoot 'Scripts\Emuera\GameProc\Process.State.cs'
$instructionRegistryText = Get-Content -LiteralPath $instructionRegistryPath -Raw -Encoding utf8
$functionRegistryText = Get-Content -LiteralPath $functionRegistryPath -Raw -Encoding utf8
$erbLoaderText = Get-Content -LiteralPath $erbLoaderPath -Raw -Encoding utf8
$processStateText = Get-Content -LiteralPath $processStatePath -Raw -Encoding utf8
Assert-CoreArchitecture ($legacyProfileText -match 'ScopedVariableInstructionsEnabled') 'Legacy profile does not retain the frozen scoped-variable setting.'
Assert-CoreArchitecture ($legacyProfileText -match 'RegistrySurfaceHash') 'Legacy profile does not expose an immutable registry-surface identity.'
Assert-CoreArchitecture ($instructionRegistryText -match 'compatibility\.RegistrySurfaceHash') 'Instruction registry cache is not bound to the frozen profile surface identity.'
Assert-CoreArchitecture ($instructionRegistryText -notmatch 'Config\.UseScopedVariableInstruction') 'Instruction registry reads mutable scoped-variable config during parser surface construction.'
Assert-CoreArchitecture ($erbLoaderText -match 'Program\.Compatibility\.Snake\.AllowsScopedVariablePreRegistration') 'Snake dynamic-variable preregistration bypasses the compatibility policy.'
Assert-CoreArchitecture ($erbLoaderText -match 'Program\.Compatibility\.ScopedVariableInstructionsEnabled') 'Snake dynamic-variable preregistration bypasses the frozen scoped-variable setting.'
Assert-CoreArchitecture ($functionRegistryText -match 'compatibility\.Plan\.CanonicalHash') 'Function registry cache is not bound to the frozen plan hash.'
Assert-CoreArchitecture ($processStateText -notmatch 'EraFlCompatibilityModule\.(?:TaskStartRoomLookupFunction|GMapQuestType|TryRecoverQuestStartRoomIndex|TryPopulateGMapRoomData|TryParseGMapDataTableFromXml)') 'Process.State bypasses the eraFL bridge policy.'

$legacyProfileConsumers = @(
    Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'Scripts') -Filter '*.cs' -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' -and $_.Name -ne 'Program.cs' }
)
foreach ($sourceFile in $legacyProfileConsumers) {
    $text = Get-Content -LiteralPath $sourceFile.FullName -Raw -Encoding utf8
    Assert-CoreArchitecture ($text -notmatch '\bProgram\.(?:IsSnakeProfile|IsEraFlProfile)\b') "Legacy runtime bypasses immutable compatibility policy: $($sourceFile.FullName)"
    Assert-CoreArchitecture ($text -notmatch 'FunctionIdentifier\.GetInstructionNameDic\s*\(\s*\)') "Legacy runtime requests an unfiltered instruction registry: $($sourceFile.FullName)"
    Assert-CoreArchitecture ($text -notmatch 'FunctionMethodCreator\.GetMethodList\s*\(\s*\)') "Legacy runtime requests an unfiltered function registry: $($sourceFile.FullName)"
}

Write-Output "Core architecture guard passed: files=$($sourceFiles.Count)."
