# 2026-09-17 兼容包契约层落地（接口定稿第一增量：DOD-1）

## 任务目标与成果

一句话：按 docs/designs/compat-pack-interface.md §11 DOD 第 1 项，把契约从设计文档落成
代码——EmueraFacade 新增 `Emuera.Compatibility.Packs` 命名空间（ICompatPack 四类贡献
接口 + 变体开放注册 + CompatPackManifest 解析器 + JSON Schema），配 30 项 fail-closed
单元测试（新工程 tests/xUnitTest/EmueraFacade.Tests），全门禁绿。

## 关键决策与 Why

1. **契约落点 EmueraFacade（设计文档 §10.1 推荐项）**：该程序集是显式 `Compile Include`
   （EnableDefaultCompileItems=false），子目录需补 `Compatibility\**\*.cs` glob——顺手
   把 schema json 以 `None Include` 随包分发不参与编译。
2. **解析器与词汇表校验分层**：CompatPackManifest 只做结构校验（fail-closed：未知字段
   整体拒绝、错误全量收集），capability id 词汇表命中/表面名单对账留给加载器（宿主侧
   才有词汇表）——契约程序集保持零依赖。
3. **targetEngineApi 接受数字或字符串两种 JSON 编码**：schema 与解析器一致，避免包作者
   踩 JSON 数字精度坑。
4. **测试工程经 csproj EmbeddedResource LogicalName 内嵌合法清单**：证明程序集资源发现
   路径（Ordinal 精确匹配 compatpack.manifest.json）而不需要运行期编译测试包。

## AI 表现复盘

- 有效：写完即自审发现两处缺陷（packId 必填被静默吞、json 变量明确赋值错误）当场修；
  测试首轮抓到可选字段返回 "" 而非 null——fail-closed 矩阵先行有效。
- 低效：sln 手写配置块时打进一个坏 GUID（8F5D2C2C），靠逐行核对才发现——下次 sln 登记
  优先 `dotnet sln add`，手写仅作回退。

## 教训 → 具体优化行动

- 新 .cs 的 .uid 侧车由 Godot 构建生成，本批 5 个全部入库（规则：与 271 个已跟踪一致）。
- 后续增量（加载器/组装/hello-world 端到端）按设计文档 §11 DOD 第 2/3 项排布。

## 验证记录

dotnet test EmueraFacade.Tests 30/30、GEmuera.Core.Tests 20/20；Godot 无头构建 DLL
01:00:43 更新（唯一 error 行为已知 EditorSettings 收尾噪声）；三冒烟全过；sln 经
`dotnet build` 隐式校验（测试工程构建走 sln 内路径解析）。
