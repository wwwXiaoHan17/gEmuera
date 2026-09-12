using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace GEmuera.Core.Compatibility;

public sealed class CompatibilityPlanSnapshot
{
    public const string SchemaVersion = "1.0.0";

    private readonly ReadOnlyCollection<DialectModuleSnapshot> _modules;
    private readonly ReadOnlyCollection<BehaviorPortSnapshot> _ports;
    private readonly ReadOnlyCollection<string> _capabilityIds;

    public string ProfileId { get; }
    public IReadOnlyList<DialectModuleSnapshot> Modules => _modules;
    public IReadOnlyList<BehaviorPortSnapshot> Ports => _ports;
    public IReadOnlyList<string> CapabilityIds => _capabilityIds;
    public string? SaveProfileId { get; }
    public string PlanSemanticHash { get; }

    private CompatibilityPlanSnapshot(
        string profileId,
        ReadOnlyCollection<DialectModuleSnapshot> modules,
        ReadOnlyCollection<BehaviorPortSnapshot> ports,
        ReadOnlyCollection<string> capabilityIds,
        string? saveProfileId)
    {
        ProfileId = ContractText.RequiredIdentifier(profileId, nameof(profileId));
        _modules = modules;
        _ports = ports;
        _capabilityIds = capabilityIds;
        SaveProfileId = string.IsNullOrWhiteSpace(saveProfileId) ? null : ContractText.RequiredIdentifier(saveProfileId, nameof(saveProfileId));
        PlanSemanticHash = ComputeSemanticHash();
    }

    public static CompatibilityPlanSnapshot Create(
        string profileId,
        IEnumerable<DialectModuleSnapshot> modules,
        IEnumerable<BehaviorPortSnapshot> ports,
        IEnumerable<string>? capabilityIds = null,
        string? saveProfileId = null)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(ports);

        var moduleList = ContractCollections.UniqueSorted(modules, module => module.ModuleId, StringComparer.Ordinal, nameof(modules));
        var moduleById = moduleList.ToDictionary(module => module.ModuleId, StringComparer.Ordinal);
        foreach (var module in moduleList)
        {
            foreach (var dependency in module.Dependencies)
            {
                if (!moduleById.TryGetValue(dependency.ModuleId, out var dependencyModule))
                    throw new ArgumentException($"Module '{module.ModuleId}' references missing dependency '{dependency.ModuleId}'.", nameof(modules));
                if (!SemanticVersionRange.Contains(dependency.VersionRange, dependencyModule.ModuleVersion))
                {
                    throw new ArgumentException(
                        $"Module '{module.ModuleId}' requires '{dependency.ModuleId}' in '{dependency.VersionRange}', " +
                        $"but snapshot version '{dependencyModule.ModuleVersion}' was supplied.",
                        nameof(modules));
                }
            }
        }
        EnsureAcyclicDependencies(moduleList);

        var portList = ContractCollections.UniqueSorted(ports, port => port.PortTypeId, StringComparer.Ordinal, nameof(ports));
        var duplicateBehaviorKey = portList
            .GroupBy(port => port.BehaviorKeyId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateBehaviorKey is not null)
            throw new ArgumentException($"Duplicate behavior key: {duplicateBehaviorKey.Key}", nameof(ports));

        foreach (var port in portList)
        {
            if (!moduleById.TryGetValue(port.TargetModuleId, out var owner))
                throw new ArgumentException($"Port '{port.PortTypeId}' targets missing module '{port.TargetModuleId}'.", nameof(ports));
            if (!owner.PortTypeIds.Contains(port.PortTypeId, StringComparer.Ordinal))
                throw new ArgumentException($"Module '{owner.ModuleId}' does not declare port '{port.PortTypeId}'.", nameof(ports));
        }

        var capabilities = ContractCollections.UniqueSortedStrings(
            capabilityIds ?? Array.Empty<string>(),
            value => ContractText.RequiredVersionedIdentifier(value, nameof(capabilityIds)),
            nameof(capabilityIds));
        return new CompatibilityPlanSnapshot(profileId, moduleList, portList, capabilities, saveProfileId);
    }

    private string ComputeSemanticHash()
    {
        var canonical = new StringBuilder();
        canonical.Append("schema=").Append(SchemaVersion).Append('\n');
        canonical.Append("profile=").Append(ProfileId).Append('\n');
        canonical.Append("save=").Append(SaveProfileId ?? string.Empty).Append('\n');
        foreach (var module in _modules)
        {
            canonical.Append("module=").Append(module.ModuleId).Append('|')
                .Append(module.ModuleVersion).Append('|').Append(module.ModuleApiVersion).Append('\n');
            foreach (var dependency in module.Dependencies)
                canonical.Append("dependency=").Append(module.ModuleId).Append('|').Append(dependency.ModuleId).Append('|').Append(dependency.VersionRange).Append('\n');
            foreach (var portTypeId in module.PortTypeIds)
                canonical.Append("module-port=").Append(module.ModuleId).Append('|').Append(portTypeId).Append('\n');
        }
        foreach (var port in _ports)
        {
            canonical.Append("port=").Append(port.BehaviorKeyId).Append('|').Append(port.PortTypeId).Append('|')
                .Append(port.ContractKind).Append('|').Append(port.DecisionOwner).Append('|').Append(port.ConsumerContractId).Append('|')
                .Append(port.TargetModuleId).Append('|').Append(port.FixtureId).Append('\n');
        }
        foreach (var capabilityId in _capabilityIds)
            canonical.Append("capability=").Append(capabilityId).Append('\n');

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static void EnsureAcyclicDependencies(IReadOnlyList<DialectModuleSnapshot> modules)
    {
        var moduleById = modules.ToDictionary(module => module.ModuleId, StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var module in modules)
        {
            Visit(module.ModuleId);
        }

        void Visit(string moduleId)
        {
            if (visited.Contains(moduleId))
                return;
            if (!visiting.Add(moduleId))
                throw new ArgumentException($"Module dependency cycle detected at '{moduleId}'.", nameof(modules));

            foreach (var dependency in moduleById[moduleId].Dependencies)
                Visit(dependency.ModuleId);

            visiting.Remove(moduleId);
            visited.Add(moduleId);
        }
    }
}

internal static class ContractText
{
    public static string Required(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must not be empty.", parameterName);
        return value.Trim();
    }

    public static string RequiredLookupKey(string? value, string parameterName)
    {
        var normalized = Required(value, parameterName);
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, "^[A-Z][A-Z0-9_]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new ArgumentException($"Invalid lookup key: {normalized}", parameterName);
        return normalized;
    }

    /// <summary>
    /// 方言标识符键的宽松校验：旧核心的注册表包含 CJK 标识符（如 snake 系的
    /// 陥落状態/陷落状态 兜底函数，它同时以 METHOD 投影出现在指令表面），
    /// 只要求"字母（含 Unicode）开头 + 字母/数字/下划线"。指令与函数两个注册器
    /// 共用本变体；RequiredLookupKey 保留给纯枚举名场景。
    /// </summary>
    public static string RequiredFunctionLookupKey(string? value, string parameterName)
    {
        var normalized = Required(value, parameterName);
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, "^[\\p{Lu}\\p{Ll}\\p{Lo}][\\p{Lu}\\p{Ll}\\p{Lo}0-9_]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new ArgumentException($"Invalid function lookup key: {normalized}", parameterName);
        return normalized;
    }

    public static string RequiredIdentifier(string? value, string parameterName)
    {
        var normalized = Required(value, parameterName);
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, "^[a-z][a-z0-9.\\-]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new ArgumentException($"Invalid identifier: {normalized}", parameterName);
        return normalized;
    }

    public static string RequiredVersionedIdentifier(string? value, string parameterName)
    {
        var normalized = Required(value, parameterName);
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, "^[a-z][a-z0-9.\\-]*\\.v[0-9]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new ArgumentException($"Invalid versioned identifier: {normalized}", parameterName);
        return normalized;
    }

    public static string RequiredTypeIdentifier(string? value, string parameterName)
    {
        var normalized = Required(value, parameterName);
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, "^I[A-Za-z][A-Za-z0-9]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new ArgumentException($"Invalid port type identifier: {normalized}", parameterName);
        return normalized;
    }

    public static string RequiredFixtureId(string? value, string parameterName)
    {
        var normalized = Required(value, parameterName);
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, "^DIA-[A-Z0-9-]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new ArgumentException($"Invalid fixture identifier: {normalized}", parameterName);
        return normalized;
    }

    public static string RequiredSemanticVersion(string? value, string parameterName)
    {
        var normalized = Required(value, parameterName);
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, "^[0-9]+\\.[0-9]+\\.[0-9]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new ArgumentException($"Invalid semantic version: {normalized}", parameterName);
        return normalized;
    }

    public static string RequiredVersionRange(string? value, string parameterName)
    {
        var normalized = Required(value, parameterName);
        if (!SemanticVersionRange.IsValid(normalized))
            throw new ArgumentException($"Invalid semantic version range: {normalized}", parameterName);
        return normalized;
    }
}

internal static class ContractCollections
{
    public static ReadOnlyCollection<T> UniqueSorted<T>(
        IEnumerable<T> values,
        Func<T, string> keySelector,
        StringComparer comparer,
        string parameterName)
    {
        var result = values.ToList();
        if (result.Any(value => value is null))
            throw new ArgumentException("Collection contains null.", parameterName);
        var duplicate = result.GroupBy(keySelector, comparer).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate collection key: {duplicate.Key}", parameterName);
        result.Sort((left, right) => comparer.Compare(keySelector(left), keySelector(right)));
        return new ReadOnlyCollection<T>(result);
    }

    public static ReadOnlyCollection<string> UniqueSortedStrings(
        IEnumerable<string> values,
        Func<string, string> normalizer,
        string parameterName)
    {
        var rawValues = values.ToList();
        if (rawValues.Any(value => value is null))
            throw new ArgumentException("Collection contains null.", parameterName);
        var normalized = rawValues.Select(normalizer).ToList();
        var duplicate = normalized.GroupBy(value => value, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate collection value: {duplicate.Key}", parameterName);
        normalized.Sort(StringComparer.Ordinal);
        return new ReadOnlyCollection<string>(normalized);
    }
}
