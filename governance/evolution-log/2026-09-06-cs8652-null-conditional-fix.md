# CS8652 null 条件赋值编译修复（cs8652-null-conditional-fix）

## 任务目标与成果

修复同步 `451ea19` 后的编译错误：`Scripts/FirstWindow.cs:1201` 的 `GetViewport()?.SizeChanged -= ...` 触发 CS8652（null 条件赋值/复合赋值是 C# 预览特性，项目 `Directory.Build.props` 的 `LangVersion=latest` 不支持）。全仓 grep 仅此一处。改为显式判空局部变量 + 取消订阅（净增 3 行）。

## 关键决策与 Why

1. **改代码而非把 LangVersion 升到 preview**：preview 语言版本不受支持且依赖 SDK 版本，会让所有协作者/构建环境被绑架；一处机械改写成本为零。
2. **不能简单去掉 `?.`**：`_ExitTree()` 时节点可能已脱离树，`GetViewport()` 可能为 null，必须保留判空语义——用局部变量判空，避免二次调用。
3. 顺带核对 FILE_STANDARD 纪律：`FirstWindow.cs`（2019 行）在"只减不增"在册清单内，本次净增 3 行属就地修复容差（≤50 行/PR）。

## AI 表现复盘

- **有效**：先全仓 grep 排查同类写法（`\?.x -=`/`\?.x =`）确认仅一处，避免"修一个漏一串"；构建验证严格按 AGENTS.md 口径（dll 时间戳而非进程退出）。
- **低效**：首次 grep 正则用了 ripgrep 不支持的前瞻语法被拒，拆成两个无前瞻模式才完成——工具语法适配浪费一轮。

## 教训 → 具体优化动作

- 上游合并后首次构建要留意**预览语言特性**类错误（CS8652/CS8666 等），这是"上游用不同 LangVersion 环境开发"的信号；grep 排查模板：`\?\.\w+ (-=|\+=|= )`。
- 若后续上游再引入此类写法，考虑在 PR 审查清单加一条"禁止依赖 preview 语言版本"（可并入 FILE_STANDARD 或 AGENTS.md，本次未动规范，仅记录）。
