using System.Reflection;
using GEmuera.Core.Agent.Contract;
using Xunit;

namespace GEmuera.Core.Tests;

public class ProfileLoaderTests
{
    /// <summary>测试程序集 → 仓库根 → examples/agent-profiles/akuma-maid/profile.json。</summary>
    public static string RepoRoot
    {
        get
        {
            // bin/Debug/net8.0 → 上跳 5 级到仓库根
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 5 && dir.Parent != null; i++)
                dir = dir.Parent!;
            return dir.FullName;
        }
    }

    [Fact]
    public void LoadAkumaMaidProfile_ParsesAllSections()
    {
        string path = Path.Combine(RepoRoot, "examples", "agent-profiles", "akuma-maid", "profile.json");
        Assert.True(File.Exists(path), "profile.json not found at " + path);

        var outcome = ProfileLoader.LoadFromJson(File.ReadAllText(path));

        Assert.True(outcome.Ok, string.Join("; ", outcome.Errors));
        var p = outcome.Profile!;
        Assert.Equal("akuma-maid", p.Game.GameId);
        Assert.Equal("era悪魔で女仆", p.Game.Title);
        Assert.Equal("train-type", p.EngineFlow);
        Assert.Equal(6, p.Koujou.Layers.Count);
        Assert.Equal(3, p.Behaviors!.Whitelist.Count);
        Assert.Equal(8000, p.Budget.TokensPerTurn);
        Assert.Equal("vanilla", p.Fallback);
        Assert.Equal("Shop_Begin", p.Checkpoints.Primary);
        // 候选数组语义：日中键名按序保留
        Assert.Equal(new[] { "素質", "素质" }, p.CharacterCsv.SemanticGroups.Talent);
        Assert.Equal(new[] { "好感度" }, p.CharacterCsv.Favor!.CflagName);
        Assert.Equal(new[] { 500, 1000, 2000 }, p.CharacterCsv.Favor.Bands);
    }

    [Fact]
    public void EmptyJson_ProducesError()
    {
        var outcome = ProfileLoader.LoadFromJson("");
        Assert.False(outcome.Ok);
        Assert.Contains(outcome.Errors, e => e.Contains("empty"));
    }

    [Fact]
    public void MissingRequiredField_ProducesFieldNameError()
    {
        string json = """
            {
              "profile_version": "0",
              "game": {"game_id": "x", "title": "t", "game_code": "1", "version": "1"},
              "character_csv": {
                "dir": "CSV/CHARA",
                "filename_pattern": "^CHARA(\\d+)",
                "semantic_groups": {"talent": ["素質"], "ability": ["能力"]}
              },
              "koujou": {"layers": [{"id": "a", "entry_label": "B", "chara_label_template": "C"}]},
              "checkpoints": {"primary": "Shop_Begin"},
              "budget": {"tokens_per_turn": 100, "llm_calls_per_checkpoint": 1},
              "fallback": "vanilla"
            }
            """;
        var outcome = ProfileLoader.LoadFromJson(json);
        Assert.False(outcome.Ok);
        Assert.Contains(outcome.Errors, e => e.Contains("engine_flow"));
    }

    [Fact]
    public void WrongProfileVersion_ProducesVersionError()
    {
        string json = """
            {
              "profile_version": "99",
              "game": {"game_id": "x", "title": "t", "game_code": "1", "version": "1"},
              "engine_flow": "train-type",
              "character_csv": {
                "dir": "CSV/CHARA",
                "filename_pattern": "^CHARA(\\d+)",
                "semantic_groups": {"talent": ["素質"], "ability": ["能力"]}
              },
              "koujou": {"layers": [{"id": "a", "entry_label": "B", "chara_label_template": "C"}]},
              "checkpoints": {"primary": "Shop_Begin"},
              "budget": {"tokens_per_turn": 100, "llm_calls_per_checkpoint": 1},
              "fallback": "vanilla"
            }
            """;
        var outcome = ProfileLoader.LoadFromJson(json);
        Assert.False(outcome.Ok);
        Assert.Contains(outcome.Errors, e => e.Contains("profile_version"));
    }

    [Fact]
    public void BehaviorEffectWithBothPrimitives_ProducesError()
    {
        string json = """
            {
              "profile_version": "0",
              "game": {"game_id": "x", "title": "t", "game_code": "1", "version": "1"},
              "engine_flow": "train-type",
              "character_csv": {
                "dir": "CSV/CHARA",
                "filename_pattern": "^CHARA(\\d+)",
                "semantic_groups": {"talent": ["素質"], "ability": ["能力"]}
              },
              "koujou": {"layers": [{"id": "a", "entry_label": "B", "chara_label_template": "C"}]},
              "checkpoints": {"primary": "Shop_Begin"},
              "behaviors": {"whitelist": [
                {"id": "broken", "contexts": ["Shop_Begin"],
                 "effect": {"set_cflag": {"name": "F", "value": 1}, "call_label": "L"}}
              ]},
              "budget": {"tokens_per_turn": 100, "llm_calls_per_checkpoint": 1},
              "fallback": "vanilla"
            }
            """;
        var outcome = ProfileLoader.LoadFromJson(json);
        Assert.False(outcome.Ok);
        Assert.Contains(outcome.Errors, e => e.Contains("exactly one of set_cflag/call_label"));
    }
}
