# 进化记录：eraFL 方言能力清单 + 接口职责修正（2026-08-22）

> 任务：修正"eraFL 游戏必须用 snake 接口才能运行"的设计缺陷——erafl 闭包
> `{v24, erafl}` 缺少 eraFL 游戏实测依赖的 snake 系能力。

## 背景（审查复核结论）

### 唯一真实缺陷：erafl 闭包缺能力

- erafl profile 闭包 = `{gemuera.v24, game.erafl}`，`game.erafl` 模块只提供策略
  （输入/标记/地图/资源），**不声明任何指令/函数能力**。
- 全面扫描 eraFL 游戏（erafl-master，恋姫→eratohoK→era4X 血统）对 snake 系
  指令/函数的依赖：**恰好只有 `SETANIMETIMER` 指令一处**
  （`グラフィック生成.ERB:356`，`SETANIMETIMER 1000 / フレームレート`，
  `@アニメフレームレート設定` 内）。SQL_*/MAP_*/UNCHECKED_* 一概未用。
- 该指令由共享仓 snake 侧注册（v24 参考仅函数版、snake 参考仅指令版），
  v24 基线隐藏它 → erafl 会话不可见 → 启动链 `グラフィック生成.ERB` 解析失败。

### 被驳斥的审查结论

- **缺陷 1（v24pure 误杀 SETANIMETIMER 函数）不成立**：当前代码中
  SETANIMETIMER 函数形态在 v24pure 可见（与 v24 参考 Creator.cs:211 一致），
  且该状态在整个 git 历史中从未以"函数被隐藏"形式存在。
- **缺陷 3（eraTW 依赖 snake）成立但定性正确**：eraTW 实测 20 种 snake 函数
  399 处调用（SQL_* 15 种 386 处 + UNCHECKED_*/GETANIMETIMER/GETPLATFORM/
  MAP_FINDKEY），v24pure 拒绝是**正确隔离**（v24 参考确无这些函数）。

## 修复

### 1. erafl 模块自持能力清单（LegacyCompatibilityModules.cs）

```csharp
// eraFL 实测依赖的 snake 系指令（handler 由共享仓 snake 侧注册，erafl 只声明
// 可见性需求）。证据：erafl-master グラフィック生成.ERB:356 "SETANIMETIMER 1000"。
private static readonly IReadOnlyCollection<string> InstructionNames =
    Array.AsReadOnly(new[] { "SETANIMETIMER" });
```

- `Declare()` 增加 `builder.DeclareInstructionNames(InstructionNames)`；
- `Apply()` 增加 `builder.ExposeInstructionNames(InstructionNames)`（在
  `SetEraFlPolicy` 之前）。
- 机制：Declare 对所有会话执行（v24pure/snake 下该指令本就隐藏、零影响）；
  Apply 仅 erafl 会话解除隐藏。
- **三接口职责不变**：v24pure = v24 参考；snake = v24 + snake 方言；
  erafl = v24 + eraFL 实测能力清单 + eraFL 策略。

### 2. v24pure 诊断提示（LegacyCompatibilityProfile + 两处报错点）

- Builder 记录隐藏名归属模块（`hiddenNameOwners`），`Compose` 在 Declare/Apply
  前 `SetDeclaringModule`。
- Profile 新增 `TryGetUnselectedModuleHint(name, out moduleId)`：名字隐藏且其
  归属模块未被本会话选中时返回归属（snake 会话查询自身隐藏的函数形态不提示）。
- 挂接两处报错：
  - `LogicalLineParser.cs` 语句指令未命中（"解釈できない行です"）追加
    "（X 属于 game.snake 模块的能力，建议在启动器中改用对应接口）"；
  - `IdentifierDictionary.ThrowException`（"は解釈できない識別子です"）同追加。

### 3. 方言治理工具链同步（dialect-inventory）

- **SurfaceSmoke**：新增 erafl 断言（SETANIMETIMER 指令可见、SQL_CONNECT/
  CALLSTR 隔离、v24pure/snake 回归护栏、TryGetUnselectedModuleHint 语义）；
  修复 2 个基线断言缺陷（BITMAP_CACHE_ENABLE 的 METHOD 投影语义、
  陥落状態 兜底可见性）。
- **RuntimeSmoke**：snake 函数计数 347→349（对齐当前注册表面）。
- **DialectInventory.psm1**：
  - 快照报告纳入 erafl profile（`profiles.erafl` + `eraflHash`）；
  - `Get-LegacyProfileSurfaceNames` 修复：去掉 `[CmdletBinding()]`（高级函数
    在模块嵌套调用时变量作用域失效）、`$matches` 自动变量冲突、
    扁平数组返回（`,@()` 引入嵌套数组破坏 `-notin` 语义）；
  - Report 内 erafl 清单提取拆中间变量（高级函数内 `@(Get-...)` 内联被 unroll）。
- 快照已生成：`docs/NewFrameworkDesign/generated/dialect-registry-snapshots.json`
  含 erafl 维度（SETANIMETIMER 可见、CALLSTR 隔离）。

## 功能偏移评估

- erafl 能力清单是**按需声明**（不把 snake 整包塞进 erafl 闭包），
  与"三接口职责分明"原则一致；src/Core 闭包定义未改动。
- v24pure 提示是宿主层诊断增强，不改变解析语义。

## 验证

- `dotnet build`：0 错误 0 警告；xUnit 20/20 通过。
- LegacyDialectSurfaceSmoke / LegacyDialectRuntimeSmoke：全绿。
- 快照 erafl 维度：SETANIMETIMER visible=True、CALLSTR visible=False。
- **无头游戏级实测（legacy-runner，Godot 4.7 mono @ `E:\Godot_v4.7-stable_mono_win64`）**：
  - eraFL 真实游戏 + erafl：3/3 Passed，完整走完启动链到达标题主菜单
    （2135 行控制台输出，errors 空）——SETANIMETIMER 解析点已越过；
  - 最小 fixture（`SETANIMETIMER 100` + `PRINTW`）+ snake：3/3 Passed；
  - 同 fixture + erafl：3/3 Passed；
  - 同 fixture + v24pure：如期失败，emuera.log 报
    `解釈できない行です（SETANIMETIMER 属于 game.snake 模块的能力…）`；
  - 表达式 fixture（`LOCAL = SQL_CONNECT("x")`）+ v24pure：如期失败，报
    `"SQL_CONNECT"は解釈できない識別子です（该标识符属于 game.snake 模块的能力…）`。

## 收尾补遗（同日实测发现并修复）

1. **LogicalLineParser 提示缺口**：语句位置报错有两条路径——`stream.EOS`
   分支（光杆标识符）与赋值解析失败分支（带参数指令，如 `SETANIMETIMER 100`，
   即 eraTW/eraFL 的真实语法）。原实现只挂了前者；实测后已给后者补挂同一提示
   （fixture 复测通过）。
2. **legacy-runner 工具链缺口**：`Invoke-LegacyRunner.ps1`（两处守卫）、
   `Invoke-LegacyDisplayBaseline.ps1`（ValidateSet）、
   `legacy-runner-config.schema.json`（两处 enum）的白名单落后于引擎侧
   （LegacyRunnerConfig 已接受 erafl），已补齐 erafl。
3. 解析时机备注：PRINTV 参数惰性求值（加载期不解析），验证表达式路径提示
   需用赋值行（右值加载期解析）。

## 后续观察项

- eraFL 游戏修复 SETANIMETIMER 后可能暴露对 snake 宽松策略（多余实参等）的
  隐性依赖——届时按"职责分明"原则在 erafl 模块内显式声明对应能力/策略。
- Test-Dialect* 完整流水线依赖 upstream 构建产物（artifacts/ 未入库），
  属既有环境依赖，非本次改动引入。
- 024e8a1 曾把 dialect-classification.json 的 workPackage 误改为
  LEGACY-DIA-01（违反 schema const: M0-DIA-01），本次已恢复。
