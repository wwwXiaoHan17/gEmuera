# 安卓导出与 SJIS 配置映射修复（android-export-sjis-map）

## 任务目标与成果

eraMegaten 真机无法运行（2185 × `字句解析中に予期しない文字"[["` 报错）→ 定位为 APK
缺失 `assets/text/emuera_config_*` 三个 SJIS 配置映射文件 → 修复导出链 → 真机实测通过。

## 关键决策与 Why

1. **根因链**：`export_filter="all_resources"` 只导出"资源"；非资源文本文件依赖
   include_filter，而 **Godot 编辑器每次导出都会用内存态重写 export_presets.cfg**，
   编辑器启动后的磁盘修改一律被冲掉（include_filter 被改回 3 次才认清此机制）。
2. **根治方案 = .import 侧车**：为 `emuera_config_shiftjis.bytes`、`emuera_config_utf8.txt`
   手写 `.import`（`importer="keep"`），文件升级为"资源"，随 all_resources 自动入包，
   **与 include_filter 彻底解耦**。验证：编辑器重写侧车后 keep 属性保留，新 APK 内
   三文件 2444/3523/2557 字节与源逐字节一致。
3. **csproj PackageDownload**：`SQLitePCLRaw.lib.e_sqlite3.android` 必须显式声明——
   transitive android 依赖在纯 net9.0+RID 解析图下不会还原；`.so` 手动解压到
   `Build/NativeLibs/android/arm64-v8a/`（gitignored）。
4. **NU1101（linux-bionic-arm）**：用户改用 `gradle_build/use_gradle_build=true` 解决，
   保留用户方案不回改。
5. **装机前先开箱**：APK 当 zip 打开，文件大小对照源文件 + 对 publish 程序集做
   字节级特征探测（MegatenCompatibilityModule / UsesVariableCaseForFunctionLabelLookup /
   ScanNestedEraGameDirectories / game.megaten 全命中）——一次往返确认修复真的在包里，
   避免真机反复折腾。
6. **探测技术细节**：dll 内 UTF16 字符串必须按字节模式匹配；整缓冲 Unicode 解码会因
   奇偶对齐漏检（emuera_config_shiftjis 曾因此假阴性）。

## 验证记录

- 新 APK（2026-09-07 19:39:59）开箱：三映射文件入包、大小与源一致；publish/arm64
  `gemuera-c#.dll` 含全部 megaten 修复特征。
- **安卓真机实测通过**（2026-09-07，用户确认）。

## 遗留登记

- 启动器导出把 tools/src/reports 等非资源目录也打进 APK（约 78MB 偏大），后续可用
  exclude_filter 收敛。
- 其余遗留见 `2026-09-07-eramegan-adaptation.md` 遗留登记（SurfaceSmoke 断言、快照
  生成器三 profile 枚举、legacy-runner 旧路径、MaxRuntimeMs、self-heal Zip 任务）。

## AI 表现复盘

- **有效**：开箱验证法（zip 列文件 + dll 字节探测）把"还是一样的问题"从用户来回装机
  变成一次定位；把"编辑器覆盖预设"确认为机制而非偶发。
- **低效**：include_filter 磁盘修复两次被冲掉后才转向侧车方案——第一次被覆盖时就应
  换思路，而不是重复同一动作。
- **教训 → 动作**：今后任何 export_presets.cfg 的磁盘改动都默认"会被编辑器覆盖"，
  依赖导出行为的文件一律走资源/侧车路线；APK 交付前必须开箱验证关键资源在包内。
