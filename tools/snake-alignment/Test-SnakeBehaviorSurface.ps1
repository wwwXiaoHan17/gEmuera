# Test-SnakeBehaviorSurface.ps1
#
# WHY THIS EXISTS
#   tools/snake-alignment/Test-SnakeReferenceSurface.ps1 is the real snake-alignment gate. It takes a
#   mandatory -ReferenceRoot pointing at an upstream reference checkout (E:\MyCode\Era\emuera_lazyloading_*).
#   That checkout does NOT exist on this machine, and the gate hard-throws at its "Required source file
#   was not found" check BEFORE reaching any assertion. Consequence: the 32 $behaviorChecks entries --
#   the half that pins specific regexes to specific source PATHS -- never run locally, even though that
#   is exactly the surface a FILE_STANDARD pure-move split is most likely to break (moving a member to a
#   Type.Feature.cs shard moves the path the pin points at).
#
# WHAT IT DOES
#   Parses the authoritative $behaviorChecks = @( ... ) literal OUT OF the real gate script and evaluates
#   just that half. The literal is parsed, never copied, so this harness cannot drift from the gate.
#   The upstream-parity half (instruction/expression-function key comparison against the reference
#   checkout) is deliberately NOT covered here -- it needs -ReferenceRoot.
#
# ANTI-VACUOUS-PASS GUARDS (both were earned the hard way)
#   1. Parsed $behaviorChecks empty  -> throw. A harness that "passes" because it parsed nothing is worse
#      than no harness.
#   2. Any entry with an empty Path   -> throw. The first version of this harness forgot to define
#      $currentInstructionPath / $currentFunctionPath / $currentLexicalAnalyzerPath, so those entries
#      parsed with an empty Path and nearly everything "passed" (Test-Path $null is false, but the
#      failure text was useless). If the gate ever renames a path variable, fail loudly instead.
#
# ASCII-ONLY ON PURPOSE
#   Windows PowerShell 5.1 decodes a BOM-less UTF-8 .ps1 as GBK, which mangles non-ASCII literals into
#   parse errors (FILE_STANDARD section 2). Both comments and output here are ASCII, so this file is
#   safe with or without a BOM and cannot be broken by a tool that rewrites it without one.
#
# READ-ONLY: this script only reads source files. It never builds and never modifies the tree.
#
# EXIT CODES
#   0 = all parsed behaviorChecks pass
#   1 = at least one check failed (missing file or pattern not found)
#   throw = harness could not obtain an authoritative check list (vacuous-pass refusal)
#
# Usage:
#   powershell -NoProfile -File tools\snake-alignment\Test-SnakeBehaviorSurface.ps1
#   powershell -NoProfile -File tools\snake-alignment\Test-SnakeBehaviorSurface.ps1 -ProjectRoot D:\gemuera
#   powershell -NoProfile -File tools\snake-alignment\Test-SnakeBehaviorSurface.ps1 -GateScript <path>

[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
    [string]$GateScript = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
}
if ([string]::IsNullOrWhiteSpace($GateScript)) {
    $GateScript = Join-Path $PSScriptRoot 'Test-SnakeReferenceSurface.ps1'
}
if (-not (Test-Path -LiteralPath $GateScript -PathType Leaf)) {
    throw "Gate script not found: $GateScript"
}

$project = (Resolve-Path -LiteralPath $ProjectRoot).Path
$lines = [IO.File]::ReadAllLines($GateScript, [Text.Encoding]::UTF8)

# The $behaviorChecks literal references these path variables, so they must exist (with the same
# meaning as in the real gate: same file names, same directory layout) before the block is evaluated.
# The $reference* / $currentInstructions / $currentFunctions variables are NOT needed: they are only
# consumed by the upstream-parity half that this harness deliberately omits.
$currentInstructionPath     = Join-Path $project 'Scripts\Emuera\GameProc\Function\FunctionIdentifier.cs'
$currentFunctionPath        = Join-Path $project 'Scripts\Emuera\GameData\Function\Creator.cs'
$currentLexicalAnalyzerPath = Join-Path $project 'Scripts\Emuera\Sub\LexicalAnalyzer.cs'

# --- locate the authoritative $behaviorChecks = @( ... ) literal ---
$startIdx = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s*\$behaviorChecks\s*=\s*@\(\s*$') { $startIdx = $i; break }
}
if ($startIdx -lt 0) { throw 'Could not locate the $behaviorChecks literal in the gate script.' }

$endIdx = -1
for ($i = $startIdx + 1; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s*\)\s*$') { $endIdx = $i; break }
}
if ($endIdx -lt 0) { throw 'Could not locate the end of the $behaviorChecks literal.' }

$block = ($lines[$startIdx..$endIdx]) -join "`r`n"
Invoke-Expression $block | Out-Null

# --- guard 1: refuse a vacuous pass ---
if ($null -eq $behaviorChecks -or $behaviorChecks.Count -eq 0) {
    throw 'Parsed $behaviorChecks is empty; refusing to report a vacuous pass.'
}

# --- guard 2: refuse silently-empty Path entries (upstream variable renamed/undefined) ---
$badPath = @($behaviorChecks | Where-Object { [string]::IsNullOrWhiteSpace($_.Path) })
if ($badPath.Count -gt 0) {
    $labels = ($badPath | ForEach-Object { $_.Label }) -join ', '
    throw ("Parsed `$behaviorChecks has entries with an empty Path (upstream variable not defined): " + $labels)
}

# --- evaluate exactly as the real gate does ---
$failed = @()
$passed = 0
foreach ($check in $behaviorChecks) {
    if (-not (Test-Path -LiteralPath $check.Path -PathType Leaf)) {
        $failed += [pscustomobject]@{ Label = $check.Label; Path = $check.Path; Why = 'file missing' }
        continue
    }
    $source = Get-Content -LiteralPath $check.Path -Raw -Encoding utf8
    if ([regex]::IsMatch($source, $check.Pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
        $passed++
    } else {
        $failed += [pscustomobject]@{ Label = $check.Label; Path = $check.Path; Why = 'pattern not found' }
    }
}

Write-Output ('gate script          : ' + $GateScript)
Write-Output ('behaviorChecks literal: lines ' + ($startIdx + 1) + '-' + ($endIdx + 1) + ' of that script (parsed, not copied)')
Write-Output ('behaviorChecks total : ' + $behaviorChecks.Count)
Write-Output ('passed               : ' + $passed)
Write-Output ('failed               : ' + $failed.Count)
Write-Output ''

if ($failed.Count -gt 0) {
    Write-Output '--- FAILED ---'
    foreach ($f in $failed) {
        Write-Output ('  ' + $f.Label + ' [' + $f.Why + '] ' + $f.Path)
    }
    Write-Output ''
    Write-Output ('RESULT: ' + $passed + ' passed / ' + $failed.Count + ' failed')
    Write-Output 'NOTE: this harness covers ONLY the behaviorChecks half. Upstream-parity comparison is NOT covered.'
    exit 1
}

Write-Output ('RESULT: ' + $passed + ' passed / 0 failed')
Write-Output 'NOTE: this harness covers ONLY the behaviorChecks half (pinned path + content regex).'
Write-Output '      The upstream-parity half of Test-SnakeReferenceSurface.ps1 needs -ReferenceRoot and is NOT covered.'
Write-Output '      Baseline/parity evidence still requires running the real gate where the reference checkout exists.'
exit 0
