# 2026-10-20 HTML_PRINT 菜单边框错位与旧 HTML 残留排查

## 现象（eraBEMANI v24pure）

1. 进入主界面后，标题/模式选择的 HTML 仍在画面上方，没有消失；
2. 左侧 `カラム` 菜单只有 `[10002] 角色•物品购买` 一行的右边框比其他行靠左（错位一格）。

## 问题 1：旧 HTML 为什么没有消失

对照游戏 ERB 与 `emuera.em-master` 后确认：这不是 `HTML_PRINT` 解析失败，而是 **Emuera 的控制台语义就是追加式**：

- `HTML_PRINT` / `PRINT` 只把新内容追加到 display line 列表；
- `@ゲーム開始` 开头把 `VARLINE = 0`，而 `@LOOP_CLEARLINE`（`9-ヘッダーファイル,システム構成関数/7-式中関数,汎用関数.ERB:703`）是
  `SIF VARLINE > 0 && !非消去 → CLEARLINE LINECOUNT-VARLINE`，所以第一步不会清屏；
- game 在 `2-SHOPコマンド関連/1-SHOP.ERB:36` 原本有一句 `;CLEARLINE LINECOUNT`，但被**注释掉**了；标题→模式选择→SHOP 之间没有其它全屏 `CLEARLINE`。
- 参考 v24 引擎同样没有“`HTML_PRINT` 自动清掉上一次 HTML”的机制；旧标题/模式行留在 scrollback 属于游戏本身的选择，不是 gEmuera 解析 bug。

因此本次没有改引擎去强制清屏（那会偏离参考语义、影响所有游戏）。如果希望强制清掉，应该由游戏侧恢复 `CLEARLINE LINECOUNT`，或把“进入 SHOP 时清屏”做成显式的兼容策略/配置项再单独评估。

> 备注：运行截图里旧模式横幅出现在主界面上，是因为这些 HTML 行仍在 console 的历史/滚动区域；gEmuera 的 HTML 图片/div 是 canvas overlay，滚动时仍按各自行位置绘制。该渲染层的“旧行 overlay 残留”与“游戏不主动清屏”是两个层次的问题，本次仅记录、未改动渲染层，避免在未对齐参考的情况下引入全局回归。

## 问题 2：`[10002]` 边框错位（已修复）

### 根因

该行文本是 `角色•物品购买`（U+2022 BULLET，`•`）。游戏 `カラム生成2` 用 `PRINTFORM %text, (文字数計算用-4)*2, LEFT%` 做左填充：

- 参考 `LangManager.GetStrlenLang` 按 **locale 编码的字节数** 计算；游戏 locale 是简体中文（`内部で使用する東アジア言語:CHINESE_HANS` → CP936）。
- CP936/CP932 都**不能映射 U+2022**，`GetByteCountLang` 的 round-trip 失败后回退到 `lang.GetBytes` 的 1 字节；所以参考把 `•` 算 **1**：
  - `角色•物品购买` = 2+2+1+2+2+2+2 = **13**
  - `PRINTFORM` 总宽 14 → 参考补 **1 个空格**，边框对齐。
- gEmuera 的 `uEmuera.Utils.CheckHalfSize` 原来只按硬编码集合分类，`•` 落入 fullwidth 分支算 **2** → 当前长度 14，补 0 个空格；实际字形只占 1 格 → 整行短一格，右边框左移。

### 修复

`scripts/uEmuera/Utils.cs` 的 `CheckHalfSize` 在 fullwidth 判断前特判：

```csharp
if (c == '\u2022')
    return true;
```

注释说明与参考 CP932/CP936 编码回退一致；只影响该字符的宽度分类，不改方言表面。

### 验证

- 字节宽度：参考 CP936 `GetByteCount("角色•物品购买")` = 13；修复后 gEmuera 同路径 = 13（修复前 14）。
- 实际画面：v24pure × eraBEMANI 主界面（1280×1000 截图）中 `[10002]` 右边框与上下行对齐；模式选择/标题画面无回归。
- 门禁：build 0 error；三冒烟（v24 561/266、snake 670/351 表面不变）；xUnit 131+35+3；governance 4/4。

## 结论

- 问题 2 是真实的 v24 参考宽度偏差，已修复并上传。
- 问题 1 是游戏 ERB 自身不主动 `CLEARLINE` 导致的 console 历史；参考 v24 同样保留，不建议在引擎层无条件清屏。若需要“切屏即清”，应作为显式兼容策略另行实现。
