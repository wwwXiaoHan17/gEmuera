using System.Text.Json;

namespace GEmuera.Core.Agent.Contract;

/// <summary>游戏身份（与 GameBase.csv 比对）。</summary>
public sealed class GameProfileGame
{
    public string GameId { get; set; } = "";
    public string Title { get; set; } = "";
    public string GameCode { get; set; } = "";
    public string Version { get; set; } = "";
    public string? VersionAccept { get; set; }
    public string? Encoding { get; set; }
}

public sealed class GameProfileFavor
{
    public string[] CflagName { get; set; } = [];
    public int[] Bands { get; set; } = [];
}

public sealed class GameProfileSemanticGroups
{
    public string[] Talent { get; set; } = [];
    public string[] Ability { get; set; } = [];
    public string[]? Experience { get; set; }
    public string[]? Base { get; set; }
    public string[]? Flag { get; set; }
}

public sealed class GameProfileCharacterCsv
{
    public string Dir { get; set; } = "";
    public string FilenamePattern { get; set; } = "";
    public string[]? NumberKey { get; set; }
    public string[]? NameKey { get; set; }
    public string[]? CallNameKey { get; set; }
    public GameProfileSemanticGroups SemanticGroups { get; set; } = new();
    public GameProfileFavor? Favor { get; set; }
}

public sealed class GameProfileKoujouLayer
{
    public string Id { get; set; } = "";
    public string? Description { get; set; }
    public string EntryLabel { get; set; } = "";
    public string CharaLabelTemplate { get; set; } = "";
    public string[] ExtraCharaLabels { get; set; } = [];
    public string[] GateVariables { get; set; } = [];
    public string[] GenericFallbackLabels { get; set; } = [];
    public bool ReplaceFallback { get; set; }
}

public sealed class GameProfileKoujou
{
    public string? GapDetection { get; set; }
    public List<GameProfileKoujouLayer> Layers { get; set; } = [];
}

public sealed class GameProfileEngineHook
{
    public string State { get; set; } = "";
    public string[] Actions { get; set; } = [];
}

public sealed class GameProfileCustomCheckpoint
{
    public string Id { get; set; } = "";
    public string? Callsharp { get; set; }
    public string? Arg { get; set; }
    public string? Variable { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("equals")]
    public JsonElement? EqualsValue { get; set; }
    public string[] Actions { get; set; } = [];
}

public sealed class GameProfileCheckpoints
{
    public string Primary { get; set; } = "";
    public List<GameProfileEngineHook> EngineHooks { get; set; } = [];
    public List<GameProfileCustomCheckpoint> Custom { get; set; } = [];
}

public sealed class GameProfileContextSnapshotEntry
{
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Band { get; set; }
}

public sealed class GameProfileContext
{
    public List<GameProfileContextSnapshotEntry> Snapshot { get; set; } = [];
    public string[] PrefetchKey { get; set; } = [];
}

public sealed class SetCflagEffect
{
    public string Name { get; set; } = "";
    /// <summary>integer 或 string（由 JsonElement 承载）。</summary>
    public JsonElement Value { get; set; }
    public bool TargetChara { get; set; } = true;
}

public sealed class BehaviorEffect
{
    public SetCflagEffect? SetCflag { get; set; }
    public string? CallLabel { get; set; }
}

public sealed class GameProfileBehavior
{
    public string Id { get; set; } = "";
    public string? Description { get; set; }
    public string[] Contexts { get; set; } = [];
    public BehaviorEffect Effect { get; set; } = new();
    public string[] Preconditions { get; set; } = [];
    public string Status { get; set; } = "draft";
}

public sealed class GameProfileBehaviors
{
    public List<GameProfileBehavior> Whitelist { get; set; } = [];
}

public sealed class GameProfileBudget
{
    public int TokensPerTurn { get; set; }
    public int LlmCallsPerCheckpoint { get; set; }
    public int PrefetchMaxVariants { get; set; } = 8;
}

/// <summary>声明式游戏适配档案（对齐 docs/designs/agent-profiles/profile.schema.json）。</summary>
public sealed class GameProfile
{
    public string ProfileVersion { get; set; } = "";
    public GameProfileGame Game { get; set; } = new();
    public string EngineFlow { get; set; } = "";
    public GameProfileCharacterCsv CharacterCsv { get; set; } = new();
    public GameProfileKoujou Koujou { get; set; } = new();
    public GameProfileCheckpoints Checkpoints { get; set; } = new();
    public GameProfileContext? Context { get; set; }
    public GameProfileBehaviors? Behaviors { get; set; }
    public GameProfileBudget Budget { get; set; } = new();
    public string Fallback { get; set; } = "";
}
