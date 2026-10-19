using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GEmuera.Core.Session;

namespace gEmuera.GodotHost;

/// <summary>
/// Immutable host-side binding between the Core's opaque game id and the
/// legacy runtime launch inputs. Game paths deliberately stop here: Core can
/// select a session but cannot learn or resolve host filesystem paths.
/// </summary>
public sealed class LegacySessionLaunchConfiguration
{
    public LegacySessionLaunchConfiguration(string gameRoot, string profileId)
    {
        GameRoot = NormalizeGameRoot(gameRoot);
        ProfileId = NormalizeProfileId(profileId);
        GameId = CreateGameId(GameRoot);
    }

    public string GameId { get; }
    public string GameRoot { get; }
    public string ProfileId { get; }

    public SessionSelection CreateSelection()
    {
        return new SessionSelection(GameId, ProfileId);
    }

    public static string CreateGameId(string gameRoot)
    {
        string normalizedRoot = NormalizeGameRoot(gameRoot);
        string identityInput = normalizedRoot.Replace('\\', '/').ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identityInput)))
            .ToLowerInvariant();
        return "legacy-" + hash[..16];
    }

    private static string NormalizeGameRoot(string gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            throw new ArgumentException("Legacy game root is required.", nameof(gameRoot));
        if (!Path.IsPathRooted(gameRoot))
            throw new ArgumentException("Legacy game root must be absolute.", nameof(gameRoot));

        string fullPath = Path.GetFullPath(gameRoot.Trim());
        string pathRoot = Path.GetPathRoot(fullPath) ?? string.Empty;
        if (!string.Equals(fullPath, pathRoot, StringComparison.OrdinalIgnoreCase))
            fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath;
    }

    private static string NormalizeProfileId(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            throw new ArgumentException("Legacy profile id is required.", nameof(profileId));

        string normalized = profileId.Trim().ToLowerInvariant();
        // Reuse the Core identifier guard while keeping the resulting
        // selection as a boundary value, not a path-bearing Core DTO.
        _ = new SessionSelection("legacy-profile-validation", normalized);
        return normalized;
    }
}

/// <summary>
/// Frozen allowlist used by the Godot bridge before a legacy session starts.
/// It never guesses a path or silently changes profiles when a Core selection
/// is unknown or mismatched.
/// </summary>
public sealed class LegacySessionLaunchRegistry
{
    private readonly IReadOnlyDictionary<LaunchKey, LegacySessionLaunchConfiguration> _bySelection;
    private readonly HashSet<string> _registeredGameIds;

    public LegacySessionLaunchRegistry(IEnumerable<LegacySessionLaunchConfiguration> configurations)
    {
        ArgumentNullException.ThrowIfNull(configurations);

        var mutable = new Dictionary<LaunchKey, LegacySessionLaunchConfiguration>();
        var gameIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var configuration in configurations)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            var key = new LaunchKey(configuration.GameId, configuration.ProfileId);
            if (!mutable.TryAdd(key, configuration))
                throw new InvalidOperationException(
                    $"Duplicate legacy session launch binding '{configuration.GameId}' / '{configuration.ProfileId}'.");
            gameIds.Add(configuration.GameId);
        }

        if (mutable.Count == 0)
            throw new ArgumentException("At least one legacy session launch configuration is required.", nameof(configurations));

        _bySelection = new ReadOnlyDictionary<LaunchKey, LegacySessionLaunchConfiguration>(mutable);
        _registeredGameIds = gameIds;
        Configurations = Array.AsReadOnly(mutable.Values
            .OrderBy(configuration => configuration.GameId, StringComparer.Ordinal)
            .ThenBy(configuration => configuration.ProfileId, StringComparer.Ordinal)
            .ToArray());
    }

    public IReadOnlyList<LegacySessionLaunchConfiguration> Configurations { get; }

    public LegacySessionLaunchConfiguration Resolve(SessionSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (!_bySelection.TryGetValue(new LaunchKey(selection.GameId, selection.ProfileId), out var configuration))
        {
            if (_registeredGameIds.Contains(selection.GameId))
                throw new InvalidOperationException(
                    $"Legacy session selection '{selection.GameId}' requested profile '{selection.ProfileId}', " +
                    "but no registered host launch binding matches that profile.");
            throw new InvalidOperationException(
                $"Legacy session selection '{selection.GameId}' has no host launch binding.");
        }

        return configuration;
    }

    private readonly record struct LaunchKey(string GameId, string ProfileId);
}
