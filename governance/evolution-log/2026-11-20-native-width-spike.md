# 2026-11-20 native spike：Rust 宽度函数打通 P/Invoke 原生构建链

## 任务目标与成果

承接同日《native-rust-cpp-opportunity-scan》推荐的第一个 spike（候选 5：文本宽度函数），目标 = 验证"Rust cdylib → P/Invoke → csproj 分发"整条原生产物链，并以STRLEN 宽度规则做语义等价样板。成果（本地分支 `ai/native-width-spike`，基于 dev 8c18958；用户指示不建 PR、本地测试）：

- `native/gemuera-native/`：Rust crate（cdylib+rlib，零第三方依赖），导出 `gem_native_abi_version` / `gem_text_width_unit(u16)` / `gem_text_width_utf16(ptr,len)`，语义契约 = `Scripts/uEmuera/Utils.cs:894-1055` 的 `GetByteCount`；9 个 Rust 单测（含 golden、全 BMP 值域、查表升序防回归）。
- `tools/native-width-check/`：独立 net9.0 控制台工具——`gen-tables` 模式用宿主 .NET `CharUnicodeInfo` 生成 Mn/Me/Cf 零宽区间表（218 区间）；默认模式做 28 项 golden + 全 BMP 65536 码点 native↔托管逐点比对 + 批量接口抽查，失配 exit 1。
- `tools/native-build/Build-GemueraNative.ps1`：桌面（MSVC）+ Android arm64（NDK，`-Wl,-z,max-page-size=16384` 16KB 对齐 + ELF PT_LOAD p_align 校验）构建与 `export/NativeLibs/` 分发；缺 NDK 时警告跳过不视为失败。
- `gemuera-c#.csproj`：`tools\native-width-check\**` 三行 glob 排除 + Android/桌面可选复制目标（产物缺失不阻塞构建）。
- `.gitignore` 增 `/native/target/`；`readme/README.md` 结构表增 native/ 与 tools 描述；`native/README.md` 说明构建与等价验证流程。

## 关键决策与 Why

- **零依赖 + 从 .NET 运行时生成分类表**（否决 `unicode-general-category` crate）：Mn/Me/Cf 数据以宿主 .NET 的 `CharUnicodeInfo` 为准逐码点生成，杜绝 Rust 生态 Unicode 版本与 .NET 的漂移；且 crates.io 不可达（本机 NU1900 无网络）也能构建。
- **FFI 以 u16 码元为单元而非 UTF-8 串**：C# 消费侧是 `str[i]` 逐 UTF-16 单元（含代理单元独立计宽），u16 API 使代理对行为天然对齐，无解码歧义。
- **产物可选接入**（`Exists` 条件 Copy 目标，否决硬依赖）：原生库尚无生产消费者，未装 Rust 工具链/未产物的环境构建不能被阻塞。
- **首个 spike 选候选 5 而非候选 1（存档 codec）**：最小 FFI 面 + 现成全量等价判据；存档 codec 留待链路验证后的下一任务。
- **不在本 PR 加 in-app interop 类与 GDUnit 测试**（被否决路线）：spike 边界 = 构建链 + 参考实现全量等价；进程内接线（`LangManager`/`StringMeasure` 换调原生 + `tests/GDUnit4Test/` 用真实 `Utils.GetByteCount` 对照）属行为变更，留给接产 PR。参考实现漂移风险由 28 项 golden + `Utils.cs` 行号溯源注释缓解。
- **排除项（查证后不做）**：GDExtension（候选全部纯计算、无 Godot 对象，P/Invoke 更简且已有 libSkiaSharp/libe_sqlite3 先例 `gemuera-c#.csproj:90-91`）；Android x86_64 产物分发（与 arm64 的 TargetPath 同名冲突，仓库 android 链路现为 arm64 单架构，模拟器调试需求进遗留）。

## 验证记录

- Rust 单测 9/9 通过 [实测]（`cargo test --manifest-path native/gemuera-native/Cargo.toml --target-dir native/target`）。
- 等价校验 PASS：golden 28 项 + 全 BMP 65536 码点 0 失配 + 批量接口抽查 [实测]（`dotnet run --project tools/native-width-check`，65536 次 FFI 逐点调用 8ms）。**校验器自身抓到 2 个真实移植 bug**（见复盘）。
- 主工程构建：`dotnet build "gemuera-c#.csproj" -nodeReuse:false -m:1` → 0 错误、`gemuera-c#.dll` 产出、`gemuera_native.dll`（117248B，与 cargo 产物同尺寸）随 `CopyGemueraNativeLibrary` 目标复制进 `.godot/mono/temp/bin/Debug/` [实测]。
- 可选性：删除 `export/NativeLibs/windows-x64/gemuera_native.dll` 后重建 → 0 错误且输出无 dll；恢复后复制恢复 [实测]。
- 脚本 Android 路径：无 NDK 时 `警告: 未找到 Android NDK...` + exit 0 [实测]。
- Android arm64 `.so` 实际构建 + 16KB p_align 校验执行 [未验证——本机无 NDK（`%LOCALAPPDATA%/Android/Sdk/ndk` 不存在，Godot 编辑器亦未配置）；脚本逻辑就绪待环境]。
- APK 端到端 P/Invoke（真机/模拟器）[未验证——依赖上一条]。
- 等价基准边界：校验与表格生成的 `CharUnicodeInfo` 来自本机 .NET 10 运行时；游戏宿主（Godot 内嵌 .NET）BMP Mn/Me/Cf 自 Unicode 15 起无变化，风险趋零，接产 PR 的 GDUnit 进程内测试将用真实宿主复核。
- 环境障碍与打通：①PowerShell 5.1 把无 BOM 的 UTF-8 脚本按 ANSI 解析，中文注释致语法错误——已按 UTF-8 BOM 修复并入库（本仓库 .ps1 一律带 BOM）；②`cargo --manifest-path` 的 target 落在 cwd（曾污染仓库根，已清理）——统一显式 `--target-dir native/target`；③NuGet 无网络（NU1900）但缓存 restore 可用，不影响构建判定。
- > 补遗（2026-11-20，修正上文②并登记新事故）：实测 `cargo --manifest-path` 的默认 target 目录落在 **manifest 同级**（`native/gemuera-native/target/`）而非 cwd，上文"落在 cwd、污染仓库根"为误判（当时 repo 根的 `rm -rf target` 删的是空目录，实为删错对象）。后果：首个提交把 `native/gemuera-native/target/` 全部构建产物误入库（分支未推送），已 `git rm -r --cached` + 删目录 + `.gitignore` 追加 `/native/*/target/` 后 amend 修正。可复用动作：cargo 命令一律显式 `--target-dir`，gitignore 用 `/native/*/target/` 覆盖任意 crate 层级，提交前必须 `git status --short` 核对无产物路径混入。

## AI 表现复盘

- **有效做法**：golden + 全 BMP 扫描的双层等价设计在交付前抓到 2 个真实 bug——HALFSIZE 表把 `0x25EE` 排在 `0x2580` 前、FULLSIZE 表 `0x00AC` 排在 `0x00A7` 前，都是"照抄 C# 源码书写顺序误当升序"导致二分漏判（触发点：`width.rs` 首轮测试 U+25AE 失配、校验器 U+00AC 失配；后果：2 次返工均在提交前；可复用动作：查表移植必须配"升序断言 + 逐成员命中断言 + 全域扫描"三件套，已全部入库 `width.rs::tests`）。
- **有效做法**：改 csproj 前先读既有 sqlite/skia 的 None Include+Copy 目标模式（`gemuera-c#.csproj:90-116`）并逐行镜像，一次通过主工程构建验证。
- **低效/返工**：①构建脚本首跑因 PS1 编码解析失败（见上），返工 1 次——写 PowerShell 前未查仓库既有脚本的 BOM 惯例；②cargo target 目录位置返工 1 次。可复用动作已写入本日志"环境障碍与打通"，并建议后续升级到 AGENTS.md 构建节（见下）。

## 教训 → 具体优化动作

1. (a) 本轮已执行：查表移植三件套防回归（`native/gemuera-native/src/width.rs` tests：`lookup_tables_are_strictly_ascending`、`fullsize_table_hits_every_member`、golden）。
2. (a) 本轮已执行：构建脚本 UTF-8 BOM + `--target-dir` 固定（`tools/native-build/Build-GemueraNative.ps1`）。
3. (a) 本轮已执行：csproj glob 排除新工具目录（`gemuera-c#.csproj` tools\native-width-check 块），延续"新建 tools 子目录必须三行 Remove"惯例。
4. 升级去向：建议在 `AGENTS.md`「构建与验证」节追加两行铁律——"仓库 .ps1 必须 UTF-8 BOM（PowerShell 5.1 兼容）"与"新增原生库统一走 native/ + tools/native-build + export/NativeLibs 可选复制模式"；本轮不动 AGENTS.md（主工作树另有会话在改同分支文件，避免跨任务混淆），随本分支合并后由下一任务落盘。

## 复审/补遗（2026-11-20：Android 端到端测试轮）

用户追加"通过模拟器来测试"后的第二轮，L1 部分落地：

- **模拟器端到端 PASS [实测]**：Pixel_10 AVD（x86_64，Android API 36 镜像），`lib/x86_64/libgemuera_native.so`（440176B）随 APK 进 `lib/x86_64/`，启动后 logcat 实锤 `I godot: [LOAD] gemuera_native OK (abi=1, A=1,HIRAGANA_A=2,BULLET=1,U+FEFF=0)`，全程无 FATAL。P/Invoke 链路 Android 侧打通。
- **真机 arm64 APK 已产出、未装 [未验证-设备受阻]**：设备 24129RT7CC（Android 16）已连 adb 且 arm64-v8a，但 ① 设备已装 versionCode 15 > 分支基线 14（`INSTALL_FAILED_VERSION_DOWNGRADE`，不降级覆盖用户数据）；② 旁路包名 `com.godot.gemuera.spike` 后遇 MIUI `INSTALL_FAILED_USER_RESTRICTED`（需手机端确认或开发者选项开启"USB 安装"）。arm64 `.so` 16KB p_align=16384 已在构建侧验证通过。
- **新增探针**（本日志上文"不在本 PR 加 in-app interop"决策的**修订**：端到端可观测性需要最小探针）：`Scripts/Emuera/_Library/NativeTextWidth.cs`（P/Invoke + 异常吞并转日志文案）+ `FirstWindow._Ready` 钩子（`GenericUtils.InitializeLogging()` 后）。仍不接入行为路径。`GD.Print` 必达 logcat——`GenericUtils.Info` 是 `[Conditional("DEBUG")]`，Release 构建整行消失，logcat 证据不能依赖它。
- **工作流坑（全部实测踩到）**：① Godot 4.7 CLI `--install-android-build-template` 必须放在 `--export-debug` **之前**（放在后面被解析为预设名）；worktree 首次导出需该旗标（模板不入库）。② x86_64 模拟器需临时工作流：preset 架构翻转（arm64→false/x86_64→true）+ NativeLibs 文件互换为 x86_64 `.so`，测后 `git checkout export_presets.cfg` + 重跑构建脚本还原；Skia/sqlite 的 arm64 ELF 会混入 x86_64 包（既有已知降级，依赖托管回退）。③ 版本号防降级：对装有新版构建的设备测试，要么 bump versionCode，要么旁路包名。④ csproj android 复制目标只在 `GodotTargetPlatform=android`（导出管线内部构建）时生效，桌面 `dotnet build` 不复制 `.so`，属预期。⑤ 尺寸指纹：arm64=460024、x86_64=440176、dll=117248。
- **脚本修复**：`Build-GemueraNative.ps1` 原先只检查 NDK clang 路径未传给 cargo——补 `CARGO_TARGET_AARCH64_LINUX_ANDROID_LINKER`；否则 cargo 用默认 `aarch64-linux-android-clang`（PATH 上不存在）直接 exit 101。
- NDK 安装：`sdkmanager --proxy=http --proxy_host=127.0.0.1 --proxy_port=7897 --install ndk;28.2.13676358`（走本地代理 7897，license 需先 `yes | --licenses`）。

### 补遗（2026-11-21）：真机 arm64 安装测试 PASS——L1 全部落地

- 用户确认 MIUI USB 安装后，`adb install -r` 成功装入 arm64 spike 包（`com.godot.gemuera.spike`，versionCode 14，与设备上 versionCode 15 的正式包并行不冲突）。启动后 logcat 实锤：`I godot: [LOAD] gemuera_native OK (abi=1, A=1,HIRAGANA_A=2,BULLET=1,U+FEFF=0)`（Android 16，arm64-v8a 生产 ABI），无 FATAL；系统 ScnModule 的 native libs 清单亦含 `libgemuera_native.so`。**上文遗留 L1（Android 端到端验收）至此完成：模拟器 x86_64 + 真机 arm64 双证据 [实测]。**
- 真机上 spike 包与正式包并存，用户验收后可 `adb shell pm uninstall -k --user 0 com.godot.gemuera.spike`（-k 保留数据）或直接在桌面卸载。

### 补遗（2026-11-21）：接产——宽度函数接入生产热路径（ABI v2）

用户指示"继续 rust/c++ 的内容"后的第三轮，上文遗留 L2（接产）落地：

- **原生库**：新增导出 `gem_text_width_units_utf16(ptr,len,out)`（逐单元宽度序列填充 + 返回总宽，`out=null` 时只算总宽），`NATIVE_ABI_VERSION` 1→2，C# 探针/checker 的 ExpectedAbiVersion 同步。cargo 单测 10/10。
- **C# 接线**（全部自动回退托管，库缺失/ABI 不符零风险）：
  - `NativeTextWidth` 扩展：`IsAvailable`（进程内探测缓存一次）+ `TryGetByteCount` / `TryFillWidthUnits` / `TryGetWidthUnit`；
  - `uEmuera.Utils.GetByteCount`：原生快路径早退；
  - `uEmuera.Utils.GetDisplayLength(s,float)`：宽度序列 + 与原实现**同序**浮点累加（`fontsize/2` 常量提升位级一致），ArrayPool 租缓冲；
  - `LangManager.GetUFTIndex`/`GetSubStringLang` 三处逐字符 `GetByteCount(c.ToString())`（每字符一次 string 分配 + UnicodeCategory/哈希查询）→ `TryGetWidthUnit` 单次 FFI。
- **等价验证升级**：checker 新增 `--vs-managed <游戏dll>` 模式——反射加载真实游戏程序集调 `uEmuera.Utils.GetByteCount` 全 BMP 对照（比参考实现副本更强的进程内锚点）。实测：默认模式（参考对照，含序列 API 与 out=null 约定）PASS；`--vs-managed` 对接线后的真实程序集全 BMP **0 失配**（78ms）。构建 0 错误；xUnit 413+79+4 全绿。
- **决策修订**：原计划 GDUnit C# 进程内测试——`tests/GDUnit4Test/` 目前只有 GDScript 套件，C# GDUnit 基建（api 包/runner/可失败断言约定）需独立立项，本轮以 `--vs-managed` 达成同等锚点强度，GDUnit C# 套件转独立遗留。
- **踩坑**：①宿主工程未开 `AllowUnsafeBlocks`（CS0227）——撤掉 `fixed` 指针方案，改 DllImport 数组 marshal（string→ushort[] 一次 O(n) 拷贝，DLL 导入自动 pin，仍远优于原逐字符路径）；②checker 误传 `--no-restore` 且 obj 已被清 → NETSDK1004，去掉即可。
- **行为影响面**：STRLEN/SUBSTRING/PRINTC/按钮布局折行（`GetDisplayLength`）等热路径自动走原生；原生库缺失环境（未分发 .so 的平台、纯托管 CI）自动回退，行为不变。

### 补遗（2026-11-21）：真机接线版冒烟 PASS

接产提交（49c02a5 + ae1e053）后重导 arm64 debug APK（接线程序集 + ABI v2 库）装真机 24129RT7CC：探针 `abi=2` 全对、启动器渲染（自动走 GetByteCount/GetDisplayLength 原生快路径）零宽度相关异常、无 FATAL [实测]。logcat 唯一 ERROR 为启动器既有的 `size_changed` 信号重复连接警告（旧问题，与本轮无关，未修）。临时包名改动已还原，工作树干净。

## 遗留登记

- **L1（P1，不在本轮原因：本机无 NDK，环境安装属用户决策）**：安装 Android NDK 后跑 `Build-GemueraNative.ps1`（含 16KB p_align 校验）产出 arm64 `.so`，导出 APK 并在模拟器/真机做 P/Invoke 冒烟；这是"Android 结论必须 APK 实测"铁律的必经步骤。
- **L2（P1，不在本轮原因：属行为变更，超出 spike 边界）**：接产 PR——新增 C# interop 包装（含 `NativeLibrary.TryLoad` 探测与托管回退），`LangManager.GetUFTIndex`/`StringMeasure` 宽度路径换调原生，并在 `tests/GDUnit4Test/` 加真实 `Utils.GetByteCount` 进程内对照测试。
  > 补遗（2026-11-21）：接产已在"补遗（2026-11-21）：接产"小节落地（GetByteCount/GetDisplayLength/LangManager 接线 + `--vs-managed` 对照）；GDUnit C# 套件因仓库无 C# GDUnit 先例转为独立基建遗留（见下 L6）。
- **L6（P2，不在本轮原因：tests/GDUnit4Test 无 C# 先例，基建独立立项）**：搭建 C# GDUnit 测试基建（gdUnit4 api 引用 + runner + 可失败断言约定），迁移 `--vs-managed` 的对照逻辑为进程内常驻测试。
- **L7（P2，不在本轮原因：收益边际）**：`GetUFTIndex`/`GetSubStringLang` 的 prefix-sum 批量 API（单次 FFI 取整串宽度序列代替逐字符 FFI）；现有逐字符 FFI 已比原实现快一个量级，等 profile 数据再定。
- **L3（P2，不在本轮原因：依赖 L1/L2 节奏，成本>本轮范围）**：候选 1 存档 codec spike（验收 = `tools/save-baseline` round-trip 门禁）。
- **L4（P2，不在本轮原因：与仓库 android 链路 arm64-only 现状一致，需求未确认）**：emulator 用 x86_64 Android 产物支持（同 TargetPath 冲突需先解决分发命名）。
- 本轮范围（桌面构建链 spike + 等价样板）已全部落地，无本轮内 P0。
