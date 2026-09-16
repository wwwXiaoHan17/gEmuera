# 2026-09-16 布局整顿：APK 导出统一 export/ 工作区 + tests 归一 + 游离文件归位

## 任务目标与成果

一句话：把"Test 文件到处飞、根目录无意义文件夹、Build/ 杂居"的仓库布局收敛为
「根目录白名单 + export/ 导出工作区 + tests/ 唯一测试根」，Build/ 退役，全部门禁绿
（Godot 构建/xUnit 20/20/gdUnit 4/4/fixture 契约/方言三冒烟），6 个分组提交待 PR。

## 关键决策与 Why

1. **Build/ 退役而非保留**：export/ 已在 .gitignore 预留（L7），且用户明确要求
   "导出 APK 所需文件统一一个 export 文件夹"。Build/ 的四个子目录分流：
   android/NativeLibs/releases → export/（仍是忽略区），Fixtures/manifest.json →
   tools/fixture-manifest/（它是 Build 下唯一入库文件，与其 schema/脚本同址才合理）。
2. **csproj 补 `export\**` 三连排除**（计划外缺口，执行中发现）：原 `Build\**`
   排除防的是"产物目录混入杂散 .cs 以 CS0579 炸构建"，export/ 接替该角色后必须
   同等兜底，否则防御缺口静默回归。
3. **gdUnit 测试根定为 tests/GDUnit4Test/**（而非 gdUnit 官方惯例的 test/）：
   用户指令"同类型文件统一归档、命名统一"优先于插件文档惯例；GdUnitRunner.cfg
   的 8 行 res:// 路径 + 4 个 fully_qualified_name 前缀（由 res:// 路径派生的
   GD 全局类名）同步改。gdUnit 本身不硬编码目录（-a 传路径），无插件侧冲突。
4. **大小写问题实证收敛**：一~三级目录名大小写不敏感扫描零冲突，"大小写不统一"
   实为命名风格问题（test/tests 并存），以 FILE_STANDARD §9 白名单定稿，不做
   全仓改名冒险。
5. **keystore 只立约定不配路径**：项目内实测无 *.keystore/*.jks；export_presets.cfg
   填无文件的签名路径会炸导出，故仅写明 export/keystore/ 约定，等密钥库到位一步接入。

## AI 表现复盘

- 有效：计划模式先派 Explore 子代理做钉扎事实普查（.gitignore 全文、csproj 排除项、
  sln/cfg 路径字面量、大小写扫描），执行时零"移完才发现引用断"；git mv 全程 rename
  识别（diff 干净可审）。
- 低效：sed 改反斜杠路径字面量被命令层转义层吃掉一层，`Build\NativeLibs` 静默未替换
  （README 正斜杠却改成功了），靠事后 grep 才发现。教训：**反斜杠路径一律用 Edit
  工具做字面量替换，不用 sed**；sed 只用于正斜杠/无转义场景。
- 计划外但必要：ProfileLoaderTests 的 RepoRoot 上跳级数随目录加深 5→6、夹具路径
  examples→docs/designs——纯移动 PR 也要预期"路径深度敏感代码"的存在，验证阶段抓到。

## 教训 → 具体优化动作

- FILE_STANDARD 新增 §9「目录树与根目录白名单」：目录拼法锁定（tests/、export/、
  xUnitTest/、GDUnit4Test/）、Build/ 禁止重建、export_presets.cfg 必须留根。
- AGENTS.md §5 与构建段改写为 export/ 约定；readme 三语结构树同步。
- 方言战略注记：本次布局是"v24 单基线 + 程序集兼容包"新方向的清场前置；
  docs/designs/agent-profiles（原 examples/）已定为包清单设计输入。

## 验证记录

Godot headless 构建 dll 23:59:09 更新 0 错误；dotnet test 20/20；
GdUnitCmdTool tests/GDUnit4Test 4/4 exit 0（报告 reports/report_1）；
Test-FixtureManifest.ps1 通过；SurfaceSmoke/RuntimeSmoke/CoreContractSmoke 全过；
git status 干净，根目录 = 白名单目录 + reports/（忽略区）。
