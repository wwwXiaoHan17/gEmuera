# 构建 gemuera_native 原生库并分发到 export/NativeLibs/（gitignored，不入库）。
#
# 用法（在项目根或任意目录）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/native-build/Build-GemueraNative.ps1 -ProjectRoot <项目根>
#   -SkipDesktop   跳过 Windows x64 构建
#   -SkipAndroid   跳过 Android arm64 交叉编译
#
# 产物：
#   export/NativeLibs/windows-x64/gemuera_native.dll        （桌面 P/Invoke）
#   export/NativeLibs/android/arm64-v8a/libgemuera_native.so（APK jniLibs，16KB page 对齐）
#
# Android 交叉编译依赖：ANDROID_HOME 下存在 ndk/<版本>（toolchains/llvm/prebuilt/windows-x86_64），
# 且已 `rustup target add aarch64-linux-android`。缺 NDK 时跳过并保留桌面产物（不视为失败）。
param(
    [string]$ProjectRoot,
    [switch]$SkipDesktop,
    [switch]$SkipAndroid
)

$ErrorActionPreference = 'Stop'

# 解析 ELF64 的最小 PT_LOAD p_align（字节）。
function Get-ElfLoadAlignmentMin {
    param([string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x7F -or $bytes[1] -ne 0x45) {
        throw "不是合法 ELF: $Path"
    }
    if ($bytes[4] -ne 2) { throw "仅支持 ELF64: $Path" }
    $phoff = [BitConverter]::ToUInt64($bytes, 0x20)
    $phentsize = [BitConverter]::ToUInt16($bytes, 0x36)
    $phnum = [BitConverter]::ToUInt16($bytes, 0x38)
    $minAlign = [uint64]::MaxValue
    for ($i = 0; $i -lt $phnum; $i++) {
        $base = [int]($phoff + $i * $phentsize)
        $ptype = [BitConverter]::ToUInt32($bytes, $base)
        if ($ptype -eq 1) { # PT_LOAD
            $align = [BitConverter]::ToUInt64($bytes, $base + 0x30)
            if ($align -lt $minAlign) { $minAlign = $align }
        }
    }
    if ($minAlign -eq [uint64]::MaxValue) { throw "未找到 PT_LOAD: $Path" }
    return $minAlign
}

if (-not $ProjectRoot) {
    $ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}
$crateManifest = Join-Path $ProjectRoot 'native\gemuera-native\Cargo.toml'
$nativeOut = Join-Path $ProjectRoot 'export\NativeLibs'
$targetDir = Join-Path $ProjectRoot 'native\target'

if (-not (Test-Path $crateManifest)) {
    throw "未找到 crate: $crateManifest"
}

# ---- Windows x64（桌面构建/等价测试） ----
if (-not $SkipDesktop) {
    & cargo build --release --manifest-path $crateManifest --target-dir $targetDir
    if ($LASTEXITCODE -ne 0) { throw "cargo 桌面构建失败 (exit $LASTEXITCODE)" }

    $outDir = Join-Path $nativeOut 'windows-x64'
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    Copy-Item (Join-Path $targetDir 'release\gemuera_native.dll') (Join-Path $outDir 'gemuera_native.dll') -Force
    Write-Host "[desktop] gemuera_native.dll -> $outDir"
}

# ---- Android arm64-v8a（APK jniLibs） ----
if (-not $SkipAndroid) {
    $sdk = $env:ANDROID_HOME
    if (-not $sdk) { $sdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
    $ndk = Get-ChildItem -Path (Join-Path $sdk 'ndk') -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
    $clang = if ($ndk) {
        Join-Path $ndk.FullName 'toolchains\llvm\prebuilt\windows-x86_64\bin\aarch64-linux-android24-clang.cmd'
    } else { $null }

    if (-not $clang -or -not (Test-Path $clang)) {
        Write-Warning "未找到 Android NDK（$sdk/ndk），跳过 Android 构建。桌面链路不受影响；安装 NDK 后重跑。"
    } else {
        # Android 15 要求 arm64 原生库按 16KB page 对齐。
        # cargo 不读 PATH 里的 NDK，必须显式指定链接器（per-arch clang wrapper）。
        $env:CARGO_TARGET_AARCH64_LINUX_ANDROID_LINKER = $clang
        $env:RUSTFLAGS = '-C link-arg=-Wl,-z,max-page-size=16384'
        & cargo build --release --target aarch64-linux-android --manifest-path $crateManifest --target-dir $targetDir
        if ($LASTEXITCODE -ne 0) { throw "cargo Android 构建失败 (exit $LASTEXITCODE)" }

        $outDir = Join-Path $nativeOut 'android\arm64-v8a'
        New-Item -ItemType Directory -Force -Path $outDir | Out-Null
        $so = Join-Path $targetDir 'aarch64-linux-android\release\libgemuera_native.so'
        Copy-Item $so (Join-Path $outDir 'libgemuera_native.so') -Force
        Write-Host "[android] libgemuera_native.so -> $outDir"

        $align = Get-ElfLoadAlignmentMin $so
        if ($align -lt 16384) {
            throw "16KB 对齐校验失败: libgemuera_native.so PT_LOAD p_align = $align 字节（要求 >= 16384）"
        }
        Write-Host "[android] 16KB page 对齐校验通过 (p_align=$align)"
    }
}
