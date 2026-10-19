# 2026-10-19 Android APK 导出修复：路径大小写 + SQLite 原生库自愈 + Gradle 代理

## 现象（用户编辑器中导出 Android 失败）

1. 大量 `Case mismatch opening requested file 'res://Scripts/...' stored as 'res://scripts/...'` 警告；
2. `PrototypeHostRegressionTest.gd` Parse Error：`Cannot assign a value of type res://Scripts/GodotHost/PrototypeInputBridge.cs to variable "bridge" with specified type Node`；
3. `Export .NET Project: Failed to build project. Check MSBuild panel for details.`；
4. Gradle wrapper 下载 `gradle-8.11.1-bin.zip` 超时（`SocketTimeoutException`）；
5. 编辑器 `Attempting to make child window exclusive...` 属于 Godot 导出对话框的窗口噪声，不是失败根因。

## 根因与修复

### 1. `Scripts` / `scripts` 大小写不一致（导出是大小写敏感的）

- WIP 把目录改成了小写 `scripts/`，但部分引用仍是 `res://Scripts/...`（`ColorMatrixGPU.cs`、GDUnit `PrototypeHostRegressionTest.gd` 等），Git 索引也仍记录 `Scripts/`（469 个文件）。
- 修复：
  - 全仓 `res://Scripts/` → `res://scripts/`（scenes/gd/cs/tools）并补齐 `tools/tests/packs/src` 里的路径引用（19 个文件）；
  - 用 `git rm -r --cached Scripts` + `git add scripts` 把 Git 索引 469 个路径统一为小写（Windows 上大小写不敏感导致此前一直没暴露）。
- 验证：Godot `--headless --import` 无 Case mismatch / Parse Error；`--check-only --script` 检查 GDUnit 测试脚本通过。

### 2. .NET 导出构建失败 = 缺 `export/NativeLibs/android/arm64-v8a/libe_sqlite3.so`

- `gemuera-c#.csproj` 已有 `RestoreAndroidSqliteNativeLibrary` 自愈目标（从 SQLitePCLRaw .aar 提取），但只在 `BeforeTargets="Build"` 运行；`dotnet publish` 的 `_CopyFilesMarkedCopyLocal` 在文件缺失时先报 `MSB3030`，导出链直接失败，用户看到的就是 "Failed to build project"。
- 修复：把自愈目标扩展到 `BeforeTargets="Build;_CopyFilesMarkedCopyLocal;_CopyOutOfDateSourceItemsToOutputDirectory"`，保证 copy 目标前文件已存在。
- 验证：删除 `export/NativeLibs` 后重新 `dotnet publish ... -p:GodotTargetPlatform=android -p:TargetFramework=net9.0 -r android-arm64` → 成功自愈并生成 .so；publish 输出含 `libe_sqlite3.so` 与 `libSkiaSharp.so`。

### 3. Gradle wrapper 下载超时

- 机器无全局代理环境给 Java/Gradle；`services.gradle.org` 直连超时。
- 修复（用户级、仓外配置）：
  - 创建 `C:\Users\30873\.gradle\gradle.properties`，写入 `systemProp.http/https.proxyHost=127.0.0.1`、`proxyPort=7897`、`nonProxyHosts=localhost|127.0.0.1`；
  - 用 JDK 21（Godot 编辑器设置里的 `java_sdk_path`）预热 `gradle-8.11.1` 到 `%USERPROFILE%\.gradle\wrapper\dists`；
  - 后续 Gradle 构建（含 Godot 导出）自动走代理。
- 注意：该配置在用户目录、不入库；换机器/代理端口变化需同步。

## 验证

- CLI 导出命令：
  `Godot_v4.7... --headless --path <项目> --export-debug "Android" E:\tmp\gemuera-export-test.apk`
  → 成功产出 **74,423,499 字节** APK（22:56:10）。
- APK 内容抽检：`lib/arm64-v8a/libgodot_android.so`、`libmonosgen-2.0.so`、`libe_sqlite3.so`、`libSkiaSharp.so`、`assets/.godot/mono/publish/arm64/gemuera-c#.dll` 均存在。
- 已复制到 `export/releases/gemuera-1.10.0-dev-20261019-debug.apk`（export/ 为 gitignore 工作区）。
- 门禁：build 0 error、三冒烟、xUnit 131+35+3、governance 4/4 全绿（Windows 上；Linux CI 的路径大小写已随索引归一）。
- 编辑器里用户本地修改的 `export_presets.cfg`（version 10 / 导出路径）与 `assets/text/manual.md` 保持未提交，避免混入本修复。

## 未验证 / 后续

- 未在真机安装该 debug APK；release 签名/keystore、AGP 与 SDK 36.1 在多台机器的兼容性未验证。
- `export_filter="all_resources"` 会把 `tests/` 等打进 APK（体积 74MB 偏大）；后续可考虑在 export_presets 增加 `exclude_filter="tests/*"`（需另评估 GDUnit 是否被运行时引用）。
- Gradle 代理写在用户级 `~/.gradle/gradle.properties`，不在仓库内；CI/新机器需要重新配置或使用镜像。
