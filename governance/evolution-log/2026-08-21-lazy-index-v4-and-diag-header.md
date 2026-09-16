# 进化记录：华扇口上懒加载索引失效修复 + 诊断日志头修正（2026-08-21）

> 任务：① 依据 snake 参考源码修复口上 `K43_K56_TSMIKO_WORKCHECK` 解釈できない識別子 BUG；
> ② 减少功能偏移；③ 性能/结构优化。

## 现象

- 手机端（Android，snake CoreProfile）读档（EVENTLOAD）后触发：
  `M_KOJO_K43_1153_事件系.ERB` 101 行调用 `K43_K56_TSMIKO_WORKCHECK(43)` 报
  「解釈できない識別子」。调用链：@EVENTLOAD → KOJO_MESSAGE_SEND → @M_KOJO_TPP_FLAGSETTING_K43。
- 该函数定义于 `自用函数.ERB`（#FUNCTION），位于 lazyloading.cfg 声明的懒加载作用域内。

## 根因

1. **索引排除语义**：含 `#FUNCTION(S/F)` 或 EVENT 函数的文件被排除出 lazy 索引
   （gEmuera 与 snake 一致），设计意图是这类文件在启动时全量加载。实测解析本地
   `lazyloading.bin`（97220 函数/2213 文件）：`自用函数.ERB` 确实不在索引中，
   而其调用方 `M_KOJO_K43_1153_事件系.ERB` 在索引中——排除逻辑本身正确。
2. **mtime 信任漏洞**：启动跳过条件是 `LazyLoadingFiles.Contains(file)`，而该集合由
   索引 + **时间戳比对**填充。压缩包解压/资源管理器复制会**保留归档内 mtime**
   （实证：游戏目录全部文件统一为 2026-07-24 09:38:46）。口上包更新后
   `自用函数.ERB` 内容已变（新增 #FUNCTION），但 mtime 未变 → 旧索引被误信 →
   文件既不进索引也不在启动加载 → 新函数不可见。
3. **日志头误导**：`GenericUtils.ExportDiagnosticLog/Package` 硬编码
   `useLazyLoading = false`，导致 gemuera 诊断日志报告头显示 `UseLazyLoading=False`，
   排障时误判方向。实际会话懒加载开启。

## 修复

### 1. 懒加载索引 v4：长度第二信号（Process.LazyLoading.cs）

- `LazyVersion` 3→4；新增 `LazyMinReadableVersion = 3`。
- `lazyloadingfiles.bin` v4 格式每条目追加 `Int64 文件长度`；
  `WriteLazyFileMeta` 写入时同步记录。
- `LoadLazyLoadingTable`：
  - 版本校验放宽为 `[v3, v4]`——snake 写的 v3 索引仍可读（降级为仅时间戳比对，
    与参考实现语义完全一致）；更高版本回退重建。
  - stat 并行校验改为「时间戳不同 → Changed；相同且 v4 → 再比长度，长度不同 → Changed」。
    长度是最廉价的第二信号：新增函数几乎必然改变文件大小。
  - **旧索引自动升级**：读到 v3（或 meta/data 版本不一致）时标记
    `metaNeedsUpgrade`，状态回填为 `UpdateTable` → 启动流程经
    `SavePartialLazyLoadingList` 将索引重写为 v4。内容不变、仅格式升级，
    保证存量设备下一次启动即获得长度保护。
- 双向共存：snake 读到 gEmuera 的 v4 索引会走其自身的版本不匹配→重建路径（安全降级，
  仅慢一次）；gEmuera 读 snake 的 v3 索引正常工作并触发一次性升级。

### 2. 诊断日志头反映真实配置（GenericUtils.cs）

- 两处导出路径改读 `Config.UseLazyLoading && Program.SupportsLazyLoading`，
  与 `Process.DoScript` 启动参数同源。

### 3. 索引写出 stat 并行化

- v4 使 `WriteLazyFileMeta` 从「纯写内存数据」变为「逐文件 stat 后写出」，
  2200+ 文件的串行 stat 会成为启动尾延迟。采用与读取侧（N6）相同的
  并行 stat + 按序回填模式；stat 异常先于建文件抛出，不留半写索引。

## 功能偏移评估

- v3 读取路径逐字段与 snake `LoadLazyLoadingTable` 对齐，仅在 v4 分支增加长度比对——
  参考实现行为不受影响。
- 日志头修正是宿主层（gEmuera 特有诊断系统），无核心语义漂移。

## 验证

- `dotnet build` 0 错误（仅 AgentLlmMethods.cs 既有 CS8632 ×3）。
- xUnit 20/20 通过。
- Python 模拟 v4 写读 roundtrip + v3 兼容读取通过。
- 用真实游戏目录 `lazyloadingfiles.bin` 复核：`自用函数.ERB` 不在索引、其余 20 个
  华扇 ERB 全部在索引、事件系.ERB 时间戳匹配——完整复现故障链。

## 后续观察项

- 若用户再次遇到「内容更新但 mtime+长度均未变」的极端情况（理论可能），需升级为
  内容哈希；当前成本收益比不支持。
