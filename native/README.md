# native/

gEmuera 原生库源码区（Rust，零第三方依赖）。产物为 cdylib，经 P/Invoke
（`DllImport("gemuera_native")`）由主 C# 工程加载；不使用 GDExtension。

## gemuera-native

当前包含：文本宽度规则（`width_unit` / `width_utf16`），语义契约 =
`Scripts/uEmuera/Utils.cs:894-1055` 的 `GetByteCount`。

## 构建

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/native-build/Build-GemueraNative.ps1 -ProjectRoot <项目根>
```

产物落 `export/NativeLibs/`（gitignored）：`windows-x64/gemuera_native.dll`、
`android/arm64-v8a/libgemuera_native.so`（16KB page 对齐）。csproj 检测到产物时自动
复制进构建输出/APK；产物缺失不阻塞主工程构建（可选增强，非必需依赖）。

## 等价性验证（改任何宽度逻辑后必跑）

```powershell
dotnet run --project tools/native-width-check -- gen-tables native/gemuera-native/src/zero_width_tables.rs
cargo test --manifest-path native/gemuera-native/Cargo.toml
dotnet run --project tools/native-width-check -- export/NativeLibs/windows-x64/gemuera_native.dll
```

`gen-tables` 用当前 .NET 运行时的 `CharUnicodeInfo` 生成零宽分类区间表（保证与
.NET 逐码点一致）；check 工具做 golden 断言 + 全 BMP 65536 码元扫描 + 批量接口抽查。
