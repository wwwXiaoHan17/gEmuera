# 纯移动拆分校验器（Test-PureMove）

`tools/pure-move/Test-PureMove.ps1`

## 为什么需要它

`FILE_STANDARD.md` §3 模式 A/B 的大文件拆分要求"纯移动、零逻辑改动"，§4.5 把允许的机械 diff 限定为：
成员搬移、分片头注释、主文件因迁出而失效的 `using` 清理、空 `#region` 删除，以及 partial 分片必需的
外壳（`namespace` 声明 / 类型声明 / 最外层包裹大括号）。

人工逐行 review 一个动辄上万行、跨 4~8 个文件的 diff 既慢又不可靠；`git diff --stat` 只能说明
"删了多少行、加了多少行"，说明不了"删的和加的是同一批行"。这个脚本把该结论变成可执行的机械校验。

它同时补了一个真实存在的盲区：**行多重集比对对顺序不敏感**。多重集只能证明"行没有丢失/新增"，
不能证明"行没有被重排"。而 C# 的 `static readonly` 字段初始化器是按**文本顺序**执行的，
成员换序是真实的行为风险 —— 这一点在拆 `Scripts/GenericUtils.cs` 时被验证过（当时由执行 agent 误报，
复核后确认是描述写反了，但工具本身确实看不见这类问题）。因此本工具加了第二道独立检查：**保序（子序列）检查**。

## 什么时候用

- 提交一个纯移动拆分 PR 之前、之后自查（拆分本身、以及别人交付的拆分）。
- 复核一个 agent 声称"纯移动"的拆分是否属实。
- 排查"某个成员是不是在拆分中丢了/被改写了"。

## 怎么用

在仓库根目录执行（Windows 环境常只有 Windows PowerShell 5.1、没有 `pwsh`）：

```powershell
# 最常用：基线取自 reports\.base-commit（该文件是本地基线指针，reports/ 不入库）
powershell -NoProfile -ExecutionPolicy Bypass -File tools/pure-move/Test-PureMove.ps1 `
  -Family Scripts/EmueraContent.cs

# 打印全部差异行（默认只列前 40 条）
powershell -NoProfile -ExecutionPolicy Bypass -File tools/pure-move/Test-PureMove.ps1 `
  -Family Scripts/GenericUtils.cs -ShowDetail

# 分片名与主文件不同名时，必须用 -Include 显式追加家族成员
# （例：主文件 VariableToken.cs 里装的其实是 VariableData 的实现，分片按事实命名为 VariableData.ReferenceTokens.cs，
#   通配 ^VariableToken(\.[A-Za-z0-9_]+)?\.cs$ 匹配不到它）
powershell -NoProfile -ExecutionPolicy Bypass -File tools/pure-move/Test-PureMove.ps1 `
  -Family Scripts/Emuera/GameData/Variable/VariableToken.cs `
  -Include Scripts/Emuera/GameData/Variable/VariableData.ReferenceTokens.cs

# 指定某个基线提交（例如某一批拆分的父提交）
powershell -NoProfile -ExecutionPolicy Bypass -File tools/pure-move/Test-PureMove.ps1 `
  -Family Scripts/EmueraContent.cs -BaseRef c8d8268

# 关掉"分片外壳"归一化，退回严格逐行口径（新建分片的类型声明/大括号会被算成新增行；
# 类型声明上的 partial 归一化仍然生效）
powershell -NoProfile -ExecutionPolicy Bypass -File tools/pure-move/Test-PureMove.ps1 `
  -Family Scripts/EmueraContent.cs -RawNormalize
```

参数：`-Family`（必填，相对仓库根的主文件路径）、`-Repo`（默认取脚本上两级目录）、`-BaseRef`、
`-Include`（可多次）、`-ShowDetail`、`-RawNormalize`、`-WorkDir`（默认 `artifacts\pure-move-tmp`，用完即删）。

### 基线口径与退化保护

`-BaseRef` 留空时回落到仓库根的 `reports\.base-commit`（纯文本，第一行非空内容即 ref）。
**若 `-BaseRef` 解析出的 commit 与 `HEAD` 相同，脚本拒绝运行并 `exit 2`**：提交之后再拿 `HEAD` 当基线，
比对会退化成"拿拆分后的自己比自己"，所有家族都会被误报成 `PURE MOVE`（本仓库真实踩过这个坑：
提交后复核，5 个家族全报 `PURE MOVE`）。此时应改为指向拆分前的提交，或更新 `reports\.base-commit`。

### 退出码

| 码 | 含义 |
| --- | --- |
| 0 | 行多重集一致 **且** 所有新建分片保序检查通过 |
| 1 | 行多重集存在非机械差异（需人工裁定） |
| 2 | 拒绝运行（`-BaseRef` 解析为 `HEAD`） |
| 3 | 行多重集一致，但存在分片正文乱序（`ORDER: VIOLATION`） |

## 它做了什么

1. **行多重集比对**：取同一「文件家族」（主文件 + 其 `Type.Feature.cs` 分片，通配
   `^<stem>(\.[A-Za-z0-9_]+)?\.cs$`，外加 `-Include` 显式成员）在基线与工作区的全部代码行，
   归一化后做多重集比对。差异为 0 → `MULTISET: PURE MOVE`。
2. **保序（子序列）检查**：只对「基线里不存在的文件」（本次新建的分片）执行。取分片正文行序列，
   用两指针贪心扫描判定它是否是**基线主文件正文行序列的子序列**（允许间隔、不要求连续）。
   若主文件不是匹配目标，会再依次尝试基线家族的其它成员（这是扩展：分片也可能从既有分片再拆出）。
   每个新建分片打印一种结论：

   | 输出 | 含义 |
   | --- | --- |
   | `ORDER: OK` | 正文是某个基线文件正文的子序列（打印是哪一个 + 正文行数） |
   | `ORDER: VIOLATION` | 正文乱序：打印匹配深度、第一处失配的分片**原始行号 + 内容**，并给出 `exit 3` |
   | `ORDER: EMPTY-BODY` | 剥壳后正文为空，无从判定（按未通过处理，请人工确认不是空壳分片） |
   | `ORDER: NO-BASELINE` | 基线家族里没有任何成员（例如 `-Family` 在基线中不存在），无从判定 |
   | `ORDER: N/A` | 本次没有新建分片，无正文可比对（多重集结论仍然有效） |
3. **归一化口径**（两道检查共用）：去首尾空白；丢空行、`using`、`#region`/`#endregion`、
   整行注释（`//`、`/*`、`*`、`*/`）；类型声明行上的 `partial` 修饰符单独抹掉
   （模式 A 拆分必然要给主文件的类声明补 `partial`，不抹就会让每个"主文件原本不是 partial"的
   纯移动拆分固定多报 1 行差异 —— 实测 `GenericUtils.cs` 家族正是仅因此多报
   `internal static class GenericUtils`；C# 里增删 `partial` 不改变语义）。再（除非 `-RawNormalize`）剥掉"分片外壳"。
4. **剥壳规则**（结构判定，不是"去掉前 N 行"）：反复执行下述剥离 —— 区域内首个把大括号深度从 0
   抬到 >0 的行是候选开块行；该块必须**恰好**包到区域末尾（从开块行起累计深度**首次**回到 0 的行
   == 区域末行）；且开块行本身是 `namespace`/类型声明，或它只是一个单独的 `{` 且其上方前缀**全部**
   是外壳行并出现过类型声明或 `namespace` 声明。三条同时成立才剥掉"前缀 + 最外层大括号"。
   这三个条件各自对应一次实测踩坑（详见脚本头注释）：只判"最后一个回到 0 的行"→ `Canvas.cs` 多剥 6 行；
   不查前缀全为外壳 → `EmueraContent.cs` 被剥掉 379 行、比对产生假差异；前缀只认类型声明 →
   块状 `namespace` 外壳剥不掉、`Creator.Method` 家族多出 18 行外壳。括号深度统计会跳过
   字符串/逐字字符串/字符字面量与行尾 `//` 注释里的括号。
   **覆盖边界**：文件含多个顶层类型时（例：`Scripts/GenericUtils.cs` 以 `public enum EmueraLogLevel`
   开头，类声明不在文件最外层），最外层不包住整个文件，剥壳整份跳过 —— 该文件的顶层声明行会留在
   正文里参与比对（两侧对称，不会假通过；只有真改了那几行才报差异）。
5. **编码**：git 对象内容一律用 `cmd /c "git show ... > 临时文件"` 落成字节文件再以 UTF-8 读回 ——
   **不要**直接捕获原生命令输出，PS 5.1 会按 GBK 解码成 mojibake。脚本自身必须保留 UTF-8 BOM
   （否则中文串被按 GBK 解码，可能直接语法错误）；用 write/edit 工具改过之后要补回：

   ```powershell
   $p='tools\pure-move\Test-PureMove.ps1'; $t=[IO.File]::ReadAllText($p,[Text.Encoding]::UTF8)
   [IO.File]::WriteAllText($p,$t,(New-Object Text.UTF8Encoding($true)))
   ```

## 它证明了什么

- 家族全部 `.cs` 成员归一化后的**代码行多重集在基线与工作区之间完全相同**（零行丢失、零行新增）。
- 每个**新建分片**的正文行序列是基线主文件正文的**子序列**（没有把成员上下重排）。

## 它**不**证明什么（务必如实理解）

- **不证明编译通过**：不调用任何编译器。删掉一个 `}` 也只是让"行多重集"多一条差异而已。
- **不证明运行期行为等价**：签名改动、常量改值、条件反转、字段初始化顺序的**跨文件**变化都不会被发现
  （部分"改写"会表现为差异行，但工具只报差异，不判语义）。
- **不证明钉扎/引用没被破坏**：`FILE_STANDARD.md` §6 的六类路径钉扎（方言证据链、契约测试、
  tools 工程源链接、场景 `.tscn` 硬路径引用、`.uid` 侧车等）都不在本工具范围内 —— 那些要用
  `tools/dialect-inventory/*`、`tools/core-contracts/Test-CoreArchitecture.ps1`、以及
  `tools/snake-alignment/Test-SnakeBehaviorSurface.ps1` 各自覆盖。
- **保序检查只覆盖"新建分片 vs 基线主文件"**，且判定是"存在某个基线文件使其成立"；
  它不能证明"整个家族的成员相对顺序完全没变"（主文件内部的成员换序不在检查范围内）。
- **归一化会丢弃 `using` 开头的行**，包括方法体里的 `using (...)` 语句；也丢弃整行注释。
  这些是刻意为之（正是 §4.5 允许变化的部分），但意味着**这类行的增删不会被报出来**。
- 只处理 `.cs` 家族成员；`*.cs.uid`、场景、工程文件不在范围内。

## 自校验（本仓库实测，基线 `e8ad27c` = `reports\.base-commit`）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/pure-move/Test-PureMove.ps1 -Family Scripts/EmueraContent.cs
```

第三个批次的三个家族全部 `PURE MOVE` + `ORDER: OK` + `exit 0`：

| 家族 | 基线/工作区代码行合计 | 新建分片 |
| --- | --- | --- |
| `Scripts/EmueraContent.cs` | 9404 = 9404 | Audio / HtmlGeometry / UiDiagnostic |
| `Scripts/Emuera/GameData/Function/Creator.Method.cs` | 10166 = 10166 | Array / Csv / Math |
| `Scripts/GenericUtils.cs` | 2499 = 2499 | Audio / DiagnosticsLogging / Trace |

**分片名与主文件不同名**的家族（`VariableToken.cs` 里装的其实是 `VariableData` 的实现）用
`-Include`，并把基线指到拆分前的提交：

```powershell
# 正确用法：3777 = 3777，PURE MOVE，ORDER: OK（635 行正文 ⊂ 基线主文件正文），exit 0
powershell -NoProfile -ExecutionPolicy Bypass -File tools/pure-move/Test-PureMove.ps1 `
  -Family Scripts/Emuera/GameData/Variable/VariableToken.cs -BaseRef '8eef88a^' `
  -Include Scripts/Emuera/GameData/Variable/VariableData.ReferenceTokens.cs
```

**保序检查的有效性怎么验证**（临时副本，绝不改真实文件）：把某个分片的正文按**完整成员边界**
对调两段后放进家族，多重集仍然完全一致（它看不见重排），而保序检查会指出第一处失配行：

```powershell
# 该 shard 的 -Include 名字与主文件不同名，因此交换体是家族里唯一的该内容副本 →
# 行多重集保持 PURE MOVE，只有保序检查报警，退出码 3：
#   MULTISET: PURE MOVE
#   ORDER: VIOLATION + <temp>.cs  (正文 635 行，前 300 行有序命中 <基线主文件>，第 333 行失配)
#     分片行号 333: private sealed class ReferenceIntScalarToken : ReferenceToken
#   结论：FAIL —— 行多重集一致，但保序检查未通过（VIOLATION 1 个 / EMPTY-BODY 0 个）（exit 3）
```

删除临时副本即恢复。这正是"多重集对顺序不敏感、保序检查补这个缺口"的直接证据。
