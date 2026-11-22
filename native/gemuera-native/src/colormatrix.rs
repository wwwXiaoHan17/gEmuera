//! RGBA8 ColorMatrix 像素变换（语义 = `GraphicsImage.ApplyColorMatrixBytes`）。
//!
//! 位级对齐要点——本模块的任何"优化"都不得改变下列算术次序与舍入：
//! 1. `byte → f32`：`f32::from(u8)` 精确；`/ 255.0` 为 IEEE 单精度除法。
//! 2. 矩阵求和必须**左结合逐项取整**（C# `a + b + c + d + e` 逐二元 op 舍入），
//!    禁 `mul_add`/FMA 收缩、禁重排序——Rust 默认浮点语义即为严格 IEEE，勿开 fast-math。
//! 3. `ToByte`：`v * 255.0f32` →（C# 隐式提升 f64）→ `Math.Round` banker's 舍入
//!    （x.5→偶数，如 165.5→166、90.5→90）→ unchecked `(int)`（.NET Core 3.0+ 定为
//!    **饱和转换**：超域 → i32::MAX/MIN，NaN → 0；2026-11-22 门禁实证，非 cvttsd2si
//!    的 INT_MIN 语义）→ `Clamp(0,255)` → `u8`。Rust `as` 转换自 1.45 起同为饱和 +
//!    NaN→0，语义天然一致，勿加自造特判。
//!    病理值（±inf/NaN 矩阵项）由 `tools/colormatrix-check` 对拍真实托管实现实证锁定。
//!
//! 矩阵布局：20 个 f32 行优先 = `cm[0..5][0..4]`（cm[输入行][输出通道]），
//! 即 `f[row*4+col]`；R 通道输出 = f[0]*r + f[4]*g + f[8]*b + f[12]*a + f[16]。

/// 5×4 ColorMatrix 行优先展平。
pub type Matrix20 = [f32; 20];

/// C# `Mathf.RoundToInt`（= `(int)Math.Round((double)x)`，banker's）+
/// unchecked int 强转 + `Clamp(0,255)` 的逐位复刻。
#[inline]
fn to_byte(v: f32) -> u8 {
    // C#: v*255.0f 先在 f32 域完成一次舍入，再隐式提升 f64（f32→f64 精确无失真）
    let prod_f64 = (v * 255.0f32) as f64;
    // Math.Round(double) 默认 MidpointRounding.ToEven
    let rounded = prod_f64.round_ties_even();
    // .NET Core 3.0+ unchecked (int)double = 饱和转换（超域→i32::MAX/MIN，NaN→0）；
    // Rust `as` 自 1.45 起同语义（勿用 cvttsd2si 式特判，2026-11-22 门禁实证纠错）
    let i: i32 = rounded as i32;
    i.clamp(0, 255) as u8
}

/// 原地变换 `data`（RGBA8 字节缓冲，尾部长度 %4 != 0 时按 C# 循环语义忽略尾部）。
#[inline]
pub fn apply_rgba8(data: &mut [u8], f: &Matrix20) {
    let pixels = data.len() / 4; // C# 循环条件 i+3 < len ⇔ 仅处理完整像素
    for p in 0..pixels {
        let i = p * 4;
        let r = f32::from(data[i]) / 255.0;
        let g = f32::from(data[i + 1]) / 255.0;
        let b = f32::from(data[i + 2]) / 255.0;
        let a = f32::from(data[i + 3]) / 255.0;

        // 左结合逐项取整（勿改写顺序/勿合并乘加）
        let nr = f[0] * r + f[4] * g + f[8] * b + f[12] * a + f[16];
        let ng = f[1] * r + f[5] * g + f[9] * b + f[13] * a + f[17];
        let nb = f[2] * r + f[6] * g + f[10] * b + f[14] * a + f[18];
        let na = f[3] * r + f[7] * g + f[11] * b + f[15] * a + f[19];

        data[i] = to_byte(nr);
        data[i + 1] = to_byte(ng);
        data[i + 2] = to_byte(nb);
        data[i + 3] = to_byte(na);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn matrix(entries: [f32; 20]) -> Matrix20 {
        entries
    }

    /// 恒等矩阵全值域扫描：输出 == 输入（256³ 逐字节，四通道独立）。
    #[test]
    fn identity_is_fixed_point() {
        let mut m = [0f32; 20];
        m[0] = 1.0;
        m[5] = 1.0;
        m[10] = 1.0;
        m[15] = 1.0;
        let mut data: Vec<u8> = Vec::with_capacity(256 * 4);
        for d in 0..=255u8 {
            data.extend_from_slice(&[d, 255 - d, d / 2, 128]);
        }
        let before = data.clone();
        apply_rgba8(&mut data, &m);
        assert_eq!(data, before, "恒等矩阵必须逐字节不动");
    }

    /// 偏移矩阵的 banker's 舍入边界：0.5*255=127.5→128（偶）。
    #[test]
    fn offset_half_ties_to_even() {
        let mut m = [0f32; 20];
        m[16] = 0.5; // R 输出 = 0.5
        m[19] = 1.0; // A 输出 = 1.0 → 255
        let mut data = vec![255u8, 0, 0, 0]; // r=1 → nr=0.5
        apply_rgba8(&mut data, &m);
        assert_eq!(data[0], 128);
        assert_eq!(data[3], 255);
    }

    /// 负偏移：-0.5*255=-127.5→-128（banker's：-128 为偶）→ clamp 0。
    #[test]
    fn negative_half_clamps_to_zero() {
        let mut m = [0f32; 20];
        m[16] = -0.5;
        let mut data = vec![255u8, 0, 0, 255];
        apply_rgba8(&mut data, &m);
        assert_eq!(data[0], 0);
    }

    /// 上溢 clamp：1.5*255=382.5→382（偶）→ 255。
    #[test]
    fn overflow_clamps_to_max() {
        let mut m = [0f32; 20];
        m[16] = 1.5;
        let mut data = vec![255u8, 0, 0, 255];
        apply_rgba8(&mut data, &m);
        assert_eq!(data[0], 255);
    }

    /// 病理值锁定：NaN/±inf 矩阵项 →（复刻 cvttsd2si）clamp 后 0/255。
    /// 托管侧同用例的逐字节对拍在 tools/colormatrix-check 完成。
    #[test]
    fn pathological_matrix_entries() {
        let mut m = [0f32; 20];
        m[0] = f32::NAN;
        m[5] = f32::INFINITY;
        m[10] = f32::NEG_INFINITY;
        m[19] = 1.0; // A 通道 = 1.0 → 255，作为"其余通道未被病理项波及"的对照
        let mut data = vec![255u8, 255, 255, 255];
        apply_rgba8(&mut data, &m);
        assert_eq!(data[0], 0, "NaN → 饱和转换 0 → clamp 0");
        assert_eq!(data[1], 255, "+inf → 饱和转换 i32::MAX → clamp 255");
        assert_eq!(data[2], 0, "-inf → 饱和转换 i32::MIN → clamp 0");
        assert_eq!(data[3], 255, "对照通道偏移 1.0 → 255");
    }

    /// 矩阵缩放半值 tie：m=2.0、r=1 → nr=2.0 → 510.0 无 tie → clamp 255；
    /// m=0.355（f32）× r=255/255 与托管完全同序，对拍兜底。
    #[test]
    fn scaling_paths_stay_in_domain() {
        let mut m = [0f32; 20];
        m[0] = 2.0;
        m[5] = 0.355;
        m[15] = 1.0;
        let mut data = vec![255u8, 255, 0, 255];
        apply_rgba8(&mut data, &m);
        assert_eq!(data[0], 255);
        assert_eq!(data[3], 255);
    }

    /// 尾部不完整像素忽略（len % 4 != 0），复刻 C# `i + 3 < len` 循环。
    #[test]
    fn trailing_partial_pixel_untouched() {
        let mut m = [0f32; 20];
        m[16] = 1.0; // R → 255
        let mut data = vec![0u8, 0, 0, 0, 9, 9];
        apply_rgba8(&mut data, &m);
        assert_eq!(&data[..4], &[255, 0, 0, 0]);
        assert_eq!(&data[4..], &[9, 9], "尾部字节必须保持原样");
    }

    /// 左结合求和顺序敏感性：2^24(ulp=2) + 1 会被舍回 2^24 → LTR 全和 = 0 → 字节 0；
    /// 若实现重排（先算 1 + (-2^24)）或收缩 FMA，则得 1 → 字节 255——
    /// 两条路径判然有别，门禁托管对拍兜底锁定。
    #[test]
    fn sequential_sum_is_left_associative() {
        let mut m = [0f32; 20];
        m[0] = 16777216.0; // 2^24
        m[4] = 1.0;
        m[8] = -16777216.0;
        let mut data = vec![255u8, 255, 255, 255];
        apply_rgba8(&mut data, &m);
        assert_eq!(data[0], 0, "左结合逐项舍入：2^24+1 舍回 2^24，全和 0");
    }
}
