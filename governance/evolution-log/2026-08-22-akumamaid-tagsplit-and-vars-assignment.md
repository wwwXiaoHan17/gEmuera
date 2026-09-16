# 进化记录：真机日志修复——HTML_TAGSPLIT 崩溃 + VARS 同名赋值劫持（2026-08-22）

> 任务：用户 Android 真机日志（eraAkumaMaid 0.305 CH + eraBlue Resort 0.103）
> 暴露的两个引擎缺陷修复，以及一个"看似缺陷实则正确"的分类确认。

## 日志分析结论

| 日志组 | 游戏 / 接口 | 发现 |
| --- | --- | --- |
| 154847 | eraBlue + v24pure | 20× SETANIMETIMER 隔离提示（符合设计，建议换 snake）；7× OUTPUTLOG 解析失败（见下） |
| 154906 | eraAkumaMaid + 非 snake | 9× "VARI/VARS requires a private variable name."（**Bug A**） |
| 155422 | eraAkumaMaid + snake | 游戏运行后在 `HTML_TAGSPLIT ARGS` 处 InvalidCastException 崩溃（**Bug B**） |

## Bug A：VARS 同名私有变量赋值被 v24 声明解析劫持

- 游戏写法：`#DIMS VARS` 后 `VARS = CFLAG`（SYSTEM_DEBUG.ERB:462 起，9 处）——这是对
  名为 VARS 的私有变量的**赋值**，不是 VARI/VARS 声明。
- 缺陷：LogicalLineParser 中 v24 声明分支先于 `ShouldPreferPrivateVariableAssignment`
  执行，非 snake 接口下该行被当作声明解析，左侧变量名为空 → 报错。
- 修复：把同名私有变量赋值判定提前，赋值优先于 v24 声明文法（snake 接口原本就走
  该判定，行为不变；真正的声明行不受影响——同名私有变量不存在时判定为 false）。

## Bug B：HTML_TAGSPLIT 对 SparseArray 后端强转 string[] 崩溃

- `RESULTS` 在懒加载后端是 `SparseArray<string>`，移植版只保留了参考实现中
  `(string[])GetArray()` 分支，丢掉了 `SparseArray<string>` 分支 → InvalidCastException。
- 修复：对照 snake 参考（Instraction.Child.cs HTML_TAGSPLIT）恢复双分支写入。
  全库仅此一处同类强转（已扫描确认）。

## 确认非缺陷：OUTPUTLOG 全接口隐藏是正确分类

- v24 参考与 snake 参考的 OUTPUTLOG 均**整体处于注释状态**（枚举、注册、dispatch
  三处皆注释），gEmuera 将其标为 port-only 并全接口隐藏与参考一致。
- eraBlue 使用的 `OUTPUTLOG @"...", 1` 两参形式是其原生 EE 变体扩展；gEmuera 现有
  handler 为 VOID 参数版，即使暴露也不兼容。eraBlue 需要专属方言 profile
  （snake 层 + OUTPUTLOG EE 扩展 + 可能的其它能力），作为后续任务。

## 验证（legacy-runner 无头游戏级实测）

- HTML_TAGSPLIT fixture + snake：3/3 Passed（RESULT=6、RESULTS:0=`<p>`）。
- `#DIMS VARS` + `VARS = CFLAG` fixture：v24pure / erafl / snake 全部 3/3 Passed
  （输出 VARS=[CFLAG]）。
- v24 声明文法回归 fixture（`VARI COUNT, 3` / `VARS NAME = "abc"`）：v24pure 3/3
  Passed（count1=7、name0=[xyz]），声明路径无回归。
- **真实 eraAkumaMaid 全量 + v24pure：3/3 Passed，真机日志中的 9 条
  "VARI/VARS requires" 报错全部消失。**

## 后续观察项

- eraBlue Resort 方言 profile（EE 系 OUTPUTLOG 两参形式 + snake 层）待立项；
  需要先确定 eraBlue 原生引擎的 OUTPUTLOG 语义与其它依赖面。
- 真机日志中的 Lv1 警告（CSV 定义缺失、CASE 重复、GETBIT 越界等）为游戏数据
  自身问题，上游引擎同样报出，不处理。
