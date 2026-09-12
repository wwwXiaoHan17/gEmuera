# 2026-09-12 方言指令变体模块化（方言接口完善第一步）

## 任务目标与成果

把方言差异从"投影缝硬编码 if/else"（`CreateProfileInstruction` 的 FOR→REPEAT、SETBGIMAGE 双变体分支）收进方言模块的声明式贡献，作为"完善方言接口 → 方言模块形态"路线的第一个工作包。产出：`LegacyInstructionVariant` 枚举 + `SubstituteInstruction` 贡献 API + 三模块（v24 基线声明 / snake 覆盖 / erafl 显式绑定），行为零可观测差异；附带 capability id 的首个运行期消费者（[LOAD] 日志）与 SurfaceSmoke 变体表契约断言。

## 关键决策与 Why

1. **变体选择用枚举数据而不是委托**——`Func<FunctionIdentifier, FunctionIdentifier>` 委托最初是候选，但 SurfaceSmoke 只链接编译 `LegacyCompatibilityProfile.cs` + `LegacyCompatibilityModules.cs` 两个文件（不编译解释器），不透明委托无法脱离 FunctionIdentifier 断言。枚举让"模块选哪个变体"成为可在 Core 层测试、可哈希的数据，"变体怎么构造"留在 FunctionIdentifier（私有嵌套类封装不破）。这是"模块=数据声明、注册表=构造知识"分层的关键。
2. **Apply 后写覆盖语义**——Compose 保证 Declare 全部先跑、选中模块的 Apply 后跑（依赖序），因此 v24 Apply 声明基线变体、snake Apply 覆盖，无需新增机制即可表达"子方言改写基线"；snake 的 FOR 用 `SharedTable` 显式回退而不是删除条目，保持"覆盖"模型的一致性。
3. **erafl 对 SETANIMETIMER 注册显式 SharedTable 绑定**——虽然今天等价于不注册（handler 即共享表原条目），但把绑定归属写进模块后，未来 snake 侧 handler 分叉时 erafl 不再隐式跟随。这是"摸黑方言的依赖要显式化"原则的应用。

## AI 表现复盘

**有效**：
- 改造前用两个 Explore agent 把"方言抽象 vs 实际解释器行为"的接缝彻底盘清（描述符通道空转、~15 处 policy 分支、两次 git 尸检），设计直接从事实推出而非拍脑袋。
- 验证矩阵分层：SurfaceSmoke（桥层契约）→ RuntimeSmoke（编译后注册表计数）→ legacy-runner 三 profile 执行级（带输入 replay 进 @EVENTFIRST，`loop×3+DONE` 文本证据）。迁移类重构的"零可观测差异"由此闭环。
- 验证中发现并排除一次假警报：`FOR 3` 报"引数不足"不是回归，而是对变体文法的错误假设（真实文法是 `FOR LOCAL, 0, 3`，从 eraTW/erafl 真实 ERB 求证后修正 fixture）。

**低效**：
- 两次自我失误浪费了构建-验证轮次：① C# 插值字符串洞里写 `\"`（编译错），还一度误判为 GdUnit4 插件问题，实际"EditorPlugin build callback failed"就是编译错误的表象；② bash heredoc 的 `\\\\` 转义把 runner 配置 JSON 写成非法单反斜杠，追了一圈"invalid_era_game_directory"。
- `ls` 时间戳判读混乱（dotnet build 与 Godot build 写同一路径 dll），应当一开始就分离两个验证步骤。

## 教训 → 具体优化动作

1. **Godot 构建失败先跑 `dotnet build gemuera-c#.csproj` 拿精确编译错误**（约 10 秒），再回 Godot 管线——"EditorPlugin build callback failed" 大概率是 C# 编译错误而非插件/环境问题。本项目下 `dotnet build` 可用（AGENTS.md 的"不要 dotnet build"警告针对的是把它当**唯一**构建手段，快速错误定位没问题）。
2. **marker 时间戳判据在增量构建下会假阴性**：源码没变时 msbuild 不重写 dll，`dll -nt marker` 判 STALE。正确判据：dll 时间戳 > 上一次源码修改时间，或直接 `strings -e l <dll> | grep <新符号>`（UTF-16 字符串堆/元数据名验证改动已编入）。
3. **写 JSON 配置用正斜杠路径**（`E:/tmp/...`），避免 heredoc/printf 反斜杠转义地狱；写完 `python -c json.load` 自检一遍再跑。
4. **legacy-runner 冒烟要区分"解析级"与"执行级"**：无 inputs 的 first-wait 到达的是系统标题菜单（只证明 ERB 被解析），要证明指令执行需配置 `inputs:[{value:"0",fromButton:true}]` 进入 @EVENTFIRST，并以 display.raw.json 的 text 字段为执行证据。

## 后续路线（方言接口完善 backlog，按序）

1. 描述符通道通电：BuiltInDialectCatalog 三模块的空贡献 → 真实指令/函数清单（Core 为唯一事实源，工具消费它消灭 legacy-runner 白名单/schema/快照三处镜像）。
2. 函数侧替换贡献（DialectFunctionContracts 的 ~40 个包装从 Snake.IsEnabled 内部判断迁到模块声明）。
3. 解析器级 policy 分支（~15 处 `Snake.AllowsX`/`EraFl.IsX`）逐批收编为模块 capability 消费点，capability id 兼作 quirk ledger。
4. v18 模块：用 emuera_v18_exported 源码机械枚举 v18↔v24 差异，检验模块系统表达力。
