using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;

namespace GEmuera.Core.Compatibility;

/// <summary>
/// 内置 eraFL 兼容包。模块以数据和确定性策略为主，只暴露冻结计划需要的
/// 输入以及 legacy bridge 消费的窄决策；不读取游戏文件、不加载程序集，也不执行 ERB。
/// </summary>
public static class EraFlCompatibilityModule
{
    public const string ModuleId = "game.erafl";
    public const string ModuleVersion = "1.0.0";
    public const string BaseModuleId = "gemuera.v24";
    public const string SaveProfileId = "gemuera.erafl";

    public const string InputOmittedDefaultArgumentBehavior = "input.omitted-default-argument.v1";
    public const string PointerBlankIntegerBehavior = "input.pointer-blank-integer.v1";
    public const string MarkupUnknownAngleTextBehavior = "markup.unknown-angle-text.v1";
    public const string DisplayDynamicScopeBehavior = "display.dynamic-scope-classifier.v1";
    public const string ResourceDynamicSpriteBehavior = "resource.dynamic-sprite-roots.v1";

    public const string InputArgumentPortType = "IEraFlInputArgumentPolicy";
    public const string PointerInputPortType = "IEraFlPointerInputPolicy";
    public const string MarkupPortType = "IEraFlMarkupPolicy";
    public const string DisplayScopePortType = "IEraFlDisplayScopePolicy";
    public const string ResourcePortType = "IEraFlResourcePolicy";

    public const string MarkupDivCapability = "markup.div-v2.v1";
    public const string MarkupDualImageCapability = "markup.image-dual-src.v1";
    public const string DynamicMapCapability = "display.dynamic-map-transaction.v1";
    public const string PointerButtonCapability = "input.pointer-button.v1";
    public const string DynamicSpriteCapability = "resource.dynamic-sprite.v1";

    /// <summary>空白指针输入直接提交字符串等待（IEraFlCompatibilityPolicy.ShouldSubmitBlankPointerStringInput）。</summary>
    public const string PointerBlankStringBehavior = "input.pointer-blank-string.v1";

    /// <summary>扩展显示历史（IEraFlCompatibilityPolicy.UsesExtendedDisplayHistory）。</summary>
    public const string DisplayExtendedHistoryBehavior = "display.extended-history.v1";

    public const string TaskStartRoomLookupFunction = "HO_FIND_ROOM_BY_TAG";
    public const string TaskStartRoomTag = "任务開始地点";
    public const string TaskStartRoomIdMarker = "[ROOM_ID:200]";
    public const string GMapQuestType = "GMAP";

    public readonly record struct GMapNodeData(long NodeId, string? NodeName, string? PathList);

    private static readonly ReadOnlyCollection<string> RequiredCapabilities =
        Array.AsReadOnly(new[]
        {
            MarkupDivCapability,
            MarkupDualImageCapability,
            DynamicMapCapability,
            PointerButtonCapability,
            DynamicSpriteCapability,
            // 布尔型 policy quirk（IEraFlCompatibilityPolicy 逐项从本清单派生）：
            InputOmittedDefaultArgumentBehavior,
            PointerBlankStringBehavior,
            DisplayExtendedHistoryBehavior,
        });

    private static readonly ReadOnlyCollection<BehaviorPortSnapshot> DefaultBehaviorPorts =
        Array.AsReadOnly(new[]
        {
            new BehaviorPortSnapshot(
                InputOmittedDefaultArgumentBehavior,
                InputArgumentPortType,
                PortContractKind.PolicyDecision,
                ModuleId,
                "input.argument-policy.v1",
                ModuleId,
                "DIA-ERAFL-INPUT-001"),
            new BehaviorPortSnapshot(
                PointerBlankIntegerBehavior,
                PointerInputPortType,
                PortContractKind.PolicyDecision,
                ModuleId,
                "input.pointer-policy.v1",
                ModuleId,
                "DIA-ERAFL-INPUT-002"),
            new BehaviorPortSnapshot(
                MarkupUnknownAngleTextBehavior,
                MarkupPortType,
                PortContractKind.PolicyDecision,
                ModuleId,
                "markup.angle-text-policy.v1",
                ModuleId,
                "DIA-ERAFL-MARKUP-001"),
            new BehaviorPortSnapshot(
                DisplayDynamicScopeBehavior,
                DisplayScopePortType,
                PortContractKind.PolicyDecision,
                ModuleId,
                "display.scope-policy.v1",
                ModuleId,
                "DIA-ERAFL-DISPLAY-001"),
            new BehaviorPortSnapshot(
                ResourceDynamicSpriteBehavior,
                ResourcePortType,
                PortContractKind.PolicyDecision,
                ModuleId,
                "resource.sprite-root-policy.v1",
                ModuleId,
                "DIA-ERAFL-RESOURCE-001"),
        });

    public static IReadOnlyList<string> RequiredCapabilityIds => RequiredCapabilities;
    public static IReadOnlyList<BehaviorPortSnapshot> DefaultPorts => DefaultBehaviorPorts;

    public static DialectModuleDefinition CreateDefinition()
    {
        return new DialectModuleDefinition(
            ModuleId,
            ModuleVersion,
            1,
            dependencies: new[]
            {
                new ModuleDependencySnapshot(BaseModuleId, "[1.0.0,2.0.0)"),
            },
            portTypeIds: new[]
            {
                InputArgumentPortType,
                PointerInputPortType,
                MarkupPortType,
                DisplayScopePortType,
                ResourcePortType,
            });
    }

    public static CompatibilityProfileDefinition CreateProfile()
    {
        return new CompatibilityProfileDefinition(
            "erafl",
            new[] { ModuleId },
            defaultPorts: DefaultPorts,
            requiredCapabilityIds: RequiredCapabilities,
            defaultSaveProfileId: SaveProfileId);
    }

    /// <summary>
    /// eraFL 在省略第一个默认字符串时会把 INPUTS 写成前导逗号。该策略只
    /// 处理词法当前位置，因此可以在 legacy 表达式解析器创建参数对象前消费。
    /// </summary>
    public static bool IsOmittedDefaultArgument(char currentToken)
    {
        return currentToken == ',';
    }

    /// <summary>
    /// eraFL 的 <c>INPUTS ,1</c> 中第二参数启用鼠标扩展结果。只接受当前已验证的
    /// 常量 1，避免把其它前导逗号写法静默扩展成同一协议。
    /// </summary>
    public static bool IsPointerInputMetadataOption(string? optionText)
    {
        return long.TryParse(optionText, out long option) && option == 1;
    }

    /// <summary>
    /// 在 eraFL 的整数等待契约中，空白右键/中键表示“没有选中的按钮”。左键
    /// 和非指针输入仍保留普通空字符串语义，避免改变 v24 的输入行为。
    /// </summary>
    public static string NormalizePointerIntegerSubmission(
        string? input,
        int mouseButton,
        bool waitingForInteger)
    {
        if (!waitingForInteger || !string.IsNullOrEmpty(input) || mouseButton is 0 or 1)
            return input ?? string.Empty;
        return "-1";
    }

    /// <summary>
    /// eraFL 的 INPUTS 鼠标循环允许点击空白区域提交空字符串。宿主只有在字符串
    /// 输入等待中收到真实指针键时才应放行，不能把普通键盘空输入扩散到其它 profile。
    /// </summary>
    public static bool ShouldSubmitBlankPointerStringInput(int mouseButton, bool waitingForString)
    {
        return waitingForString && mouseButton != 0;
    }

    /// <summary>
    /// 宿主内部使用 Win32 VK（中键为 0x04），eraFL 写入 RESULT:1 的鼠标协议则是
    /// 1=左键、2=右键、3=中键。这里只转换中键，不擅自改写其它未知输入值。
    /// </summary>
    public static int NormalizePointerButtonResult(int mouseButton)
    {
        return mouseButton == 0x04 ? 3 : mouseButton;
    }

    public static bool UsesExtendedDisplayHistory(string? profileId)
    {
        return string.Equals(profileId, "erafl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(profileId, "snake", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// eraFL 的任务初始化会通过 <c>HO_FIND_ROOM_BY_TAG</c> 查找“任务開始地点”。
    /// 某些运行路径会让游戏侧标签查询错误地返回 -1；但任务地图本身仍保留
    /// <c>[ROOM_ID:200]</c> 这一唯一的起点标记。此处只根据已加载的运行时地图
    /// 恢复对应的房间下标，不读取游戏文件，也不会把任意未命中标签改写为成功。
    /// </summary>
    public static bool TryRecoverQuestStartRoomIndex(
        string? functionName,
        long returnedRoomIndex,
        string? requestedRoomTag,
        long mapId,
        string[,]? mapData,
        out long recoveredRoomIndex)
    {
        return TryRecoverQuestStartRoomIndex(
            functionName,
            returnedRoomIndex,
            requestedRoomTag,
            mapId,
            null,
            mapData,
            out recoveredRoomIndex);
    }

    /// <summary>
    /// 带任务类型的 eraFL 起点恢复。GMAP 脚本明确规定 node 0 是任务起点，但必须先
    /// 确认运行时 node 0 已完成实体化；普通 MAP 仍只接受唯一的 <c>[ROOM_ID:200]</c>。
    /// </summary>
    public static bool TryRecoverQuestStartRoomIndex(
        string? functionName,
        long returnedRoomIndex,
        string? requestedRoomTag,
        long mapId,
        string? questType,
        string[,]? mapData,
        out long recoveredRoomIndex)
    {
        recoveredRoomIndex = returnedRoomIndex;
        if (returnedRoomIndex != -1
            || !string.Equals(functionName, TaskStartRoomLookupFunction, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(requestedRoomTag, TaskStartRoomTag, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(questType, GMapQuestType, StringComparison.Ordinal))
        {
            string? startRoomData = mapData is not null && mapId >= 0 && mapId < mapData.GetLength(0)
                && mapData.GetLength(1) > 0
                ? mapData[(int)mapId, 0]
                : null;
            if (mapData is null || mapId < 0 || mapId >= mapData.GetLength(0)
                || mapData.GetLength(1) == 0
                || string.IsNullOrEmpty(startRoomData)
                || startRoomData.IndexOf(TaskStartRoomIdMarker, StringComparison.Ordinal) < 0)
            {
                return false;
            }
            recoveredRoomIndex = 0;
            return true;
        }

        if (mapData is null || mapId < 0 || mapId >= mapData.GetLength(0))
            return false;

        int candidateRoomIndex = -1;
        int mapIndex = (int)mapId;
        for (int roomIndex = 0; roomIndex < mapData.GetLength(1); roomIndex++)
        {
            string? roomData = mapData[mapIndex, roomIndex];
            if (roomData is null
                || roomData.IndexOf(TaskStartRoomIdMarker, StringComparison.Ordinal) < 0)
            {
                continue;
            }

            // 起点必须唯一。多个候选意味着游戏数据本身不明确，保持原始失败值而不是猜测。
            if (candidateRoomIndex >= 0)
                return false;
            candidateRoomIndex = roomIndex;
        }

        if (candidateRoomIndex < 0)
            return false;

        recoveredRoomIndex = candidateRoomIndex;
        return true;
    }

    /// <summary>
    /// 把 eraFL 已导入 DT 的 GMAP 节点补入脚本运行时的二维房间字典。
    /// 该方法只处理节点元数据，不读取文件；所有节点先完整校验并生成新值，
    /// 任一节点无效时不会留下半张地图。
    /// </summary>
    public static bool TryPopulateGMapRoomData(
        long mapId,
        string[,]? mapData,
        IReadOnlyList<GMapNodeData>? nodes)
    {
        if (mapData is null || nodes is null || nodes.Count == 0
            || mapId < 0 || mapId >= mapData.GetLength(0))
        {
            return false;
        }

        int mapIndex = (int)mapId;
        int roomCapacity = mapData.GetLength(1);
        var seenNodeIds = new HashSet<int>();
        var pending = new List<KeyValuePair<int, string>>(nodes.Count);
        bool hasStartNode = false;

        for (int i = 0; i < nodes.Count; i++)
        {
            GMapNodeData node = nodes[i];
            if (node.NodeId < 0 || node.NodeId >= roomCapacity)
                return false;
            int nodeId = (int)node.NodeId;
            if (!seenNodeIds.Add(nodeId))
                return false;

            string roomData = mapData[mapIndex, nodeId] ?? "";
            if (!TrySetDictionaryValue(roomData, "ROOM_NAME", node.NodeName ?? "", out roomData)
                || !TrySetDictionaryValue(roomData, "ROOM_PATH", node.PathList ?? "", out roomData)
                || !TrySetDictionaryValue(roomData, "ROOM_ID", nodeId == 0 ? "200" : "201", out roomData))
            {
                return false;
            }

            pending.Add(new KeyValuePair<int, string>(nodeId, roomData));
            hasStartNode |= nodeId == 0;
        }

        if (!hasStartNode)
            return false;

        for (int i = 0; i < pending.Count; i++)
            mapData[mapIndex, pending[i].Key] = pending[i].Value;
        return true;
    }

    /// <summary>
    /// 从 eraFL GMAP 的 schema/XML 文本提取节点。文件定位和读取仍由宿主负责，
    /// 兼容模块只处理确定性的 XML→节点转换，便于 Android 与桌面共用并独立验证。
    /// </summary>
    public static bool TryParseGMapNodesFromXml(
        string? schemaXml,
        string? dataXml,
        out IReadOnlyList<GMapNodeData> nodes)
    {
        if (!TryParseGMapDataTableAndNodesFromXml(
            schemaXml,
            dataXml,
            requireDrawingColumns: false,
            out DataTable? table,
            out nodes))
        {
            return false;
        }
        table?.Dispose();
        return true;
    }

    /// <summary>
    /// 从 eraFL GMAP 的 schema/XML 文本构造完整 DataTable，同时提取房间字典需要的
    /// 节点字段。宿主会把完整表注册到 legacy DT 存储，避免只恢复房间名却丢失
    /// <c>POS_X/POS_Y</c> 后无法绘制地图节点。
    /// </summary>
    public static bool TryParseGMapDataTableFromXml(
        string? schemaXml,
        string? dataXml,
        out DataTable? table,
        out IReadOnlyList<GMapNodeData> nodes)
    {
        return TryParseGMapDataTableAndNodesFromXml(
            schemaXml,
            dataXml,
            requireDrawingColumns: true,
            out table,
            out nodes);
    }

    private static bool TryParseGMapDataTableAndNodesFromXml(
        string? schemaXml,
        string? dataXml,
        bool requireDrawingColumns,
        out DataTable? table,
        out IReadOnlyList<GMapNodeData> nodes)
    {
        table = null;
        nodes = Array.Empty<GMapNodeData>();
        if (string.IsNullOrWhiteSpace(schemaXml) || string.IsNullOrWhiteSpace(dataXml))
            return false;

        DataTable? parsedTable = null;
        try
        {
            parsedTable = new DataTable("GMAPDATA");
            using (var reader = new StringReader(schemaXml))
                parsedTable.ReadXmlSchema(reader);
            using (var reader = new StringReader(dataXml))
                parsedTable.ReadXml(reader);

            // DT_SELECT 会读取 id，地图绘制会按主键再取 NODE_ID/POS_X/POS_Y；
            // 这些列缺失时不能把表当成可用的绘图数据。
            if (parsedTable.Rows.Count == 0
                || !parsedTable.Columns.Contains("NODE_ID")
                || !parsedTable.Columns.Contains("NODE_NAME")
                || !parsedTable.Columns.Contains("PATH_LIST")
                || (requireDrawingColumns
                    && (!parsedTable.Columns.Contains("id")
                        || !parsedTable.Columns.Contains("POS_X")
                        || !parsedTable.Columns.Contains("POS_Y"))))
            {
                return false;
            }
            if (requireDrawingColumns && parsedTable.PrimaryKey.Length == 0)
                parsedTable.PrimaryKey = new[] { parsedTable.Columns["id"]! };

            var parsed = new List<GMapNodeData>(parsedTable.Rows.Count);
            foreach (DataRow row in parsedTable.Rows)
            {
                if (row == null || row["NODE_ID"] == DBNull.Value
                    || (requireDrawingColumns
                        && (row["id"] == DBNull.Value
                            || row["POS_X"] == DBNull.Value
                            || row["POS_Y"] == DBNull.Value)))
                {
                    return false;
                }
                if (requireDrawingColumns)
                {
                    _ = Convert.ToInt64(row["id"]);
                    _ = Convert.ToInt64(row["POS_X"]);
                    _ = Convert.ToInt64(row["POS_Y"]);
                }
                parsed.Add(new GMapNodeData(
                    Convert.ToInt64(row["NODE_ID"]),
                    row["NODE_NAME"] == DBNull.Value ? "" : Convert.ToString(row["NODE_NAME"]),
                    row["PATH_LIST"] == DBNull.Value ? "" : Convert.ToString(row["PATH_LIST"])));
            }

            table = parsedTable;
            parsedTable = null;
            nodes = parsed.AsReadOnly();
            return true;
        }
        catch (Exception)
        {
            table = null;
            nodes = Array.Empty<GMapNodeData>();
            return false;
        }
        finally
        {
            parsedTable?.Dispose();
        }
    }

    private static bool TrySetDictionaryValue(string source, string key, string value, out string result)
    {
        source ??= "";
        value ??= "";
        string prefix = "[" + key + ":";
        int open = source.IndexOf(prefix, StringComparison.Ordinal);
        if (open < 0)
        {
            result = source + prefix + value + "]";
            return true;
        }

        int close = source.IndexOf(']', open + prefix.Length);
        if (close < 0)
        {
            result = source;
            return false;
        }

        result = source.Substring(0, open) + prefix + value + "]" + source.Substring(close + 1);
        return true;
    }
}
