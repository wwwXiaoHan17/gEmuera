[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [Parameter(Mandatory = $true)]
    [string]$ReferenceRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-NormalizedRoot([string]$Path, [string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "$Name must not be empty."
    }
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    return $resolved.Path
}

function Get-InstructionKeys([string]$Path) {
    $text = Get-Content -LiteralPath $Path -Raw -Encoding utf8
    $matches = [regex]::Matches(
        $text,
        'add(?:PrintData|Print|Function)\s*\(\s*FunctionCode\.([A-Z0-9_]+)',
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    return @($matches | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
}

function Get-ExpressionFunctionKeys([string]$Path) {
    $text = Get-Content -LiteralPath $Path -Raw -Encoding utf8
    $matches = [regex]::Matches(
        $text,
        '\["([^"]+)"\]\s*=',
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    return @($matches | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
}

$project = Get-NormalizedRoot $ProjectRoot 'ProjectRoot'
$reference = Get-NormalizedRoot $ReferenceRoot 'ReferenceRoot'

$currentInstructionPath = Join-Path $project 'Scripts\Emuera\GameProc\Function\FunctionIdentifier.cs'
$currentFunctionPath = Join-Path $project 'Scripts\Emuera\GameData\Function\Creator.cs'
$currentLexicalAnalyzerPath = Join-Path $project 'Scripts\Emuera\Sub\LexicalAnalyzer.cs'
$referenceInstructionPath = Join-Path $reference 'Emuera\Runtime\Script\Statements\FunctionIdentifier.cs'
$referenceFunctionPath = Join-Path $reference 'Emuera\Runtime\Script\Statements\Function\Creator.cs'

foreach ($path in @($currentInstructionPath, $currentFunctionPath, $currentLexicalAnalyzerPath, $referenceInstructionPath, $referenceFunctionPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required source file was not found: $path"
    }
}

$currentInstructions = @(Get-InstructionKeys $currentInstructionPath)
$referenceInstructions = @(Get-InstructionKeys $referenceInstructionPath)
$currentFunctions = @(Get-ExpressionFunctionKeys $currentFunctionPath)
$referenceFunctions = @(Get-ExpressionFunctionKeys $referenceFunctionPath)

$missingInstructions = @($referenceInstructions | Where-Object { $_ -notin $currentInstructions })
$missingFunctions = @($referenceFunctions | Where-Object { $_ -notin $currentFunctions })

if ($missingInstructions.Count -gt 0 -or $missingFunctions.Count -gt 0) {
    if ($missingInstructions.Count -gt 0) {
        Write-Output ('Missing reference instruction keys: ' + ($missingInstructions -join ', '))
    }
    if ($missingFunctions.Count -gt 0) {
        Write-Output ('Missing reference expression-function keys: ' + ($missingFunctions -join ', '))
    }
    exit 1
}

$behaviorChecks = @(
    @{ Path = $currentInstructionPath; Pattern = 'FunctionCode\.SETIMAGELAYERL'; Label = 'SETIMAGELAYERL registration' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameProc\Function\BuiltInFunctionCode.cs'); Pattern = '(?m)^\s*SETIMAGELAYERL\s*,?\s*$'; Label = 'SETIMAGELAYERL enum member' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameProc\Function\FunctionArgType.cs'); Pattern = '(?m)^\s*SP_SETIMAGELAYERL\s*,?\s*$'; Label = 'SP_SETIMAGELAYERL argument type' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameProc\Function\Argument.cs'); Pattern = 'SpSetImageLayerArgument'; Label = 'SpSetImageLayerArgument model' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameProc\Function\ArgumentBuilder.cs'); Pattern = 'SP_SETIMAGELAYERL'; Label = 'SP_SETIMAGELAYERL builder' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameProc\Function\Instraction.Child.cs'); Pattern = 'SETIMAGELAYERL_Instruction'; Label = 'SETIMAGELAYERL instruction' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameData\Function\Creator.Method.cs'); Pattern = 'class StrFormCheckMethod'; Label = 'STRFORMCHECK implementation' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameData\Function\Creator.Method.cs'); Pattern = 'class GetLineYMethod'; Label = 'GETLINEY implementation' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameData\Function\Creator.Method.cs'); Pattern = 'class SequenceInputMethod'; Label = 'SEQUENCEINPUT implementation' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameData\Function\Creator.Method.cs'); Pattern = 'class DisableInputMacroMethod'; Label = 'DISABLE_INPUT_MACRO implementation' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameData\Function\Creator.Method.cs'); Pattern = 'class EnableInputMacroMethod'; Label = 'ENABLE_INPUT_MACRO implementation' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameProc\Process.SystemProc.cs'); Pattern = 'InputMacroEnabled'; Label = 'input macro process state' },
    @{ Path = $currentLexicalAnalyzerPath; Pattern = '(?ms)case\s+''e''\s*:\s*buffer\.Append\(''\\\\''\)\s*;\s*buffer\.Append\(''e''\)\s*;\s*break\s*;'; Label = 'SEQUENCEINPUT \e lexical preservation' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameView\EmueraConsole.Input.cs'); Pattern = '(?ms)inputs\.IndexOf\("\\\\e"\)\s*>=\s*0.*?inputs\s*=\s*inputs\.Replace\("\\\\e",\s*""\).*?MesSkip\s*=\s*true'; Label = 'SEQUENCEINPUT \e MesSkip consumption' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameView\EmueraConsole.Input.cs'); Pattern = '(?ms)if\s*\(!inputMacroEnabled\).*?callEmueraProgram\(str,\s*changedByMouse\)\s*;.*?goto\s+endMacro\s*;'; Label = 'disabled input macro literal bypass' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameView\EmueraConsole.cs'); Pattern = 'SimulateSequenceInput'; Label = 'sequence input wait integration' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameView\EmueraConsole.cs'); Pattern = 'GetLinePointY'; Label = 'line coordinate integration' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameProc\Function\FunctionArgType.cs'); Pattern = 'SP_COLOR_ALPHA'; Label = 'TEXT_BGC_ON dedicated argument type' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameProc\Function\Argument.cs'); Pattern = 'class\s+SpColorAlphaArgument'; Label = 'TEXT_BGC_ON dedicated argument model' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameProc\Function\ArgumentParser.TypeChecked.cs'); Pattern = 'class\s+SP_COLOR_ALPHA_ArgumentBuilder'; Label = 'TEXT_BGC_ON strict argument builder' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameProc\Function\Instraction.Child.cs'); Pattern = 'FunctionArgType\.SP_COLOR_ALPHA'; Label = 'TEXT_BGC_ON strict builder wiring' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameProc\Function\Instraction.Child.cs'); Pattern = '(?ms)class\s+SNAKE_HTML_PRINT_ArgumentBuilder.*?LexAnalyzeFlag\.AnalyzePrintV'; Label = 'Snake HTML AnalyzePrintV wiring' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameView\HtmlManager.cs'); Pattern = 'case\s+.*valign'; Label = 'font valign parser' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameView\HtmlManager.cs'); Pattern = 'Convert\.ToInt64\(colorvalue,\s*16\)'; Label = 'ARGB 8-digit parser' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameView\HtmlManager.cs'); Pattern = '0xFF000000'; Label = 'RGB opaque-alpha normalization' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameView\HtmlManager.cs'); Pattern = '(?ms)new\s+ConsoleStyledString\(txt,\s*state\.GetSS\(\).*?state\.FontSize,\s*state\.VerticalAlign\)'; Label = 'HTML font extended state propagation' },
    @{ Path = (Join-Path $project 'Scripts\Emuera\GameView\ConsoleDivPart.cs'); Pattern = '(?ms)CalculateAutoHeight.*?children\.Length\s*\*\s*Config\.LineHeight.*?BoxDirection\.Bottom'; Label = 'div auto-height model' },
    @{ Path = (Join-Path $project 'Scripts\uEmuera\Drawing.cs'); Pattern = '\(\(argb\s*>>\s*24\)\s*&\s*0xFF\)'; Label = 'signed ARGB alpha decode' },
    @{ Path = (Join-Path $project 'Scripts\EmueraContent.Canvas.cs'); Pattern = '(?ms)css\.VerticalAlign.*?FontVerticalAlign\.Top.*?FontVerticalAlign\.Middle.*?FontVerticalAlign\.Bottom'; Label = 'Canvas font valign rendering' },
    @{ Path = (Join-Path $project 'Scripts\EmueraContent.Canvas.cs'); Pattern = 'actualFontSize\s*=\s*emuFont'; Label = 'Canvas per-run font size rendering' },
    @{ Path = (Join-Path $project 'Scripts\EmueraContent.cs'); Pattern = '(?ms)CreateTextPart\(css\.Str.*?css\.VerticalAlign.*?FontVerticalAlign\.Top.*?FontVerticalAlign\.Middle.*?FontVerticalAlign\.Bottom'; Label = 'Control font valign rendering' },
    @{ Path = (Join-Path $project 'Scripts\EmueraContent.cs'); Pattern = '(?ms)uint\s+argb\s*=\s*unchecked\(\(uint\)color\).*?argb\s*>>\s*24'; Label = 'Godot div border ARGB rendering' }
)
$missingBehavior = @()
foreach ($check in $behaviorChecks) {
    if (-not (Test-Path -LiteralPath $check.Path -PathType Leaf)) {
        $missingBehavior += $check.Label + ' (file missing)'
        continue
    }
    $source = Get-Content -LiteralPath $check.Path -Raw -Encoding utf8
    if (-not [regex]::IsMatch($source, $check.Pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
        $missingBehavior += $check.Label
    }
}
if ($missingBehavior.Count -gt 0) {
    Write-Output ('Missing Snake behavior wiring: ' + ($missingBehavior -join ', '))
    exit 1
}

Write-Output ("Snake reference surface checks passed: instructions={0}/{1}, expressionFunctions={2}/{3}." -f `
    $currentInstructions.Count, $referenceInstructions.Count, $currentFunctions.Count, $referenceFunctions.Count)
Write-Output 'Static surface and wiring checks only; full runtime behavior parity is not implied.'

