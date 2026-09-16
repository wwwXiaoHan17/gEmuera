<#
Test-PureMove.ps1 —— 纯移动拆分的机械校验器（常驻工具）

WHY / 用途
  证明一次 FILE_STANDARD §3 模式 A/B 拆分确实是"纯移动"（零逻辑改动）。
  §4.5 允许的机械 diff：成员搬移、分片头注释、主文件因迁出而失效的 using 清理、空 #region 删除，
  以及 partial 分片必需的外壳（namespace 声明 / 类型声明 / 最外层包裹大括号）。

原理（两道互相独立的检查）
  1) 行多重集比对：取出同一「文件家族」（主文件 + Type.Feature.cs 分片）在 git 基线（-BaseRef）与
     工作区的全部代码行，归一化后做**多重集**比对。它证明"没有行丢失 / 没有行新增"，
     但**对顺序不敏感** —— 只能证明"行都在"，不能证明"没被重排"。
  2) 保序（subsequence）检查：对每个「基线里不存在的文件」（本次新建的分片），验证它的正文行序列
     是基线主文件正文行序列的**子序列**（允许有间隔、不要求连续）。C# 的 static readonly 字段
     初始化器按文本顺序执行，成员换序是真实的行为风险；多重集比对看不见它，第 2 步就是补这个缺口。

归一化口径（两道检查共用）
  去行首尾空白；丢空行、**using 指令**（保留 `using (…)` 语句，见下）、#region/#endregion、整行注释（//   /*   *   */）。
  再（除非 -RawNormalize）剥掉"分片外壳"：namespace 声明、类型声明（含特性行 / # 指令行）、最外层包裹大括号。
    · 剥壳是**结构判定**，不是硬编码的"去掉前 N 行"：反复执行下述剥离 —— 区域内首个把大括号深度从 0
      抬到 >0 的行是候选开块行；该块必须**恰好**包到区域末尾（从开块行起累计深度"首次"回到 0 的行
      == 区域末行）；且开块行本身是 namespace / 类型声明，或它只是一个单独的 '{' 且其上方前缀
      **全部**是外壳行并出现过类型声明或 namespace 声明。三者同时成立才剥掉"前缀 + 最外层大括号"。
      这几条都是为了防真实踩过的坑：只看"最后一个回到 0 的行"会把类体里每个成员结束都算成块尾
      （Canvas.cs 被多剥 6 行）；不检查前缀全为外壳会把类体第一个成员之后的大段成员整段丢掉
      （EmueraContent.cs 被剥掉 379 行）；前缀只认类型声明会让块状 namespace 的外壳剥不掉
      （Creator.Method 家族 3 个分片多出 18 行外壳）。因此"文件里唯一一个方法"这类块不会被误当外壳剥掉。
    · 括号深度按行统计，统计时跳过普通字符串 / 逐字字符串 @"..." / 字符字面量 / 行尾 // 注释里的括号。
    · 已知残余误差：插值字符串 $"..." 的 {} 洞、跨行 /* */ 块注释里的括号会被计入。它只会让剥离
      "少剥一层"（保守方向），不会造成假通过。
    · 已知覆盖边界：一个文件里有多个顶层类型时（例：Scripts/GenericUtils.cs 以 public enum 开头，
      类声明不在文件最外层），最外层并不包住整个文件，剥壳会整份跳过 —— 该文件的顶层声明行因此
      会留在正文里参与比对（两侧对称，不会产生假通过；只有真的改了那几行才会报差异）。
  · 类型声明行上的 partial 修饰符单独归一化（-creplace '\bpartial\s+' -> ''）：模式 A 拆分必然要给
    主文件的类声明补 partial，否则分片无法共用类型；不抹掉它，每个"主文件原本不是 partial"的纯移动
    拆分都会固定多报 1 行差异（实测：GenericUtils.cs 家族仅因此多报 internal static class GenericUtils）。
    C# 里增删 partial 不改变语义，因此这一步不会掩盖行为风险。
  正文（body）= 归一化 + 剥壳之后剩下的代码行。

BaseRef 与退化保护
  -BaseRef 留空时回落到仓库根的 reports\.base-commit（本地基线指针，reports/ 不入库）。
  若 -BaseRef 解析出的 commit 与 HEAD 相同，脚本**拒绝运行**并 exit 2：提交之后再跑，比对会退化成
  "拿拆分后的自己比自己"，所有家族都会被误报成 PURE MOVE（本仓库真实踩过：提交后复核 5 个家族全报 PURE MOVE）。

编码（Windows PowerShell 5.1 陷阱）
  本机 PowerShell 5.1 会按 GBK 解码无 BOM 的 UTF-8 .ps1，中文串被破坏后可能直接产生语法错误，
  因此本文件必须保留 UTF-8 BOM。用 write/edit 工具改过本文件后 BOM 会丢，用下面两行补回：
    $p='tools\pure-move\Test-PureMove.ps1'; $t=[IO.File]::ReadAllText($p,[Text.Encoding]::UTF8)
    [IO.File]::WriteAllText($p,$t,(New-Object Text.UTF8Encoding($true)))
  git 对象内容一律用 cmd 重定向落地为字节文件，再以 UTF-8 读回 —— **不要**直接捕获原生命令输出，
  PS 5.1 会按 GBK 解码成 mojibake。

退出码
  0 = 行多重集一致 且 保序检查全部 OK
  1 = 行多重集存在非机械差异（需人工裁定）
  2 = 拒绝运行（-BaseRef 解析为 HEAD）
  3 = 行多重集一致，但存在分片正文乱序（ORDER: VIOLATION）

本工具**只读**源码：除 <Repo>\artifacts\pure-move-tmp\ 这个临时落地目录外不写任何文件。

用法
  powershell -NoProfile -File tools\pure-move\Test-PureMove.ps1 -Family Scripts/EmueraContent.cs
  powershell -NoProfile -File tools\pure-move\Test-PureMove.ps1 -Family Scripts/GenericUtils.cs -ShowDetail
  powershell -NoProfile -File tools\pure-move\Test-PureMove.ps1 `
      -Family Scripts/Emuera/GameData/Variable/VariableToken.cs `
      -Include Scripts/Emuera/GameData/Variable/VariableData.ReferenceTokens.cs
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Family,
    [string]$Repo = '',
    # 留空 = 回落到 <Repo>\reports\.base-commit
    [string]$BaseRef = '',
    [switch]$ShowDetail,
    # 显式追加的家族成员（相对仓库根的路径，/ 或 \ 均可）。
    # 必需场景：分片名与主文件不同名时。例：VariableToken.cs 里装的其实是 VariableData 的实现，
    # 分片按事实命名为 VariableData.ReferenceTokens.cs，无法被 "VariableToken*" 通配命中。
    [string[]]$Include = @(),
    # 关闭"分片外壳"归一化，退回严格逐行多重集（新建分片的类型声明/大括号会被算成新增行）。
    # 注意：类型声明行上的 partial 归一化不受本开关影响，仍然生效。
    [switch]$RawNormalize,
    # git 对象临时落地目录，默认 <Repo>\artifacts\pure-move-tmp（artifacts/ 不入库）
    [string]$WorkDir = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Repo)) {
    $Repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
}
$Repo = (Resolve-Path -LiteralPath $Repo).Path

# ================= BaseRef 解析 + 退化保护 =================
$baseSource = '-BaseRef'
if ([string]::IsNullOrWhiteSpace($BaseRef)) {
    $baseCommitFile = Join-Path $Repo 'reports\.base-commit'
    if (-not (Test-Path -LiteralPath $baseCommitFile -PathType Leaf)) {
        throw "BaseRef not specified and $baseCommitFile not found. Pass -BaseRef <ref> explicitly."
    }
    $raw = [IO.File]::ReadAllText($baseCommitFile, [System.Text.Encoding]::UTF8)
    foreach ($ln in ($raw -split "`r?`n")) {
        if (-not [string]::IsNullOrWhiteSpace($ln)) { $BaseRef = $ln.Trim(); break }
    }
    if ([string]::IsNullOrWhiteSpace($BaseRef)) { throw "reports/.base-commit has no usable ref: $baseCommitFile" }
    $baseSource = 'reports/.base-commit'
}

$baseSha = ([string]((& git -C $Repo rev-parse --verify --quiet "$BaseRef^{commit}" 2>$null))).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($baseSha)) {
    throw "Cannot resolve -BaseRef '$BaseRef' to a commit in $Repo."
}
$headSha = ([string]((& git -C $Repo rev-parse --verify --quiet HEAD 2>$null))).Trim()
if ([string]::IsNullOrWhiteSpace($headSha)) { throw "Cannot resolve HEAD in $Repo." }

if ($baseSha -eq $headSha) {
    Write-Output "REFUSE: -BaseRef '$BaseRef' (from $baseSource) resolves to HEAD ($headSha)."
    Write-Output "Comparing the workspace against HEAD degenerates into comparing the split result with"
    Write-Output "itself: every family is then reported as PURE MOVE. Point -BaseRef at the pre-split"
    Write-Output "commit (or update reports\.base-commit) and run again."
    exit 2
}

# ================= 归一化 =================

function Get-CodeEntries {
    # 归一化：去首尾空白、丢空行/using/#region/整行注释、抹掉类型声明上的 partial 修饰符，
    # 保留原始行号用于定位。
    param([string]$Text)
    $out = New-Object System.Collections.Generic.List[object]
    $ln = 0
    foreach ($l in ($Text -split "`r?`n")) {
        $ln += 1
        $t = $l.Trim()
        if ($t -eq '') { continue }
        # 只丢 using 指令（using X.Y; / using Alias = X;）。**必须保留 using 语句**（using (var x = ...) { … }）：
        # 后者是方法体内的真实代码，若一并丢掉，校验器对这类行的丢失/改写会完全失明
        # （实测全仓有 79 行分布在 18 个文件里，不是可以忽略的量）。
        if ($t -cmatch '^using\s' -and $t -cnotmatch '^using\s*\(') { continue }
        if ($t -cmatch '^#region\b' -or $t -cmatch '^#endregion\b') { continue }
        if ($t.StartsWith('//')) { continue }
        if ($t.StartsWith('/*') -or $t.StartsWith('*') -or $t.StartsWith('*/')) { continue }
        # §4.5 机械变化：模式 A 拆分必然要给主文件的类声明补上 partial 修饰符（否则分片无法共用类型）。
        # 把它归一化掉，否则每个"主文件原本不是 partial"的纯移动拆分都会固定多出 1 行差异
        # （实测：GenericUtils.cs 家族仅因此多报 [1x] internal static class GenericUtils）。
        # 只对类型声明行生效，且只抹掉 partial 这一个关键字：C# 里增删 partial 不改变语义。
        if (Test-TypeDeclLine $t) { $t = ($t -creplace '\bpartial\s+', '') }
        $out.Add([pscustomobject]@{ Line = $ln; Text = $t })
    }
    # 注意：这里**不要**写成 `return ,$out`。带逗号返回会把 List 当成单个对象塞进调用方的 @()，
    # 于是 $e 变成 List 本身、$e.Text 触发 PowerShell 成员枚举返回整列字符串，
    # 多重集 key 会塌成"一整份文件拼成的一行"。去掉逗号让管道正常逐项展开。
    return $out
}

function Get-BraceDelta {
    # 统计一行里"代码层面"的括号净变化，跳过字符串/字符字面量与行尾 // 注释。
    # 用字符码比较（47='/',64='@',34='"',39="'",123='{',125='}'），避免 PS 的 char/string 隐式转换歧义。
    param([string]$Line)
    $d = 0
    $i = 0
    $n = $Line.Length
    while ($i -lt $n) {
        $c = [int]$Line[$i]
        if ($c -eq 47) {                                             # `/`
            if ((($i + 1) -lt $n) -and ([int]$Line[$i + 1] -eq 47)) { break }   # 行尾注释，后面都没有代码
            $i += 1
            continue
        }
        if ($c -eq 64 -and ($i + 1) -lt $n -and [int]$Line[$i + 1] -eq 34) {   # @" : 逐字字符串
            $i += 2
            while ($i -lt $n) {
                if ([int]$Line[$i] -eq 34) {
                    if ((($i + 1) -lt $n) -and ([int]$Line[$i + 1] -eq 34)) { $i += 2; continue }  # "" 转义
                    $i += 1
                    break
                }
                $i += 1
            }
            continue
        }
        if ($c -eq 34) {                                             # 普通字符串 "..."（含 $"..." 的引号部分）
            $i += 1
            while ($i -lt $n) {
                if ([int]$Line[$i] -eq 92) { $i += 2; continue }       # 反斜杠转义
                if ([int]$Line[$i] -eq 34) { $i += 1; break }
                $i += 1
            }
            continue
        }
        if ($c -eq 39) {                                             # 字符字面量 '...'
            $i += 1
            while ($i -lt $n) {
                if ([int]$Line[$i] -eq 92) { $i += 2; continue }
                if ([int]$Line[$i] -eq 39) { $i += 1; break }
                $i += 1
            }
            continue
        }
        if ($c -eq 123) { $d += 1 }
        elseif ($c -eq 125) { $d -= 1 }
        $i += 1
    }
    return $d
}

function Test-TypeDeclLine {
    # 类型声明行：<修饰符>* class|struct|interface|record|enum ...
    # 大小写敏感（-cmatch）：C# 关键字全小写，这样 "StructFoo"/"GetClass"/"enumValue" 不会被误判。
    param([string]$Text)
    return ($Text -cmatch '^(?:(?:public|internal|private|protected|static|sealed|abstract|partial|unsafe|file|new)\s+)*(?:class|struct|interface|record|enum)\b')
}

function Test-ShellLine {
    # 外壳行 = 允许出现在"被剥掉的包裹层"之前的行：类型声明、namespace 声明、特性行、# 指令行。
    # 成员声明（字段/属性/方法签名）都不算外壳行 —— 这正是防"整段成员被当外壳丢掉"的关键。
    param([string]$Text)
    if ($Text -cmatch '^namespace\b') { return $true }
    if ($Text.StartsWith('[')) { return $true }
    if ($Text.StartsWith('#')) { return $true }
    return (Test-TypeDeclLine $Text)
}

function Get-Body {
    # 归一化 + 剥壳 -> 正文。剥壳规则见文件头注释。
    param([object[]]$Entries)
    if ($null -eq $Entries -or $Entries.Count -eq 0) { return @() }
    $n = $Entries.Count

    $delta = New-Object 'int[]' $n
    for ($i = 0; $i -lt $n; $i++) {
        $t = $Entries[$i].Text
        if ($t.IndexOf('{') -lt 0 -and $t.IndexOf('}') -lt 0) { $delta[$i] = 0 }
        else { $delta[$i] = Get-BraceDelta $t }
    }

    $lo = 0
    $hi = $n - 1
    $guard = 0
    while ($hi -gt $lo -and $guard -lt 8) {
        $guard += 1
        # (a) 首个把大括号深度从 0 抬到 >0 的行
        $depth = 0; $openIdx = -1
        for ($i = $lo; $i -le $hi; $i++) {
            $d0 = $depth
            $depth += $delta[$i]
            if ($d0 -eq 0 -and $depth -gt 0) { $openIdx = $i; break }
        }
        if ($openIdx -lt 0) { break }
        # (b) 该块必须**恰好**包到区域末尾：从 openIdx 起累计深度"首次"回到 0 的行 == 区域末行。
        #     不能写成"最后一个回到 0 的行"——类体里每个成员结束都会回到 0，那种写法几乎恒真，
        #     会把类体开头的嵌套类型当外壳剥掉（实测：Canvas.cs 因此被剥掉 6 行而不是 3 行）。
        $depth = 0; $closeIdx = -1
        for ($i = $openIdx; $i -le $hi; $i++) {
            $depth += $delta[$i]
            if ($depth -eq 0) { $closeIdx = $i; break }
        }
        if ($closeIdx -ne $hi) { break }
        # (c) 开块行必须是"外壳"：namespace/类型声明本身，或单独的 '{'。
        #     且**开块行之前的每一行前缀也必须是外壳**——只查"前缀里出现过类型声明"是不够的：
        #     类体里第一个成员之后出现的第一个 '{' 同样满足 (a)(b)，会把中间的成员整段丢掉
        #     （实测踩过：EmueraContent.cs 被剥掉 379 行，多重集比对随之产生假差异）。
        $openText = $Entries[$openIdx].Text
        $openIsWrapper = $false
        if ($openText -cmatch '^namespace\b') { $openIsWrapper = $true }
        elseif (Test-TypeDeclLine $openText) { $openIsWrapper = $true }

        if ($openIsWrapper) {
            for ($i = $lo; $i -lt $openIdx; $i++) {
                if (-not (Test-ShellLine $Entries[$i].Text)) { $openIsWrapper = $false; break }
            }
        } elseif ($openText -ceq '{') {
            # 前缀里必须出现"包裹性声明"：类型声明或 namespace 声明。
            # 二者都要认：块状 namespace（namespace X { ... class C { ... } }）里，
            # 标在第一层的 '{' 上方只有 namespace 行，只认类型声明会导致整层外壳剥不掉
            # （实测：Creator.Method 家族 3 个分片因此多出 18 行外壳，被误报成非机械差异）。
            $prefixHasWrapper = $false
            for ($i = $lo; $i -lt $openIdx; $i++) {
                $pt = $Entries[$i].Text
                if (-not (Test-ShellLine $pt)) { $prefixHasWrapper = $false; break }
                if ((Test-TypeDeclLine $pt) -or ($pt -cmatch '^namespace\b')) { $prefixHasWrapper = $true }
            }
            $openIsWrapper = $prefixHasWrapper
        }
        if (-not $openIsWrapper) { break }
        if ($openIdx -ge ($closeIdx - 1)) { break }   # 空外壳，别剥成空
        $lo = $openIdx + 1
        $hi = $closeIdx - 1
    }

    if ($hi -lt $lo) { return @() }
    $out = New-Object System.Collections.Generic.List[object]
    for ($i = $lo; $i -le $hi; $i++) { $out.Add($Entries[$i]) }
    return $out     # 同 Get-CodeEntries：不要带逗号返回，否则调用方 @() 会套一层
}

function Add-ToMultiSet {
    param($Set, [string]$Line)
    if ($Set.ContainsKey($Line)) { $Set[$Line] = $Set[$Line] + 1 } else { $Set[$Line] = 1 }
}

function Test-Subsequence {
    # 两指针贪心：i 扫目标（基线主文件正文），j 扫分片正文；匹配则 j++。j 走到底即为子序列。
    # 贪心对"是否为子序列"是最优判定；失败时返回已匹配深度与第一处无法匹配的分片下标。
    param([object[]]$Target, [object[]]$Shard)
    $i = 0; $j = 0
    $tn = $Target.Count; $sn = $Shard.Count
    while ($j -lt $sn -and $i -lt $tn) {
        if ($Target[$i].Text -ceq $Shard[$j].Text) { $i += 1; $j += 1 } else { $i += 1 }
    }
    return @{ Ok = ($j -ge $sn); J = $j; I = $i }
}

# ================= 家族成员定位 =================
$familyPath = $Family.Replace('/', '\')
$dir        = Split-Path $familyPath -Parent
$stem       = [IO.Path]::GetFileNameWithoutExtension($familyPath)
$rx         = '^' + [regex]::Escape($stem) + '(\.[A-Za-z0-9_]+)?\.cs$'
if ([string]::IsNullOrWhiteSpace($dir)) {
    $dir = ''
    $dirSlash = '.'
    $nowDirAbs = $Repo
} else {
    $dirSlash = $dir.Replace('\', '/')
    $nowDirAbs = Join-Path $Repo $dir
}

if ([string]::IsNullOrWhiteSpace($WorkDir)) { $WorkDir = Join-Path $Repo 'artifacts\pure-move-tmp' }
if (Test-Path -LiteralPath $WorkDir) { Remove-Item -LiteralPath $WorkDir -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

# ---------- 基线（git） ----------
$headList  = & git -C $Repo ls-tree -r --name-only $BaseRef -- $dirSlash
$headFiles = @($headList | Where-Object { $_ -and ([IO.Path]::GetFileName($_)) -match $rx })

$baseBodies = @{}      # 相对路径 -> 归一化后的全部 entries（读盘阶段只做归一化，不剥壳）
try {
    foreach ($f in $headFiles) {
        $safe = ($f -replace '[\\/:*?"<>|]', '_')
        $tmp  = Join-Path $WorkDir $safe
        cmd /c "git -C `"$Repo`" show `"$BaseRef`:$f`" > `"$tmp`""
        if (-not (Test-Path -LiteralPath $tmp) -or (Get-Item -LiteralPath $tmp).Length -eq 0) {
            throw "git show returned no content for $BaseRef`:$f"
        }
        $txt = [IO.File]::ReadAllText($tmp, [System.Text.Encoding]::UTF8)
        $baseBodies[$f] = @(Get-CodeEntries $txt)
    }
} finally {
    Remove-Item -LiteralPath $WorkDir -Recurse -Force -ErrorAction SilentlyContinue
}

# 多重集口径：默认用"正文"（剥壳后），-RawNormalize 时退回归一化全量。
$baseSet = @{}
$baseBodyByFile = @{}
foreach ($f in $headFiles) {
    $baseBodyByFile[$f] = @(Get-Body @($baseBodies[$f]))
    $b = $baseBodyByFile[$f]
    if ($RawNormalize) { $b = @($baseBodies[$f]) }
    foreach ($e in $b) { Add-ToMultiSet $baseSet $e.Text }
}

# ---------- 工作区 ----------
$nowFiles = @()
if (Test-Path -LiteralPath $nowDirAbs) {
    $nowFiles = @(Get-ChildItem -LiteralPath $nowDirAbs -Filter *.cs -File |
        Where-Object { $_.Name -match $rx } |
        ForEach-Object { if ($dirSlash -eq '.') { $_.Name } else { "$dirSlash/$($_.Name)" } })
}
# 显式追加的分片（处理"分片名与主文件不同名"的情形），并去重。
foreach ($inc in $Include) {
    if ([string]::IsNullOrWhiteSpace($inc)) { continue }
    $rel = $inc.Replace('\', '/')
    if ($nowFiles -notcontains $rel) { $nowFiles += $rel }
}

$nowSet = @{}
$nowBodies = @{}
foreach ($f in $nowFiles) {
    $abs = Join-Path $Repo ($f.Replace('/', '\'))
    if (-not (Test-Path -LiteralPath $abs)) { throw "Workspace family member not found: $f" }
    $all = @(Get-CodeEntries ([IO.File]::ReadAllText($abs, [System.Text.Encoding]::UTF8)))
    $nowBodies[$f] = @(Get-Body $all)
    $use = $nowBodies[$f]
    if ($RawNormalize) { $use = $all }
    foreach ($e in $use) { Add-ToMultiSet $nowSet $e.Text }
}

# ---------- 比对：行多重集 ----------
$baseTotal = ($baseSet.Values | Measure-Object -Sum).Sum
$nowTotal  = ($nowSet.Values  | Measure-Object -Sum).Sum
if ($null -eq $baseTotal) { $baseTotal = 0 }
if ($null -eq $nowTotal)  { $nowTotal = 0 }

$onlyBase = @()
foreach ($k in $baseSet.Keys) {
    $n = $nowSet[$k]; if ($null -eq $n) { $n = 0 }
    if ($baseSet[$k] -gt $n) { $onlyBase += [pscustomobject]@{ Count = $baseSet[$k] - $n; Line = $k } }
}
$onlyNow = @()
foreach ($k in $nowSet.Keys) {
    $n = $baseSet[$k]; if ($null -eq $n) { $n = 0 }
    if ($nowSet[$k] -gt $n) { $onlyNow += [pscustomobject]@{ Count = $nowSet[$k] - $n; Line = $k } }
}

$delCount = ($onlyBase | Measure-Object Count -Sum).Sum; if ($null -eq $delCount) { $delCount = 0 }
$addCount = ($onlyNow  | Measure-Object Count -Sum).Sum; if ($null -eq $addCount) { $addCount = 0 }
$multiSetOk = ($onlyBase.Count -eq 0 -and $onlyNow.Count -eq 0)

# ---------- 比对：保序（只查"基线里不存在的文件"，即本次新建的分片） ----------
$mainAtBase = $headFiles | Where-Object { $_ -eq $familyPath.Replace('\', '/') } | Select-Object -First 1
$orderTargets = @()
if ($mainAtBase) { $orderTargets += $mainAtBase }
foreach ($f in ($headFiles | Sort-Object)) {
    if ($f -ne $mainAtBase) { $orderTargets += $f }
}

$newFiles      = @($nowFiles | Where-Object { $headFiles -notcontains $_ })
$orderResults  = @()
foreach ($f in ($newFiles | Sort-Object)) {
    $shard = @($nowBodies[$f])
    if ($shard.Count -eq 0) {
        $orderResults += [pscustomobject]@{ Path = $f; Status = 'EMPTY-BODY'; Target = ''; Body = 0; Line = 0; Text = ''; Matched = 0 }
        continue
    }
    if ($orderTargets.Count -eq 0) {
        # 基线里根本没有家族成员（例如 -Family 指向基线中不存在的文件）：
        # 没有可比对的正文，保序无从判定 —— 显式报出来，而不是凭空造一条"第 0 行失配"。
        $orderResults += [pscustomobject]@{ Path = $f; Status = 'NO-BASELINE'; Target = ''; Body = $shard.Count; Line = 0; Text = ''; Matched = 0 }
        continue
    }
    $hit = $null
    $firstFail = $null
    foreach ($t in $orderTargets) {
        $target = @($baseBodyByFile[$t])
        if ($target.Count -eq 0) { continue }
        $r = Test-Subsequence -Target $target -Shard $shard
        if ($r.Ok) { $hit = $t; break }
        if ($null -eq $firstFail) {
            $j = $r.J
            $firstFail = [pscustomobject]@{
                Target = $t
                Line = $shard[$j].Line
                Text = $shard[$j].Text
                Index = $j + 1
                Matched = $j
            }
        }
    }
    if ($hit) {
        $orderResults += [pscustomobject]@{ Path = $f; Status = 'OK'; Target = $hit; Body = $shard.Count; Line = 0; Text = ''; Matched = $shard.Count }
    } else {
        $orderResults += [pscustomobject]@{
            Path = $f; Status = 'VIOLATION'; Target = $firstFail.Target; Body = $shard.Count
            Line = $firstFail.Line; Text = $firstFail.Text; Matched = $firstFail.Matched
        }
    }
}

# ---------- 输出 ----------
$normNote = if ($RawNormalize) { '严格逐行（未剥分片外壳）' } else { '已剥分片外壳（namespace/类型声明/最外层大括号）' }
Write-Output "=== 纯移动校验：$Family ==="
Write-Output ("基线 (-BaseRef) {0}  来源 {1}  {2}" -f $baseSha.Substring(0, 12), $baseSource, $BaseRef)
Write-Output "HEAD $($headSha.Substring(0,12))"
Write-Output "归一化：$normNote"
Write-Output ""
Write-Output ("基线家族文件：{0}" -f $headFiles.Count)
$headFiles | ForEach-Object { Write-Output "   - $_" }
Write-Output ("工作区家族文件：{0}" -f $nowFiles.Count)
$nowFiles | ForEach-Object { Write-Output "   + $_" }
Write-Output ""
Write-Output ("基线代码行合计：{0}" -f $baseTotal)
Write-Output ("工作区代码行合计：{0}" -f $nowTotal)
Write-Output ("仅基线有（被删除/改写）：{0} 行" -f $delCount)
Write-Output ("仅工作区有（新增/改写）：{0} 行" -f $addCount)
Write-Output ""

if ($multiSetOk) {
    Write-Output "MULTISET: PURE MOVE —— 行多重集完全一致，零行丢失/零行新增。"
} else {
    Write-Output "MULTISET: DIFF —— 存在非机械差异，需人工裁定（§4.5 只允许成员搬移/分片头注释/using 清理/空 #region 删除/分片外壳）。"
}
Write-Output ""

Write-Output ("保序检查（新建分片 {0} 个）" -f $newFiles.Count)
if ($newFiles.Count -eq 0) {
    Write-Output "ORDER: N/A —— 本次没有新建分片，无正文可做子序列比对（多重集结论仍然有效）。"
} else {
    foreach ($r in $orderResults) {
        switch ($r.Status) {
            'OK' {
                Write-Output ("ORDER: OK        + {0}  (正文 {1} 行 ⊂ {2} 正文)" -f $r.Path, $r.Body, $r.Target)
            }
            'EMPTY-BODY' {
                Write-Output ("ORDER: EMPTY-BODY + {0}  (剥壳后正文为空，无法判定——请人工确认该分片不是空壳)" -f $r.Path)
            }
            'NO-BASELINE' {
                Write-Output ("ORDER: NO-BASELINE + {0}  (基线家族里没有任何成员，无法判定保序；同一命令的行多重集一侧必然已报差异)" -f $r.Path)
            }
            default {
                Write-Output ("ORDER: VIOLATION + {0}  (正文 {1} 行，前 {2} 行有序命中 {3}，第 {4} 行失配)" -f `
                    $r.Path, $r.Body, $r.Matched, $r.Target, $r.Line)
                Write-Output ("   分片行号 {0}: {1}" -f $r.Line, $r.Text)
                Write-Output "   说明：该分片正文不是基线主文件正文的子序列 —— 成员相对顺序被改动过。"
                Write-Output "   static readonly 字段初始化器按文本顺序执行，顺序改动是真实行为风险，请人工核对。"
            }
        }
    }
}
Write-Output ""

$orderViolation = @($orderResults | Where-Object { $_.Status -eq 'VIOLATION' })
$orderEmpty     = @($orderResults | Where-Object { $_.Status -eq 'EMPTY-BODY' })

if (-not $multiSetOk) {
    $cap = if ($ShowDetail) { 100000 } else { 40 }
    if ($onlyBase.Count -gt 0) {
        Write-Output "--- 仅基线有 ---"
        $onlyBase | Sort-Object Line | Select-Object -First $cap | ForEach-Object { Write-Output ("  [{0}x] {1}" -f $_.Count, $_.Line) }
        Write-Output ""
    }
    if ($onlyNow.Count -gt 0) {
        Write-Output "--- 仅工作区有 ---"
        $onlyNow | Sort-Object Line | Select-Object -First $cap | ForEach-Object { Write-Output ("  [{0}x] {1}" -f $_.Count, $_.Line) }
        Write-Output ""
    }
}

$orderBad = ($orderViolation.Count -gt 0 -or $orderEmpty.Count -gt 0)

if (-not $multiSetOk -and $orderBad) {
    Write-Output ("结论：FAIL —— 行多重集不一致（仅基线 {0} 行 / 仅工作区 {1} 行），且保序检查未通过（VIOLATION {2} 个 / EMPTY-BODY {3} 个）（exit 1）。" -f `
        $delCount, $addCount, $orderViolation.Count, $orderEmpty.Count)
    exit 1
}
if (-not $multiSetOk) {
    Write-Output ("结论：FAIL —— 行多重集不一致（仅基线 {0} 行 / 仅工作区 {1} 行）（exit 1）。" -f $delCount, $addCount)
    exit 1
}
if ($orderBad) {
    Write-Output ("结论：FAIL —— 行多重集一致，但保序检查未通过（VIOLATION {0} 个 / EMPTY-BODY {1} 个）（exit 3）。" -f $orderViolation.Count, $orderEmpty.Count)
    exit 3
}
Write-Output "结论：PASS —— 行多重集一致，且所有新建分片的正文都是基线主文件正文的子序列（exit 0）。"
Write-Output "注意：本结论只覆盖『行内容无增删 + 正文保序』；编译通过、运行期行为、钉扎/引用路径均不在本工具范围内。"
exit 0
