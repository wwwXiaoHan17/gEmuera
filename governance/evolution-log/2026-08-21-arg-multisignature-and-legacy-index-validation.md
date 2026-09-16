# 进化记录：多签名参数机制移植 + 旧索引完整性验证（2026-08-21）

> 任务：① 修复 eraBEMANI/eraTW 因引擎功能偏移导致的无法游玩；② snake 与 v24 参考源码
> 全部对照核查；③ 从根源解决（移植机制而非打补丁）。

## 现象

### 故障 1（eraBEMANI+α 2.28，emuera (7).log）

- 读档进入商店画面即崩：`2-SHOPコマンド関連/1-SHOP.ERB` 349 行 `CALL COLUMNPRINT` →
  内部调用 **GCLEAR 6 参**（ID, cARGB, x, y, w, h 矩形清除）→
  `GCLEAR関数:引数的数が間違っています`。

### 故障 2（eraThe World 华扇口上，emuera (8).log）

- `LazyLoading: 2237 files indexed`（新版本已运行、索引已读取）后，读档 EVENTLOAD 仍报
  `"K43_K56_TSMIKO_WORKCHECK"は解釈できない識別子です` —— 上一版 v4 长度修复**未生效**。

## 根因

### 根因 1：多签名参数机制缺失（GCLEAR）

- v24/snake 的 `FunctionMethod` 基类有 `argumentTypeArrayEx: ArgTypeList[]`（多组签名，
  含 Ref/数组维度/变参/省略起点等位标志语义）；GCLEAR 借此声明 2 参（全屏）与 6 参
  （矩形区域）两组签名——`EM_私家版_GCLEAR拡張`。
- gEmuera 移植时基类只有单签名 `argumentTypeArray: EraType[]`，GCLEAR 6 参签名丢失，
  仅保留 2 参。eraBEMANI 按 snake/v24 语义调用 6 参 → 运行时参数校验失败。
- 上一轮 GGETTEXTSIZE 同类问题（OmitStart 缺失）是打了单函数 override 补丁——本次
  按用户要求**从根源解决**：把 v24/snake 的 `ArgType`/`ArgTypeList`/`CheckArgumentTypeEx`
  完整机制移植进 gEmuera 基类，GCLEAR 改用机制声明，不再逐函数打补丁。

### 根因 2：v3 旧索引含被排除文件的旧条目（WORKCHECK）

- 手机端索引（2237 文件）构建于口上包更新之前，当时 `自用函数.ERB` 尚未含
  `#FUNCTION K43_K56_TSMIKO_WORKCHECK` → 被正常索引。
- 口上包更新后文件已含 #FUNCTION，但**归档解压保留了 mtime** → 时间戳比对通过 →
  文件留在 `LazyLoadingFiles` 跳过集 → 启动不加载 → 新函数不可解析。
- 上一版 v4 长度修复无效的原因：手机旧索引是 **v3**（无长度字段），且"部分升级"
  路径（metaNeedsUpgrade→UpdateTable→SavePartial）在 ChangedFiles 为空时 early-return
  不写盘 → 长度保护永远不生效。本次改为：**v3 索引读取时做标签完整性验证**。

## 修复

### 1. FunctionMethod 基类：多签名参数机制（对照 v24/snake FunctionMethod.cs）

- 移植 `ArgType` 位标志枚举、`_ArgType` 包装类（含 `implicit operator`）、
  `ArgTypeList`（含 `LastVariadics`/`OmitStart`/`MatchVariadicGroup`）、
  `CheckArgumentTypeEx` 完整校验（数量/类型/Ref/数组维度/变参/CharacterData/
  SameAsFirst/AllowConstRef）。
- 适配点：gEmuera 用 `EraType`（Integer/String/Float）与 `IOperandTerm[]`；
  错误文案键 `SyntaxErrMesMethod*` 按 v24 trerror 原文新增到 `uEmuera/Properties.cs`。
- `CheckArgumentType` 基类：`argumentTypeArrayEx != null` 时走多签名校验，
  否则走原有单签名路径（既有 89 处 `argumentTypeArray = null` 的类全部已带
  override 或改用 ex，零回归）。

### 2. GCLEAR / GCREATEFROMFILE：恢复 EM 扩展（对照 v24/snake Creator.Method.cs）

- `GraphicsClearMethod` 改用 `argumentTypeArrayEx` 声明 [Int,Int] 与
  [Int,Int,Int,Int,Int,Int] 两组签名；
- `GetIntValue`：2 参 `g.GClear(c)`，6 参 `g.GClear(c, x, y, w, h)`；
- `GraphicsImage.GClear(Color, x, y, w, h)`：对照 v24 SetClip+Clear+ResetClip 语义，
  Godot 端用 `FillRect` 限定矩形区域填充；
- 顺带修复同类偏移 `GCREATEFROMFILE(ID, filename[, isRelative])`（v24 OmitStart=2，
  gEmuera 此前仅 2 参）——用同一机制声明，isRelative 分支按 v24 语义解析路径。

### 3. 懒加载：v3 旧索引完整性验证（Process.LazyLoading.cs）

- 新增 `ValidateLazyIndexFilesForLegacyVersion`：仅当读取的索引为 v3（无长度字段）时，
  对时间戳匹配（将被跳过加载）的文件并行做标签扫描（与 `TryScanLazyFileLabels` 同源），
  发现含 `#FUNCTION(S/F)` 或事件标签的文件 → 从 `LazyLoadingFiles`/`lazyLoadingFilesTable`
  剔除并加入 `ChangedFiles` → 启动正常加载，后续 `SavePartialLazyLoadingList` 按
  IsEvent||IsMethod 语义将其排除出新索引。
- 剔除仅作用于内存，不写盘：避免与 snake 双引擎互相重建形成每次切换全量加载震荡。
- v4 索引有长度第二信号，跳过验证（正常路径零开销）。

### 4. 诊断日志头（上一轮遗留）

- `GenericUtils` 两处导出路径的 `useLazyLoading` 改为读真实配置
  （`Config.UseLazyLoading && Program.SupportsLazyLoading`）。

## 功能偏移评估

- 多签名机制逐字段对照 v24/snake `FunctionMethod.cs` 移植，错误文案同源；
- GCLEAR 签名/行为与 v24/snake 完全一致；
- 懒加载 v3 验证是 gEmuera 特有的防御（snake 无 Android 场景），不改索引格式语义，
  不触碰 v4 读取路径；
- 既有单签名函数路径未被改动。

## 验证

- `dotnet build`：0 错误 0 警告；xUnit 20/20 通过。
- GCLEAR：基类多签名校验对 6 参签名命中第二组 → 通过 → 矩形清除执行。
- GCREATEFROMFILE：3 参（含 isRelative）按 v24 语义通过校验并解析路径。
- WORKCHECK：用真实 `自用函数.ERB` 验证扫描逻辑能检测到 176/192 行的 `#FUNCTION`；
  本地 v3 索引 2213 文件全量扫描 0 误报（本地索引健康，验证只在手机场景触发）。
- 手机 v3 索引 + 已更新文件 → 剔除 → 启动加载 → 函数注册 → SavePartial 排除出新索引，
  链路闭环且一次性自愈。
- 多签名机制兼容性：89 处 `argumentTypeArray = null` 的既有类全部已带 override 或
  改用 argumentTypeArrayEx，基类新路径零回归。

## 后续观察项

- snake 独有扩展 `SPRITECREATE` 8/10 参（截取+偏移+缩放）在 gEmuera 中仅支持 2~6 参；
  当前无游戏实测依赖，且 v24 无此扩展——如遇依赖再按多签名机制补充。
- `GFILLRECTANGLE` 等其余多签名函数签名与 v24/snake 一致，无需改动。
