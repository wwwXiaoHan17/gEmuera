using System;

namespace GEmuera.Core.Compatibility;

/// <summary>
/// 预设变量同名 ERD 合并规则（Emuera-SKIA Skiav13.0 起，snake 语义）：
/// 全部 CSV 加载完、逆引字典建立前，把 ERB 目录下与预设变量同名的 .erd
/// （如 ITEM.erd）合并进名字槽；CSV 优先，ERD 只填空槽，合并后的名字同样进入逆引字典。
/// 本类是纯函数层：不触碰文件系统/控制台，扫描、解码与警告落盘由引擎侧负责，
/// 规则本身由 xUnit 覆盖。
/// </summary>
public static class PresetErdMerge
{
    /// <summary>无名字来源（空槽）。</summary>
    public const sbyte NameSourceEmpty = -1;
    /// <summary>名字来自 CSV（CSV 优先，ERD 不得覆盖）。</summary>
    public const sbyte NameSourceCsv = 0;
    /// <summary>名字来自 ERD 补入。</summary>
    public const sbyte NameSourceErd = 1;

    /// <summary>一行 ERD 的解析结果。</summary>
    public enum LineParseResult
    {
        Ok = 0,
        MissingComma = 1,
        IndexNotInteger = 2,
    }

    /// <summary>名字槽的合并动作（引擎侧据此发警告）。</summary>
    public enum NameApplyResult
    {
        None = 0,
        /// <summary>空槽被 ERD 填充。</summary>
        Filled = 1,
        /// <summary>该名字已存在于其他槽：警告并跳过（避免逆引先胜造成失联槽）。</summary>
        DuplicateAcrossSlots = 2,
        /// <summary>CSV 已有不同名字：CSV 优先，仅警告。</summary>
        ConflictWithCsv = 3,
    }

    /// <summary>ITEM 第三列价格的合并动作（引擎侧据此发警告）。</summary>
    public enum PriceApplyResult
    {
        None = 0,
        /// <summary>CSV 未设价格时由 ERD 填入。</summary>
        Filled = 1,
        /// <summary>CSV 已设价格（含显式 0）且与 ERD 不同：CSV 优先，仅警告。</summary>
        ConflictWithCsv = 2,
        /// <summary>ERD 价格字段不是整数。</summary>
        Invalid = 3,
        /// <summary>价格目标槽不可用/索引越界（引擎侧发数组越界警告）。</summary>
        OutOfRange = 4,
    }

    /// <summary>解析后的一行：索引 + 名字 + 可选的 ITEM 价格字段。</summary>
    public readonly struct Row
    {
        public Row(int index, string name, bool hasPriceField, bool priceValid, long price)
        {
            Index = index;
            Name = name;
            HasPriceField = hasPriceField;
            PriceValid = priceValid;
            Price = price;
        }

        public int Index { get; }
        /// <summary>第二列原文（与 CSV 名字解析一致：不做 Trim/引号处理）。</summary>
        public string Name { get; }
        public bool HasPriceField { get; }
        public bool PriceValid { get; }
        public long Price { get; }
    }

    /// <summary>合并动作聚合：一行可能同时产生名字与价格两类动作。</summary>
    public readonly struct ApplyResult
    {
        public readonly NameApplyResult Name;
        public readonly PriceApplyResult Price;

        public ApplyResult(NameApplyResult name, PriceApplyResult price)
        {
            Name = name;
            Price = price;
        }
    }

    /// <summary>
    /// 解析单行。字段分隔符为 ','，无引号，最多读取前三列（后续列忽略）；
    /// 第二列名字保持原文（不 Trim），第三列价格仅 TrimEnd 后按整数解析。
    /// </summary>
    public static LineParseResult TryParseLine(ReadOnlySpan<char> line, out Row row)
    {
        row = default;
        int firstComma = line.IndexOf(',');
        if (firstComma < 0)
            return LineParseResult.MissingComma;

        ReadOnlySpan<char> indexField = line.Slice(0, firstComma);
        ReadOnlySpan<char> rest = line.Slice(firstComma + 1);
        int secondComma = rest.IndexOf(',');

        ReadOnlySpan<char> nameField = secondComma < 0 ? rest : rest.Slice(0, secondComma);
        ReadOnlySpan<char> priceField = default;
        if (secondComma >= 0)
        {
            // 上游按最多 5 段切分：第三列到下一个逗号为止，第四列及以后忽略。
            ReadOnlySpan<char> priceRest = rest.Slice(secondComma + 1);
            int thirdComma = priceRest.IndexOf(',');
            priceField = thirdComma < 0 ? priceRest : priceRest.Slice(0, thirdComma);
        }

        if (!int.TryParse(indexField, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int index))
        {
            return LineParseResult.IndexNotInteger;
        }

        bool hasPriceField = secondComma >= 0;
        bool priceValid = false;
        long price = 0;
        if (hasPriceField)
        {
            // 上流实现为 ros[dest[2]].TrimEnd()；保留其"仅去尾随空白"语义。
            ReadOnlySpan<char> trimmed = priceField.TrimEnd();
            priceValid = long.TryParse(trimmed, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out price);
        }

        row = new Row(index, nameField.ToString(), hasPriceField, priceValid, price);
        return LineParseResult.Ok;
    }

    /// <summary>
    /// 按 CSV 优先语义把一行合并进目标槽。调用方必须保证 index 已通过越界检查
    /// （<see cref="TryApply"/> 仍会再校验一次）。
    /// <paramref name="nameExistsInOtherSlot"/> 返回该名字是否已存在于其他槽；
    /// 由引擎侧提供（需要扫描全部名字槽），因此不把引擎数据结构带进 Core。
    /// </summary>
    public static bool TryApply(
        in Row row,
        int index,
        string[] names,
        sbyte[]? nameSources,
        long[]? itemPrices,
        sbyte[]? priceSources,
        bool isItemSlot,
        Func<string, bool> nameExistsInOtherSlot,
        out ApplyResult result)
    {
        result = default;
        if (names == null || (uint)index >= (uint)names.Length)
            return false;
        if (nameExistsInOtherSlot == null)
            throw new ArgumentNullException(nameof(nameExistsInOtherSlot));

        NameApplyResult nameResult = NameApplyResult.None;
        string csvName = names[index];
        if (string.IsNullOrEmpty(csvName))
        {
            // 跨槽重名检查：逆引字典先胜，后填的同名槽会"写后即失联"，警告并跳过。
            if (!string.IsNullOrEmpty(row.Name) && nameExistsInOtherSlot(row.Name))
            {
                nameResult = NameApplyResult.DuplicateAcrossSlots;
                // 与上游 continue 一致：名字重复时整行跳过，第三列价格也不再处理。
                result = new ApplyResult(nameResult, PriceApplyResult.None);
                return true;
            }

            names[index] = row.Name;
            if (nameSources != null && index < nameSources.Length)
                nameSources[index] = NameSourceErd;
            nameResult = NameApplyResult.Filled;
        }
        else if (!string.Equals(csvName, row.Name, StringComparison.Ordinal))
        {
            // CSV 已有值：CSV 优先，仅在名字不一致时警告（一致则静默通过）。
            nameResult = NameApplyResult.ConflictWithCsv;
        }

        PriceApplyResult priceResult = PriceApplyResult.None;
        if (isItemSlot && row.HasPriceField)
        {
            if (itemPrices == null || priceSources == null
                || (uint)index >= (uint)itemPrices.Length
                || (uint)index >= (uint)priceSources.Length)
            {
                // 上游在该条件下发 OoRArray 警告并跳过价格写入。
                priceResult = PriceApplyResult.OutOfRange;
            }
            else if (!row.PriceValid)
            {
                priceResult = PriceApplyResult.Invalid;
            }
            else if (priceSources[index] < 0)
            {
                itemPrices[index] = row.Price;
                priceSources[index] = NameSourceErd;
                priceResult = PriceApplyResult.Filled;
            }
            else if (priceSources[index] == NameSourceCsv && itemPrices[index] != row.Price)
            {
                priceResult = PriceApplyResult.ConflictWithCsv;
            }
        }

        result = new ApplyResult(nameResult, priceResult);
        return true;
    }
}
