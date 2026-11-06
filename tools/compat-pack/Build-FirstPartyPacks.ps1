# =============================================================================
# Build-FirstPartyPacks.ps1 -- 第一方兼容包 staging（导出前必跑）
# =============================================================================
# 用途（执行设计 §6.1）：把 5 个第一方数据包工程 Release/net8.0 产物拷贝到
#   <ProjectRoot>/compat_packs/<packId>/<packId>.dll
# 供 export_presets.cfg 的 include_filter 打进 PCK；启动时由
# FirstPartyPackBootstrap 释放到安装根（桌面 exe 同级 / Android 主存储 packs）。
#
# 规则（fail-closed）：
#   - 一包一目录；目录内只允许 <packId>.dll（loader 拒载其它形态/多余文件）。
#   - staging 集合必须与 assets/text/pack-profile-map.json 的第一方 packId 集合一致。
#   - 任一构建/拷贝/校验失败退出 1，绝不留下"少一包"的 staging 被导出。
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/compat-pack/Build-FirstPartyPacks.ps1
#   powershell ... -ProjectRoot <项目根> [-Configuration Release]
#
# 注意：本文件必须保存为 UTF-8 BOM（Windows PowerShell 5.1 读中文注释）。
# =============================================================================

[CmdletBinding()]
param(
    [string]$ProjectRoot = '.',
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

function Fail([string]$Message) {
    Write-Output ('[FAIL] ' + $Message)
    exit 1
}

$root = (Resolve-Path -LiteralPath $ProjectRoot).Path
$packsRoot = Join-Path $root 'packs'
$stagingRoot = Join-Path $root 'compat_packs'
$mapPath = Join-Path $root 'assets/text/pack-profile-map.json'

if (-not (Test-Path -LiteralPath $mapPath -PathType Leaf)) {
    Fail ('缺少 profile 映射：' + $mapPath)
}

# 第一方 staging 集合 = 生产映射里 pack.gemuera.* 条目（唯一事实来源）。
$map = Get-Content -LiteralPath $mapPath -Raw -Encoding UTF8 | ConvertFrom-Json
$mapPackIds = @()
foreach ($entry in @($map.profiles)) {
    $packId = [string]$entry.packId
    if ($packId -like 'pack.gemuera.*') { $mapPackIds += $packId }
}
$mapPackIds = @($mapPackIds | Sort-Object -Unique)
if ($mapPackIds.Count -eq 0) {
    Fail '生产映射未声明任何 pack.gemuera.* 第一方包。'
}

if (Test-Path -LiteralPath $stagingRoot) {
    # 只清理第一方目录，保留用户手放的其它包（staging 是构建产物目录）。
    foreach ($dir in @(Get-ChildItem -LiteralPath $stagingRoot -Directory -ErrorAction SilentlyContinue)) {
        if ($mapPackIds -contains $dir.Name) {
            Remove-Item -LiteralPath $dir.FullName -Recurse -Force
        }
    }
}
else {
    New-Item -ItemType Directory -Path $stagingRoot | Out-Null
}

$failures = New-Object System.Collections.Generic.List[string]
foreach ($packId in $mapPackIds) {
    $project = Join-Path $packsRoot ($packId + '/' + $packId + '.csproj')
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        $failures.Add(('缺少包工程：' + $project))
        continue
    }

    Write-Output ('[BUILD] ' + $packId)
    & dotnet build $project -c $Configuration -nodeReuse:false -m:1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        $failures.Add(('构建失败（exit=' + $LASTEXITCODE + '）：' + $packId))
        continue
    }

    $builtDll = Join-Path $packsRoot ($packId + '/bin/' + $Configuration + '/net8.0/' + $packId + '.dll')
    if (-not (Test-Path -LiteralPath $builtDll -PathType Leaf)) {
        $failures.Add(('构建产物不存在：' + $builtDll))
        continue
    }

    $targetDir = Join-Path $stagingRoot $packId
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    Copy-Item -LiteralPath $builtDll -Destination (Join-Path $targetDir ($packId + '.dll')) -Force
}

# staging 完整性校验：每个第一方 packId 恰好一个主 DLL、非空、目录内无杂散条目。
$issues = New-Object System.Collections.Generic.List[string]
$actualDirs = @(Get-ChildItem -LiteralPath $stagingRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object { $_.Name } | Sort-Object)
foreach ($packId in $mapPackIds) {
    if ($actualDirs -notcontains $packId) {
        $issues.Add(('staging 缺目录：' + $packId))
        continue
    }
    $dir = Join-Path $stagingRoot $packId
    $files = @(Get-ChildItem -LiteralPath $dir -File)
    $expected = $packId + '.dll'
    if ($files.Count -ne 1 -or $files[0].Name -ne $expected) {
        $issues.Add(('staging 目录形态错误：' + $packId + ' -> ' + (($files | ForEach-Object { $_.Name }) -join ',')))
        continue
    }
    if ($files[0].Length -le 0) {
        $issues.Add(('staging DLL 为空：' + $packId))
    }
}
foreach ($dir in $actualDirs) {
    if ($mapPackIds -notcontains $dir) { $issues.Add(('staging 含映射外第一方/未知目录：' + $dir)) }
}
if ($failures.Count -gt 0 -or $issues.Count -gt 0) {
    foreach ($failure in $failures) { Write-Output ('[FAIL] ' + $failure) }
    foreach ($issue in $issues) { Write-Output ('[FAIL] ' + $issue) }
    Fail ('第一方包 staging 未完成（失败 ' + $failures.Count + ' 项 / 校验 ' + $issues.Count + ' 项）。')
}

Write-Output ''
Write-Output ('=== 结果: PASS（' + $mapPackIds.Count + ' 个第一方包 staging 完整：' + $stagingRoot + '） ===')
exit 0
