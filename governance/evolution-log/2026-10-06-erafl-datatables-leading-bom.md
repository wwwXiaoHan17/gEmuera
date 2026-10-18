# 2026-10-06 eraFL DT_FROMXML 前导 BOM 剥离（活动目标 GLOBAL_GOAL 静默空表修复）

## 任务与证据

用户指令：`@"C:\Users\20109\Downloads\emuera (4).log"` + eraFL 游戏根（`erafl-master`），"解决这个报错问题"，
并追加约束"**使用 fl 的兼容模块**"（即按 AGENTS.md 游戏适配铁律走 eraFL 方言模块，不动 v24 基线共享路径的未门控代码）。

日志尾部（`gemuera Godot runtime` / `eraFL`）给出唯一硬错误：

```text
SHOP/SHOP_LIFE/SHOP_LIFE16_活動目標.ERBの279行目で予期しないエラーが発生しました:
System.Data.EvaluateException: Cannot find column [name].
   at System.Data.DataExpression.Bind(DataTable table, List`1 list)
   ...
   at MinorShift.Emuera.GameData.Function.FunctionMethodCreator.DtSelectMethod.GetIntValue(...)
```

对应 ERB：`GOAL_COUNT = DT_SELECT("GLOBAL_GOAL", @"name = '%GOAL_NAME%'",,IDS)`。

## 根因（可复现的最小实验）

1. `GLOBAL_GOAL` 表由 `ERB/SYSTEM/FL_INIT_LOADER.ERB` 的 `@LOAD_GLOBAL_GOALS` 建立：
   `LOADTEXT "XML/GLOBAL_GOAL_schema.xml"` / `LOADTEXT "XML/GLOBAL_GOAL.xml"` →
   `DT_CREATE "GLOBAL_GOAL"` → `DT_FROMXML "GLOBAL_GOAL", schema, gameplaygoalXml`。
2. eraFL 的 `XML/*.xml`、`*_schema.xml` 全部是 **UTF-8 with BOM**（`EF BB BF 3C ...` 实测）。
   legacy `LOADTEXT` 走 `Creator.Method.cs:ReadAllTextWithDetectedEncoding`，BOM 分支用
   `Encoding.UTF8.GetString(bytes)`——**BOM 字符保留**成字符串首字符 `U+FEFF`。
3. `DT_FROMXML`（`Creator.Method.DT.cs:DtFromXmlMethod`）是真共享路径：`try { new DataTable(key);
   ReadXmlSchema(StringReader); ReadXml(StringReader); ... } catch { return 0; }`。
   `XmlReader` 拒绝根级 BOM → `XmlException: Data at the root level is invalid. Line 1, position 1.`
   → **catch 静默吞掉** → 表保持 `DT_CREATE` 的 id 壳（只有 `id` 列）。
4. 于是 `DT_ROW_LENGTH` 之类的 id 操作照常，直到 `DT_SELECT(..., "name = ...")` 才在
   `DataExpression.Bind` 抛 `Cannot find column [name]`——**症状与根因相隔一个屏幕**。
   旁证：`DT_CELL_GETS(..., "description", ...)`（同表其它列）也会以同样方式失败。

定位实验（`D:\gemuera\.tmp_repro`，已删）：同一份 schema/data 文本，带 BOM 时
`schema=FAIL(XmlException: Data at the root level is invalid...) cols=[]`，去 BOM 后
`schema=OK data=OK rows=44 cols=[id][sort_order][name][category][difficulity][description][tooltip] select=1`。

**为什么只有 DT_FROMXML 炸**：同一份 BOM 文本走 `XmlDocument` 的路径早有等价归一化
（`Creator.Method.Xml.cs:NormalizeXmlText` 剥前导 `\uFEFF` 与空白），所以 `XML_DOCUMENT` /
`SKILL_*` / `ENEMY_DATA` 等 eraFL 初始化全线正常；只有直连 `DataTable.ReadXmlSchema` 的
`DT_FROMXML` 没有这层归一化。这也解释了为什么游戏能正常进到口上编辑器才崩。

## 修复（eraFL 方言能力门控，未选中方言逐字节不变）

1. Core `GEmuera.Core.Compatibility.EraFlCompatibilityModule`
   - 新能力 `DataTableXmlLeadingBomBehavior = "datatable.xml-leading-bom.v1"`，加入
     `RequiredCapabilities`（随 erafl profile 的 `requiredCapabilityIds` 生效）。
   - 新增确定性纯函数 `StripLeadingBom(string?)`：只丢弃开头一个 `U+FEFF`，其它字符
     （含标签间空白）与参考实现读入内容逐字一致。
2. `MinorShift.Emuera.Compatibility.IEraFlCompatibilityPolicy` 新增具名属性
   `StripsLeadingBomFromDataTableXml`；`DisabledEraFlCompatibilityPolicy`（恒 false）与
   `LegacyEraFlCompatibilityPolicy`（`capabilities.Contains(...)` 派生）双实现同步。
3. 唯一消费点 `Creator.Method.DT.cs:DtFromXmlMethod`：**具名门控**
   ```csharp
   if (Program.Compatibility.EraFl.StripsLeadingBomFromDataTableXml)
   {
       schemaXml = EraFlCompatibilityModule.StripLeadingBom(schemaXml);
       dataXml   = EraFlCompatibilityModule.StripLeadingBom(dataXml);
   }
   ```
   同时把 `catch { return 0; }` 改为 `catch (Exception ex)` + eraFL 会话下的
   `GenericUtils.Warn(EmueraLogCategory.Script, ...)`：返回值契约（失败=0）不变，但
   "表存在却缺列"这类远距离爆栈不再无声。
4. 未选中方言（v24pure/snake）不声明该 capability → 属性恒 false → 走原样分支，
   `DT_FROMXML` 行为与修复前逐字节等价（与 `UsesSafeArithmeticGuard` /
   `AllowsExtendedHtmlAttributes` 等既有 7 项 quirk 同一模式）。

**为什么不改 `ReadAllTextWithDetectedEncoding`**：那会改变全部 profile 的 `LOADTEXT` 语义，
属共享基线未门控路径，违反铁律；且 eraFL 的真实缺陷面只在 DT 这一条消费链上。

## 验证记录

- `dotnet build D:\gemuera\gemuera-c#.csproj -nodeReuse:false -m:1 -t:Rebuild`：**已成功生成，
  0 错误 0 警告**；三个产物时间戳同步更新（`GEmuera.Core.dll` 23:12:40 / `Emuera.dll` 23:12:41 /
  `gemuera-c#.dll` 23:12:44），并在 `gemuera-c#.dll` 中确认 `DT_FROMXML` 与
  `[ERAFL_COMPAT]` 日志字符串、在 `GEmuera.Core.dll` 中确认 `StripLeadingBom` /
  `StripsLeadingBomFromDataTableXml` 已随本次改动编译进去。
- `dotnet run --project tools/core-contracts/CoreContractSmoke.csproj`：`Core contract smoke passed`。
  新增内容：erafl 能力序列钉扎更新（+`datatable.xml-leading-bom.v1`）；新增 BOM 回归块
  （`StripLeadingBom` 只吃首字符 + 剥离后 `name` 列可 Select + **反向前提**：未剥离的 schema
  必被 `ReadXmlSchema` 拒绝，前提失效即红）。
- `dotnet run --project tools/dialect-inventory/LegacyDialectSurfaceSmoke`：`passed`。
  新增内容：erafl 计划必须声明该 capability、v24pure 必须不声明；policy 由账本派生为真 /
  v24pure 恒 false 两条断言扩项。
- `dotnet run --project tools/dialect-inventory/LegacyDialectRuntimeSmoke`：`passed`
  （v24 合并指令 561 / snake 668；v24 函数 266 / snake 349——未选中侧不变）。
- 方言清单未重生成：本次只增 capability id，未动指令/函数/端口名录，
  `LegacyDialectInventories.Generated.cs` 与 `profiles.generated.json` 无差异（已 grep 确认
  capability id 不出现在生成物中）。
- **真机级复现/回归（legacy-runner，真实 eraFL 游戏根，profile=erafl）**：
  - 诊断取证：`erafl-bom-diag`（首等待）与 `erafl-ng-diag`（新游戏）两轮，
    `diagnostics.json` 记录
    `stripBom=True, schemaLen=1314, schemaHead=<?xml versio, dataLen=10988, dataHead=<DocumentEle`
    与 `rows=44, columns=id/sort_order/name/category/difficulity/description/tooltip`
    ——证明方言能力在真实会话里为真、BOM 已剥离、表完整。
  - 端到端：`erafl-goal-diag3` 用 25 步输入把新游戏推进到
    `@ASK_EDIT_MASTER_STATUS` 的「決定」（`MASTER_EDIT_EXIT` → `GLOBAL_GOAL_COMPLETE("GOAL_CHANGE_EQUIPMENT")`，
    即用户报错的同一条 ERB 行），`errors.json` 三项全空、exit 0。
    修复前该动作必然在 `SHOP_LIFE16_活動目標.ERB:279` 抛 `Cannot find column [name]`。
- **用户侧「修复后仍报同错」的口径**：附带的第二份 `emuera(1).log`（23:22 导出）与首份
  报错位置不同（決定 vs 口上エディタ）但症状一致，时间为本次构建（23:12:44）之后——然而
  该会话的运行时日志里**没有任何**本次新增的诊断行，即那次运行加载的不是修复后的程序集
  （长驻的 Godot 编辑器游戏窗口持有旧 assembly）。需重启编辑器/游戏窗口后再验。

## 环境坑（本批额外消耗）

- Godot `--build-solutions` 在本机被沙箱拒绝写 `%APPDATA%\Godot\mono\build_logs\...`
  → 走 AGENTS.md 的逃生通道 `dotnet build <csproj> -nodeReuse:false -m:1`。
- `dotnet build` 随后在 `D:\gemuera\src\{Core,EmueraCompatPack,Scripts}\obj` 被拒写。
  按 `diagnose-windows-sandbox-acl` 技能逐路径修复（DACL 备份在 `D:\gemuera\.acl-report\`，
  每条都带 rollback 命令）：修复**未能**解除受限模式的拒绝——受限模式下
  `D:\gemuera` 根可写，但 `src`、`Scripts`、各 `obj` 一律 `访问被拒绝`，同路径非受限写入正常。
  最终以**非受限执行**完成构建与三个门禁。这是环境问题，与本次代码改动无关；
  报告已落在 `D:\gemuera\.acl-report\acl-report-*.jsonl`。

## 教训

1. **"引擎把文本读成什么"必须进根因链**：LOADTEXT 的 BOM 保留是编码层缺陷，但它只在
   `DataTable.ReadXmlSchema` 这条链上致命；同源的 `XmlDocument` 链因为早就写了
   `NormalizeXmlText` 而"看起来没事"。查共享路径的方言差异，要先看**同一份文本有几条消费链**。
2. **静默 catch 会把根因推离症状**：`catch { return 0; }` 让"表只有 id 壳"潜伏到几百行之后的
   `DT_SELECT` 才爆栈，日志里只有 `Cannot find column [name]`，与 LOADTEXT/DT_FROMXML 毫无字面关联。
   按方言会话加一条带原始异常类型的 Warn，比事后猜栈便宜得多。
3. **`new DataTable(name)` 的列完全来自成功读入的 schema**：任何让 `ReadXmlSchema` 提前退出的
   因素（BOM、编码、XML 声明）都会表现为"表在但列没了"，而不是"表不存在"。
4. **门禁结论必须在非受限环境复现**：本批三个冒烟全部因为写不进 `obj` 才不得不升级执行，
   说明"受限模式下构建失败"不能当成代码错误来排查。
5. **"修好了但用户还报同错"先验程序集身份，再怀疑修复本身**：用户第二次报错时间晚于构建时间，
   但该会话日志里缺少新代码才会产生的诊断行——长驻的编辑器游戏窗口仍在跑旧 assembly。
   对 Godot 项目，"改了 C# 代码"与"用户窗口里跑的是新 assembly"是两件事；
   可回放的真实游戏 runner 是判定二者的裁判（本次靠它证明修复在真实游戏里成立）。

## 未完成 / 后续任务

- eraFL 真机（桌面 + Android APK）走一遍"活动目标"页与目标达成提示，确认 44 行目标全部可读。
- 同类前导 BOM 风险面复扫：`MAP_FROMXML`（已走 `XmlDocument` 归一化）、
  `Creator.Method.DT.cs` 其余 XML 入口（`DT_TOXML`/`DT_CELL_*` 不消费文本）。
  若后续发现 eraFL 其它 `LOADTEXT`→非 XmlDocument 消费链，按同一 capability 模式扩项。
- 上游基线对齐：参考 Emuera 的 `StreamReader`（BOM 检测即剥离）与移植侧
  `ReadAllTextWithDetectedEncoding`（保留 BOM）确有偏差；若要按基线修正，应作为独立批次
  评估对全部 profile 的影响，而不是塞进本方言修复。
