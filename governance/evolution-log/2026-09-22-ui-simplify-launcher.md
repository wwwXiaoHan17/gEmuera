# 启动器 UI 全面精简：v24/snake 双标签合并 + 兼容设置折叠 + 装饰删除

任务：用户裁定"UI 太臃肿，全部精简、不需要太好看、保留基本功能"——v24/snake 标签
合并（FileDialog 已落地使分类标签失去必要性）+ 其余 UI 同步做减法。分支
ai/ui-simplify-launcher（自 dev=57ab49d，#18/#19 已并入）。

## 成果（FirstWindow.cs 1686 → 1553 行，净减 133）

- **标签 4→3**：游戏/调试日志/操作手册。LauncherTab 枚举去 V24Pure/Snake 改单 Game；
  LauncherGameCategory 枚举与 currentCategory 字段整体删除。
- **扫描管线合一**：ScanV24Root/ScanSnakeRoot 两套近重复扫描器合并为参数化
  ScanRoot（profileId/source/scanCompatibilityDirectory 由调用方给出）；ScanGames 一次
  全扫（基根 v24 语义 + 各基根/snake 子目录 snake 语义 + compat 目录路由）。
- **分类提示删除**：categoryHintLabel/UpdateCategoryHint/GetSnakeRootHint/GetNormalRootHint
  全链路删除（空列表时的"请放入以下路径"提示仍保留 roots 展示）。
- **兼容设置默认折叠**：高级兼容+profile 下拉+兼容包整块收进"兼容设置 ▸"折叠区
  （默认收起，40px 开关行），游戏列表回到内容区第一屏；折叠态不持久化（低频设置）。
- **装饰删除**：入场 fade+上移动效（PlayLauncherEntrance）、tab 切换 FadeInContent、
  tabFadeTween 字段、标题渐变 accentBar、标题字号 28→18、"选择游戏"节标题。
- **手动游戏默认 profile 常量化**：GetCurrentCategoryDefaultProfileId 删除，固定
  v24pure（无标签上下文；snake 游戏经扫描根自带 profile，高级兼容可覆盖）。
- 语言文件四份：删 V24PureHint/SnakeHint/SelectGame，增 GameTab/CompatSettingsToggle。
- 钉扎重钉（本次最终文件哈希）。

## 关键决策与 Why

1. **保留物清单（基本功能红线）**：诊断 tab（FirstWindow.DiagnosticsSettings.cs 未动）、
   手册 tab（用户手写 manual.md）、反馈群号复制、GitHub 链接、安全区组件、
   RefitLauncherToViewport、Android 权限流程、状态标签（错误提示必需）、
   浏览/移除手动游戏、兼容包按游戏选择——只删结构与装饰，不删能力。
2. **合并扫描顺序 v24 先 snake 后**：addedPaths 去重使 compat/<profile> 路由条目
   （v24 pass 扫出、带正确 profile）优先于 snake pass 的嵌套发现，行为与旧 v24 分类
   等价或更准。
3. **折叠态不持久化**：低频设置默认收起即达目的，省一个 cfg 键与读写路径。

## AI 表现复盘

- 有效：改动集中单文件族，直接主会话串行实施（未拆 subagent）+ 中途两次增量构建
  稳步收敛；删除类重构用"改完即 grep 残留引用"逐个清零（FadeIn/tabFadeTween/
  GetNormalRootHint 均如此收口）。
- 低效：三连 Edit 失误（删空行而非删方法、误加空行），Python 脚本删方法后 Edit 工具
  状态失效需重 Read——**大段删除应一开始就用 python 行级脚本，别用 Edit 玩字符串拼接**。
- 教训 → 动作：整方法/整块删除优先用脚本按行号处理；Edit 后接 python 改动的文件必须
  重新 Read 才能 Edit（工具状态跟踪）。
