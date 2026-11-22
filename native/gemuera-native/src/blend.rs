//! RGBA8 掩码混合（语义 = `GraphicsImage.BlendWithMaskBytes`，GDrawGWithMask 的纯字节算术）。
//!
//! 位级对齐要点——任何"优化"不得改变下列整数算术与分支语义：
//! 1. `maskByte` 取掩码像素单通道：`mask_alpha` = C# `UsesMaskAlphaChannel`
//!    （snake 参考取 A 通道 / 其余取 R 通道），C# 侧每像素读同一常量属性，此处置参数等价。
//! 2. 三分支：`0` → 跳过保持 dst；`255` → 四字节直拷；其余
//!    `ma = maskByte + 1`（整数域，无浮点舍入）、`ia = 256 - ma`、
//!    `dst = ((src * ma + dst * ia) >> 8) as u8`（最大 255*256+255*255 = 130,305 不溢出 u32）。
//! 3. 列裁剪 = 托管逐像素 `dx ∈ [0,dw)` 检查的区间化（`x_start = max(0, -destX)`、
//!    `x_end = min(w, dw - destX)`），行裁剪同理逐行 `dy ∈ [0,dh)`。
//! 4. `modified` = 是否有任何像素**通过 mask≠0 且在裁剪内**发生写入（与写入结果无关），
//!    供 GDrawGWithMask 判定免 SetData 上传。
//!
//! 性能结构（对标 C# gather/scatter SIMD 0.94x 负优化裁决——本模块为标量运行分段）：
//! 掩码通道按 4 字节步长扫描，识别 0/255/混合**运行段**：0 段跳过、255 段
//! `copy_from_slice` 整段拷贝（src/dst 行内连续区间一一对应）、混合段逐像素标量。

/// 原地混合。返回 `modified`；调用方（C# 包装）须先行校验缓冲尺寸
///（mask/src 行宽满足 `(h-1)*w行 + w ≤ len/4`、`dst.len() ≥ dw*dh*4`），
/// 违反时走托管路径抛 IndexOutOfRange——本函数不重复校验、不 panic。
#[inline]
pub fn blend_with_mask(
    dst: &mut [u8], dw: usize, dh: usize,
    src: &[u8], src_w: usize,
    mask: &[u8], mask_w: usize,
    w: usize, h: usize, dest_x: i64, dest_y: i64,
    mask_alpha: bool,
) -> bool {
    let mut modified = false;
    let m_off = if mask_alpha { 3 } else { 2 };
    for y in 0..h {
        let dy = dest_y + y as i64;
        if dy < 0 || dy >= dh as i64 {
            continue;
        }
        let dy = dy as usize;
        // 行裁剪后的列区间 [x_start, x_end)：dx = dest_x + x ∈ [0, dw)
        let x_start = (-dest_x).clamp(0, w as i64) as usize;
        let x_end = (dw as i64 - dest_x).clamp(0, w as i64) as usize;
        if x_start >= x_end {
            continue;
        }
        let dst_row = &mut dst[dy * dw * 4..(dy + 1) * dw * 4];
        let src_row = &src[y * src_w * 4..];
        let mask_row = &mask[y * mask_w * 4..];
        let mut x = x_start;
        while x < x_end {
            let m = mask_row[x * 4 + m_off];
            if m == 0 {
                x += 1;
                continue;
            }
            if m == 255 {
                // 直拷运行段：src/dst 行内均连续，整段 copy_from_slice
                let mut e = x + 1;
                while e < x_end && mask_row[e * 4 + m_off] == 255 {
                    e += 1;
                }
                let len = (e - x) * 4;
                let s = x * 4;
                let d = (dest_x + x as i64) as usize * 4;
                dst_row[d..d + len].copy_from_slice(&src_row[s..s + len]);
                modified = true;
                x = e;
                continue;
            }
            // 混合运行段：直到 0/255/行尾
            let mut e = x + 1;
            while e < x_end {
                let mm = mask_row[e * 4 + m_off];
                if mm == 0 || mm == 255 {
                    break;
                }
                e += 1;
            }
            for xi in x..e {
                let ma = mask_row[xi * 4 + m_off] as u32 + 1;
                let ia = 256 - ma;
                let s = xi * 4;
                let d = (dest_x + xi as i64) as usize * 4;
                for c in 0..4 {
                    dst_row[d + c] =
                        ((src_row[s + c] as u32 * ma + dst_row[d + c] as u32 * ia) >> 8) as u8;
                }
            }
            modified = true;
            x = e;
        }
    }
    modified
}

#[cfg(test)]
mod tests {
    use super::*;

    /// 像素打包（通道语义只对掩码通道有意义，其余值任意）
    fn px(r: u8, g: u8, b: u8, a: u8) -> [u8; 4] {
        [r, g, b, a]
    }

    /// 构造掩码：R 模式写 B 通道（m_off=2），A 模式写 A 通道（m_off=3）
    fn mask_pixels(m: &[u8], mask_alpha: bool) -> Vec<u8> {
        let mut v = vec![0u8; m.len() * 4];
        for (i, &mm) in m.iter().enumerate() {
            v[i * 4 + if mask_alpha { 3 } else { 2 }] = mm;
        }
        v
    }

    #[test]
    fn three_branch_semantics() {
        // 1x2 dst，掩码 [0, 128]，src [10.., 200..]
        let mut dst = px(50, 60, 70, 80).repeat(2).to_vec();
        let src = px(10, 20, 30, 40).repeat(2).to_vec();
        let mask = mask_pixels(&[0, 128], false);
        blend_with_mask(&mut dst, 2, 1, &src, 2, &mask, 2, 2, 1, 0, 0, false);
        assert_eq!(&dst[0..4], &px(50, 60, 70, 80), "mask=0 必须保持 dst");
        let expect = ((10u32 * 129 + 50u32 * 127) >> 8) as u8;
        assert_eq!(dst[4], expect, "混合 = (src*ma + dst*ia)>>8");
    }

    #[test]
    fn full_copy_run_uses_same_bytes() {
        let src = px(1, 2, 3, 4).repeat(4).to_vec();
        let mut dst = vec![99u8; 16];
        let mask = mask_pixels(&[255, 255, 255, 255], false);
        blend_with_mask(&mut dst, 4, 1, &src, 4, &mask, 4, 4, 1, 0, 0, false);
        assert_eq!(dst, src, "mask=255 运行段必须逐字节直拷");
    }

    /// 列/行裁剪：dest(-1,-1) 时仅 src 像素 (1,1) 可见落到 dst(0,0)
    #[test]
    fn clipping_skips_out_of_bounds() {
        let src = px(10, 0, 0, 0).repeat(4).to_vec(); // 2x2
        let mask = mask_pixels(&[255, 255, 255, 255], false); // 2x2 全 255
        let mut dst = vec![77u8; 2 * 2 * 4];
        blend_with_mask(&mut dst, 2, 2, &src, 2, &mask, 2, 2, 2, -1, -1, false);
        assert_eq!(&dst[0..4], &src[4..8], "唯一可见像素 src(1,1) → dst(0,0)");
        assert_eq!(&dst[4..], &vec![77u8; 12][..], "其余像素保持");
    }

    /// modified 语义：全 0 掩码 / 整幅超界 = false；任一非零且可见 = true
    #[test]
    fn modified_flag_semantics() {
        let src = px(1, 1, 1, 1).repeat(4).to_vec();
        let mut dst = vec![9u8; 16];
        let zero_mask = mask_pixels(&[0, 0, 0, 0], false);
        assert!(!blend_with_mask(&mut dst, 4, 1, &src, 4, &zero_mask, 4, 4, 1, 0, 0, false));
        let mut dst2 = vec![9u8; 16];
        let some_mask = mask_pixels(&[255, 0, 0, 0], false);
        assert!(blend_with_mask(&mut dst2, 4, 1, &src, 4, &some_mask, 4, 4, 1, 0, 0, false));
        // 极端裁剪：整幅超界 → false（无任何写入）
        let mut dst3 = vec![9u8; 16];
        assert!(!blend_with_mask(&mut dst3, 4, 1, &src, 4, &some_mask, 4, 4, 1, 4, 0, false));
    }

    /// mask_alpha 通道切换：掩码同时带 B=255 / A=128，两模式各取所需
    #[test]
    fn alpha_channel_selection() {
        let src = px(10, 10, 10, 10).repeat(1).to_vec();
        let mask = [0u8, 0, 255, 128].to_vec();
        // alpha 模式取 A=128 → 混合
        let mut dst = px(50, 50, 50, 50).to_vec();
        blend_with_mask(&mut dst, 1, 1, &src, 1, &mask, 1, 1, 1, 0, 0, true);
        let expect = ((10u32 * 129 + 50u32 * 127) >> 8) as u8;
        assert!(dst.iter().all(|&b| b == expect), "A 通道混合");
        // R(B) 模式取 255 → 直拷
        let mut dst2 = px(50, 50, 50, 50).to_vec();
        blend_with_mask(&mut dst2, 1, 1, &src, 1, &mask, 1, 1, 1, 0, 0, false);
        assert_eq!(dst2, px(10, 10, 10, 10), "B 通道直拷");
    }

    /// 随机 fuzz：与逐像素朴素实现（托管逐行翻译）比对，含裁剪、三分支混布
    #[test]
    fn fuzz_vs_naive_reference() {
        let mut state: u64 = 0x2026_1122;
        let mut next = |bound: u64| {
            state = state.wrapping_mul(6364136223846793005).wrapping_add(1442695040888963407);
            (state >> 33) % bound
        };
        for case in 0..200u32 {
            let dw = 1 + next(24) as usize;
            let dh = 1 + next(24) as usize;
            let w = 1 + next(24) as usize;
            let h = 1 + next(24) as usize;
            let dest_x = next(48) as i64 - 24;
            let dest_y = next(48) as i64 - 24;
            let mask_alpha = next(2) == 1;
            let m_off = if mask_alpha { 3 } else { 2 };
            let mut dst = vec![0u8; dw * dh * 4];
            for b in dst.iter_mut() {
                *b = next(256) as u8;
            }
            let src: Vec<u8> = (0..w * h * 4).map(|_| next(256) as u8).collect();
            let mut mask = vec![0u8; w * h * 4];
            for i in 0..w * h {
                // 三分支分布：30% 0 / 30% 255 / 40% 中间值
                let roll = next(100);
                let m = match roll {
                    0..=29 => 0u8,
                    30..=59 => 255u8,
                    _ => next(254) as u8 + 1,
                };
                mask[i * 4 + m_off] = m;
            }
            let mut native = dst.clone();
            let modified_native = blend_with_mask(
                &mut native, dw, dh, &src, w, &mask, w, w, h, dest_x, dest_y, mask_alpha,
            );
            // 朴素参考（托管循环逐行翻译）
            let mut expect = dst.clone();
            let mut modified_ref = false;
            for y in 0..h {
                let dy = dest_y + y as i64;
                if dy < 0 || dy >= dh as i64 {
                    continue;
                }
                for x in 0..w {
                    let dx = dest_x + x as i64;
                    if dx < 0 || dx >= dw as i64 {
                        continue;
                    }
                    let m = mask[(y * w + x) * 4 + m_off];
                    if m == 0 {
                        continue;
                    }
                    let si = (y * w + x) * 4;
                    let di = (dy as usize * dw + dx as usize) * 4;
                    if m == 255 {
                        expect[di..di + 4].copy_from_slice(&src[si..si + 4]);
                    } else {
                        let ma = m as u32 + 1;
                        let ia = 256 - ma;
                        for c in 0..4 {
                            expect[di + c] = ((src[si + c] as u32 * ma
                                + expect[di + c] as u32 * ia)
                                >> 8) as u8;
                        }
                    }
                    modified_ref = true;
                }
            }
            assert_eq!(native, expect, "case {case}");
            assert_eq!(modified_native, modified_ref, "modified flag case {case}");
        }
    }
}
