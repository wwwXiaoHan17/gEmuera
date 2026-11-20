//! 文本宽度规则。
//!
//! 语义契约 = `Scripts/uEmuera/Utils.cs:894-1055`（GetByteCount 单元宽度），
//! 由 `tools/native-width-check`（全 BMP 扫描 + golden 断言）验证等价。
//! 移植时必须保持与 C# 版本相同的判定顺序（CheckFullSize 先于 halfsize 表）。
//!
//! 输入是单个 UTF-16 码元（`u16`），与 C# `str[i]`（char）逐单元处理一致；
//! 代理对单元按 C# `UnicodeCategory.Surrogate` 路径处理（非零宽、非半宽 → 2）。

use crate::zero_width_tables;

/// C# `Utils.fullsize`（Utils.cs:894-897）。注意：**必须保持升序**（二分查找），
/// 与 C# 源码书写顺序不同（C# 中 '¬'=0x00AC 排在 '§'=0x00A7 之前）。
pub const FULLSIZE: &[u16] = &[
    0x00A2, 0x00A3, 0x00A7, 0x00A8, 0x00AC, 0x00B0, 0x00B1, 0x00B4, 0x00B6, 0x00B7, 0x00D7, 0x00F7,
];

/// C# `Utils.halfsize`（Utils.cs:902-912），已排序供二分查找。
/// 注意 `0x00B4`（´）同时在 FULLSIZE 中，但 C# 版 CheckHalfSize 先判 fullsize，
/// 因此实际宽度为全宽——此表仅保持集合成员一致。
pub const HALFSIZE: &[u16] = &[
    0x007E, 0x00B4, 0x2580, 0x2581, 0x2582, 0x2583, 0x2584, 0x2585, 0x2586, 0x2587, 0x2588, 0x2589,
    0x258A, 0x258B, 0x258C, 0x258D, 0x258E, 0x258F, 0x2590, 0x2591, 0x2592, 0x2593, 0x2594, 0x2595,
    0x25A4, 0x25A5, 0x25A6, 0x25A7, 0x25A8, 0x25A9, 0x25AA, 0x25AB, 0x25AE, 0x25EE, 0x266E, 0x276E,
    0x27EE, 0x282E, 0x286E, 0x28AE, 0x28EE, 0xFF61, 0xFF65, 0xFF84,
];

fn set_contains(set: &[u16], c: u16) -> bool {
    set.binary_search(&c).is_ok()
}

/// C# `CharUnicodeInfo.GetUnicodeCategory` ∈ {NonSpacingMark, EnclosingMark, Format}，
/// 由 `tools/native-width-check --gen-tables` 从实际 .NET 运行时生成。
fn is_zero_width_category(c: u16) -> bool {
    zero_width_tables::is_unicode_zero_width_category(c)
}

/// C# `Utils.CheckZeroWidth`（Utils.cs:953-968）。
pub fn check_zero_width(c: u16) -> bool {
    is_zero_width_category(c)
        || matches!(
            c,
            0x200B | 0x200C | 0x200D | 0x2060 | 0xFEFF | 0x180E | 0x00AD
        )
        || (0xFE00..=0xFE0F).contains(&c)
}

/// C# `Utils.IsFullWidthForm`（Utils.cs:942-946）。
fn is_full_width_form(c: u16) -> bool {
    (0xFF01..=0xFF60).contains(&c) || (0xFFE0..=0xFFE6).contains(&c)
}

/// C# `Utils.CheckFullSize`（Utils.cs:898-901）。
pub fn check_full_size(c: u16) -> bool {
    set_contains(FULLSIZE, c) || is_full_width_form(c)
}

/// C# `Utils.IsSingleCellUnicodeArt`（Utils.cs:931-940）。
fn is_single_cell_unicode_art(c: u16) -> bool {
    (0x2580..=0x259F).contains(&c) || (0x2800..=0x28FF).contains(&c)
}

/// C# `Utils.IsHalfWidthKatakana`（Utils.cs:948-951）。
fn is_half_width_katakana(c: u16) -> bool {
    (0xFF61..=0xFF9F).contains(&c)
}

/// C# `Utils.CheckHalfSize`（Utils.cs:913-929）。判定顺序不可调换。
pub fn check_half_size(c: u16) -> bool {
    if c == 0x2022 {
        return true;
    }
    if check_full_size(c) {
        return false;
    }
    if is_single_cell_unicode_art(c) {
        return true;
    }
    c < 0x127 || is_half_width_katakana(c) || set_contains(HALFSIZE, c)
}

/// 单个 UTF-16 码元的显示宽度：0（零宽）/ 1（半宽）/ 2（全宽）。
/// 对应 C# `Utils.GetByteCount`（Utils.cs:1039-1055）对单字符的处理。
pub fn width_unit(c: u16) -> i32 {
    if check_zero_width(c) {
        0
    } else if check_half_size(c) {
        1
    } else {
        2
    }
}

/// UTF-16 单元序列的总宽度（对应 `Utils.GetByteCount(string)`）。
pub fn width_utf16(units: &[u16]) -> i64 {
    units.iter().map(|&c| width_unit(c) as i64).sum()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn golden_single_units() {
        // (码元, 期望宽度, 说明)
        let golden: &[(u16, i32, &str)] = &[
            (0x0041, 1, "'A' ASCII"),
            (0x3042, 2, "'あ' 平假名"),
            (0x00A2, 2, "'¢' fullsize 表"),
            (0x00B4, 2, "'´' fullsize 表（halfsize 成员被遮蔽，Utils.cs:898 先判 fullsize）"),
            (0x2022, 1, "'•' CP932 不可映射特例（Utils.cs:921）"),
            (0x2588, 1, "'█' Block Elements 单格 art（Utils.cs:936）"),
            (0x25AE, 1, "'▮' halfsize 表"),
            (0x25A4, 1, "'▤' halfsize 表"),
            (0xFF71, 1, "'ｱ' 半角片假名"),
            (0x05D0, 2, "'א' 希伯来字母 → 全宽"),
            (0x0430, 2, "'а' 西里尔字母 → 全宽"),
        ];
        for &(unit, expected, why) in golden {
            assert_eq!(width_unit(unit), expected, "U+{unit:04X}: {why}");
        }
    }

    #[test]
    fn golden_boundary_and_special() {
        // c < 0x127 边界（Utils.cs:928）：0x126 半宽、0x127 全宽。
        assert_eq!(width_unit(0x0126), 1, "U+0126 Ħ < 0x127 → 半宽");
        assert_eq!(width_unit(0x0127), 2, "U+0127 ħ 不满足 c < 0x127 → 全宽");
        // 零宽族。
        for unit in [0x0301, 0xFEFF, 0x200D, 0x2060, 0x180E, 0x00AD, 0xFE0F] {
            assert_eq!(width_unit(unit), 0, "U+{unit:04X} 应为零宽");
        }
        // 半角片假名区间两端。
        assert_eq!(width_unit(0xFF61), 1, "U+FF61 ｡");
        assert_eq!(width_unit(0xFF9F), 1, "U+FF9F 半角片假名上界");
        assert_eq!(width_unit(0xFFA0), 2, "U+FFA0 超出半角片假名 → 全宽");
        // 全角形式区间。
        assert_eq!(width_unit(0xFF01), 2, "U+FF01 ！全角形式下界");
        assert_eq!(width_unit(0xFF60), 2, "U+FF60 全角形式上界");
        assert_eq!(width_unit(0xFFE6), 2, "U+FFE6 全角形式附加区上界");
        // 代理对单元按 Surrogate 路径 → 全宽（与 C# 一致）。
        assert_eq!(width_unit(0xD83C), 2, "高代理单元非零宽非半宽 → 2");
        // 控制字符与空白按规则落半宽（C# 不把 Cc 归零宽）。
        assert_eq!(width_unit(0x000A), 1, "LF 控制字符 < 0x127 → 1（保持 C# 行为）");
    }

    #[test]
    fn width_is_zero_one_or_two_for_full_bmp() {
        for unit in 0u32..=0xFFFF {
            let w = width_unit(unit as u16);
            assert!(
                w == 0 || w == 1 || w == 2,
                "U+{unit:04X} 宽度越界: {w}"
            );
        }
    }

    #[test]
    fn zero_width_implies_not_half_size() {
        for unit in 0u32..=0xFFFF {
            let c = unit as u16;
            if check_zero_width(c) {
                // 宽度函数中零宽优先，无需断言 half 状态本身，只保证语义分层一致。
                assert_eq!(width_unit(c), 0, "U+{unit:04X}");
            }
        }
    }

    #[test]
    fn ascii_range_is_half_width() {
        for unit in 0x20u16..=0x7E {
            assert_eq!(width_unit(unit), 1, "U+{unit:04X}");
        }
    }

    #[test]
    fn batch_matches_unit_sum() {
        let s: Vec<u16> = "ABあ•█ä}".encode_utf16().collect();
        let sum: i64 = s.iter().map(|&u| width_unit(u) as i64).sum();
        assert_eq!(width_utf16(&s), sum);
        assert_eq!(width_utf16(&[]), 0);
    }

    #[test]
    fn width_units_fill_matches_unit_values() {
        // lib.rs 的 gem_text_width_units_utf16 逻辑锚点：填充值必须与 width_unit 逐单元一致。
        let s: Vec<u16> = "Aあ•\u{FEFF}".encode_utf16().collect();
        let mut widths = vec![9u8; s.len()];
        let total: i64 = s
            .iter()
            .enumerate()
            .map(|(i, &u)| {
                let w = width_unit(u);
                widths[i] = w as u8;
                w as i64
            })
            .sum();
        assert_eq!(widths, vec![1, 2, 1, 0]);
        assert_eq!(total, 4);
        // 全 BMP 值域防护：序列值恒 ∈ {0,1,2}。
        for unit in 0u32..=0xFFFF {
            let w = width_unit(unit as u16);
            assert!(w == 0 || w == 1 || w == 2);
        }
    }

    #[test]
    fn lookup_tables_are_strictly_ascending() {
        // 防回归：两个表都按二分查找消费，乱序会导致静默漏判（FULLSIZE 曾因此漏判 U+00AC）。
        for w in FULLSIZE.windows(2) {
            assert!(w[0] < w[1], "FULLSIZE 非升序: {:#06X} >= {:#06X}", w[0], w[1]);
        }
        for w in HALFSIZE.windows(2) {
            assert!(w[0] < w[1], "HALFSIZE 非升序: {:#06X} >= {:#06X}", w[0], w[1]);
        }
    }

    #[test]
    fn fullsize_table_hits_every_member() {
        // 逐成员验证可被二分命中（升序断言之外的语义级保护）。
        for &c in FULLSIZE {
            assert_eq!(width_unit(c), 2, "FULLSIZE 成员 U+{c:04X} 应为全宽");
        }
    }

    #[test]
    fn generated_table_ranges_sorted_and_disjoint() {
        let ranges = zero_width_tables::UNICODE_ZERO_WIDTH_RANGES;
        assert!(!ranges.is_empty(), "生成表为空——检查 --gen-tables 是否运行");
        for w in ranges.windows(2) {
            assert!(
                w[0].1 < w[1].0,
                "区间不升序或重叠: ({:04X},{:04X}) 与 ({:04X},{:04X})",
                w[0].0,
                w[0].1,
                w[1].0,
                w[1].1
            );
        }
    }
}
