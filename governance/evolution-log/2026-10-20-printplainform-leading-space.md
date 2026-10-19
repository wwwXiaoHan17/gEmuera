# 2026-10-20 PRINTPLAINFORM 前导空格丢失导致 eraBEMANI 教程框错位

## 背景

用户报告 `eraBEMANI` 教程（`ERB/1-タイトル,ニューゲーム処理/8-チュートリアル.ERB`）的
`TPRINTL` / `TSHOW` 文本框显示错位：图标盖住框线和首字，画面整体看起来左移/裁切。

## 根因

教程 `TSHOW` 使用：

```erb
PRINTPLAINFORM 　　　　　┃%TUTORIAL_TEXT:0, 罫線文字数*2, LEFT%┃
```

`PRINTPLAINFORM` 后面是一个半角分隔空格 + **五个全角空格**，用来把框线排到 5 个字符宽度的
`<img>` 右侧。参考 v24 的 `LogicalLineParser` 在识别到命令后只 `ShiftNext()` 消费一个分隔符，
随后把原始参数流交给 `FORM_STR_ArgumentBuilder`，因此五个全角空格会进入 `FORM_STR` 并被打印。

gEmuera 的 `LogicalLineParser` 在命令不是 print-preserve 类型时执行了
`LexicalAnalyzer.SkipWhiteSpace(stream)`，把后续所有空格（含五个全角空格）都跳过；
`PRINTPLAIN/PRINTPLAINFORM` 在 `FunctionIdentifier` 注册时没有 `IS_PRINT` 标志，
`ShouldPreserveRawPrintArgument` 返回 false，于是命中了该 SkipWhiteSpace 分支。

结果：框线从第 0 列开始，被左侧 `<img>` 覆盖；首字也被覆盖，和用户截图一致。

## 修复

在 `scripts/Emuera/GameProc/Function/FunctionIdentifier.cs` 给
`PRINTPLAIN` / `PRINTPLAINFORM` 注册补上 `IS_PRINT`：

```csharp
addFunction(FunctionCode.PRINTPLAIN, ..., METHOD_SAFE | EXTENDED | IS_PRINT);
addFunction(FunctionCode.PRINTPLAINFORM, ..., METHOD_SAFE | EXTENDED | IS_PRINT);
```

这样 `ShouldPreserveRawPrintArgument` 对两者成立，解析器只消费一个分隔符，保留后续全角空格，
与参考 v24 的 `ShiftNext()` 行为一致。`IS_PRINT` 对 SKIPDISP 的处理也与参考已有
`case PRINTPLAIN: if (skipPrint) break;` 等价，不改变跳过语义。

## 验证

- 构建 0 error。
- eraBEMANI：接受许可后的教程截图
  `E:\tmp\gemuera-reports\bemani-tutorial-fix1\run-001\viewport.png`：
  `display.json` 中框线行出现 `　　　　　┃` 五个全角空格，框线/文字在图标右侧，不再被覆盖。
- eratoho v24pure 无头回归：`Passed`，semantic
  `4859125d3f984f087eb143dd83fa54f3b754055187cb87a9e6b166d4505754a1` 不变。
- 三冒烟（v24 561/266、snake 670/351）、CoreContractSmoke、xUnit 131+35+3、governance 4/4 全绿。
- Android 真机仍未测试（本机无 adb 设备）；教程布局与桌面 canvas 后端一致。

## 相关文件

- `scripts/Emuera/GameProc/Function/FunctionIdentifier.cs`
- `scripts/Emuera/GameProc/LogicalLineParser.cs`（现有 ShouldPreserveRawPrintArgument 路径，未改）
- 参考：`E:\MyCode\eraCode\emuera.em-master\Emuera\Runtime\Script\Parser\LogicalLineParser.cs` 546-547 行
