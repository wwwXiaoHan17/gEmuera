# 文档断层修复（docs-fault-line-fix）

## 任务目标与成果

消除 2026-08-22 同步（`581cb03`→`451ea19`）后暴露的文档新旧断层：修复 AGENTS.md 失真引用、readme 三语版过时构建命令与死链、DeveloperHandoff.md 的 12 处断链与过时断言、xEmueraCodeWiki 索引错位。共修改 7 个文档文件。

## 关键决策与 Why

1. **DeveloperHandoff 采用"历史快照化"而非重写**：顶部加权威入口声明 + 逐条过时要点 + 断链去链接化。理由：保持 2026-07-17 快照原貌可追溯，成本最低，且 readme 不再把它指为权威。
2. **构建命令以 AGENTS.md 2026-08 实测为准统一三语 README**（Godot headless `--build-solutions`，禁 `dotnet build`）：实测经验优先于历史文档。
3. **断链标注真实删除日期（逐路径 git 取证）而非统一日期**：10 个 NewFrameworkDesign 文档实际删除于 99d7c16（2026-08-07），仅 CODE_MAP.md 删除于 d24f656（2026-07-25）；tools/m3-m7 实为重命名并入 tools/governance/ 而非删除。

## AI 表现复盘

- **有效**：三路对抗审查（链接核验/diff 纪律/事实核验）抓到实施层的批量日期错误（10 处 major）与 m3-m7"删除"措辞失实；日文版中文式破折号也被 diff 审计抓出。审查波投入产出比极高。
- **低效**：首轮文档侦察给出的"删除于 2026-07-25"结论未做逐路径 git 验证就被写进修复指令，导致实施者批量复制了错误日期，靠第二波审查才纠正——同一事实被两个 agent 链式转手后失真。

## 教训 → 具体优化动作

- **日期/计数类事实断言，转录进文档前必须逐条 git 取证**（`git log --full-history --diff-filter=D -- <path>`），不接受侦察报告的转述。
- en/ja 翻译新增段落时检查标点风格本地化（中文「——」在日文原文 0 次出现即应改写）。
- 后续发现待处理（本次范围外，已登记）：`tools/doc-guards` 必需文档清单仍引用已删除的 docs/NewFrameworkDesign 设计文档（守卫当前不可跑）；`tools/legacy-runner` 多个测试仍引用 `Scripts\M0\*` 旧路径（M0→LegacyRunner 改名时静默失联）。
