**中文** | [English](README_en.md) | [日本語](README_ja.md)

# gEmuera

基于 Godot 4.7 + .NET 8.0 的跨平台 Emuera 文字游戏引擎移植版。

Emuera 是日本 eramaker 系列文字游戏的执行引擎，通过解析 `.ERB` 脚本文件和 `.CSV` 数据文件来运行游戏。本项目将原版 Windows Forms / GDI+ 渲染架构替换为 Godot 节点系统，实现了桌面端和 Android 移动端的跨平台支持。

## 开发者接手

AI/开发者接手以仓库根目录 [AGENTS.md](../AGENTS.md) 为权威入口（项目简介、必读与配套文档、构建与验证、架构速览、协作规则）；需要扩展 ERB 解释器接口时参见 [ERBAPI.md](../ERBAPI.md)。[DeveloperHandoff.md](../NewFrameworkDesign/DeveloperHandoff.md) 是 2026-07-17 的历史存档快照，仅作参考；不要把其中的长期目标设计直接视为已完成实现。

## 特性

- 统一的 `Emuera1824+v24+EMv18+EEv55` 公共核心
- v18 游戏兼容；v24 / EE / EM 扩展游戏原生支持
- `DIMF` / `FUNCTIONF` / `LOCALF` / `ARGF` / `RESULTF` 浮点数支持
- `VARIADIC` 可变参数函数、`#REF` / `#REFS` / `#REFF` 引用型参数
- `SETIMAGELAYER` / `CLEARIMAGELAYER` 图像层控制、`SETANIMETIMER` 动画定时器
- `EXISTFUNCTION` 支持 lazyload 补载触发
- 可选 Lazyload 加速策略（`lazyloading.cfg` + 按需补载）
- Snake 兼容 profile（同一核心上的兼容配置，非独立核心）
- 存档兼容说明：v18 存档可读；v24 新存档不保证可回旧 v18 引擎
- 未实现功能诚实声明：SafeArithmetic、Sprite 翻转、动画暂停/恢复、Zip 压缩存档等尚未实现
- ERB 脚本执行、CSV 数据加载、SHIFT-JIS/UTF-8 编码
- GPU 加速的 ColorMatrix 颜色变换（桌面端，角色立绘着色）
- Godot 原生 `Image.BlendRect` 精灵合成（高性能替代逐像素混合）
- 固定行高渲染模型，图片通过 Y 偏移覆盖绘制（与原版一致）
- 节点数量上限管理（最大 1000 行），防止内存无限增长
- 支持 HTML `<img>` 标签内联图片、形状绘制、按钮交互
- 双线程架构：UI 渲染与脚本执行分离，避免界面卡顿
- 屏幕输入板、快捷按钮、缩放控制
- 多语言支持（日语/中文）
- Android 自适应屏幕宽度布局

## 平台支持

| 平台 | 框架 | 状态 |
|------|------|------|
| Windows | .NET 8.0 + D3D12 | 可用 |
| Linux | .NET 8.0 | 可用 |
| Android | .NET 9.0 | 可用 |

## 快速开始

### 环境要求

- Godot 4.7（.NET 版本）
- .NET 8.0 SDK
- （Android 构建）.NET 9.0 SDK

### 游戏文件放置

将游戏文件夹放置在以下位置（文件夹名必须以 `era` 开头）：

- **桌面端**：与可执行文件同目录，或 Godot 项目 `res://` 目录下
- **Android**：`/storage/emulated/0/emuera/`

游戏文件夹结构：

```
eraGameName/
├── csv/              # 必需 — 游戏数据 CSV 文件
├── erb/              # 必需 — 脚本 ERB 文件
├── resources/        # 可选 — 图片资源 (PNG, JPG, WEBP, BMP, TGA)
└── fonts/            # 可选 — 外部 TTF 字体
```

### 运行

1. 用 Godot 4.7 (.NET) 打开项目
2. 将游戏文件夹放到正确位置
3. 运行项目，在启动界面选择游戏

## 架构

```
┌─────────────────────────────────────────────────────────┐
│  Godot UI Layer                                         │
│  EmueraContent (VBoxContainer, 固定行高布局)             │
│  EmueraImage, Button, Label, ColorRect, Inputpad        │
├─────────────────────────────────────────────────────────┤
│  Console Layer (GameView)                               │
│  EmueraConsole → ConsoleDisplayLine → parts             │
│  PrintStringBuffer, StringStyle, HtmlManager            │
├─────────────────────────────────────────────────────────┤
│  Process Layer (GameProc)                               │
│  Process → runScriptProc → Instruction execution        │
│  ErbLoader, LogicalLineParser, LabelDictionary          │
├─────────────────────────────────────────────────────────┤
│  Data Layer (GameData)                                  │
│  VariableEvaluator, ExpressionParser, GameBase          │
│  ConstantData, CharacterData, IdentifierDictionary      │
└─────────────────────────────────────────────────────────┘
```

### 线程模型

引擎采用双线程架构：

- **主线程**（Godot）：UI 渲染、输入处理、GPU 工作队列、每帧最多加载 1 张纹理
- **后台线程**（EmueraThread）：ERB 脚本执行、精灵合成、纹理文件 I/O

跨线程通信：
- 后台 → 主线程：`GenericUtils.uiQueue`（ConcurrentQueue）
- 主线程 → 后台：`EmueraThread.Input()` + `ManualResetEventSlim`
- GPU 工作：`EmueraMain.gpuQueue`（仅桌面端）

### 渲染模型

本项目采用与原版 Emuera 一致的固定行高渲染模型：

- 每个 `ConsoleDisplayLine` 固定占据 `EffectiveLineHeight` 的垂直空间（基于字体度量 + 行间距计算）
- 图片通过负 Y 偏移（`ypos`）向上绘制，覆盖在前面的行上方
- 行内容允许溢出（`ClipContents = false`），实现图片叠加效果
- 节点上限 1000 行，超出时批量移除最旧的 100 行
- 精灵合成使用 Godot 原生 `Image.BlendRect`（C++ 实现，性能远优于 C# 逐像素循环）

### ColorMatrix 颜色变换

支持 ERB 脚本的 `GDRAWSPRITE` 7 参数版本，通过 5×5 ColorMatrix 实现角色立绘着色：

- GPU 路径：SubViewport + canvas_item shader 实时渲染（仅桌面端）
- CPU 路径：逐像素矩阵乘法（Android fallback）
- 矩阵约定：GDI+ 格式 `cm[input][output]`，图片自动转换为 RGBA8 格式

### Android 特殊行为

- `Config.WindowX` 自动覆盖为实际屏幕宽度，实现全宽布局
- GPU ColorMatrix 不可用，使用 CPU 路径
- `SpriteManager.UpdateOtherThreads` 每帧最多加载 1 张纹理，防止主线程卡顿
- 游戏文件夹扫描路径：`/storage/emulated/0/emuera/`

## 项目结构

```
gemuera-c#/
├── project.godot              # Godot 项目配置
├── gemuera-c#.csproj          # .NET 项目文件
├── first_window.tscn          # 启动器场景
├── main.tscn                  # 主游戏场景
├── assets/                    # 全部 Godot 运行时资源（统一管理、复用）
│   ├── fonts/                 # 字体（MS Gothic / Microsoft YaHei，单一副本）
│   ├── icons/                 # 界面图标（SVG）
│   ├── lang/                  # 多语言文本（default/en_us/jp/zh_cn）
│   ├── scenes/                # 面板场景（*.tscn）
│   ├── text/                  # emuera_config 模板
│   └── theme/                 # 全局 Theme（gemuera_theme.tres）
├── Scripts/
│   ├── EmueraMain.cs          # Godot 入口
│   ├── EmueraThread.cs        # 后台线程包装器
│   ├── EmueraContent.cs       # UI 渲染器（行布局、节点管理）
│   ├── GenericUtils.cs        # 引擎↔UI 桥接
│   ├── FirstWindow.cs         # 启动器（游戏扫描）
│   ├── Emuera/                # 核心 Emuera 引擎
│   │   ├── Config/            # 配置系统
│   │   ├── Content/           # 图片/资源管理
│   │   ├── GameData/          # 数据模型、表达式、变量
│   │   ├── GameProc/          # 脚本执行引擎
│   │   └── GameView/          # 控制台模拟和渲染
│   ├── LegacyRunner/          # 旧版显示/输入回放诊断（原 Scripts/M0，命名空间 gEmuera.LegacyRunner）
│   ├── GodotHost/             # Godot 生命周期/平台桥
│   ├── Shaders/
│   │   └── color_matrix.gdshader
│   └── uEmuera/               # System.Drawing/Forms 兼容层
├── src/Core/                  # 纯 C# 核心契约（GEmuera.Core，独立编译）
├── tools/                     # PowerShell 工具（governance/core-contracts 等）
├── Build/                     # 构建/打包产物统一管理（gitignore：android/NativeLibs）
│   ├── android/               # Godot Android 导出工程（gradle 构建，不入库）
│   ├── NativeLibs/            # 预编译原生库（Android 构建自愈恢复，不入库）
│   ├── Fixtures/              # 测试固件（manifest.json）
│   └── *.apk / *.idsig        # 导出的 APK 产物（不入库）
├── test/                      # 测试
└── addons/                    # Godot 编辑器插件
```

## 构建

> **注意**：不要直接 `dotnet build`——`Godot.NET.Sdk` 依赖 Godot 环境解析，命令行下常因 SDK resolver/证书问题失败。

C# 编译/构建使用 Godot 4.7 mono 无头构建：

```bash
Godot_v4.7-stable_mono_win64_console.exe --headless --path <项目根> --build-solutions --quit
```

构建成功的判定：检查 `.godot/mono/temp/bin/Debug/gemuera-c#.dll` 时间戳已更新，不要等进程退出——无头/受限环境下 Godot 可能卡在收尾阶段，但编译早已完成。

Android 构建产物统一放在 `Build/` 下管理；Android 相关结论必须以 APK 实测为准，桌面端仅用于调试。

## 致谢

- [Emuera](http://osdn.jp/projects/emuera/) — 原版 Windows 引擎
- [XEmuera](https://github.com/xerysherry/XEmuera) — Xamarin/SkiaSharp 移动端移植（参考实现）
- [uEmuera](https://github.com/xerysherry/uEmuera) — Unity 移植版（参考实现）

## 许可证

本项目基于原版 Emuera 引擎移植，遵循其原始许可条款。
