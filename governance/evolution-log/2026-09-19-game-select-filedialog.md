# 游戏选择扩展：FileDialog 浏览添加任意目录 + 手动条目持久化

任务：把启动器游戏选择从"仅扫描根列表"扩展为"扫描列表 + FileDialog 浏览任意目录"，
移动端为主。分支 ai/game-select-filedialog（叠在 PR #18 之上，FirstWindow.cs 大改依赖其
partial 布局，#18 未合并前不可从 dev 直接切）。

## 成果

- 新 partial `Scripts/FirstWindow.GameBrowseUi.cs`（约 230 行）：浏览按钮（FileDialog
  OpenDir 模式，桌面起始=上次游戏父目录/exe 目录，Android=/storage/emulated/0/emuera）+
  移除手动条目按钮（仅选中手动条目时可用）+ 失效记录自动剔除。
- 手动条目持久化：launcher.cfg [launcher] manual_games = "profile|path" 分号清单
  （LauncherSettingsStore 增 raw 存取一对方法）；同路径重选=更新 profile 记录；与扫描
  条目同路径时扫描优先、记录保留（目录移出扫描根后自动恢复可见）。
- FirstWindow.cs 净增 ~14 行挂钩（枚举值/浏览行挂接/装配调用/路由描述/选中联动）。
- 门禁：构建 0 错误、Core 90/90、first_window 无头探针 0 错误、.uid 侧车经无头编辑器
  导入生成并入库。

## 关键决策与 Why

1. **扩展而非替换**：扫描列表仍是主选择面（移动端一手可见、零输入）；FileDialog 补
   "游戏不在扫描根"的通道——Android 上 Godot FileDialog 可浏览共享存储（应用已有全
   文件访问权限），目录选择模式对触屏可用（对比：兼容包选择当初定扫描+勾选，是因为
   多选文件在触屏上更繁琐，本任务是单目录选择，FileDialog 体验成立）。
2. **新 partial 不含方言标识符**：FILE_STANDARD §6 + dialect-classification 约束——
   新文件命中 branch marker（如 SelectedCoreProfileName）而无 fileRegex 规则会炸
   "Unmapped dialect branch hit"。手法：profile 值只用常量与参数（类别默认
   v24pure/snake），`SelectedCoreProfileName` 等标识符留在 FirstWindow.cs（已有规则覆盖）。
3. **FirstWindow.cs 整文件 SHA-256 钉扎（dialect-profile-selection.json）既有漂移不修**：
   钉扎值与 dev/当前均不符（#17/#18 均未更新，FILE_STANDARD 已注明两条既有漂移），
   本次不扩范围；该钉扎的消费脚本不在标准门禁集内。
4. **手动条目 profile = 添加时标签的目录路由 profile**：与扫描条目同源可预期；高级
   兼容模式按既有语义在每次启动时覆盖（GetSelectedCoreProfileName 不动）。

## AI 表现复盘

- 有效：先查 FILE_STANDARD 的钉扎/marker 约束再动手，避免了拆分陷阱（新文件 marker
  命中、sha256 钉扎语义）；FileDialog 用法直接复用 #18 里 CompatPackUi 的先例（信号
  清理/CurrentDir/PopupCentered 模式），三次编译错误（out 关键字、FileModeEnum 成员名
  OpenDir）都是低成本即改。
- 低效：FileModeEnum 成员名猜了两次（FileModeDir→Dir→OpenDir）——应一开始就 grep
  GodotSharp 生成代码或既有用法；首轮 result-review 97 分的 P1（8 个本地化 key 未进
  四份语言文件）本可在实现时按既有先例（FirstWindow.Start 在全部语言文件有条目）自查
  避免——"fallback 可用"不等于"交付完整"。
- 教训 → 动作：Godot C# API 拼写不确定时先查仓库内既有调用或 GodotSharp generated
  目录，不要连猜；新 .cs 的 .uid 侧车用 `--headless --editor --quit` 导入一遍即可生成
  （dotnet build 不生成）；**新增 MultiLanguage key 时同 PR 补全四份语言文件**（并发现
  PR #18 的 CompatPack key 也有同缺口，留其自行收口）。

## 返工记录（result-review 97 → 复评 100）

- P1：8 个 key 补进 default/zh_cn（中文）/en_us（英文）/jp（日文）四份语言文件。
- P2：FirstWindow.cs 整文件 sha256 重钉（2014ae06…，基于 #18+本改动后的最终文件；
  PR 合并顺序须先 #18 后本 PR，否则钉扎再漂移）。
- P3×2：profile 记录装配/回写均用归一值；scanMessages 透传（上限溢出提示不再静默）。

