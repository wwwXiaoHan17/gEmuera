# Snake behaviorChecks 本地替身 harness（Test-SnakeBehaviorSurface）

`tools/snake-alignment/Test-SnakeBehaviorSurface.ps1`

## 为什么需要它

同目录的 `Test-SnakeReferenceSurface.ps1` 是 snake 对齐的**真门禁**，它有一个必填参数 `-ReferenceRoot`，
必须指向上游参考检出 `E:\MyCode\Era\emuera_lazyloading_*`。

**不是每台机器都有该参考检出。** 真门禁在 `Required source file was not found` 处直接 `throw`，
根本走不到后面的断言 —— 也就是说，那 32 条 `$behaviorChecks` 在无参考检出的机器上**永远跑不到**。
而 `$behaviorChecks` 恰好是门禁里"按**精确路径** + 内容正则钉源码"的那一半：

```powershell
@{ Path = (Join-Path $project 'Scripts\Emuera\GameProc\Function\Instraction.Child.cs'); Pattern = 'SETIMAGELAYERL_Instruction'; Label = 'SETIMAGELAYERL instruction' },
```

这正是 `FILE_STANDARD.md` §3 纯移动拆分**最容易破坏**的表面：把成员搬到
`Type.Feature.cs` 分片里去，钉扎指向的路径就变了，正则再也匹配不到，真门禁会在有参考检出的机器上失败，
而在无参考检出的机器上"看起来一切正常"。

本 harness 就是补这一段：**从真门禁脚本里解析出权威的 `$behaviorChecks` 字面量（解析，不复制，
因此不可能与门禁漂移）**，然后逐条按其断言方式求值：文件存在 + 内容正则命中。

## 什么时候用

- 改过 `Scripts/Emuera/` 解释器、或对其中任何文件做过 partial 拆分 / 成员搬移之后。
- 提交纯移动拆分 PR 之前（配合 `tools/pure-move/Test-PureMove.ps1`：一个管"行没变"，一个管"路径钉扎还在"）。
- 想在**没有参考检出的机器**上确认 snake 行为钉扎没被拆散时。

## 怎么用

在仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/snake-alignment/Test-SnakeBehaviorSurface.ps1
```

可选参数：`-ProjectRoot`（默认取脚本上两级目录）、`-GateScript`（默认同目录的 `Test-SnakeReferenceSurface.ps1`）。

预期输出（本仓库当前状态）：

```
behaviorChecks total : 32
passed               : 32
failed               : 0

RESULT: 32 passed / 0 failed
```

失败时逐条打印 `Label [file missing|pattern not found] 路径` 并以 `exit 1` 退出。

## 两条防呆（都是踩坑换来的，别删）

1. **解析结果为空 → 直接 `throw`**，拒绝空跑通过。
2. **任一条目的 `Path` 为空 → `throw`**，并列出条目 Label。
   首版 harness 忘了定义 `$currentInstructionPath` 等变量，导致这些条目 `Path` 为空、
   几乎"全过"。上游若给路径变量改名，这里必须**显式暴露**，而不是静默全过。

（真门禁里 `$behaviorChecks` 字面量当前位于第 70–103 行；harness 会把实际解析到的行号打印出来，
行号漂移不影响使用，因为定位是按 `$behaviorChecks = @(` / `)` 结构做的。）

## 它证明了什么

- 真门禁的 32 条 `$behaviorChecks`（精确路径 + 内容正则）在当前工作区**全部命中**。
- 也就是说：这一半钉扎没有被纯移动拆分、改名、删除或搬走。

## 它**不**证明什么（务必如实理解）

- **只覆盖 `$behaviorChecks` 这 32 条**。真门禁的**上游一致性比对那一半完全没有覆盖**
  （`FunctionIdentifier.cs` / `Creator.cs` 的指令键与表达式函数键 vs 参考检出），
  因为那一半必须读参考检出。有参考检出的机器上仍**必须**跑真门禁。
- `$behaviorChecks` 是**静态文本**断言：正则命中 ≠ 语义正确、≠ 运行期行为对齐、≠ ERB 方言行为一致。
  真门禁自己也写着 "Static surface and wiring checks only; full runtime behavior parity is not implied."
- 不调用编译器（不证明编译通过），不做构建，不写任何文件，只读源码。
- 依赖真门禁脚本的结构（存在 `$behaviorChecks = @( ... )` 字面量）。若真门禁改成别的表达形式，
  本 harness 会 `throw` 而不是静默放过 —— 这是设计意图。

## 编码

本文件是 **ASCII-only**（注释与输出都是），因此有无 UTF-8 BOM 都能在 Windows PowerShell 5.1 下正确解析 ——
PS 5.1 会把无 BOM 的 UTF-8 `.ps1` 按 GBK 解码，非 ASCII 字面量会被破坏到语法错误（FILE_STANDARD §2 记录的坑）。
改动时请保持 ASCII-only；若一定要写中文，必须同时确保文件带 UTF-8 BOM。
