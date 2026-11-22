# 原生化候选：存档读侧 Int64 一维数组解码（Rust save.rs 读路径）

承接 [2026-11-21-native-save-codec.md](2026-11-21-native-save-codec.md)（编码侧 7.0x）与同日原生化盘点（ColorMatrix 已完成 [2026-11-22-native-colormatrix.md](2026-11-22-native-colormatrix.md)）。分支 `ai/ui-scale-setting`（单分支工作流）。

## 任务目标与成果

把 `EraBinaryDataReader.ReadIntArray(Int64[])`（1D，读档最热路径）原生化，位级等价约束下加速读档。成果：

- `save.rs` 新增 `decode_int64_segment_into`（段 = Int32 长度前缀之后的负载+EoD，原地写入调用方工作数组）：
  逐条复刻托管写入语义——`write_zeros`（= `needInit && !isNewArray`）控制游程写零与尾部 `x..length0` 补零；
  cnt 按 C# `(int)m_ReadInt()` 截断 i32；越界写 → `BadIndex`（托管 IndexOutOfRange 仅损坏存档可达）；
  未知 marker → `BadMarker`（托管 FileEE）。crate 单测 28/28（新增 6 项：round-trip 300 组、尾部补零、
  新数组跳写、207/208 长游程、错误路径、负值符号扩展）。
- FFI `gem_save_decode_int64_segment`（consumed 出参；-2 格式错/截断、-3 越界），`NATIVE_ABI_VERSION` 4→5；
  `NativeSaveCodec` 双探针（编码 ≥3 / 解码 ≥5 最低版本语义）+ `TryDecodeInt64Segment`（errorCode 区分截断与坏数据）。
- `EraBinaryDataReader.ReadIntArray(Int64[])` 接线：**整窗口读 + Rust 找 EoD 回报 consumed + `stream.Position` 回退**，
  池化缓冲 64KB 起步、-2 时 4 倍扩窗重试（`work` 重解码天然幂等）；`!stream.CanSeek` 或库缺失回退托管循环
  （托管体零改动，位级语义唯一权威）。托管侧零逐字节扫描。
- 门禁 `tools/save-codec-check` 扩展三模式：默认 sweep 增加解码对拍（新数组 + needInit 既有数组两变体 + 参考解码互证）；
  新 `--vs-reader`（反射私有嵌套 `EraBinaryDataReader1808`，**毒化 probeState/decodeProbeState 强制托管取真参考**，
  与生产路径互证 363 组含 300k 元素大数组强制扩窗路径）；`--bench`。
- 基线同步：`legacy-save-baseline.json` 中 `EraBinaryDataReader.cs` 的 sha256/byteLength（32849→35848，因接线漂移，
  托管循环未动 + `--vs-reader` 托管参考互证语义未变，按 2026-11-21 codec 先例审查后同步）。

性能：200k 元素（段 465KB）×50 次实测托管 334.9ms → 生产读路径 153.7ms，**2.18x**。

## 关键决策与 Why

1. **范围只做 1D**：2D/3D 段有额外 marker（`ZeroA1/EoA1/ZeroA2/EoA2` 行级语法）与行列交叉写零语义，独立工程；
   1D 与已原生的编码侧对称。2D/3D/float 数组进遗留。
2. **首版"托管结构化步进抽段"仅 1.12x**：抽段扫描本身 ≈ 托管解码循环（都是逐 marker 分支），
   原生解码的收益被抽段吃掉。重写为**整窗口 + Rust 全责解析 + Seek 回退**——托管侧只做一次 memcpy，
   收益升至 2.18x。教训：FFI 加速的前提是托管侧不再逐字节干活。
3. **扩窗重试而非预知段长**：段长度前缀是**元素数**不是字节数，无法从头部算窗口；64KB 起步 + 4 倍扩窗
   覆盖 99% 数组单次命中，大数组至多 2 次重试（重试解码幂等，`work` 前缀重写一致）。`-2` 的"截断"歧义
   （窗口不够 vs 真截断）由 `got == window && window < remaining` 判据区分。
4. **基线哈希漂移处理**：`save-baseline` 门禁钉扎 5 个格式源文件哈希；本轮只动 reader 且托管回退体未改 +
   363 组托管/原生互证 → 审查后同步 sha256/byteLength（先例：2026-11-21 codec 任务"先审后同步"）。

## 验证记录

- Rust 单测 28/28 [实测]。
- `save-codec-check` 默认 sweep PASS：golden + 300 随机，编码对拍 + 解码双变体（新数组/既有数组）+ 参考解码互证 [实测]。
- `--vs-writer` PASS（回归）[实测]；`--vs-reader` PASS：363 组（120 case × 3 变体 + 300k 大数组），
  毒化探针取托管真参考与生产路径互证，覆盖扩窗重试路径 [实测]。
- bench：200k 元素 ×50，托管 334.9ms → 原生 153.7ms = **2.18x** [实测]（含反射/流/数组分配固定开销，两侧同摊）。
- 回归：`native-width-check` PASS（65536 码点 0 失配）、`colormatrix-check` PASS（ABI=5 下 400 case 三方一致）[实测]。
- 基线门禁：`Test-LegacySaveBaseline.ps1` → "M0 legacy save baseline contract tests passed."（哈希+长度同步后）[实测]。
- 构建：主工程 0 错误；原生桌面 dll + Android arm64 `.so`（16KB p_align=16384 校验通过，ABI=5）[实测]。
- xUnit：429 通过 / 0 失败 [实测]。
- 桌面 runner 轮次：**未跑**——boot-only fixture 不触读档路径，`--vs-reader` 已做系统级读路径互证；真机读档验收进遗留。
- 用户配置：本轮未触碰（无 runner/守卫需求）。

## AI 表现复盘

有效：
- 门禁三模式扩展（sweep/vs-reader/bench）沿用 colormatrix 的毒化探针设计，一举覆盖扩窗重试等边界路径。
- 1.12x 弱收益**没有硬收**，识别出"托管抽段 ≈ 托管解码"的结构性问题后重设计为窗口方案，2.18x 才交付。

低效/返工：
- 门禁工具重写 `--vs-writer` 时凭记忆猜 `EraBinaryDataWriter` 构造器签名（实际接 Stream 且 zip 路径落内部
  memoryStream），MissingMethodException 返工 1 次——教训：重写既有工具段前先读被重写段的当前实现，不信记忆。
- `GetConstructor` 只给 `BindingFlags.NonPublic` 漏掉 public 构造器（reader 嵌套类 ctor 是 public），
  再返工 1 次——教训：反射 flags 是精确匹配不是"包含"。
- 升级去向：`FFI 前提=托管侧不逐字节干活` 已写入 save.rs 模块注释与本日志；暂不升级 AGENTS.md（单点工程教训）。

## 遗留登记

- P1 真机读档验收：需重导 APK 后真机 LOAD 存档验证（boot-only runner 不覆盖读档；`--vs-reader` 已互证）。
- P2 2D/3D Int64 数组（`ZeroA1/EoA1/ZeroA2/EoA2` 行级语法）与 float 数组族的原生化：读档剩余大头，
  需独立工程（行级交叉写零语义复杂），本轮范围外。
- P2 SparseArray 路径的 `ToArray/FromArray` 双拷贝（`EraBinaryDataReader.cs` 扫描发现项）：1D 原生化后
  该路径已走快路径，双拷贝仍在但占比小；触碰 SparseArray 语义时再评估。
- P2 编码侧与读侧探针阈值不同（≥3/≥5）：旧 ABI 库混载时编码可用解码不可用——现状行为正确（回退托管），
  无需动作，仅备注。
