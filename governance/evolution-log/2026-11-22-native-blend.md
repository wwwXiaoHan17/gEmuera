# 原生化候选：blend 掩码混合算子（Rust blend.rs）

承接 [2026-11-20-native-rust-cpp-opportunity-scan.md](2026-11-20-native-rust-cpp-opportunity-scan.md) P0 像素算子线的收尾（ColorMatrix 已完成 [2026-11-22-native-colormatrix.md](2026-11-22-native-colormatrix.md)）。分支 `ai/ui-scale-setting`（单分支工作流）。

## 任务目标与成果

把 `GraphicsImage.BlendWithMaskBytes`（GDrawGWithMask 的纯字节算术，三分支掩码混合）原生化。此前 2026-09-12 的否决只针对 **C# System.Numerics gather/scatter SIMD**（0.94x 负优化，`GraphicsImage.cs:527` 注释），标量路线未测——本轮以 Rust 标量运行分段补位。成果：

- `blend.rs`：`blend_with_mask` 掩码通道运行分段——`0` 段跳过、`255` 段 `copy_from_slice` 整段直拷（src/dst 行内连续区间一一对应）、混合段逐像素 `ma=mask+1; ia=256-ma; (src*ma+dst*ia)>>8`（整数域，130,305 峰值不溢出）。列/行裁剪区间化（`x_start=max(0,-destX)`、`x_end=min(w, dw-destX)`），`modified` 语义与托管逐像素置位一致。crate 单测 34/34（新增 6 项：三分支、255 运行段、裁剪、modified 标志、A/B 通道切换、**200 组 fuzz 对朴素参考**）。
- FFI `gem_blend_with_mask`（返回 -1/0/1，0/1=modified），`NATIVE_ABI_VERSION` 5→6；探针 `NativeBlend.cs`（≥6 最低版本语义）**包装层预校验缓冲尺寸**——托管 IndexOutOfRange 场景（缓冲过小）返回 false 走托管抛同款异常，Rust 侧零 per-pixel 守卫不 panic。
- 接线：`BlendWithMaskBytes` 顶部原生快路径（与 colormatrix 同构：托管体零改动为位级权威）。
- 门禁 `tools/blend-check`：golden（三分支/四象限裁剪/全越界/空绘制）+ 300 随机（含 64 内随机 dest 偏移的裁剪象限）+ 反射真实方法三方互证 + `--bench`。

性能：1Mpx×30 实测托管 72.5 ms/Mpx → 生产接线 **23.0 ms/Mpx，3.16x**（含反射/克隆固定开销，两侧同摊）。

## 关键决策与 Why

1. **标量运行分段而非 SIMD**：C# SIMD 的负优化根因是 gather/scatter 通道重排；Rust 标量按掩码值分运行段后，255 段退化为 memcpy、0 段退化为跳过，只有混合段做算术——数据分布红利而非向量宽度红利。实现简单且位级可控，不再走 SIMD（若未来掩码分布实测偏斜可再评估）。
2. **门禁 alpha 用例的设计修正（本轮踩坑）**：反射真实方法读的是**全局** `Program.Compatibility.Snake.UsesMaskAlphaChannel`（工具进程恒 false），不是用例的 alpha 参数——首版 58 项"real vs reference 失配"全是该设计错（real 走 R 通道读全 0 → 全跳过）。修正：alpha 用例做 native↔参考比对（托管对等性 = 参考转写与托管仅通道下标不同 + Rust fuzz 200 组 alpha 朴素参考），R 通道用例做三方互证。教训：反射被测方法时先清点它消费的**全部全局状态**。
3. **runner display.json 抖动甄别（ P0 级排查）**：blend-native-1 轮 `display.json` 与基线 DIFF，7/8 语义文件一致。结构化 diff 定位到唯一差异 `featureCoverage.scrollIntent.count 1 vs 2`（派生自 trace 事件时序）；**同二进制第二次运行（守卫失败留下的 blend-native-2 残档）count=2、events=17 与基线一致**——同二进制两值都出现，判定为固定回放窗口边界的时序抖动，非本改动回归。教训：哈希 DIFF 先做**结构化字段级定位** + **同二进制重复试验**，再谈回归。
4. **缓冲校验前置到 C# 包装层**：托管越界 = IndexOutOfRange（损坏图像数据场景），原生侧无法复刻异常类型；包装层把"会越界的输入"整体路由回托管（dst 未触碰），异常语义由托管保持——原生侧零校验代码、零 panic 面。

## 验证记录

- Rust 单测 34/28+6 通过 [实测]。
- `blend-check` PASS：golden + 300 随机 native↔参考逐字节（含 modified 标志一致）；R 通道用例三方互证（真实托管 vs 参考经毒化探针）[实测]。
- bench：1Mpx×30 托管 72.5 ms/Mpx → 生产接线 23.0 ms/Mpx = **3.16x** [实测]。
- 回归：width（65536 码点）/ save sweep+vs-reader（363 组）/ colormatrix（ABI=6 下 400 case）全 PASS；xUnit 429 通过 [实测]。
- 构建：主工程 0 错误；桌面 dll + Android arm64 `.so`（16KB p_align=16384）[实测]。
- 桌面 runner：blend-native-1 轮 8 语义文件中 7 项与退役基线一致，`display.json` 唯一差异为 `scrollIntent.count`（时序派生计数器）；同二进制第二运行复现 count=2/17 事件 → 判定抖动非回归 [实测]。
- 用户配置：mirror 开关按备份→临时改→跑→diff 还原→删备份流程操作，最终 `log_level="warn"/mirror_to_godot=false` 与原值一致 [实测]。
- 真机/APK：**未验证**——需重导 APK 后真机跑带 GDrawGWithMask 的画面（遗留 P1）。

## AI 表现复盘

有效：
- 三分支运行分段设计把 2026-09-12 的 SIMD 负优化裁决转化为"数据分布红利"的标量方案，3.16x 且零位级风险。
- display.json DIFF 排查走"结构化字段定位 → 跨历史运行对比 → 同二进制重复"三步，用残档证据闭环，没有轻率归因也没有盲目回退。

低效/返工：
- gate 设计漏审被测方法的全局状态依赖（UsesMaskAlphaChannel），58 项假失配返工 1 次——教训已写进门禁注释与日志。
- runner 轮忘记先翻 user://config mirror（G10 守卫失败一次白跑约 2 分钟）——本轮第二次踩（colormatrix 轮是第一次），教训：**runner 轮 checklist 应固定为"备份→翻 mirror→跑→还原→删备份"单步脚本**，升级去向：暂不自动化（手动频率低，自动化工时>复发成本）。
- Rust 测试掩码通道摆放错（m_off=2 是 B 通道却按 R 摆）+ helper 签名数组/切片不匹配，编译/断言返工 2 次——教训：测试先行在纸上列通道下标表。

## 遗留登记

- P1 真机验收：与 ColorMatrix/存档读侧合并为一次 APK 重导验收（三项原生化共用一轮真机流程）。
- P2 runner `scrollIntent.count` 时序抖动本身：固定回放窗口的边界抖动会污染语义哈希门禁的严格性（本轮 7/8+结构化定位足够裁定，但每次都要人工甄别）——若复发频率高，建议 runner 把 coverage 计数器移出语义哈希输入集（独立任务，需 runner 维护者决策）。
- P2 alpha 用例的三方互证缺口（全局通道模式绑定会话）：可通过反射注入带 MaskAlphaChannel capability 的 LegacyCompatibilityProfile 补齐，成本中等；现有覆盖（参考转写 + Rust fuzz）已充分，暂不做。
