# 2026-10-20 SelectCase 跳转表重复 CASE 警告污染 v24 启动画面

## 背景

用户对比 Emuera 与 gEmuera 启动同一份 eraBEMANI：Emuera 启动日志/画面没有这些
`警告Lv1 ... CASEの値(...)が重複しています`，gEmuera 却在 console 刷出大量 Lv1 警告。

## 根因

`警告Lv1:CASEの値(...)が重複しています` 不是参考 v24 的行为，而是 gEmuera 的
`SelectCaseJumpTable` 性能优化在建立跳转表时新增的检查：

```csharp
if (table.intTable.ContainsKey(value))
{
    ParserMediator.Warn("CASEの値(" + value + ")が重複しています", caseLine, 1, false, false);
    continue;
}
```

参考 v24 的顺序扫描 SELECTCASE 对重复 CASE 静默取第一个匹配，不产生任何警告。eraBEMANI
里有大量重复 CASE（多文件重复定义），所以 gEmuera 启动时刷出几十条 Lv1。

## 修复

`scripts/Emuera/GameProc/SelectCaseJumpTable.cs` 的三个重复检测分支加会话门控：

```csharp
if (Program.Compatibility.Snake.IsEnabled)
    ParserMediator.Warn(...);
continue;
```

v24pure 下不再产生这些警告；snake 会话保留原来的 Lv1 提示。跳转表本身仍只登记第一个值，
运行时语义与参考保持“第一个匹配 CASE 生效”。

## 验证

- 构建 0 error。
- legacy-runner v24pure eraBEMANI 启动：`display.json` 的 `警告Lv1` 从几十条降为 0；
  仅剩一条 `警告Lv2:2-実績.ERB:292 ...`（另一个独立问题，不在本次 Lv1 范围）。
- 启动截图不再出现 Lv1 警告刷屏。
- eratoho v24pure 回归：`Passed`，`semanticSha 4859125d3f98…` 不变。
- 三冒烟、CoreContractSmoke、xUnit 131+35+3、governance 4/4 全绿。

## 相关

- `scripts/Emuera/GameProc/SelectCaseJumpTable.cs`
- `scripts/Emuera/GameProc/ErbLoader.cs` 的 `SelectCaseJumpTable.TryBuild` 调用
- 参考 v24 无跳转表重复 CASE 警告（顺序扫描语义）
