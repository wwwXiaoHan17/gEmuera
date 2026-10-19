# =============================================================================
# Test-CompatPackLifecycle.ps1 — CompatPack 会话生命周期 P0 静态回归守卫
# =============================================================================
# 背景（设计文档 docs/designs/compat-pack-interface.md §13.6/§13.7，基线 v1.1）：
#   会话拆除必须无条件清理 compat-plan 绑定。若 StopLegacySession 出现
#   `!backend.IsRunning` 式条件早退，Back/Restart/ERB 重启路径（先 EmueraThread.End()）
#   会跳过 GlobalStatic.Reset + Program.ClearCompatibilityPlan，上一局 plan 绑定残留，
#   同进程下一次启动在 hash 防御处失败。
#
# 检查项（exit 0 = 全部通过；exit 1 = 任一失败）：
#   1. scripts/EmueraMain.cs 的 StopLegacySession 方法体内不得出现
#      `IsRunning` 条件早退（if 条件含 IsRunning 且守卫语句 return）。
#   2. scripts/GodotHost/LegacySessionBackend.cs 的 StopLegacyBaselineAsync
#      方法体必须调用 ClearCompatibilityPlan。
#   3. scripts/Emuera/Compatibility/CompatPackHost.cs 不得再出现
#      ActivePackModuleIds / ActiveVariantSelections / ResetActiveSessionProjection
#      （投影数据已内聚进 DialectPlan，禁止 process-wide 静态回潮）。
#   4. src/Core/Compatibility/DialectRuntime.cs 的 DialectPlan 类必须声明
#      PackModuleIds 与 VariantSelections。
#
# 实现说明（为何不会因格式/注释误报）：
#   - 所有检查先做"注释与字符串字面量剥离"（单遍状态机），再按大括号配对提取
#     方法/类体。StopLegacySession 内的 P0 说明注释本身提到 `!backend.IsRunning`，
#     剥离后不会触发检查 1；字符串内的 `{}`（如插值格式串）不会干扰大括号配对。
#   - 已知限制：插值字符串洞内再嵌引号（$"...{dict["k"]}..."）这类罕见写法
#     可能导致该字符串提前截断；对四个目标文件的现存代码形态不构成影响。
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/compat-pack/Test-CompatPackLifecycle.ps1 -ProjectRoot <项目根>
#   -ProjectRoot 缺省为当前目录（`.`）。
# =============================================================================

param([string]$ProjectRoot = '.')

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$script:failures = 0

# -----------------------------------------------------------------------------
# 辅助函数
# -----------------------------------------------------------------------------

function Write-CheckResult {
    param([string]$Name, [bool]$Passed, [string]$Detail)
    if ($Passed) {
        Write-Output ("[PASS] {0}" -f $Name)
    }
    else {
        Write-Output ("[FAIL] {0}" -f $Name)
        $script:failures++
    }
    if ($Detail) { Write-Output ("       {0}" -f $Detail) }
}

function Read-SourceText {
    param([string]$RelativePath)
    $full = [System.IO.Path]::Combine($ProjectRoot, $RelativePath)
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { return $null }
    return [System.IO.File]::ReadAllText($full, [System.Text.Encoding]::UTF8)
}

<#
.SYNOPSIS
  单遍状态机：把 C# 源文本中的注释与字符串/字符字面量内容替换为空白，
  保留换行与代码骨架。用于后续的标识符检查与大括号配对。
#>
function Remove-CsCommentsAndLiterals {
    param([string]$Text)
    $out = New-Object System.Text.StringBuilder
    [void]$out.EnsureCapacity($Text.Length)
    $state = 'code'   # code | lineComment | blockComment | str | char
    for ($i = 0; $i -lt $Text.Length; $i++) {
        $c = $Text[$i]
        if ($i + 1 -lt $Text.Length) { $next = $Text[$i + 1] } else { $next = [char]0 }
        switch ($state) {
            'code' {
                if ($c -eq '/' -and $next -eq '/') { $state = 'lineComment'; [void]$out.Append(' '); $i++ }
                elseif ($c -eq '/' -and $next -eq '*') { $state = 'blockComment'; [void]$out.Append(' '); $i++ }
                elseif ($c -eq '"') { $state = 'str'; [void]$out.Append(' ') }
                elseif ($c -eq "'") { $state = 'char'; [void]$out.Append(' ') }
                else { [void]$out.Append($c) }
            }
            'lineComment' {
                if ($c -eq "`n") { $state = 'code'; [void]$out.Append("`n") }
            }
            'blockComment' {
                if ($c -eq '*' -and $next -eq '/') { $state = 'code'; $i++; [void]$out.Append(' ') }
            }
            'str' {
                if ($c -eq '\') { $i++ }
                elseif ($c -eq '"') { $state = 'code' }
            }
            'char' {
                if ($c -eq '\') { $i++ }
                elseif ($c -eq "'") { $state = 'code' }
            }
        }
    }
    return $out.ToString()
}

<#
.SYNOPSIS
  在（已剥离注释/字符串的）文本上做括号配对，返回 @{Start=开括号下标; End=闭括号下标}，
  配对失败返回 $null。
#>
function Get-BracketSpan {
    param([string]$Text, [int]$OpenIndex, [char]$OpenChar, [char]$CloseChar)
    $depth = 0
    for ($i = $OpenIndex; $i -lt $Text.Length; $i++) {
        $ch = $Text[$i]
        if ($ch -eq $OpenChar) { $depth++ }
        elseif ($ch -eq $CloseChar) {
            $depth--
            if ($depth -eq 0) { return @{ Start = $OpenIndex; End = $i } }
        }
    }
    return $null
}

<#
.SYNOPSIS
  按"签名正则 → 首个 '{' → 大括号配对"提取方法/类体（要求输入已剥离注释与字符串）。
  找不到返回 $null。
#>
function Get-CsBracedBody {
    param([string]$StrippedText, [string]$SignaturePattern)
    $m = [regex]::Match($StrippedText, $SignaturePattern)
    if (-not $m.Success) { return $null }
    $open = $StrippedText.IndexOf('{', $m.Index + $m.Length)
    if ($open -lt 0) { return $null }
    $span = Get-BracketSpan -Text $StrippedText -OpenIndex $open -OpenChar '{' -CloseChar '}'
    if ($span -eq $null) { return $null }
    return $StrippedText.Substring($span.Start + 1, $span.End - $span.Start - 1)
}

<#
.SYNOPSIS
  检查项 1 的核心：在方法体文本中查找 "if 条件含 IsRunning 且其守卫语句包含 return"
  的条件早退模式。返回违例描述列表（空列表 = 无违例）。
#>
function Find-IsRunningEarlyExit {
    param([string]$MethodBody)
    $issues = @()
    foreach ($m in [regex]::Matches($MethodBody, '\bif\b')) {
        $condStart = $MethodBody.IndexOf('(', $m.Index + $m.Length)
        if ($condStart -lt 0) { continue }
        $between = $MethodBody.Substring($m.Index + $m.Length, $condStart - $m.Index - $m.Length)
        if ([regex]::IsMatch($between, '\S')) { continue }   # if 与 ( 之间只能是空白
        $condSpan = Get-BracketSpan -Text $MethodBody -OpenIndex $condStart -OpenChar '(' -CloseChar ')'
        if ($condSpan -eq $null) { continue }
        $condText = $MethodBody.Substring($condSpan.Start + 1, $condSpan.End - $condSpan.Start - 1)
        if (-not [regex]::IsMatch($condText, 'IsRunning')) { continue }

        # 找到含 IsRunning 的条件：检查其守卫语句是否为早退（return）
        $stmtStart = $condSpan.End + 1
        while ($stmtStart -lt $MethodBody.Length -and [char]::IsWhiteSpace($MethodBody[$stmtStart])) { $stmtStart++ }
        if ($stmtStart -ge $MethodBody.Length) { continue }
        if ($MethodBody[$stmtStart] -eq '{') {
            $blkSpan = Get-BracketSpan -Text $MethodBody -OpenIndex $stmtStart -OpenChar '{' -CloseChar '}'
            if ($blkSpan -ne $null) {
                $blkText = $MethodBody.Substring($blkSpan.Start + 1, $blkSpan.End - $blkSpan.Start - 1)
                if ([regex]::IsMatch($blkText, '\breturn\b')) {
                    $issues += ("if ({0}) {{ ... return ... }}（条件早退）" -f $condText.Trim())
                }
            }
        }
        else {
            $semi = $MethodBody.IndexOf(';', $stmtStart)
            if ($semi -ge 0) {
                $stmt = $MethodBody.Substring($stmtStart, $semi - $stmtStart)
                if ([regex]::IsMatch($stmt, '\breturn\b')) {
                    $issues += ("if ({0}) return;（条件早退）" -f $condText.Trim())
                }
            }
        }
    }
    return $issues
}

# -----------------------------------------------------------------------------
# 主流程
# -----------------------------------------------------------------------------

$rootDisplay = (Resolve-Path -LiteralPath $ProjectRoot).Path
Write-Output "=== CompatPack 会话生命周期 P0 静态回归守卫 ==="
Write-Output ("项目根: {0}" -f $rootDisplay)
Write-Output ""

$emueraMainText = Read-SourceText 'scripts/EmueraMain.cs'
$backendText    = Read-SourceText 'scripts/GodotHost/LegacySessionBackend.cs'
$hostText       = Read-SourceText 'scripts/Emuera/Compatibility/CompatPackHost.cs'
$dialectText    = Read-SourceText 'src/Core/Compatibility/DialectRuntime.cs'

# ---- 检查 1：StopLegacySession 不得出现 IsRunning 条件早退 -------------------
$check1Name = '检查1/4: StopLegacySession 方法体不得包含 IsRunning 条件早退（scripts/EmueraMain.cs）'
if ($null -eq $emueraMainText) {
    Write-CheckResult -Name $check1Name -Passed $false -Detail '文件缺失: scripts/EmueraMain.cs'
}
else {
    $stripped = Remove-CsCommentsAndLiterals -Text $emueraMainText
    $body = Get-CsBracedBody -StrippedText $stripped -SignaturePattern 'void\s+StopLegacySession\s*\('
    if ($null -eq $body) {
        Write-CheckResult -Name $check1Name -Passed $false -Detail '未找到 void StopLegacySession 方法体（可能被重命名或移动，需人工确认）'
    }
    else {
        $issues = Find-IsRunningEarlyExit -MethodBody $body
        if ($issues.Count -eq 0) {
            Write-CheckResult -Name $check1Name -Passed $true -Detail '方法体内未检测到 IsRunning 条件早退，StopLegacyBaselineAsync 为无条件调用'
        }
        else {
            $detail = "检测到 {0} 处 IsRunning 条件早退: {1} —— Back/Restart/ERB 重启会跳过会话清理，plan 绑定残留导致同进程下一次启动 hash 防御失败（设计 §13.6 P0）" -f $issues.Count, ($issues -join ' ; ')
            Write-CheckResult -Name $check1Name -Passed $false -Detail $detail
        }
    }
}

# ---- 检查 2：StopLegacyBaselineAsync 必须调用 ClearCompatibilityPlan ---------
$check2Name = '检查2/4: StopLegacyBaselineAsync 必须调用 ClearCompatibilityPlan（scripts/GodotHost/LegacySessionBackend.cs）'
if ($null -eq $backendText) {
    Write-CheckResult -Name $check2Name -Passed $false -Detail '文件缺失: scripts/GodotHost/LegacySessionBackend.cs'
}
else {
    $stripped = Remove-CsCommentsAndLiterals -Text $backendText
    $body = Get-CsBracedBody -StrippedText $stripped -SignaturePattern 'ValueTask\s+StopLegacyBaselineAsync\s*\('
    if ($null -eq $body) {
        Write-CheckResult -Name $check2Name -Passed $false -Detail '未找到 StopLegacyBaselineAsync 方法体（可能被重命名或移动，需人工确认）'
    }
    elseif ([regex]::IsMatch($body, '\bClearCompatibilityPlan\s*\(')) {
        Write-CheckResult -Name $check2Name -Passed $true -Detail '已调用 ClearCompatibilityPlan，plan 绑定随会话拆除清理'
    }
    else {
        Write-CheckResult -Name $check2Name -Passed $false -Detail 'StopLegacyBaselineAsync 方法体内未调用 ClearCompatibilityPlan —— 会话拆除不清 plan 绑定，同进程下一次启动 hash 防御失败（设计 §13.6 P0）'
    }
}

# ---- 检查 3：CompatPackHost 不得再现投影静态 --------------------------------
$check3Name = '检查3/4: CompatPackHost 不得再现 ActivePackModuleIds / ActiveVariantSelections / ResetActiveSessionProjection（scripts/Emuera/Compatibility/CompatPackHost.cs）'
if ($null -eq $hostText) {
    Write-CheckResult -Name $check3Name -Passed $false -Detail '文件缺失: scripts/Emuera/Compatibility/CompatPackHost.cs'
}
else {
    $stripped = Remove-CsCommentsAndLiterals -Text $hostText
    $forbidden = @('ActivePackModuleIds', 'ActiveVariantSelections', 'ResetActiveSessionProjection')
    $found = @()
    foreach ($symbol in $forbidden) {
        if ([regex]::IsMatch($stripped, ('\b' + [regex]::Escape($symbol) + '\b'))) { $found += $symbol }
    }
    if ($found.Count -eq 0) {
        Write-CheckResult -Name $check3Name -Passed $true -Detail '投影数据仍内聚于 DialectPlan，无 process-wide 静态回潮'
    }
    else {
        Write-CheckResult -Name $check3Name -Passed $false -Detail ("发现被删除的投影静态回潮: {0} —— 投影数据必须来自 DialectPlan.PackModuleIds/VariantSelections（设计 §13.4）" -f ($found -join ' / '))
    }
}

# ---- 检查 4：DialectPlan 必须声明 PackModuleIds 与 VariantSelections ---------
$check4Name = '检查4/4: DialectPlan 必须声明 PackModuleIds 与 VariantSelections（src/Core/Compatibility/DialectRuntime.cs）'
if ($null -eq $dialectText) {
    Write-CheckResult -Name $check4Name -Passed $false -Detail '文件缺失: src/Core/Compatibility/DialectRuntime.cs'
}
else {
    $stripped = Remove-CsCommentsAndLiterals -Text $dialectText
    $body = Get-CsBracedBody -StrippedText $stripped -SignaturePattern 'class\s+DialectPlan\b'
    if ($null -eq $body) {
        Write-CheckResult -Name $check4Name -Passed $false -Detail '未找到 DialectPlan 类体（可能被重命名或移动，需人工确认）'
    }
    else {
        $missing = @()
        if (-not [regex]::IsMatch($body, '\bPackModuleIds\s*(=>|\{)')) { $missing += 'PackModuleIds' }
        if (-not [regex]::IsMatch($body, '\bVariantSelections\s*(=>|\{)')) { $missing += 'VariantSelections' }
        if ($missing.Count -eq 0) {
            Write-CheckResult -Name $check4Name -Passed $true -Detail 'PackModuleIds 与 VariantSelections 均有声明，投影数据随 plan 会话内聚'
        }
        else {
            Write-CheckResult -Name $check4Name -Passed $false -Detail ("DialectPlan 缺少声明: {0} —— 投影数据被移出 plan 会使会话清理与 hash 链失效（设计 §13.4）" -f ($missing -join ' / '))
        }
    }
}

Write-Output ""
if ($script:failures -gt 0) {
    Write-Output ("=== 结果: {0} 项失败（4 项检查） ===" -f $script:failures)
    exit 1
}
else {
    Write-Output "=== 结果: 4/4 项检查全部通过 ==="
    exit 0
}
