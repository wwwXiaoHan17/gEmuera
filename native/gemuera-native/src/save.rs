//! legacy 存档二进制格式：Int64 一维数组段编解码。
//!
//! 语义契约 = `Scripts/Emuera/Sub/EraBinaryDataWriter.cs` 的 `writeData(Int64[])`
//! + `m_WriteInt`（逐行移植，marker 常量与 `EraBinaryDataReader.cs` 的 `EraBDConst` 一致）。
//! 等价门禁：`tools/save-codec-check`（C# 参考实现随机对拍 + golden 字节序列 +
//! 反射真实 EraBinaryDataWriter 对拍）；基线 `tools/save-baseline` 钉扎源文件哈希。
//!
//! 段格式（大结构由 C# 侧控制，本模块只负责一个数组的完整段）：
//! `[Int32 LE 数组长度] [负载] [0xFF EoD]`
//! 负载 token：`0xF0 + m_WriteInt(游程数)` 表示连续零；其余值直接 `m_WriteInt`。
//! m_WriteInt：0..=207 → 单字节；Int16 范围 → 0xD0 + i16 LE；
//! Int32 范围 → 0xD1 + i32 LE；其余 → 0xD2 + i64 LE。

/// marker 常量（对应 EraBinaryDataReader.cs 的 EraBDConst，勿改值）
pub const MARKER_BYTE: u8 = 0xCF;
pub const MARKER_INT16: u8 = 0xD0;
pub const MARKER_INT32: u8 = 0xD1;
pub const MARKER_INT64: u8 = 0xD2;
pub const MARKER_ZERO: u8 = 0xF0;
pub const MARKER_EOD: u8 = 0xFF;

/// m_WriteInt：自适应宽度整数（契约见模块注释）。
pub fn write_int(out: &mut Vec<u8>, v: i64) {
    if (0..=MARKER_BYTE as i64).contains(&v) {
        out.push(v as u8);
    } else if (i16::MIN as i64..=i16::MAX as i64).contains(&v) {
        out.push(MARKER_INT16);
        out.extend_from_slice(&(v as i16).to_le_bytes());
    } else if (i32::MIN as i64..=i32::MAX as i64).contains(&v) {
        out.push(MARKER_INT32);
        out.extend_from_slice(&(v as i32).to_le_bytes());
    } else {
        out.push(MARKER_INT64);
        out.extend_from_slice(&v.to_le_bytes());
    }
}

/// `writeData(Int64[])`：编码完整数组段（含长度前缀与 EoD）。
pub fn encode_int64_array(array: &[i64]) -> Vec<u8> {
    let mut out = Vec::with_capacity(array.len() * 2 + 16);
    out.extend_from_slice(&(array.len() as i32).to_le_bytes());
    let mut count_zero: i64 = 0;
    for &v in array {
        if v == 0 {
            count_zero += 1;
        } else {
            if count_zero > 0 {
                out.push(MARKER_ZERO);
                write_int(&mut out, count_zero);
                count_zero = 0;
            }
            write_int(&mut out, v);
        }
    }
    out.push(MARKER_EOD);
    out
}

/// 解码错误（格式违背；C# BinaryReader 侧等价场景会抛异常/数据损坏）。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DecodeError {
    UnexpectedEof,
    BadLength,
    TrailingData,
    /// 越界写（C# 托管对应 IndexOutOfRangeException；仅损坏存档可达）
    BadIndex,
    /// 未知 marker（C# 托管对应 FileEE("バイナリデータの異常")）
    BadMarker,
}

/// `ReadIntArray` 对应的解码（用于 round-trip 自检；生产 Reader 仍是 C# 流式状态机）。
/// 成功时返回数组并消耗整个段；输入含多余字节报 `TrailingData`。
pub fn decode_int64_array(input: &[u8]) -> Result<Vec<i64>, DecodeError> {
    if input.len() < 5 {
        return Err(DecodeError::UnexpectedEof);
    }
    let len = i32::from_le_bytes([input[0], input[1], input[2], input[3]]);
    if len < 0 {
        return Err(DecodeError::BadLength);
    }
    let mut pos = 4usize;
    let mut array = Vec::with_capacity(len as usize);
    let read_int = |pos: &mut usize| -> Result<i64, DecodeError> {
        if *pos >= input.len() {
            return Err(DecodeError::UnexpectedEof);
        }
        let marker = input[*pos];
        *pos += 1;
        let size = match marker {
            MARKER_INT16 => 2,
            MARKER_INT32 => 4,
            MARKER_INT64 => 8,
            m if m <= MARKER_BYTE => return Ok(m as i64),
            _ => return Err(DecodeError::UnexpectedEof),
        };
        if *pos + size > input.len() {
            return Err(DecodeError::UnexpectedEof);
        }
        // 符号扩展：C# ReadInt16/ReadInt32 返回带符号值再升位（零填充会把负数读成大正数）
        let v = match size {
            2 => i16::from_le_bytes([input[*pos], input[*pos + 1]]) as i64,
            4 => i32::from_le_bytes([
                input[*pos],
                input[*pos + 1],
                input[*pos + 2],
                input[*pos + 3],
            ]) as i64,
            _ => i64::from_le_bytes(input[*pos..*pos + 8].try_into().unwrap()),
        };
        *pos += size;
        Ok(v)
    };
    while pos < input.len() && input[pos] != MARKER_EOD {
        if input[pos] == MARKER_ZERO {
            pos += 1;
            let run = read_int(&mut pos)?;
            for _ in 0..run {
                array.push(0);
            }
        } else {
            let v = read_int(&mut pos)?;
            array.push(v);
        }
        if array.len() > len as usize {
            return Err(DecodeError::BadLength);
        }
    }
    if pos >= input.len() {
        return Err(DecodeError::UnexpectedEof);
    }
    pos += 1; // EoD
    if pos != input.len() {
        return Err(DecodeError::TrailingData);
    }
    // 尾部剩余全零不写入字节（writer 语义），按声明长度隐式补零（reader 对新数组零初始化等价）
    while array.len() < len as usize {
        array.push(0);
    }
    Ok(array)
}

/// 读侧段解码（2026-11-22 原生化）：复刻 `EraBinaryDataReader.ReadIntArray(Int64[])`
/// 的解码与写入语义。段 = **Int32 长度前缀之后**的负载 + EoD（前缀由 C# reader 消费）。
///
/// 语义逐条对齐托管循环（勿"优化"掉任何一条）：
/// - `x` 自 0 递增；值写入 `out[x]`，越界（x<0 或 ≥out.len()）→ `BadIndex`
///   （托管为 IndexOutOfRangeException，仅损坏存档可达）。
/// - `0xF0` 零游程：cnt 经 `read_int` 后按 C# `(int)m_ReadInt()` **截断为 i32**
///   （`cnt as i32`）；`write_zeros && cnt > 0` 时写零（越界 → `BadIndex`）；
///   cnt ≤ 0 时零次迭代（负 cnt 仍使 x 回退——托管同此行为）。
/// - `write_zeros` = C# `needInit && !isNewArray`：false 时跳过全部零写入
///   （与"新数组零初始化"/"needInit=false"两分支等价）。
/// - 终止后尾部补零 `x..min(length0, out.len())`（仅 write_zeros；x<0 → `BadIndex`，
///   托管尾循环首访问 refArray[x] 即抛）。`length0` = C# min(旧长, 存档长)。
/// - 返回消耗字节数（含 EoD）；段内字节不足 → `UnexpectedEof`；未知 marker → `BadMarker`。
pub fn decode_int64_segment_into(
    seg: &[u8],
    out: &mut [i64],
    length0: usize,
    write_zeros: bool,
) -> Result<usize, DecodeError> {
    let mut pos = 0usize;
    // 托管 x 为 int；此处 i64 足以容纳全部现实取值（越界由显式检查拦截）
    let mut x: i64 = 0;
    let capacity = out.len() as i64;

    let read_int = |pos: &mut usize| -> Result<i64, DecodeError> {
        if *pos >= seg.len() {
            return Err(DecodeError::UnexpectedEof);
        }
        let marker = seg[*pos];
        *pos += 1;
        let size = match marker {
            MARKER_INT16 => 2usize,
            MARKER_INT32 => 4,
            MARKER_INT64 => 8,
            m if m <= MARKER_BYTE => return Ok(m as i64),
            _ => return Err(DecodeError::BadMarker),
        };
        if *pos + size > seg.len() {
            return Err(DecodeError::UnexpectedEof);
        }
        let v = match size {
            2 => i16::from_le_bytes([seg[*pos], seg[*pos + 1]]) as i64,
            4 => i32::from_le_bytes([
                seg[*pos],
                seg[*pos + 1],
                seg[*pos + 2],
                seg[*pos + 3],
            ]) as i64,
            _ => i64::from_le_bytes(seg[*pos..*pos + 8].try_into().unwrap()),
        };
        *pos += size;
        Ok(v)
    };

    loop {
        if pos >= seg.len() {
            return Err(DecodeError::UnexpectedEof);
        }
        let b = seg[pos];
        pos += 1;
        if b == MARKER_EOD {
            break;
        }
        if b == MARKER_ZERO {
            // C# `int cnt = (int)m_ReadInt()`：i64 值截断为 i32 再按符号扩展参与运算
            let run = read_int(&mut pos)? as i32 as i64;
            if write_zeros && run > 0 {
                if x < 0 || x + run > capacity {
                    return Err(DecodeError::BadIndex);
                }
                for i in 0..run {
                    out[(x + i) as usize] = 0;
                }
            }
            x += run;
            continue;
        }
        let v = if b <= MARKER_BYTE {
            b as i64
        } else {
            pos -= 1; // read_int 从 marker 本身开始
            read_int(&mut pos)?
        };
        if x < 0 || x >= capacity {
            return Err(DecodeError::BadIndex);
        }
        out[x as usize] = v;
        x += 1;
    }
    if write_zeros {
        if x < 0 {
            return Err(DecodeError::BadIndex);
        }
        let tail_end = (length0 as i64).min(capacity);
        let mut i = x;
        while i < tail_end {
            out[i as usize] = 0;
            i += 1;
        }
    }
    Ok(pos)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn write_int_bytes(v: i64) -> Vec<u8> {
        let mut out = Vec::new();
        write_int(&mut out, v);
        out
    }

    #[test]
    fn write_int_matches_marker_table() {
        assert_eq!(write_int_bytes(0), vec![0x00]);
        assert_eq!(write_int_bytes(207), vec![MARKER_BYTE]); // 0xCF=207 单字节上界
        assert_eq!(write_int_bytes(208), vec![MARKER_INT16, 0xD0, 0x00]); // 0x00D0 LE
        assert_eq!(write_int_bytes(-1), vec![MARKER_INT16, 0xFF, 0xFF]);
        assert_eq!(write_int_bytes(32767), vec![MARKER_INT16, 0xFF, 0x7F]);
        assert_eq!(write_int_bytes(32768), vec![MARKER_INT32, 0x00, 0x80, 0x00, 0x00]);
        assert_eq!(write_int_bytes(70000), vec![MARKER_INT32, 0x70, 0x11, 0x01, 0x00]);
        let big = 5_000_000_000i64;
        let mut expect = vec![MARKER_INT64];
        expect.extend_from_slice(&big.to_le_bytes());
        assert_eq!(write_int_bytes(big), expect);
    }

    #[test]
    fn encode_golden_byte_sequences() {
        // [0,0,0]：全零数组尾部游程不写（writer 语义：残り全部 0 なら EoD のみ）
        assert_eq!(
            encode_int64_array(&[0, 0, 0]),
            vec![0x03, 0x00, 0x00, 0x00, MARKER_EOD]
        );
        // [0,0,7,0,0,0,0]：非零前游程 2 → 7；**尾部 4 个零不写**
        assert_eq!(
            encode_int64_array(&[0, 0, 7, 0, 0, 0, 0]),
            vec![0x07, 0x00, 0x00, 0x00, MARKER_ZERO, 0x02, 0x07, MARKER_EOD]
        );
        // 全零 207/208：同样只有长度 + EoD
        assert_eq!(encode_int64_array(&vec![0i64; 207]), {
            let mut v = vec![207u8, 0, 0, 0];
            v.push(MARKER_EOD);
            v
        });
        assert_eq!(encode_int64_array(&vec![0i64; 208]), {
            let mut v = vec![208u8, 0, 0, 0];
            v.push(MARKER_EOD);
            v
        });
        // 游程值恰好踩 207/208 边界（中间零游程，非尾部）
        let mid_run_207 = {
            let mut a = vec![0i64; 207];
            a.push(9);
            a
        };
        assert_eq!(
            encode_int64_array(&mid_run_207),
            {
                let mut v = vec![208u8, 0, 0, 0, MARKER_ZERO, MARKER_BYTE, 0x09, MARKER_EOD];
                v
            }
        );
        let mid_run_208 = {
            let mut a = vec![0i64; 208];
            a.push(9);
            a
        };
        assert_eq!(
            encode_int64_array(&mid_run_208),
            vec![
                209u8, 0, 0, 0, MARKER_ZERO, MARKER_INT16, 0xD0, 0x00, 0x09, MARKER_EOD,
            ]
        );
        // 空数组：长度 0 + EoD
        assert_eq!(encode_int64_array(&[]), vec![0x00, 0x00, 0x00, 0x00, MARKER_EOD]);
    }

    #[test]
    fn roundtrip_random_fuzz() {
        // LCG 伪随机（无依赖）：覆盖各宽度区间与长零游程
        let mut state: u64 = 0x2026_1121;
        let mut next = |bound: u64| {
            state = state.wrapping_mul(6364136223846793005).wrapping_add(1442695040888963407);
            (state >> 33) % bound
        };
        for case in 0..500u32 {
            let len = next(2000) as usize;
            let mut array = Vec::with_capacity(len);
            for _ in 0..len {
                let roll = next(100);
                array.push(match roll {
                    0..=69 => 0,
                    70..=84 => next(208) as i64,
                    85..=94 => next(65536) as i64 - 32768,
                    95..=99 => next(1 << 40) as i64,
                    _ => -(next(1 << 40) as i64),
                });
            }
            if case % 50 == 0 && !array.is_empty() {
                // 注入长零游程（跨 207 边界）
                let start = next(array.len() as u64) as usize;
                let run = 200 + next(300) as usize;
                for k in start..(start + run).min(array.len()) {
                    array[k] = 0;
                }
            }
            let encoded = encode_int64_array(&array);
            let decoded = decode_int64_array(&encoded).expect("roundtrip decode failed");
            assert_eq!(decoded, array, "roundtrip mismatch at case {case}");
        }
    }

    #[test]
    fn decode_rejects_corrupt_input() {
        assert_eq!(decode_int64_array(&[]), Err(DecodeError::UnexpectedEof));
        assert_eq!(decode_int64_array(&[1, 0, 0]), Err(DecodeError::UnexpectedEof));
        // 长度声明 1、EoD 直接出现 → 合法（尾部隐式补零，reader 对新数组零初始化等价）
        assert_eq!(decode_int64_array(&[1, 0, 0, 0, MARKER_EOD]), Ok(vec![0]));
        // EoD 缺失（长度声明后直接 EOF）
        assert_eq!(
            decode_int64_array(&[2, 0, 0, 0, 0x05]),
            Err(DecodeError::UnexpectedEof)
        );
        // 段后有多余字节
        let mut extra = encode_int64_array(&[1]);
        extra.push(0xAA);
        assert_eq!(decode_int64_array(&extra), Err(DecodeError::TrailingData));
        // 未知 marker（0xE0 区域不在本段语法）
        assert_eq!(
            decode_int64_array(&[1, 0, 0, 0, 0xE0, MARKER_EOD]),
            Err(DecodeError::UnexpectedEof)
        );
    }

    // ===== decode_int64_segment_into（读侧，2026-11-22） =====

    /// 编码后剥掉 Int32 前缀的段（读侧输入形态）
    fn payload_of(array: &[i64]) -> Vec<u8> {
        encode_int64_array(array)[4..].to_vec()
    }

    #[test]
    fn segment_roundtrip_exact_capacity() {
        let mut state: u64 = 0x2026_1122;
        let mut next = |bound: u64| {
            state = state.wrapping_mul(6364136223846793005).wrapping_add(1442695040888963407);
            (state >> 33) % bound
        };
        for case in 0..300u32 {
            let len = next(1500) as usize;
            let mut array = Vec::with_capacity(len);
            for _ in 0..len {
                let roll = next(100);
                array.push(match roll {
                    0..=69 => 0,
                    70..=84 => next(208) as i64,
                    85..=94 => next(65536) as i64 - 32768,
                    95..=99 => next(1 << 40) as i64,
                    _ => -(next(1 << 40) as i64),
                });
            }
            let seg = payload_of(&array);
            // write_zeros=false（新数组语义）：C# 侧新数组零初始化 → 工作数组预置零，
            // 解码只填非零位，结果应等于原数组
            let mut work = vec![0i64; array.len()];
            let consumed =
                decode_int64_segment_into(&seg, &mut work, array.len(), false).expect("decode");
            assert_eq!(consumed, seg.len());
            assert_eq!(work, array, "case {case}");
        }
    }

    /// needInit && !isNewArray（write_zeros=true）：工作数组比存档长时尾部补零；
    /// 且入参非零填充必须被游程/尾部补零覆盖。
    #[test]
    fn segment_tail_zero_with_write_zeros() {
        // 存档 [5,0,7]；工作数组长度 6（更长，旧值 0x55）
        let seg = payload_of(&[5, 0, 7]);
        let mut work = vec![0x55i64; 6];
        decode_int64_segment_into(&seg, &mut work, 6, true).expect("decode");
        assert_eq!(work, [5, 0, 7, 0, 0, 0]);
    }

    /// write_zeros=false（新数组/needInit=false 语义）：尾部不补零，游程零不写——
    /// 新数组零初始化由 C# 分配器保证，等价托管 isNewArray 分支。
    #[test]
    fn segment_skip_zero_writes_when_new_array() {
        let seg = payload_of(&[5, 0, 7]);
        let mut work = vec![0x55i64; 6];
        decode_int64_segment_into(&seg, &mut work, 6, false).expect("decode");
        assert_eq!(work, [5, 0x55, 7, 0x55, 0x55, 0x55]); // 游程位与尾部都保持原值
    }

    /// 长零游程（跨 207/208 边界）+ write_zeros 的游程写零/尾部补零路径
    #[test]
    fn segment_long_zero_run_writes_zeros() {
        let mut array = vec![0i64; 207];
        array.push(9);
        let seg = payload_of(&array);
        let mut work = vec![0x55i64; 210];
        decode_int64_segment_into(&seg, &mut work, 210, true).expect("decode");
        // 游程 [0,207) 写零、out[207]=9，尾循环 x=208..210 把哨兵 0x55 补成 0
        let mut expect = vec![0i64; 207];
        expect.push(9);
        expect.extend([0i64; 2]);
        assert_eq!(work, expect);
    }

    /// 越界与未知 marker 错误路径
    #[test]
    fn segment_error_paths() {
        // 元素数超容量 → BadIndex
        let seg = payload_of(&[1, 2, 3]);
        let mut work = [0i64; 2];
        assert_eq!(
            decode_int64_segment_into(&seg, &mut work, 2, true),
            Err(DecodeError::BadIndex)
        );
        // 未知 marker（EoA1 属 2D 语法）→ BadMarker
        let bad = [0xE0u8, MARKER_EOD];
        let mut work2 = [0i64; 4];
        assert_eq!(
            decode_int64_segment_into(&bad, &mut work2, 4, true),
            Err(DecodeError::BadMarker)
        );
        // 截断（EoD 缺失）→ UnexpectedEof
        let cut = [0x05u8];
        assert_eq!(
            decode_int64_segment_into(&cut, &mut work2, 4, true),
            Err(DecodeError::UnexpectedEof)
        );
    }

    /// 负值经 Int16/Int32 marker 的符号扩展（与 C# ReadInt16/ReadInt32 带符号一致）
    #[test]
    fn segment_negative_values_sign_extend() {
        let seg = payload_of(&[-1, -70000, 1]);
        let mut work = [0i64; 3];
        decode_int64_segment_into(&seg, &mut work, 3, false).expect("decode");
        assert_eq!(work, [-1, -70000, 1]);
    }
}
