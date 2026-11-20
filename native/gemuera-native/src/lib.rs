//! gEmuera 原生工具库。
//!
//! 通过 P/Invoke（`DllImport("gemuera_native")`）由主 C# 工程加载；
//! Android 产物为 `libgemuera_native.so`（arm64-v8a），桌面为 `gemuera_native.dll`。
//! 构建与产物分发见 `tools/native-build/Build-GemueraNative.ps1`。
//!
//! 所有导出函数为纯计算，不得 panic（extern "C" 边界）。

pub mod width;
pub mod zero_width_tables;

/// 导出 ABI 版本，宿主加载后先校验，防止库与 C# 侧签名漂移。
/// 新增导出函数时递增此版本并同步 C# 探针的 ExpectedAbiVersion。
pub const NATIVE_ABI_VERSION: u32 = 2;

#[no_mangle]
pub extern "C" fn gem_native_abi_version() -> u32 {
    NATIVE_ABI_VERSION
}

/// 单个 UTF-16 码元的显示宽度：0（零宽）/ 1（半宽）/ 2（全宽）。
#[no_mangle]
pub extern "C" fn gem_text_width_unit(unit: u16) -> i32 {
    width::width_unit(unit)
}

/// UTF-16 单元序列总宽度（对应 C# `Utils.GetByteCount(string)`）。
///
/// # Safety
/// `ptr` 必须指向至少 `len` 个可读的 `u16`（len==0 或 null 时返回 0）。
#[no_mangle]
pub extern "C" fn gem_text_width_utf16(ptr: *const u16, len: usize) -> i64 {
    if ptr.is_null() || len == 0 {
        return 0;
    }
    let units = unsafe { std::slice::from_raw_parts(ptr, len) };
    width::width_utf16(units)
}

/// 逐单元宽度序列：把每个 UTF-16 码元的宽度（0/1/2）写入 `out`，返回总宽度。
/// 供 C# 侧 LangManager（按宽度定位/切片）与 GetDisplayLength（网格累加）使用，
/// 避免逐字符 FFI 往返。
///
/// # Safety
/// `ptr` 必须指向至少 `len` 个可读的 `u16`；`out` 为 null 时只计算总宽，
/// 非 null 时必须可写至少 `len` 个 `u8`。
#[no_mangle]
pub extern "C" fn gem_text_width_units_utf16(ptr: *const u16, len: usize, out: *mut u8) -> i64 {
    if ptr.is_null() || len == 0 {
        return 0;
    }
    let units = unsafe { std::slice::from_raw_parts(ptr, len) };
    let mut total: i64 = 0;
    if out.is_null() {
        for &u in units {
            total += width::width_unit(u) as i64;
        }
    } else {
        let widths = unsafe { std::slice::from_raw_parts_mut(out, len) };
        for (i, &u) in units.iter().enumerate() {
            let w = width::width_unit(u);
            widths[i] = w as u8;
            total += w as i64;
        }
    }
    total
}
