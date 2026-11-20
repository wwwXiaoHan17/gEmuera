# 2026-11-21 原生化候选 1：legacy 存档 Int64 数组段编码（Rust save.rs）

承接 [2026-11-21-scan-deterministic-waste-fixes.md](2026-11-21-scan-deterministic-waste-fixes.md) 同日扫描与 2026-11-20 原生化分析的候选 1。分支 `ai/native-save-codec`（基于 `ai/native-width-spike`，继承 native/ crate 与已验证构建链）。

## 任务目标与成果

把 legacy 存档二进制格式中数据量最大的部分——**Int64 一维数组段**（`EraBinaryDataWriter.writeData(Int64[])`：Int32 长度前缀 + Zero 游程 + m_WriteInt 自适应整数 + EoD）——原生化。成果：

- `native/gemuera-native/src/save.rs`：`encode_int64_array` + `decode_int64_array`（解码器用于 round-trip 自检与将来读侧原生化）+ golden/边界/fuzz（LCG 500 组）单测，14/14 通过。
- FFI `gem_save_encode_int64_array`（缓冲不足 -1 / 空输入 -2），`NATIVE_ABI_VERSION` 2→3；C# 探针统一改**最低版本语义**（宽度 ≥2、存档 ≥3），`tools/native-width-check` 同步。
- `Scripts/Emuera/_Library/NativeSaveCodec.cs`（探测缓存 + TryEncodeInt64Array，ArrayPool 由调用方管理）+ `EraBinaryDataWriter.writeData(Int64[])` 接线（原生快路径 `writer.Write(buf,0,n)` 一次落段，回退托管循环）。
- 等价门禁 `tools/save-codec-check`：C# 参考实现（逐行复刻）随机/边界 sweep（golden 13 组 + 300 组随机，含 207/208 游程边界、各宽度边界值）+ **`--vs-writer` 三方互证**（反射真实 internal `EraBinaryDataWriter` 的私有 `writeData`，对拍 参考实现==原生==真实 Writer，313 组逐字节）+ `--bench`。
- 性能：1000 元素数组编码 **7.0x**（160ms → 23ms / 5k 次）。
- `tools/save-baseline/legacy-save-baseline.json` 两步同步（见下），门禁绿：`M0 legacy save baseline contract tests passed.`

## 关键决策与 Why

- **先修门禁再动实现**：开工即发现 baseline 门禁在 dev 上**已经 FAIL**（M0 时点快照 vs 后续演进），且掩盖了第二个 bug。改动顺序=①同步哈希 ②修大小写键 ③绿后开发 ④实现后再同步。把"基线过时"作为独立提交（28513f8），codec 提交只含自己的漂移，审阅清晰。
- **基线漂移审查**：重新同步前审查 M0 后 3 个提交（`3e8413f` 存档 I/O 优化、`c45226c` v24 对齐、`ff2e82a` 路径统一）——marker/语法零改动，仅 1 行 Dictionary comparer（v24 有意决策）——同步安全。
- **大小写键 bug（意外发现）**：`ff2e82a` 统一小写路径时把 catalog `relativePath` 改成小写 `scripts/`，但 psm1 用 `StringComparer.Ordinal` 字典以大写 `Scripts/` 取值——"required source binding is missing"，一直被更早的哈希漂移掩盖。修 catalog 键（数据对齐工具预期），不动门禁代码。
- **"尾部全零不写游程"语义**：首版 golden 期望错了（以为写游程），实读 C# 注释（"残り全部 0 なら EoD のみ"）+ Reader 补零行为后修正测试而非实现——先怀疑实现后核对源码注释，确认实现正确。
- **解码器进 Rust 的理由**：编码正确性的最强自证是 round-trip；同时它是将来读侧原生化的现成组件（读侧本轮不做——流式 token 状态机接 FFI 复杂度高，见遗留）。
- **排除项**：2D/3D 数组与字符串段本轮不做（数据占比次之，边界状态机更复杂，待 profile）；GZip/文件原子写不动（已原生）；`EraBinaryDataReader` 完全不改（基线钉扎面更小）。

## 验证记录

- cargo 单测 14/14 [实测]（golden 字节序列、write_int marker 表、500 组 fuzz roundtrip、corrupt 输入拒绝）。
- fuzz 抓到真 bug 并修复 [实测]：`read_int` 2/4 字节路径零填充缺符号扩展（-4105 → 61431）——C# `ReadInt16/ReadInt32` 是符号扩展；已改为 `i16/i32::from_le_bytes as i64`。
- save-codec-check 默认 sweep PASS（golden 13 组 + 300 组随机/边界逐字节）[实测]。
- `--vs-writer` 三方互证 PASS：接线后真实 `EraBinaryDataWriter`（internal/private 反射调用）== 参考实现 == 原生，313 组逐字节 [实测]。
- `--bench`：7.0x（1000 元素 × 5k 次，160ms→23ms）[实测]。
- baseline 门禁绿（codec 后再次同步）[实测]；width checker PASS（ABI ≥2 语义）[实测]；主工程构建 0 错误 [实测]；xUnit 413+79+4 全绿 [实测]。
- Android：arm64 `.so` 构建脚本就绪，本轮未重导 APK [未验证——构建链已两轮真机验证，接线行为桌面等价门禁覆盖]。
- 环境障碍与打通：①无；②工具参数顺序踩一次（`--bench` 需在 dll 路径前），非阻塞。

## AI 表现复盘

- **有效做法**：开工先跑门禁而非直接写代码——发现"门禁已红"这一现状，把基线同步独立成前置提交，避免把既有漂移混进 codec diff（触发点：Test fail-fast "source hash drifted"；后果：审查范围可控；可复用动作：接手钉扎类门禁先跑绿基线）。
- **有效做法**：golden 期望与实现不一致时，先回读 C# 源码注释确认语义（尾部零省略）再决定改哪边——避免了按错误直觉改坏实现。
- **低效/返工**：①golden 首版按想当然写（游程语义），返工 1 次——教训：移植类 golden 必须逐行对照源码注释而非凭格式直觉；②fuzz 暴露符号扩展 bug 说明纯边界 golden 不够，随机 roundtrip 应成为编解码默认配置。
- **险些踩坑（未踩）**：catalog 大小写键修复时 `json.dumps` 重排格式导致第一次替换 0 命中——改为按实际格式匹配，没有硬推。

## 教训 → 具体优化动作

1. (a) 本轮已执行：编解码模块标配"golden 字节序列 + 随机 roundtrip + corrupt 拒绝"三件套（save.rs tests）。
2. (a) 本轮已执行：C# 探针统一最低版本语义（`AbiVersion() >= MinAbi`），消除"加导出必须改全部探针"的耦合（NativeTextWidth/NativeSaveCodec/native-width-check 三处已统一）。
3. (a) 本轮已执行：AGENTS.md 构建节补丁已在前一任务落地（CS0104 变体），本轮无新规范级教训；native/README.md 待 save 稳定后补模块清单（本轮不做，见遗留）。

## 遗留登记

- **L1（P1，不在本轮原因：流式 token 状态机接 FFI 复杂度高、收益待量化）**：读侧原生化——`EraBinaryDataReader` 的 Int64 数组段解码（消勘察确认的双拷贝 + 逐元素循环），可用 `save::decode_int64_array` 现成组件，需设计"框段字节→一次解码"的读法。
- **L2（P2，不在本轮原因：数据占比次之）**：2D/3D Int64 数组段与 string 段原生化（ZeroA1/A2 嵌套游程状态机）。
- **L3（P2，不在本轮原因：等 Android 冒烟轮次顺带）**：接线后 arm64 APK 重导 + 真机 SAVEDATA/LOADGAME 冒烟（构建链已验证）。
- **L4（P3）**：native/README.md 补 save 模块清单与门禁命令。
- 无 P0（本轮范围：编码端 + 门禁 + 基线修复全部落地）。
