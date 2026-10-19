# 2026-10-19 v24pure 启动修复：式中関数独立语句（OUTPUTLOG 等）恢复参考语义

## 现象

用 `-Profile v24pure` 启动 eraBEMANI（机翻版 1.5）时，加载到最后直接退出：

```
警告Lv2:20-学園モード/学園モード_TRAIN.ERB:662行目:解釈できない行です
警告Lv2:90-CSV_Editor_改/CSVエディター.ERB:1100/1102行目:解釈できない行です
警告Lv2:90-口上エディタ/口上エディタ.ERB:36/1409/1419/2039/2049/2057/2065行目:解釈できない行です
...
ERBコードに解釈不可能な行があるためEmueraを終了します
```

对应 ERB 行全部是 `OUTPUTLOG "file"` / `OUTPUTLOG @"..."` 这类**式中関数を独立した文として書く**语法。
游戏 `emuera.config` 的 `解釈不可能な行があっても実行する:NO`，所以 v24pure 加载失败
（runner: `initial_wait_timeout` → 后续出现致命异常/退出）。snake profile 因 snake 兼容启动策略会继续，
掩盖了问题。

## 参考对照（emuera.em-master / snake fork）

两份参考的 `FunctionIdentifier` 静态构造都做：

```csharp
foreach (var pair in FunctionMethodCreator.GetMethodList())
    if (!funcDic.ContainsKey(pair.Key))
        funcDic.Add(pair.Key, new FunctionIdentifier(pair.Key, pair.Value, methodInstruction));
```

即**所有未与指令重名的式中関数**都会得到一个 `METHOD_Instruction` 标识符，`OUTPUTLOG "file"`
因此是可解析的独立语句行。参考的 `FunctionIdentifier` 中 OUTPUTLOG 指令注册是注释掉的，
`BuiltInFunctionCode.OUTPUTLOG` 也不在指令枚举中。

gEmuera 的差异：
- `scripts/Emuera/GameProc/Function/FunctionIdentifier.cs` 仍注册了 **port-only 的 OUTPUTLOG 指令**
  （`addFunction(FunctionCode.OUTPUTLOG, argb[FunctionArgType.VOID])`），v24 模块再用
  `PortOnlyInstructionNames` 把它从方言表面隐藏；
- 表面隐藏后，`GetInstructionNameDic` 不再包含任何 OUTPUTLOG 条目（函数→METHOD 投影只对
  显式声明的 `MethodProjectedFunctionNames` 生效），于是 `LogicalLineParser` 看到
  `func == null`，落回赋值解析 → “解釈できない行”。

## 修复

**`scripts/Emuera/GameData/IdentifierDictionary.cs`**
1. `legacyInstructionNames` 只收集 `FunctionIdentifier.Method == null` 的真实指令名：
   METHOD 投影属于函数表面（plan Functions），不应被当作指令参与
   `LegacyCompatibilityPlanConsumption` 校验（否则会报
   “Legacy instruction 'OUTPUTLOG' is not declared by the compatibility plan”）。
2. `BindCompatibilityPlan` 在 plan 会话视图构建成功后，做 **parser-only 补齐**：
   遍历计划可见的 `methodDic`，对 `plan.Dialect.Functions` 中存在、且尚无同名指令标识符的函数，
   用 `FunctionIdentifier.CreateMethodStatement` 补一个 METHOD 语句入口。
   这些条目只进入 `compatibilityInstructionDic`（解析查找），不改变
   `GetInstructionNameDic` 的方言表面/清单，也不参与 plan 校验。

**`scripts/Emuera/GameProc/Function/FunctionIdentifier.cs`**
新增 `internal static FunctionIdentifier CreateMethodStatement(string name, FunctionMethod method)`：
复用静态 `methodInstruction`（`METHOD_Instruction`）构造 parser-only 的 METHOD 标识符。
执行路径沿用既有 `METHOD_Instruction.DoInstruction`（把返回值写入 RESULT/RESULTS），
与参考的独立式中関数语句一致。

刻意**没有**把 OUTPUTLOG 加进 `MethodProjectedFunctionNames` / plan 指令清单：
参考没有 OUTPUTLOG 指令，加进去会污染方言表面与反射/清单对比；parser-only 补齐是等价且不漂移的做法。

## 验证

- v24pure × eraBEMANI：`status=Passed`、`exitReason=first_wait_reached`、`errors.json` 三组 errors
  全空；emuera.log 中 `解釈できない行` / Lv2 警告数量 0。
- 回归：v24pure × eratohoYMAEM `PassedRunnerConsistency`，semanticSha `4859125d3f98…` 与修复前基线一致。
- 门禁：build 0 error；`LegacyDialectSurfaceSmoke`（v24 561 指令/266 函数、snake 670/351 不变）、
  `LegacyDialectRuntimeSmoke`、`CoreContractSmoke` 全绿；xUnit 131+35+3；governance 4/4。
- 未验证：snake/eraFL 会话的该语法行为（同一 parser 路径，理论一致）；真机/APK 不受影响。

## 遗留（与参考一致，不阻塞）

- `警告Lv1` 的 CASE 值重复、`警告Lv2:VAR の要素を文字列で指定できません` 等是游戏数据自身的
  参考警告（emuera.em-master 有同样文案），不是 v24 语义偏差；游戏配置只把“解釈できない行”
  视为致命，修复后这些警告不影响启动。
