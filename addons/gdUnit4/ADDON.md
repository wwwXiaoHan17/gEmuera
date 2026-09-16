# gdUnit4 插件使用说明

> 插件：**gdUnit4** v6.1.3 · 作者：Mike Schulze
> 适配引擎：Godot 4.5+（本项目使用 Godot 4.7）
> 语言支持：GDScript 与 C#（需 GdUnit4Net / mono 版 Godot）
> 适用项目：gEmuera-future（Godot 4.7 + C# 的 Emuera 模拟器移植，首要目标 Android/手机端）

本文件是项目内对 gdUnit4 插件的中文速查与决策文档，覆盖 **WHY / WHEN / WHERE / HOW** 四个维度。所有 API 名、命令行参数、退出码均依据插件源码（`plugin.cfg`、`plugin.gd`、`src/GdUnitTestSuite.gd`、`src/GdUnitSceneRunner.gd`、`src/core/runners/GdUnitTestCIRunner.gd`、`runtest.cmd`/`runtest.sh`）核对，未做臆造。

---

## 目录

- [WHY 为什么要使用 gdUnit4](#why-为什么要使用-gdunit4)
- [WHEN 什么时候使用 gdUnit4](#when-什么时候使用-gdunit4)
- [WHERE 用在哪里](#where-用在哪里)
- [HOW 怎么使用](#how-怎么使用)
  - [环境要求](#环境要求)
  - [编写第一个测试套件](#编写第一个测试套件)
  - [断言 API 速览](#断言-api-速览)
  - [Mock / Spy / 参数匹配器](#mock--spy--参数匹配器)
  - [SceneRunner 场景与触屏模拟](#scenerunner-场景与触屏模拟)
  - [信号测试](#信号测试)
  - [Fuzzer 模糊测试](#fuzzer-模糊测试)
  - [资源管理与孤儿节点监控](#资源管理与孤儿节点监控)
  - [在编辑器内运行测试](#在编辑器内运行测试)
  - [命令行 / CI 运行测试](#命令行--ci-运行测试)
  - [报告输出](#报告输出)
  - [C# 集成说明](#c-集成说明)
- [注意事项与限制](#注意事项与限制)
- [参考链接](#参考链接)

---

## WHY 为什么要使用 gdUnit4

Godot 引擎本身不内置单元测试框架，开发者通常只能用 `assert()` 或手写 `print` 对比来验证逻辑，这种方式存在明显短板：断言失败不会生成结构化报告、无法批量组织与发现测试、无法在 CI 中产出 JUnit/XML、无法模拟场景输入与信号。gdUnit4 正是为补齐这些能力而存在。

**1. 一套完整的测试框架，而非零散断言**

gdUnit4 提供测试套件基类 `GdUnitTestSuite`、生命周期钩子（`before`/`after`/`before_test`/`after_test`）、自动测试发现（保存脚本即扫描）、编辑器内 Inspector + Console 双面板，以及命令行 CI 运行器。它是一套「写 → 发现 → 运行 → 报告」的闭环，不是简单的断言库。

**2. 丰富的类型化断言，链式调用**

`assert_that(value)` 会按值的类型自动分派到对应断言（bool/int/float/str/vector/array/dict/object 等），每个断言方法返回 `self` 支持链式调用，并统一走 `report_success`/`report_error` 通道，失败信息带颜色高亮与上下文对比，比裸 `assert` 更易读。

**3. Mock / Spy / 参数匹配器，隔离依赖**

内置 `mock(clazz)`、`spy(instance)`、`do_return(value).on(mock).method()`、`verify(obj, times)` 以及一整套 `any_int()`/`any_string()`/`any_class()` 参数匹配器，可在不依赖真实 Godot 节点的情况下测试业务逻辑。

**4. SceneRunner：场景级集成测试，含触屏模拟**

`scene_runner(scene)` 能加载并驱动一个场景，模拟键盘、鼠标与**屏幕触摸**（`simulate_screen_touch_*` 系列）、模拟帧推进、等待信号与函数返回值。对本项目（首要目标是 Android/手机端）而言，触屏模拟是验证 UI 交互回归的关键能力——无需真机即可在编辑器或桌面端跑出触屏交互的自动化测试。

**5. CI 友好：命令行运行 + HTML/JUnit 报告 + 退出码**

`runtest.cmd`/`runtest.sh` 封装了命令行入口，支持 `-a`（指定目录/套件）、`-i`（忽略）、`-c`（不 fail-fast）、`-conf`（配置文件）、`-rd`/`-rc`（报告目录与历史数量）等参数，产出 HTML 与 JUnit XML 报告，并用退出码（成功/警告/错误）驱动 CI 流水线判定。

**6. 对 C# 项目的原生支持**

本项目是 C# 项目，gdUnit4 通过 `GdUnit4CSharpApiLoader` 加载 GdUnit4Net，`runtest` 脚本会自动检测 mono 版 Godot 并执行 `dotnet build`，C# 测试可与 GDScript 测试统一在同一框架下运行。

---

## WHEN 什么时候使用 gdUnit4

### 适合使用的时机

| 时机 | 说明 | 示例 |
|------|------|------|
| **单元测试** | 隔离测试单个类/函数的纯逻辑，不依赖场景树 | 表达式求值、变量读写、ERB 行解析、数值运算 |
| **集成测试** | 测试多个组件协作的流程 | 脚本加载 → label 索引 → 状态机推进 |
| **场景/UI 测试** | 用 SceneRunner 加载 `.tscn`，模拟输入验证 UI 行为 | 控制台按钮点击、输入面板交互、快速按钮触屏 |
| **回归测试** | 修复 Bug 后补测试，防止复发 | 修复某表达式解析 Bug 后写一条断言锁定行为 |
| **CI 流水线** | 提交/PR 前自动跑全部测试，用退出码卡门禁 | GitHub Actions 中调用 `runtest.sh -a tests/GDUnit4Test/` |
| **重构保护** | 重构前先补测试，确保行为不变 | 重构 `Process.cs` 前先覆盖关键脚本路径 |

### 不适合 / 需注意的场景

- **headless 模式下的 UI 输入测试**：Godot 在 headless 模式下不传递 `InputEvent`，`simulate_screen_touch_*` / `simulate_key_*` / `simulate_mouse_*` 在 headless 下无效。gdUnit4 默认禁止 headless 运行并报错退出，需用 `--ignoreHeadlessMode` 显式跳过检查（但输入相关测试仍不生效）。涉及输入的 UI 测试应在编辑器或带窗口的桌面端运行。
- **需要真机/真 APK 验证的问题**：Android 端的渲染、性能、触控手感、平台 API 行为无法被 gdUnit4 完全替代。gdUnit4 验证的是逻辑与场景结构，APK 级验证仍需导出安装。
- **需要真实 Emuera 游戏目录的端到端测试**：加载真实 ERA 游戏脚本跑完整流程更适合用诊断日志（`Scripts/Diagnostics/`）而非单元测试。
- **热路径性能基准测试**：gdUnit4 的孤儿监控与报告开销不适合做精确性能基准，性能问题应走诊断面板与性能监视器。

---

## WHERE 用在哪里

### 1. 编辑器内

插件在 `plugin.gd` 的 `_enter_tree()` 中注册以下 UI（Godot 编辑器启动时自动加载）：

- **GdUnit Inspector**（左侧 Dock，`DOCK_SLOT_LEFT_UR`）：测试树面板，展示已发现的测试套件与用例，提供运行/调试/停止工具栏按钮、进度条、状态栏、孤儿节点监控面板。这是编辑器内最常用的入口。
- **GdUnit Console**（底部面板，标签名 `gdUnitConsole`）：测试运行时的实时输出、彩色消息、失败详情。
- **右键菜单**：在文件系统面板（`CONTEXT_SLOT_FILESYSTEM`）与脚本编辑器（`CONTEXT_SLOT_SCRIPT_EDITOR` / `CONTEXT_SLOT_SCRIPT_EDITOR_CODE`）上注册右键菜单，可对当前文件/目录「运行测试」「调试测试」「创建测试」等。
- **自动发现**：保存脚本（`resource_saved` 信号）时，`GdUnitTestDiscoverGuard` 会自动扫描新测试并刷新 Inspector 树。

### 2. 命令行 / CI

- **入口脚本**：`addons/gdUnit4/runtest.cmd`（Windows）/ `addons/gdUnit4/runtest.sh`（Linux/macOS）。
- **实际运行器**：`addons/gdUnit4/bin/GdUnitCmdTool.gd`（继承 `SceneTree`，由 `GdUnitTestCIRunner` 驱动）。
- **报告输出目录**：默认 `res://reports/`，可用 `-rd` 覆盖。

### 3. 在 gEmuera-future 项目中的落点

根据 `CODE_MAP.md` 的模块划分，以下模块最适合写 gdUnit4 测试：

| 模块 | 路径 | 适合测试的内容 |
|------|------|----------------|
| GameData | `Scripts/Emuera/GameData/` | 表达式求值（`Expression/`）、变量读写（`Variable/`）、常量、函数方法、角色数据——**纯逻辑，最适合单元测试** |
| GameProc | `Scripts/Emuera/GameProc/` | ERB 加载、逻辑行解析、label 索引、脚本执行状态机、lazy loading——适合集成测试 |
| GameView | `Scripts/Emuera/GameView/` | 控制台显示模型（文本/按钮/HTML/图片/形状/输入等待）——适合用 SceneRunner 做场景测试 |
| Content | `Scripts/Emuera/Content/` | 图片/精灵/Graphics surface/ColorMatrix 绘制——部分逻辑可单测，渲染结果需真机验证 |
| uEmuera | `Scripts/uEmuera/` | `System.Drawing`/`System.Windows.Forms` 兼容层——适合单元测试验证兼容行为 |
| Diagnostics | `Scripts/Diagnostics/` | 日志路由、配置读取、导出——适合单元测试 |

> 建议：优先为 GameData 的表达式与变量模块补单元测试，这是投入产出比最高的起点。

---

## HOW 怎么使用

### 环境要求

- **Godot 版本**：gdUnit4 要求 Godot **4.5+**（`plugin.gd` 中 `Engine.get_version_info().hex < 0x40500` 会拒绝加载）。本项目使用 Godot 4.7，满足要求。
- **C# 支持**：本项目是 C# 项目（`project.godot` 中 `[dotnet] project/assembly_name="gemuera-c#"`），需使用 **mono/.NET 版 Godot**。gdUnit4 通过 `GdUnit4CSharpApiLoader.is_api_loaded()` 检测 GdUnit4Net 是否可用。
- **插件启用**：本项目 `project.godot` 已启用该插件：
  ```ini
  [editor_plugins]
  enabled=PackedStringArray("res://addons/gdUnit4/plugin.cfg")
  ```
- **命令行运行所需环境变量**：需设置 `GODOT_BIN` 指向 Godot 可执行文件，或在调用 `runtest` 时传 `--godot_binary <path>`。

### 编写第一个测试套件

测试套件是一个继承 `GdUnitTestSuite` 的 GDScript，文件名通常以 `Test.gd` 结尾（如 `MyClassTest.gd`），放在 `tests/GDUnit4Test/` 目录下（本项目测试唯一根为 tests/）。每个 `func test_xxx()` 是一个独立测试用例。

```gdscript
extends GdUnitTestSuite
# 文件: tests/GDUnit4Test/game_data/expression_test.gd

# 整个套件开始前执行一次（准备数据）
func before() -> void:
    pass

# 整个套件结束后执行一次（清理）
func after() -> void:
    pass

# 每个用例开始前执行
func before_test() -> void:
    pass

# 每个用例结束后执行
func after_test() -> void:
    pass

# 一个测试用例
func test_add() -> void:
    assert_int(1 + 1).is_equal(2)

# 需要等待的异步测试用 await
func test_async() -> void:
    await await_millis(100)
    assert_bool(true).is_true()
```

> 套件基类 `GdUnitTestSuite` 继承自 `Node`，测试运行时会被加入场景树，因此可以直接 `add_child` 节点进行测试（基类已重写 `add_child` 以启动孤儿节点监控）。

### 断言 API 速览

入口函数 `assert_that(current)` 会按 `typeof(current)` 自动分派到对应类型断言。也可直接调用具体断言函数：

| 断言函数 | 适用类型 | 常用方法（链式） |
|----------|----------|------------------|
| `assert_that(v)` | 自动分派 | 按类型路由到下表 |
| `assert_bool(v)` | `bool` | `is_true()` / `is_false()` / `is_equal(expected)` |
| `assert_int(v)` | `int` | `is_equal(expected)` / `is_not_equal(expected)` / `is_greater(v)` / `is_less(v)` |
| `assert_float(v)` | `float` | `is_equal(expected, tolerance)` / `is_not_equal(expected)` |
| `assert_str(v)` | `String` | `is_equal(expected)` / `contains(sub)` / `starts_with(s)` / `is_empty()` |
| `assert_vector(v)` | Vector2/2i/3/3i/4/4i | `is_equal(expected)` |
| `assert_array(v)` | Array / Packed*Array | `has_size(n)` / `contains([v])` / `is_equal(expected)` |
| `assert_dict(v)` | Dictionary | `has_size(n)` / `contains_keys([k])` / `is_equal(expected)` |
| `assert_file(v)` | FileAccess / 路径 | `exists()` / `is_empty()` / `contains(text)` |
| `assert_object(v)` | Object / nil | `is_null()` / `is_not_null()` / `is_equal(expected)` / `is_instanceof(clazz)` |
| `assert_result(v)` | GdUnitResult | `is_success()` / `is_error()` / `has_message(msg)` |
| `assert_func(instance, func_name, args)` | 函数返回值（等待） | `is_equal(expected)` —— 等待函数返回值再断言 |
| `assert_signal(instance)` | 信号 | `is_emitted(name)` / `is_not_emitted(name)` |
| `assert_error(callable)` | Godot 错误 | `is_success()` / `is_push_error(msg)` / `is_push_warning(msg)` |
| `assert_failure(callable)` | 失败断言 | `has_message(msg)` —— 验证某断言会失败（主要用于测试断言本身） |

通用方法（所有断言都有）：`is_null()` / `is_not_null()` / `override_failure_message(msg)` / `append_failure_message(msg)`。

显式失败与未实现：

```gdscript
func test_custom_check() -> void:
    if not my_check():
        fail("自定义失败原因")
        return  # GDScript 无异常，fail 后需手动 return

func test_todo() -> void:
    assert_not_yet_implemented()  # 标记尚未实现的功能
```

### Mock / Spy / 参数匹配器

**Mock**：创建一个类的替身，按模式返回默认值或自定义值。

```gdscript
const RETURN_DEFAULTS = GdUnitMock.RETURN_DEFAULTS    # 基本类型返回默认值
const CALL_REAL_FUNC  = GdUnitMock.CALL_REAL_FUNC     # 调用真实实现
const RETURN_DEEP_STUB = GdUnitMock.RETURN_DEEP_STUB  # 对象类型深度 mock

func test_mock() -> void:
    var mocked := mock(MyClass, RETURN_DEFAULTS)
    # 配置返回值
    do_return(false).on(mocked).is_selected()
    assert_bool(mocked.is_selected()).is_false()
    # 验证调用次数
    verify(mocked, 1).is_selected()
    verify_no_interactions(mocked)  # 验证无其他调用
```

**Spy**：包装真实对象，记录调用但不改变行为。

```gdscript
func test_spy() -> void:
    var real := MyClass.new()
    var spied := spy(real)
    spied.do_something()
    verify(spied, 1).do_something()
```

**参数匹配器**：用于 `verify` 与 `do_return` 时匹配任意参数。

```gdscript
func test_matchers() -> void:
    var mocked := mock(MyClass)
    do_return(42).on(mocked).compute(any_int(), any_string())
    verify(mocked).compute(any_int(), any_string())
    # 类型匹配器：any_bool/any_int/any_float/any_string/any_vector/any_object/
    #            any_dictionary/any_array/any_packed_*_array 等
    # 类实例匹配器：
    verify(mocked).handle(any_class(MyNode))
```

其他工具：`verify_no_more_interactions(obj)`（验证无未验证的调用）、`reset(obj)`（重置调用计数）。

### SceneRunner 场景与触屏模拟

`scene_runner(scene)` 创建一个场景运行器，加载并驱动场景。`scene` 参数可以是已实例化的节点，也可以是场景资源路径字符串。运行器会在测试结束后自动释放场景（`auto_free`）。

```gdscript
func test_scene_touch() -> void:
    var runner := scene_runner("res://main.tscn")
    # 模拟触屏按下
    await runner.simulate_screen_touch_pressed(0, Vector2(100, 200))
    # 模拟触屏拖拽到相对位置
    await runner.simulate_screen_touch_drag_relative(0, Vector2(50, 0))
    # 模拟触屏释放
    runner.simulate_screen_touch_release(0)
    # 推进 10 帧
    runner.simulate_frames(10)
    # 验证场景状态
    var node := runner.find_child("MyButton")
    assert_object(node).is_not_null()
```

SceneRunner 完整能力（依据 `GdUnitSceneRunner.gd`）：

| 类别 | 方法 |
|------|------|
| **触屏** | `simulate_screen_touch_pressed(index, pos, double_tap)` / `simulate_screen_touch_press(...)` / `simulate_screen_touch_release(index, double_tap)` / `simulate_screen_touch_drag(index, pos)` / `simulate_screen_touch_drag_relative(index, relative, time, trans)` / `simulate_screen_touch_drag_absolute(index, pos, time, trans)` / `simulate_screen_touch_drag_drop(index, pos, drop_pos, time, trans)` / `get_screen_touch_drag_position(index)` |
| **键盘** | `simulate_key_pressed(key_code)` / `simulate_key_press(key_code)` / `simulate_key_release(key_code)`（旧的 `shift_pressed`/`ctrl_pressed` 参数已废弃，组合键用分别 press 再 `await_input_processed()`） |
| **动作** | `simulate_action_pressed(action)` / `simulate_action_press(action)` / `simulate_action_release(action)` |
| **鼠标** | `set_mouse_position(pos)` / `get_mouse_position()` / `get_global_mouse_position()` / `simulate_mouse_move(pos)` / `simulate_mouse_move_relative(rel, time, trans)` / `simulate_mouse_move_absolute(pos, time, trans)` / `simulate_mouse_button_pressed(btn, double_click)` / `simulate_mouse_button_press(btn, double_click)` / `simulate_mouse_button_release(btn)` |
| **帧/时间** | `simulate_frames(frames, delta_milli)` / `set_time_factor(factor)` / `await_input_processed()` |
| **信号等待** | `simulate_until_signal(name, ...args)` / `simulate_until_object_signal(source, name, ...args)` / `await_signal(name, args, timeout)` / `await_signal_on(source, name, args, timeout)` |
| **函数等待** | `await_func(func_name, ...args)` / `await_func_on(source, func_name, ...args)` —— 返回 `GdUnitFuncAssert` 可直接断言返回值 |
| **场景交互** | `get_property(name)` / `set_property(name, value)` / `invoke(name, ...args)` / `find_child(name, recursive, owned)` / `scene()` |
| **窗口** | `move_window_to_foreground()` / `move_window_to_background()` |

> **对本项目 Android 目标的意义**：`simulate_screen_touch_*` 系列可在桌面端 Godot 编辑器中模拟手机触屏的按下、长按、拖拽、双击、多指（用不同 `index`）等交互，无需真机即可验证 `Scripts/Emuera/GameView/` 的按钮与输入面板行为。注意这些输入测试**不能在 headless 模式下运行**。

### 信号测试

```gdscript
func test_signal() -> void:
    var emitter := monitor_signals(MyEmitter.new())  # auto_free 默认 true
    emitter.do_it()
    await assert_signal(emitter).is_emitted("my_signal")

func test_await_signal_on() -> void:
    var node := auto_free(MyNode.new())
    await await_signal_on(node, "state_changed", [], 2000)  # 超时 2000ms
```

### Fuzzer 模糊测试

内置 Fuzzer：`BoolFuzzer` / `IntFuzzer` / `FloatFuzzer` / `StringFuzzer` / `Vector2Fuzzer` / `Vector3Fuzzer`（位于 `src/fuzzers/`）。Fuzzer 用于用大量随机输入探测边界 Bug。

```gdscript
func test_parse_int(fuzzer := IntFuzzer.new(-1000, 1000)) -> void:
    var v := fuzzer.next_value()
    assert_int(parse_int(str(v))).is_equal(v)
```

### 资源管理与孤儿节点监控

- **`auto_free(obj)`**：注册对象在测试结束后自动释放，避免内存泄漏。所有 `mock`/`spy`/`scene_runner`/`monitor_signals` 默认已 auto_free。
- **孤儿节点监控**：测试套件重写了 `add_child`，会自动启动孤儿监控。测试结束后若存在未释放的孤儿节点，会报告为警告（退出码为警告码）。调试时可调用 `collect_orphan_node_details()` 收集详细信息。
- **临时目录/文件**：`create_temp_dir(relative_path)` 与 `create_temp_file(relative_path, file_name, mode)` 在 `user://tmp` 下创建，测试套件结束后自动清理。
- **资源读取**：`resource_as_array(path)` / `resource_as_string(path)` / `resource_as_var(path)` 读取 `res://` 资源。
- **等待工具**：`await_idle_frame()` / `await_millis(ms)` / `await_signal_on(source, name, args, timeout)`。测试中应使用这些等待器而非 `get_tree().create_timer()`，以避免超时冲突。

### 在编辑器内运行测试

1. 打开 Godot 编辑器，左侧出现 **GdUnit Inspector** 面板。
2. 测试套件保存后会自动扫描并出现在 Inspector 树中。
3. 在 Inspector 中：
   - 选中套件/用例/目录，点击工具栏 **运行** 按钮执行。
   - 点击 **调试** 按钮可在断点处暂停。
   - 顶部进度条与状态栏显示执行进度与统计。
   - 底部 **gdUnitConsole** 面板显示实时输出与失败详情。
4. 在文件系统或脚本编辑器中**右键**，可选择「运行测试」「调试测试」「创建测试」等。

### 命令行 / CI 运行测试

#### 前置：指定 Godot 可执行文件

设置环境变量（推荐写入 CI secrets 或本地 shell 配置）：

```bash
# Linux/macOS
export GODOT_BIN=/path/to/godot
# Windows (PowerShell)
$env:GODOT_BIN = "C:\path\to\godot.exe"
```

或在调用时传参：`runtest.cmd --godot_binary C:\path\to\godot.exe ...`。

#### 基本用法

```bash
# 运行整个 test 目录
runtest.cmd -a tests/GDUnit4Test/

# 运行单个套件
runtest.cmd -a tests/GDUnit4Test/game_data/expression_test.gd

# 运行目录但忽略某个套件/用例
runtest.cmd -a tests/GDUnit4Test/ -i tests/GDUnit4Test/slow/slow_test.gd
runtest.cmd -a tests/GDUnit4Test/ -i ExpressionTest:test_add   # 忽略套件中的指定用例

# 不在首个失败时停止（跑完全部）
runtest.cmd -a tests/GDUnit4Test/ -c

# 使用测试配置文件
runtest.cmd -conf my_test_config.cfg
```

#### 命令行参数表（依据 `GdUnitTestCIRunner.gd` 的 `CmdOptions`）

**默认参数：**

| 参数 | 等价 | 说明 |
|------|------|------|
| `-a <目录\|套件路径>` | `--add` | 添加要执行的测试套件或目录，可多次使用 |
| `-i <套件\|套件:用例>` | `--ignore` | 添加到忽略列表，可多次使用 |
| `-c` | `--continue` | 不在首个失败时停止（默认 fail-fast） |
| `-conf <file>` | `--config` | 按配置文件运行，默认 `GdUnitRunner.cfg` |
| `-help` |  | 显示帮助 |
| `--help-advanced` |  | 显示高级选项 |

**高级参数：**

| 参数 | 等价 | 说明 |
|------|------|------|
| `-rd <目录>` | `--report-directory` | 报告输出目录，默认 `res://reports/` |
| `-rc <数量>` | `--report-count` | 保留报告历史数量，默认 20 |
| `--info` |  | 显示 GdUnit 与 Godot 版本信息 |
| `--selftest` |  | 运行 GdUnit 自身测试 |
| `--ignoreHeadlessMode` |  | 跳过 headless 模式检查（输入测试仍不生效） |

#### 退出码语义（依据 `GdUnitTestSessionRunner.gd` 常量与 `report_exit_code()`）

| 退出码 | 常量 | 含义 |
|--------|------|------|
| `0` | `RETURN_SUCCESS` | 无错误、无失败、无孤儿节点 |
| `100` | `RETURN_ERROR` | 存在错误或失败的测试用例 |
| `101` | `RETURN_WARNING` | 存在孤儿节点（测试本身通过但有内存泄漏） |
| `103` | `RETURN_ERROR_HEADLESS_NOT_SUPPORTED` | headless 模式被拒绝（未加 `--ignoreHeadlessMode`） |
| `104` | `RETURN_ERROR_GODOT_VERSION_NOT_SUPPORTED` | Godot 版本不满足要求 |
| `105` | `RETURN_ERROR_SCRIPT_ERRORS_DETECTED` | 测试发现阶段检测到脚本错误 |

> CI 中检查 `%ERRORLEVEL%`/`$?`：仅当为 `0` 时才算真正通过；`101` 表示有孤儿节点（逻辑通过但有泄漏），`100` 表示有失败。注意 `runtest.cmd` 最终 `exit /b %exit_code%`（测试退出码），日志复制步骤的退出码不覆盖结果。

### 报告输出

- **HTML 报告**：默认输出到 `res://reports/`，模板位于 `src/reporters/html/template/`（`index.html` 总览、`folder_report.html` 目录报告、`suite_report.html` 套件报告，样式在 `css/styles.css`）。
- **JUnit XML 报告**：由 `src/reporters/xml/JUnitXmlReportWriter.gd` 生成，可被 Jenkins/GitHub Actions 等 CI 系统解析显示测试结果。
- **报告历史**：默认保留 20 份（`DEFAULT_REPORT_HISTORY_COUNT = 20`），超过后自动删除最旧的，可用 `-rc` 调整。
- **复制日志**：`runtest` 脚本在测试结束后会额外执行 `GdUnitCopyLog.gd` 复制日志。

### C# 集成说明

本项目使用 C#，gdUnit4 的 C# 支持要点：

1. **GdUnit4Net**：C# 版 API 通过 `src/dotnet/GdUnit4CSharpApi.cs` 与 `src/dotnet/GdUnit4CSharpApiLoader.gd` 加载。插件启动时（`plugin.gd`）会打印 `GdUnit4Net version '...' loaded.` 或 `No GdUnit4Net found.`。
2. **自动编译**：`runtest.cmd`/`runtest.sh` 会检测 Godot 是否为 mono 版本（`--version` 输出含 `mono`），若是则自动执行 `dotnet build --debug`，无需手动编译。
3. **C# 测试编写**：C# 测试使用 GdUnit4Net 的 API（与 GDScript 版 API 对应），同样在编辑器 Inspector 与命令行中运行。具体 C# API 详见 GdUnit4Net 官方文档。
4. **项目配置**：本项目 `gemuera-c#.csproj` 已配置程序集名 `gemuera-c#`，C# 测试需与之配合。

---

## 注意事项与限制

1. **headless 模式限制**：Godot 在 headless 模式下不传递 `InputEvent`，所有 `simulate_*` 输入模拟无效。gdUnit4 默认禁止 headless 运行（返回 `RETURN_ERROR_HEADLESS_NOT_SUPPORTED`），必须用 `--ignoreHeadlessMode` 才能跳过检查，但输入相关测试仍不生效。涉及输入/UI 交互的测试应在带窗口的环境运行。
2. **最低 Godot 版本**：需 Godot 4.5+。`plugin.gd` 中 `Engine.get_version_info().hex < 0x40500` 会拒绝在更低版本加载。
3. **测试环境互斥**：`plugin.gd` 的 `check_running_in_test_env()` 会在检测到 headless、`--selftest`、`--add`、`-a`、`--quit-after`、`--import` 等参数时跳过插件加载（避免测试时再加载插件造成递归）。
4. **不修改插件源码**：gdUnit4 是第三方插件，本项目不应修改其源码。如需扩展断言类型，参考 `src/asserts/CLAUDE.md` 的五步模式（但产物应放在项目自有目录，而非直接改插件）。
5. **fail 后需手动 return**：GDScript 无异常机制，调用 `fail(msg)` 后必须显式 `return` 退出测试函数，否则后续代码仍会执行。
6. **等待器选择**：测试中使用 `await_millis()` / `await_idle_frame()`，不要用 `get_tree().create_timer().timeout`，后者在测试超时时会引发错误。
7. **APK 验证不可替代**：gdUnit4 验证逻辑与场景结构，但 Android 端的渲染、性能、平台 API 行为仍需导出 APK 真机验证。

---

## 参考链接

- gdUnit4 官方文档：https://mikeschulze.github.io/gdUnit4/
- 测试套件写法 FAQ：https://mikeschulze.github.io/gdUnit4/faq/test-suite/
- 插件源码（本仓库）：`addons/gdUnit4/`
- 关键源码索引：
  - 插件入口：`addons/gdUnit4/plugin.gd` / `plugin.cfg`
  - 测试套件基类：`addons/gdUnit4/src/GdUnitTestSuite.gd`
  - 场景运行器：`addons/gdUnit4/src/GdUnitSceneRunner.gd`（抽象）/ `src/core/GdUnitSceneRunnerImpl.gd`（实现）
  - CI 运行器：`addons/gdUnit4/src/core/runners/GdUnitTestCIRunner.gd`
  - 命令行入口：`addons/gdUnit4/bin/GdUnitCmdTool.gd`
  - 运行脚本：`addons/gdUnit4/runtest.cmd` / `runtest.sh`
  - 断言实现：`addons/gdUnit4/src/asserts/`
  - Mock/Spy：`addons/gdUnit4/src/mocking/` / `src/spy/`
  - Fuzzer：`addons/gdUnit4/src/fuzzers/`
  - 报告：`addons/gdUnit4/src/reporters/html/` / `src/reporters/xml/`
  - C# API：`addons/gdUnit4/src/dotnet/`
  - 新增断言类型指南：`addons/gdUnit4/src/asserts/CLAUDE.md`
