# 原生化候选：GraphicsImage ColorMatrix 像素算子（Rust colormatrix.rs）

承接 [2026-11-20-native-rust-cpp-opportunity-scan.md](2026-11-20-native-rust-cpp-opportunity-scan.md) P0 候选清单（候选 1 存档 codec、候选 5 宽度函数已完成）与本日解析缓存退役后的算子盘点。分支 `ai/ui-scale-setting`（单分支工作流，继承 ABI v3 构建链）。

## 任务目标与成果

把 `GraphicsImage.ApplyColorMatrixBytes`（CPU ColorMatrix 回退路径，仓库既有标量基线 41.8 ms/Mpx，`GraphicsImage.cs:527` 注释）原生化，位级等价约束下加速。成果：

- `native/gemuera-native/src/colormatrix.rs`：`apply_rgba8` 原地 RGBA8 变换 + `to_byte`（banker's 舍入 + 饱和 int 转换逐位复刻）+ 8 个单测（恒等全值域不动点、±0.5 tie、溢出 clamp、NaN/±inf 病理、尾部像素、左结合判别），crate 22/22 通过。
- FFI `gem_cm_apply_rgba8`（空指针/len%4!=0 → -1），`NATIVE_ABI_VERSION` 3→4；C# 探针 `scripts/Emuera/_Library/NativeColorMatrix.cs` 最低版本语义（≥4），矩阵按 `cm[行][通道]` 行优先打包（与托管索引同序）。
- 接线点：`GraphicsImage.ApplyColorMatrixBytes` 顶部原生快路径，缺库/ABI 低/缓冲异常/矩阵残缺回退托管参考实现（托管体保持位级语义唯一权威，零改动）。
- 等价门禁 `tools/colormatrix-check`：C# 逐行参考实现 + 反射真实 `ApplyColorMatrixBytes` 三方互证。**关键设计：毒化 `NativeColorMatrix.probeState` 强制反射走托管**——否则 DllImport 命中本工具已 Load 的同名模块，对拍沦为"原生 vs 原生"自证（save-codec-check 的 --vs-writer 存在同构问题，本轮未改它，进遗留）。
- 性能：生产路径 **2.92x**（1Mpx×30：69.6 → 23.8 ms/Mpx，含反射+克隆固定开销，`--bench` 实测）。

## 关键决策与 Why

1. **选 ColorMatrix 而非剩余 P0（启动枚举/HTML 解析）**：retirement 后 load_times 实测枚举仅 384ms 而算子基线 41.8ms/Mpx 且已有位级判据；blend 路线维持 2026-09-12 的 SIMD 负优化裁决不动（11.2 ms/Mpx 标量已足够便宜）。
2. **浮点对齐三铁律写入模块注释**：byte→f32 精确 + /255 IEEE；求和**左结合逐项取整**（禁 FMA/重排，Rust 默认严格 IEEE 正好）；ToByte = f32 乘 255 → 提升 f64 → banker's → 饱和 int → clamp。Rust `f64::round_ties_even`（1.77+）与 .NET `Math.Round` 对齐。
3. **饱和转换实证纠错（门禁抓到的真 bug）**：首版按"经典 cvttsd2si 语义"（超域/NaN → i32::MIN）实现，门禁 86 项失配（native=0 / ref=255）。独立 .NET 10 控制台实证：.NET Core 3.0 起 `(int)double` 为**饱和转换**（+∞/超域 → int.MaxValue，NaN → 0），与 Rust `as`（1.45+ 同为饱和+NaN→0）天然一致——删除自造特判即全绿。教训：跨运行时整型转换语义必须实测，不能背 x86 指令行为。
4. **诊断/G10 前置知识复用**：桌面 runner 轮次需要 user://config.toml 的 `debug_model_zh_cn` mirror（见 [2026-11-22-parse-cache-retirement.md](2026-11-22-parse-cache-retirement.md) 环境障碍节），备份→临时改→跑→diff 还原→删备份五步照做。

## 验证记录

- Rust 单测 22/22（含新增 colormatrix 8 项）[实测]（`cargo test --manifest-path native/gemuera-native/Cargo.toml --target-dir native/target`）。
- 门禁 `tools/colormatrix-check` PASS：golden（恒等全值域/全零/±0.5 tie/溢出/病理 NaN±inf/注释 tie 值邻域）+ 400 随机 case（现实域/野生大数/病理注入三矩阵域 × 1..2000px），native/参考/真实托管（毒化后）三方逐字节一致 [实测]。
- 首轮门禁抓到饱和转换 bug：86 项失配 → 实证 .NET 语义 → 修正 → 复跑 PASS [实测]。
- bench：1Mpx×30 托管 69.6 ms/Mpx → 生产接线（原生）23.8 ms/Mpx，**2.92x**（含反射/克隆固定开销，两侧同摊）[实测]。
- 回归门禁：`native-width-check` PASS（65536 码点 0 失配）、`save-codec-check` 默认 + `--vs-writer` 双模式 PASS [实测]。
- 主工程构建 0 错误；xUnit 429 通过/0 失败 [实测]。
- 原生产物：桌面 dll + Android arm64 `.so`（`Build-GemueraNative.ps1`，16KB page 对齐校验 p_align=16384 通过）[实测]。
- 桌面 runner 轮次 `D:\gemuera-reports\colormatrix-native-1`：8 个语义文件哈希与退役基线 `cache-retire-1` **全部一致**（系统级行为不变），G10 假绿守卫通过 [实测]。
- 用户配置还原：`diff` 与备份逐字节一致后备份才删除 [实测]。
- 真机/APK：**未验证**——当前设备 APK 不含本次 `.so`，需重导出验收（遗留 P1）。

## AI 表现复盘

有效：
- 门禁先行设计（毒化 probeState 三方互证）在首轮就抓住了饱和转换误设，避免该 bug 流入真机——工具抓 bug 的既定模式第三次兑现（宽度 spike 2 次 + 本轮 1 次）。
- 选型直接消费仓库既有实测注释（41.8 ms/Mpx、SIMD 0.94x 否决线），零重复测量。

低效/返工：
- Rust 单测首跑 3 个断言写错（A 通道偏移没设 1.0、冒烟用例值域设计错误）返工 1 次——教训：浮点 tie/病理用例先在纸上推一遍值域再写断言。
- 饱和转换误设：门禁 FAIL 后未直接假设"我对 .NET 语义的理解错了"，仍先怀疑求和顺序，绕了一圈才做独立实证。教训：等价门禁 FAIL 时第一动作是**对最小可疑语义做独立实证**，不是逐个怀疑实现。
- 升级去向：`跨运行时数值语义必须实测` 已写入 colormatrix.rs 模块注释 + 本日志；暂不升级 AGENTS.md（理由：单点教训，模块注释已就近防护）。

## 遗留登记

- P1 真机验收：新 `.so` 需重导 APK 后真机跑（用户侧操作）；桌面 8/8 哈希等价 + 位级门禁已绿，风险低。
- P2 `save-codec-check --vs-writer` 的同构循环自证问题（接线后反射 Writer 也会走原生快路径）：本轮未验证其是否真的发生（Writer 接线在库加载后 probe 同样会命中）——下次触碰该工具时同法毒化。
- P2 blend 路线（`BlendWithMaskBytes` 11.2 ms/Mpx）维持负优化裁决不动；若将来 Rust 标量试（免 FFI gather 的连续布局重排），需重新过位级门禁，本轮不做（成本>收益）。
