# 2026-09-17 兼容包启用源接 launcher（按游戏的包选择）

## 任务目标与成果

一句话：把启用源从环境变量联调入口升级为 launcher 正式机制——launcher.cfg
`[compat_packs]` 节按游戏存选择、FirstWindow 设置区新增包路径输入框（按所选游戏
加载/保存）、启动时注入进程环境变量（launcher 与引擎同进程，Host 加载管线零改动）。
Core.Tests 78/78（+5 配置语义）、全门禁绿。

## 关键决策与 Why

1. **launcher.cfg 是唯一存储，环境变量是进程内传递通道**：`GEMUERA_COMPAT_PACKS`
   语义升级为「launcher 注入 / 外部诊断覆盖」（Host 注释更新），加载管线
   （CompatPackHost.ReadEnabledPackPaths）一行未动——新 UI 只是新的注入者。
2. **游戏键规范化 = 去尾斜杠 + 小写**（Core 纯函数 CompatPackLauncherConfig）：
   Windows 路径大小写不敏感，launcher 扫描路由与用户手输的盘符大小写差异不产生
   重复条目；路径分隔符保持原样（不转 '/'），序列化/解析/去重保序。
3. **注入点覆盖全部三条启动路径**：LaunchGameEntry（UI 启动）→ SetSelectedGamePath；
   ResolveStartupGamePath 两个返回分支（EmueraMain/PrototypeRuntimeNode 直启上次
   游戏）——静态方法 ApplyCompatPackEnvironment 统一从 launcher.cfg 读当前游戏的
   选择注入/清除（空选择 = 清除变量，回到纯 v24）。
4. **UI 最小面**：LineEdit（占位符+tooltip 双语键，MultiLanguage.Get 内联默认先例），
   TextSubmitted/FocusExited 提交、启动前强制 Commit——不做文件选择对话框（包分发
   UX 属后续，v1 用户手输路径）。
5. **存储层职责分离**：LauncherSettingsStore 只做 raw 读写（新节 [compat_packs]，
   空选择 EraseSectionKey 不留孤儿）；解析/匹配语义全在 Core 可测。

## AI 表现复盘

- 有效：八锚点 python 补丁先跑锚点诊断再全量应用；门禁顺序跑全。
- 低效：三连低级错误——(1) 补丁串吞了 OnGameSelected 的闭括号（new 串没带回 `\n\t}`，
  靠括号深度扫描定位）；(2) Godot `Environment` 类型遮蔽 System.Environment（CS0117
  才想起 Godot 有同名类）；(3) 测试期望值想当然（反斜杠保留却期望正斜杠）。三条全是
  「写之前没核对目标文件真实形态」——多锚点补丁应逐锚点锚定真实文本后再拼 new 串。

## 教训 → 具体优化行动

- Godot 侧代码引用 System 类型一律 `System.` 全限定（Environment/IO 等遮蔽高发）。
- 多锚点文本补丁：每个 old 串先 `assert in` 单测（本次诊断脚本模式），拼 new 时逐行
  对照 old 的结构（尤其收尾括号）。

## 验证记录

Core.Tests 78/78（+5 CompatPackLauncherConfig）、Facade.Tests 33/33；三冒烟全过；
主工程构建 0 错误、新增代码零警告（仅 dev 存量两条）；Godot 无头构建 DLL 23:33:21
更新 0 编译错误；新 .uid 2 枚入库。


## result-review 返工记录（首轮 92 → 复评见后）

首轮 92/100 三项：核心是**注释契约与实现相反**——CompatPackHost 文档承诺"外部显式
设置优先于 launcher 注入"，但 ApplyCompatPackEnvironment 无条件覆盖/清除变量，
外部联调设置在任何启动链（含仅打开 UI）被静默抹除。修复：FirstWindow 静态快照
`externalCompatPacksOverride`（类加载即取，早于一切注入调用），外部值非空时注入
完全不碰变量。另收口：测试 null→null!（CS8625）；Store 类头注释补 [compat_packs]
节职责。复验：主工程 0 错误零新警告、Core.Tests 78/78、三冒烟全过。
