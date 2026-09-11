using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GEmuera.Core.Runtime;

namespace GEmuera.Core.Compatibility;

/// <summary>
/// A trusted, compile-time dialect module. Modules are composed explicitly; the
/// runtime never discovers assemblies or lets a later registration overwrite an
/// earlier one.
/// </summary>
public interface IDialectModule
{
    DialectModuleDefinition Definition { get; }
    IReadOnlyList<IDialectContribution> Contributions { get; }
}

public sealed class DialectModuleDefinition
{
    private readonly ReadOnlyCollection<ModuleDependencySnapshot> _dependencies;
    private readonly ReadOnlyCollection<string> _portTypeIds;

    public string ModuleId { get; }
    public string ModuleVersion { get; }
    public int ModuleApiVersion { get; }
    public IReadOnlyList<ModuleDependencySnapshot> Dependencies => _dependencies;
    public IReadOnlyList<string> PortTypeIds => _portTypeIds;

    public DialectModuleDefinition(
        string moduleId,
        string moduleVersion,
        int moduleApiVersion,
        IEnumerable<ModuleDependencySnapshot>? dependencies = null,
        IEnumerable<string>? portTypeIds = null)
    {
        ModuleId = ContractText.RequiredIdentifier(moduleId, nameof(moduleId));
        ModuleVersion = ContractText.RequiredSemanticVersion(moduleVersion, nameof(moduleVersion));
        if (moduleApiVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(moduleApiVersion), "Module API version must be positive.");

        _dependencies = ContractCollections.UniqueSorted(
            dependencies ?? Array.Empty<ModuleDependencySnapshot>(),
            dependency => dependency.ModuleId,
            StringComparer.Ordinal,
            nameof(dependencies));
        _portTypeIds = ContractCollections.UniqueSortedStrings(
            portTypeIds ?? Array.Empty<string>(),
            value => ContractText.RequiredTypeIdentifier(value, nameof(portTypeIds)),
            nameof(portTypeIds));
        ModuleApiVersion = moduleApiVersion;
    }

    public DialectModuleSnapshot ToSnapshot()
    {
        return new DialectModuleSnapshot(
            ModuleId,
            ModuleVersion,
            ModuleApiVersion,
            Dependencies,
            PortTypeIds);
    }
}

public interface IDialectContribution
{
    string ContributionId { get; }
}

public interface IInstructionContribution : IDialectContribution
{
    void Apply(InstructionRegistryBuilder builder);
}

public interface IFunctionContribution : IDialectContribution
{
    void Apply(FunctionRegistryBuilder builder);
}

public sealed record InstructionDescriptor(
    string Name,
    string Signature,
    string ModuleId,
    VmCompletionMode CompletionMode);

public sealed record FunctionDescriptor(
    string Name,
    string Signature,
    string ModuleId,
    string ReturnType);

public sealed class InstructionRegistryBuilder
{
    private readonly Dictionary<string, InstructionDescriptor> _entries = new(StringComparer.Ordinal);

    internal IReadOnlyCollection<InstructionDescriptor> Entries => _entries.Values;

    public void Register(InstructionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var key = ContractText.RequiredFunctionLookupKey(descriptor.Name, nameof(descriptor.Name));
        if (!string.Equals(key, descriptor.Name, StringComparison.Ordinal))
            descriptor = descriptor with { Name = key };
        if (!_entries.TryAdd(key, descriptor))
            throw new InvalidOperationException($"Duplicate instruction registration: {key}.");
    }

    internal IReadOnlyDictionary<string, InstructionDescriptor> Freeze()
    {
        return new ReadOnlyDictionary<string, InstructionDescriptor>(
            new Dictionary<string, InstructionDescriptor>(_entries, StringComparer.Ordinal));
    }
}

public sealed class FunctionRegistryBuilder
{
    private readonly Dictionary<string, FunctionDescriptor> _entries = new(StringComparer.Ordinal);

    internal IReadOnlyCollection<FunctionDescriptor> Entries => _entries.Values;

    public void Register(FunctionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var key = ContractText.RequiredFunctionLookupKey(descriptor.Name, nameof(descriptor.Name));
        if (!string.Equals(key, descriptor.Name, StringComparison.Ordinal))
            descriptor = descriptor with { Name = key };
        if (!_entries.TryAdd(key, descriptor))
            throw new InvalidOperationException($"Duplicate function registration: {key}.");
    }

    internal IReadOnlyDictionary<string, FunctionDescriptor> Freeze()
    {
        return new ReadOnlyDictionary<string, FunctionDescriptor>(
            new Dictionary<string, FunctionDescriptor>(_entries, StringComparer.Ordinal));
    }
}

public sealed class DialectModuleCatalog
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IDialectModule> _modules = new(StringComparer.Ordinal);
    private bool _frozen;

    /// <summary>
    /// Registration is allowed only while application composition is still in
    /// progress. Plans resolve an immutable snapshot of the registered module
    /// definitions and contribution collection.
    /// </summary>
    public bool IsFrozen
    {
        get
        {
            lock (_gate)
                return _frozen;
        }
    }

    public void Freeze()
    {
        lock (_gate)
            _frozen = true;
    }

    public void Register(IDialectModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        lock (_gate)
        {
            if (_frozen)
                throw new InvalidOperationException("Dialect module catalog is frozen.");

            var registered = RegisteredDialectModule.Create(module);
            var id = registered.Definition.ModuleId;
            if (!_modules.TryAdd(id, registered))
                throw new InvalidOperationException($"Duplicate dialect module: {id}.");
        }
    }

    public IReadOnlyList<IDialectModule> Resolve(IEnumerable<string> requestedModuleIds)
    {
        ArgumentNullException.ThrowIfNull(requestedModuleIds);
        var requested = requestedModuleIds
            .Select(value => ContractText.RequiredIdentifier(value, nameof(requestedModuleIds)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, IDialectModule> modules;
        lock (_gate)
            modules = new Dictionary<string, IDialectModule>(_modules, StringComparer.Ordinal);
        var ordered = new List<IDialectModule>();
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var moduleId in requested)
            Visit(moduleId, null);

        return new ReadOnlyCollection<IDialectModule>(ordered);

        void Visit(string moduleId, string? requiredBy)
        {
            if (visited.Contains(moduleId))
                return;
            if (!modules.TryGetValue(moduleId, out var module))
                throw new InvalidOperationException(
                    $"Dialect module '{moduleId}' was not registered" +
                    (requiredBy is null ? "." : $" (required by '{requiredBy}')."));
            if (!visiting.Add(moduleId))
                throw new InvalidOperationException($"Dialect module dependency cycle detected at '{moduleId}'.");

            foreach (var dependency in module.Definition.Dependencies.OrderBy(value => value.ModuleId, StringComparer.Ordinal))
            {
                Visit(dependency.ModuleId, moduleId);
                var dependencyModule = modules[dependency.ModuleId];
                if (!SemanticVersionRange.Contains(dependency.VersionRange, dependencyModule.Definition.ModuleVersion))
                {
                    throw new InvalidOperationException(
                        $"Module '{moduleId}' requires '{dependency.ModuleId}' in '{dependency.VersionRange}', " +
                        $"but '{dependencyModule.Definition.ModuleVersion}' is registered.");
                }
            }

            visiting.Remove(moduleId);
            visited.Add(moduleId);
            ordered.Add(module);
        }
    }

    private sealed class RegisteredDialectModule : IDialectModule
    {
        private readonly ReadOnlyCollection<IDialectContribution> _contributions;

        private RegisteredDialectModule(
            DialectModuleDefinition definition,
            IEnumerable<IDialectContribution> contributions)
        {
            Definition = definition;
            _contributions = new ReadOnlyCollection<IDialectContribution>(contributions.ToList());
        }

        public DialectModuleDefinition Definition { get; }
        public IReadOnlyList<IDialectContribution> Contributions => _contributions;

        public static RegisteredDialectModule Create(IDialectModule source)
        {
            var sourceDefinition = source.Definition
                ?? throw new ArgumentException("Dialect module definition must not be null.", nameof(source));
            var sourceContributions = source.Contributions
                ?? throw new ArgumentException("Dialect module contributions must not be null.", nameof(source));
            var contributions = sourceContributions.ToArray();
            if (contributions.Any(contribution => contribution is null))
                throw new ArgumentException("Dialect module contributions must not contain null.", nameof(source));

            var duplicateContribution = contributions
                .GroupBy(
                    contribution => ContractText.Required(contribution.ContributionId, nameof(source.Contributions)),
                    StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateContribution is not null)
            {
                throw new InvalidOperationException(
                    $"Duplicate dialect contribution: {duplicateContribution.Key} " +
                    $"in module '{sourceDefinition.ModuleId}'.");
            }

            var definition = new DialectModuleDefinition(
                sourceDefinition.ModuleId,
                sourceDefinition.ModuleVersion,
                sourceDefinition.ModuleApiVersion,
                sourceDefinition.Dependencies.Select(dependency => new ModuleDependencySnapshot(
                    dependency.ModuleId,
                    dependency.VersionRange)),
                sourceDefinition.PortTypeIds);
            return new RegisteredDialectModule(definition, contributions);
        }
    }
}

public sealed class DialectPlan
{
    private readonly ReadOnlyCollection<DialectModuleSnapshot> _modules;
    private readonly ReadOnlyCollection<BehaviorPortSnapshot> _ports;

    internal DialectPlan(
        IEnumerable<DialectModuleSnapshot> modules,
        IEnumerable<BehaviorPortSnapshot> ports,
        IReadOnlyDictionary<string, InstructionDescriptor> instructions,
        IReadOnlyDictionary<string, FunctionDescriptor> functions,
        string canonicalHash)
    {
        _modules = new ReadOnlyCollection<DialectModuleSnapshot>(modules.ToList());
        _ports = new ReadOnlyCollection<BehaviorPortSnapshot>(ports.ToList());
        Instructions = instructions;
        Functions = functions;
        CanonicalHash = ContractText.Required(canonicalHash, nameof(canonicalHash));
    }

    public IReadOnlyList<DialectModuleSnapshot> Modules => _modules;
    public IReadOnlyList<BehaviorPortSnapshot> Ports => _ports;
    public IReadOnlyDictionary<string, InstructionDescriptor> Instructions { get; }
    public IReadOnlyDictionary<string, FunctionDescriptor> Functions { get; }
    public string CanonicalHash { get; }

    public bool TryGetInstruction(string name, out InstructionDescriptor descriptor)
    {
        return Instructions.TryGetValue(ContractText.RequiredFunctionLookupKey(name, nameof(name)), out descriptor!);
    }

    public bool TryGetFunction(string name, out FunctionDescriptor descriptor)
    {
        return Functions.TryGetValue(ContractText.RequiredFunctionLookupKey(name, nameof(name)), out descriptor!);
    }
}

public sealed class CompatibilityPlan
{
    private readonly ReadOnlyCollection<string> _capabilityIds;

    internal CompatibilityPlan(
        string profileId,
        DialectPlan dialect,
        IEnumerable<string> capabilityIds,
        string? saveProfileId,
        string canonicalHash)
    {
        ProfileId = ContractText.RequiredIdentifier(profileId, nameof(profileId));
        Dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        _capabilityIds = ContractCollections.UniqueSortedStrings(
            capabilityIds,
            value => ContractText.RequiredVersionedIdentifier(value, nameof(capabilityIds)),
            nameof(capabilityIds));
        SaveProfileId = string.IsNullOrWhiteSpace(saveProfileId)
            ? null
            : ContractText.RequiredIdentifier(saveProfileId, nameof(saveProfileId));
        CanonicalHash = ContractText.Required(canonicalHash, nameof(canonicalHash));
    }

    public string ProfileId { get; }
    public DialectPlan Dialect { get; }
    public IReadOnlyList<string> CapabilityIds => _capabilityIds;
    public string? SaveProfileId { get; }
    public string CanonicalHash { get; }
}

public sealed class CompatibilityPlanBuilder
{
    private readonly DialectModuleCatalog _catalog;

    public CompatibilityPlanBuilder(DialectModuleCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _catalog.Freeze();
    }

    public CompatibilityPlan Build(
        string profileId,
        IEnumerable<string> requestedModuleIds,
        IEnumerable<BehaviorPortSnapshot>? ports = null,
        IEnumerable<string>? capabilityIds = null,
        string? saveProfileId = null)
    {
        var normalizedProfileId = ContractText.RequiredIdentifier(profileId, nameof(profileId));
        var modules = _catalog.Resolve(requestedModuleIds);
        var selectedPorts = (ports ?? Array.Empty<BehaviorPortSnapshot>()).ToArray();
        var normalizedCapabilities = ContractCollections.UniqueSortedStrings(
            capabilityIds ?? Array.Empty<string>(),
            value => ContractText.RequiredVersionedIdentifier(value, nameof(capabilityIds)),
            nameof(capabilityIds));
        var moduleSnapshots = modules.Select(module => module.Definition.ToSnapshot()).ToArray();
        var snapshot = CompatibilityPlanSnapshot.Create(normalizedProfileId, moduleSnapshots, selectedPorts, normalizedCapabilities, saveProfileId);

        var instructionBuilder = new InstructionRegistryBuilder();
        var functionBuilder = new FunctionRegistryBuilder();
        foreach (var module in modules)
        {
            foreach (var contribution in module.Contributions.OrderBy(value => value.ContributionId, StringComparer.Ordinal))
            {
                var instructionKeysBefore = instructionBuilder.Entries
                    .Select(value => value.Name)
                    .ToHashSet(StringComparer.Ordinal);
                var functionKeysBefore = functionBuilder.Entries
                    .Select(value => value.Name)
                    .ToHashSet(StringComparer.Ordinal);
                switch (contribution)
                {
                    case IInstructionContribution instruction:
                        instruction.Apply(instructionBuilder);
                        break;
                    case IFunctionContribution function:
                        function.Apply(functionBuilder);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported dialect contribution '{contribution.ContributionId}' " +
                            $"from module '{module.Definition.ModuleId}'.");
                }

                foreach (var descriptor in instructionBuilder.Entries.Where(value => !instructionKeysBefore.Contains(value.Name)))
                {
                    if (!string.Equals(descriptor.ModuleId, module.Definition.ModuleId, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            $"Instruction '{descriptor.Name}' is owned by '{descriptor.ModuleId}' but was contributed by '{module.Definition.ModuleId}'.");
                }
                foreach (var descriptor in functionBuilder.Entries.Where(value => !functionKeysBefore.Contains(value.Name)))
                {
                    if (!string.Equals(descriptor.ModuleId, module.Definition.ModuleId, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            $"Function '{descriptor.Name}' is owned by '{descriptor.ModuleId}' but was contributed by '{module.Definition.ModuleId}'.");
                }
            }
        }

        var instructions = instructionBuilder.Freeze();
        var functions = functionBuilder.Freeze();
        var selectedModuleIds = moduleSnapshots.Select(module => module.ModuleId).ToHashSet(StringComparer.Ordinal);
        foreach (var descriptor in instructions.Values)
        {
            if (!selectedModuleIds.Contains(descriptor.ModuleId))
                throw new InvalidOperationException($"Instruction '{descriptor.Name}' claims an unselected module '{descriptor.ModuleId}'.");
        }
        foreach (var descriptor in functions.Values)
        {
            if (!selectedModuleIds.Contains(descriptor.ModuleId))
                throw new InvalidOperationException($"Function '{descriptor.Name}' claims an unselected module '{descriptor.ModuleId}'.");
        }
        var dialectHash = ComputeDialectHash(snapshot.PlanSemanticHash, instructions, functions);
        var dialect = new DialectPlan(moduleSnapshots, selectedPorts, instructions, functions, dialectHash);
        var planHash = ComputePlanHash(normalizedProfileId, dialectHash, normalizedCapabilities, saveProfileId);
        return new CompatibilityPlan(normalizedProfileId, dialect, normalizedCapabilities, saveProfileId, planHash);
    }

    private static string ComputeDialectHash(
        string snapshotHash,
        IReadOnlyDictionary<string, InstructionDescriptor> instructions,
        IReadOnlyDictionary<string, FunctionDescriptor> functions)
    {
        var canonical = new StringBuilder().Append("snapshot=").Append(snapshotHash).Append('\n');
        foreach (var item in instructions.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            canonical.Append("instruction=").Append(item.Key).Append('|')
                .Append(item.Value.Signature).Append('|').Append(item.Value.ModuleId).Append('|')
                .Append(item.Value.CompletionMode).Append('\n');
        }
        foreach (var item in functions.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            canonical.Append("function=").Append(item.Key).Append('|')
                .Append(item.Value.Signature).Append('|').Append(item.Value.ModuleId).Append('|')
                .Append(item.Value.ReturnType).Append('\n');
        }
        return Hash(canonical.ToString());
    }

    private static string ComputePlanHash(
        string profileId,
        string dialectHash,
        IEnumerable<string> capabilityIds,
        string? saveProfileId)
    {
        var canonical = new StringBuilder()
            .Append("profile=").Append(profileId).Append('\n')
            .Append("dialect=").Append(dialectHash).Append('\n')
            .Append("save=").Append(saveProfileId ?? string.Empty).Append('\n');
        foreach (var capabilityId in capabilityIds.Order(StringComparer.Ordinal))
            canonical.Append("capability=").Append(capabilityId).Append('\n');
        return Hash(canonical.ToString());
    }

    private static string Hash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}

internal static class SemanticVersionRange
{
    private static readonly Regex RangePattern = new(
        "^(?<left>\\[|\\()(?<min>[0-9]+\\.[0-9]+\\.[0-9]+),(?<max>[0-9]+\\.[0-9]+\\.[0-9]+)(?<right>\\]|\\))$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool Contains(string range, string version)
    {
        if (string.Equals(range, version, StringComparison.Ordinal))
            return true;
        var match = RangePattern.Match(range);
        if (!match.Success || !Version.TryParse(version, out var actual) ||
            !Version.TryParse(match.Groups["min"].Value, out var min) ||
            !Version.TryParse(match.Groups["max"].Value, out var max))
            return false;

        var minComparison = actual.CompareTo(min);
        var maxComparison = actual.CompareTo(max);
        var minInclusive = match.Groups["left"].Value == "[";
        var maxInclusive = match.Groups["right"].Value == "]";
        return (minInclusive ? minComparison >= 0 : minComparison > 0) &&
            (maxInclusive ? maxComparison <= 0 : maxComparison < 0);
    }

    public static bool IsValid(string? range)
    {
        if (string.IsNullOrWhiteSpace(range))
            return false;
        if (System.Text.RegularExpressions.Regex.IsMatch(
                range,
                "^[0-9]+\\.[0-9]+\\.[0-9]+$",
                RegexOptions.CultureInvariant))
        {
            return true;
        }

        var match = RangePattern.Match(range);
        if (!match.Success ||
            !Version.TryParse(match.Groups["min"].Value, out var min) ||
            !Version.TryParse(match.Groups["max"].Value, out var max))
        {
            return false;
        }

        var comparison = min.CompareTo(max);
        if (comparison < 0)
            return true;
        if (comparison > 0)
            return false;

        return match.Groups["left"].Value == "[" && match.Groups["right"].Value == "]";
    }
}
