# gemuera-c#

基于 Godot 4.6 + .NET 8.0 的跨平台 Emuera 文字游戏引擎移植版。

Emuera 是日本 eramaker 系列文字游戏的执行引擎，通过解析 `.ERB` 脚本文件和 `.CSV` 数据文件来运行游戏。本项目将原版 Windows Forms / GDI+ 渲染架构替换为 Godot 节点系统，实现了桌面端和 Android 移动端的跨平台支持。

## 特性

- 完整的 Emuera 1824+v18 引擎兼容
- 支持 ERB 脚本执行、CSV 数据加载、SHIFT-JIS/UTF-8 编码
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

- Godot 4.6（.NET 版本）
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

1. 用 Godot 4.6 (.NET) 打开项目
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
├── Scripts/
│   ├── EmueraMain.cs          # Godot 入口，GPU 渲染管线
│   ├── EmueraThread.cs        # 后台线程包装器
│   ├── EmueraContent.cs       # UI 渲染器（行布局、节点管理）
│   ├── EmueraImage.cs         # 纹理绘制控件
│   ├── ColorMatrixGPU.cs      # GPU ColorMatrix shader 管理
│   ├── SpriteManager.cs       # 纹理缓存（每帧限流加载）
│   ├── SpriteDebugViewer.cs   # 精灵调试查看器（F3 切换）
│   ├── GenericUtils.cs        # 引擎↔UI 桥接
│   ├── FirstWindow.cs         # 启动器（游戏扫描）
│   ├── Emuera/                # 核心 Emuera 引擎
│   │   ├── Config/            # 配置系统
│   │   ├── Content/           # 图片/资源管理（原生 BlendRect）
│   │   ├── GameData/          # 数据模型、表达式、变量
│   │   ├── GameProc/          # 脚本执行引擎
│   │   └── GameView/          # 控制台模拟和渲染
│   ├── Shaders/
│   │   └── color_matrix.gdshader
│   └── uEmuera/               # System.Drawing/Forms 兼容层
├── Fonts/                     # 内嵌字体 (MS Gothic)
└── addons/                    # Godot 编辑器插件
```

## 构建

```bash
# 桌面端构建
dotnet build

# Android 构建（需要 .NET 9.0 SDK）
dotnet build -p:GodotTargetPlatform=android
```

## 致谢

- [Emuera](http://osdn.jp/projects/emuera/) — 原版 Windows 引擎
- [XEmuera](https://github.com/xerysherry/XEmuera) — Xamarin/SkiaSharp 移动端移植（参考实现）
- [uEmuera](https://github.com/xerysherry/uEmuera) — Unity 移植版（参考实现）

## 许可证

本项目基于原版 Emuera 引擎移植，遵循其原始许可条款。
