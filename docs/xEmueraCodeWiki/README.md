# XEmuera CodeWiki

XEmuera 是 Emuera (一个 ERA 游戏脚本引擎) 的跨平台移动端移植版本，基于 Xamarin.Forms 构建，支持 Android 和 iOS 平台。它能够解释执行 ERB 脚本语言，提供文本冒险/模拟游戏的运行环境。

## 文档索引

| 文档 | 说明 |
|------|------|
| [Architecture.md](./Architecture.md) | 项目整体架构与分层设计 |
| [ModuleOverview.md](./ModuleOverview.md) | 主要模块职责划分 |
| [CoreClasses.md](./CoreClasses.md) | 关键类与函数说明 |
| [GameEngine.md](./GameEngine.md) | 游戏执行引擎（Process 状态机） |
| [ScriptSystem.md](./ScriptSystem.md) | ERB/CSV 脚本加载与解析系统 |
| [RenderingSystem.md](./RenderingSystem.md) | 显示与渲染管线 |
| [VariableSystem.md](./VariableSystem.md) | 变量系统与数据模型 |
| [ConfigSystem.md](./ConfigSystem.md) | 配置系统 |
| [DependencyGraph.md](./DependencyGraph.md) | 项目间依赖与 NuGet 包依赖 |
| [HowToRun.md](./HowToRun.md) | 项目构建与运行方式 |
| [PlatformAbstraction.md](./PlatformAbstraction.md) | 平台抽象层与移动端移植适配 |
| [I18nSystem.md](./I18nSystem.md) | 多语言系统 (I18n) |
| [Glossary.md](./Glossary.md) | 术语表 |

## 项目基本信息

- **解决方案**: `XEmuera.sln`
- **框架**: Xamarin.Forms (跨平台移动UI)
- **渲染引擎**: SkiaSharp (2D绘图)
- **目标平台**: Android, iOS
- **语言**: C#
- **原始项目**: Emuera (Windows桌面版 ERA 游戏引擎)
