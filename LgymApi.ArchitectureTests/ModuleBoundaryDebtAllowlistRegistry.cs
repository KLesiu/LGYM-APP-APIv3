namespace LgymApi.ArchitectureTests;

public static class ModuleBoundaryDebtAllowlistRegistry
{
    public const int MaximumAllowedEntryCount = 0;

    private static readonly IReadOnlyList<ModuleBoundaryDebtEntry> Entries =
    [];

    public static IReadOnlyList<ModuleBoundaryDebtEntry> AllEntries
    {
        get
        {
            AssertRegistryDoesNotGrow();
            return Entries;
        }
    }

    public static IReadOnlyList<ModuleBoundaryDebtEntry> GetEntriesForGuard(string guardId)
    {
        AssertRegistryDoesNotGrow();
        var normalizedGuardId = ModuleBoundaryDebtKey.NormalizeRequiredValue(guardId, nameof(guardId));

        return Entries
            .Where(entry => entry.Key.GuardId.Equals(normalizedGuardId, StringComparison.Ordinal))
            .ToList();
    }

    public static ModuleBoundaryDebtAllowlistEvaluation Evaluate(string guardId, IEnumerable<ModuleBoundaryObservedViolation> observedViolations)
    {
        ArgumentNullException.ThrowIfNull(observedViolations);

        var normalizedGuardId = ModuleBoundaryDebtKey.NormalizeRequiredValue(guardId, nameof(guardId));
        var allowlistedEntries = GetEntriesForGuard(normalizedGuardId);

        return ModuleBoundaryDebtAllowlistEvaluator.Evaluate(allowlistedEntries, observedViolations, normalizedGuardId);
    }

    public static void AssertNoUnexpectedViolations(string guardId, IEnumerable<ModuleBoundaryObservedViolation> observedViolations)
    {
        var evaluation = Evaluate(guardId, observedViolations);
        if (evaluation.IsSuccess)
        {
            return;
        }

        throw new AssertionException(evaluation.BuildFailureMessage());
    }

    private static void AssertRegistryDoesNotGrow()
    {
        if (Entries.Count <= MaximumAllowedEntryCount)
        {
            return;
        }

        throw new AssertionException(
            $"Module-boundary debt allowlist contains {Entries.Count} entries, but the approved baseline is {MaximumAllowedEntryCount}; debt must not grow.");
    }
}

public static class ModuleBoundaryDebtAllowlistEvaluator
{
    public static ModuleBoundaryDebtAllowlistEvaluation Evaluate(
        IEnumerable<ModuleBoundaryDebtEntry> allowlistedEntries,
        IEnumerable<ModuleBoundaryObservedViolation> observedViolations,
        string? guardId = null)
        => EvaluateCore(
            allowlistedEntries,
            observedViolations,
            guardId,
            ModuleBoundaryDebtAllowlistRegistry.MaximumAllowedEntryCount);

    internal static ModuleBoundaryDebtAllowlistEvaluation EvaluateForTesting(
        IEnumerable<ModuleBoundaryDebtEntry> allowlistedEntries,
        IEnumerable<ModuleBoundaryObservedViolation> observedViolations,
        string? guardId,
        int maximumAllowedEntryCount)
        => EvaluateCore(allowlistedEntries, observedViolations, guardId, maximumAllowedEntryCount);

    private static ModuleBoundaryDebtAllowlistEvaluation EvaluateCore(
        IEnumerable<ModuleBoundaryDebtEntry> allowlistedEntries,
        IEnumerable<ModuleBoundaryObservedViolation> observedViolations,
        string? guardId,
        int maximumAllowedEntryCount)
    {
        ArgumentNullException.ThrowIfNull(allowlistedEntries);
        ArgumentNullException.ThrowIfNull(observedViolations);

        var entries = allowlistedEntries.ToList();
        var observed = observedViolations.ToList();
        var normalizedGuardId = NormalizeGuardId(guardId, entries, observed);

        ValidateEntries(entries, normalizedGuardId, maximumAllowedEntryCount);
        ValidateObservedViolations(observed, normalizedGuardId);

        var matchedObservedKeys = observed
            .Select(violation => violation.IdentityKey)
            .ToHashSet(StringComparer.Ordinal);

        var unexpectedViolations = observed
            .Where(violation => entries.All(entry => !entry.Matches(violation)))
            .OrderBy(violation => violation.IdentityKey, StringComparer.Ordinal)
            .ToList();

        var staleEntries = entries
            .Where(entry => !matchedObservedKeys.Contains(entry.IdentityKey))
            .OrderBy(entry => entry.IdentityKey, StringComparer.Ordinal)
            .ToList();

        return new ModuleBoundaryDebtAllowlistEvaluation(normalizedGuardId, unexpectedViolations, staleEntries);
    }

    private static void ValidateEntries(
        IReadOnlyCollection<ModuleBoundaryDebtEntry> entries,
        string normalizedGuardId,
        int maximumAllowedEntryCount)
    {
        if (entries.Count > maximumAllowedEntryCount)
        {
            throw new AssertionException(
                $"Module-boundary debt allowlist for guard '{normalizedGuardId}' contains {entries.Count} entries, but the approved baseline is {maximumAllowedEntryCount}; debt must not grow.");
        }

        var broadEntries = entries
            .Where(entry => entry.Key.ContainsWildcardIdentityValue())
            .Select(entry => entry.Key.ToDisplayString())
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();

        if (broadEntries.Count > 0)
        {
            throw new AssertionException(
                $"Module-boundary debt allowlist for guard '{normalizedGuardId}' contains wildcard entries. Allowlist entries must identify one exact current violation:{Environment.NewLine}" +
                string.Join(Environment.NewLine, broadEntries));
        }

        var duplicateEntryKeys = entries
            .GroupBy(entry => entry.Key.NormalizedKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        if (duplicateEntryKeys.Count > 0)
        {
            throw new AssertionException(
                $"Module-boundary debt allowlist for guard '{normalizedGuardId}' contains duplicate exact entries:{Environment.NewLine}" +
                string.Join(Environment.NewLine, duplicateEntryKeys));
        }

        var duplicateIdentityKeys = entries
            .GroupBy(entry => entry.IdentityKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        if (duplicateIdentityKeys.Count > 0)
        {
            throw new AssertionException(
                $"Module-boundary debt allowlist for guard '{normalizedGuardId}' contains duplicate identity matches. Keep one exact entry per live violation:{Environment.NewLine}" +
                string.Join(Environment.NewLine, duplicateIdentityKeys));
        }
    }

    private static void ValidateObservedViolations(IReadOnlyCollection<ModuleBoundaryObservedViolation> observedViolations, string normalizedGuardId)
    {
        var invalidGuardViolations = observedViolations
            .Where(violation => !violation.GuardId.Equals(normalizedGuardId, StringComparison.Ordinal))
            .Select(violation => violation.IdentityKey)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        if (invalidGuardViolations.Count > 0)
        {
            throw new AssertionException(
                $"Observed module-boundary violations must use the requested guard id '{normalizedGuardId}':{Environment.NewLine}" +
                string.Join(Environment.NewLine, invalidGuardViolations));
        }
    }

    private static string NormalizeGuardId(
        string? guardId,
        IReadOnlyCollection<ModuleBoundaryDebtEntry> entries,
        IReadOnlyCollection<ModuleBoundaryObservedViolation> observedViolations)
    {
        if (!string.IsNullOrWhiteSpace(guardId))
        {
            return ModuleBoundaryDebtKey.NormalizeRequiredValue(guardId, nameof(guardId));
        }

        var discoveredGuardId = entries.Select(entry => entry.Key.GuardId)
            .Concat(observedViolations.Select(violation => violation.GuardId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return discoveredGuardId.Count switch
        {
            0 => throw new AssertionException("Module-boundary debt allowlist evaluation requires a guard id when no entries or observed violations are present."),
            1 => discoveredGuardId[0],
            _ => throw new AssertionException(
                "Module-boundary debt allowlist evaluation requires exactly one guard id. Found:" + Environment.NewLine +
                string.Join(Environment.NewLine, discoveredGuardId.OrderBy(id => id, StringComparer.Ordinal)))
        };
    }
}

public sealed record ModuleBoundaryDebtAllowlistEvaluation(
    string GuardId,
    IReadOnlyList<ModuleBoundaryObservedViolation> UnexpectedViolations,
    IReadOnlyList<ModuleBoundaryDebtEntry> StaleEntries)
{
    public bool IsSuccess => UnexpectedViolations.Count == 0 && StaleEntries.Count == 0;

    public string BuildFailureMessage()
    {
        var sections = new List<string>();

        if (UnexpectedViolations.Count > 0)
        {
            sections.Add(
                "New module-boundary violations must be fixed or explicitly allowlisted as exact current debt:" + Environment.NewLine +
                string.Join(Environment.NewLine + Environment.NewLine, UnexpectedViolations.Select(violation => violation.ToString())));
        }

        if (StaleEntries.Count > 0)
        {
            sections.Add(
                "Stale module-boundary allowlist entries must be removed once the live violation disappears:" + Environment.NewLine +
                string.Join(Environment.NewLine + Environment.NewLine, StaleEntries.Select(entry => entry.ToString())));
        }

        return $"Module-boundary shrink-only debt allowlist failed for guard '{GuardId}'.{Environment.NewLine}" +
               string.Join(Environment.NewLine + Environment.NewLine, sections);
    }
}

public sealed record ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey Key)
{
    public string IdentityKey => Key.IdentityKey;

    public bool Matches(ModuleBoundaryObservedViolation observedViolation)
    {
        ArgumentNullException.ThrowIfNull(observedViolation);
        return IdentityKey.Equals(observedViolation.IdentityKey, StringComparison.Ordinal);
    }

    public override string ToString() => Key.ToDisplayString();
}

public sealed record ModuleBoundaryObservedViolation(
    string GuardId,
    string SourceModule,
    string TargetModule,
    string SourceSymbolOrPath,
    string TargetSymbolOrPath)
{
    public string IdentityKey => ModuleBoundaryDebtKey.BuildIdentityKey(GuardId, SourceModule, TargetModule, SourceSymbolOrPath, TargetSymbolOrPath);

    public override string ToString()
    {
        return $"Rule: {GuardId}{Environment.NewLine}" +
               $"Source module: {SourceModule}{Environment.NewLine}" +
               $"Target module: {TargetModule}{Environment.NewLine}" +
               $"Source symbol/file: {SourceSymbolOrPath}{Environment.NewLine}" +
               $"Target symbol/file: {TargetSymbolOrPath}";
    }
}

public sealed record ModuleBoundaryDebtKey(
    string GuardId,
    string SourceModule,
    string TargetModule,
    string SourceSymbolOrPath,
    string TargetSymbolOrPath,
    string Rationale)
{
    public string IdentityKey => BuildIdentityKey(GuardId, SourceModule, TargetModule, SourceSymbolOrPath, TargetSymbolOrPath);

    public string NormalizedKey => $"{IdentityKey}|rationale:{Rationale}";

    public string ToDisplayString()
    {
        return $"Rule: {GuardId}{Environment.NewLine}" +
               $"Source module: {SourceModule}{Environment.NewLine}" +
               $"Target module: {TargetModule}{Environment.NewLine}" +
               $"Source symbol/file: {SourceSymbolOrPath}{Environment.NewLine}" +
               $"Target symbol/file: {TargetSymbolOrPath}{Environment.NewLine}" +
               $"Rationale: {Rationale}";
    }

    public static ModuleBoundaryDebtKey Create(
        string guardId,
        string sourceModule,
        string targetModule,
        string sourceSymbolOrPath,
        string targetSymbolOrPath,
        string rationale)
    {
        return new ModuleBoundaryDebtKey(
            NormalizeRequiredExactIdentityValue(guardId, nameof(guardId)),
            NormalizeRequiredExactIdentityValue(sourceModule, nameof(sourceModule)),
            NormalizeRequiredExactIdentityValue(targetModule, nameof(targetModule)),
            NormalizeRequiredExactPathOrSymbol(sourceSymbolOrPath, nameof(sourceSymbolOrPath)),
            NormalizeRequiredExactPathOrSymbol(targetSymbolOrPath, nameof(targetSymbolOrPath)),
            NormalizeRequiredValue(rationale, nameof(rationale)));
    }

    public static string BuildIdentityKey(
        string guardId,
        string sourceModule,
        string targetModule,
        string sourceSymbolOrPath,
        string targetSymbolOrPath)
    {
        return string.Join(
            "|",
            $"guard:{NormalizeRequiredExactIdentityValue(guardId, nameof(guardId))}",
            $"source-module:{NormalizeRequiredExactIdentityValue(sourceModule, nameof(sourceModule))}",
            $"target-module:{NormalizeRequiredExactIdentityValue(targetModule, nameof(targetModule))}",
            $"source:{NormalizeRequiredExactPathOrSymbol(sourceSymbolOrPath, nameof(sourceSymbolOrPath))}",
            $"target:{NormalizeRequiredExactPathOrSymbol(targetSymbolOrPath, nameof(targetSymbolOrPath))}");
    }

    public static string NormalizeRequiredPathOrSymbol(string value, string paramName)
    {
        return NormalizeRequiredValue(ArchitectureTestHelpers.NormalizePath(value), paramName);
    }

    internal bool ContainsWildcardIdentityValue()
    {
        return ContainsWildcard(GuardId) ||
               ContainsWildcard(SourceModule) ||
               ContainsWildcard(TargetModule) ||
               ContainsWildcard(SourceSymbolOrPath) ||
               ContainsWildcard(TargetSymbolOrPath);
    }

    private static string NormalizeRequiredExactPathOrSymbol(string value, string paramName)
    {
        return NormalizeRequiredExactIdentityValue(ArchitectureTestHelpers.NormalizePath(value), paramName);
    }

    private static string NormalizeRequiredExactIdentityValue(string value, string paramName)
    {
        var normalizedValue = NormalizeRequiredValue(value, paramName);
        if (ContainsWildcard(normalizedValue))
        {
            throw new ArgumentException($"{paramName} must identify one exact violation and cannot contain wildcards.", paramName);
        }

        return normalizedValue;
    }

    private static bool ContainsWildcard(string value)
    {
        return value.Contains('*') || value.Contains('?');
    }

    public static string NormalizeRequiredValue(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value.Trim();
    }
}
