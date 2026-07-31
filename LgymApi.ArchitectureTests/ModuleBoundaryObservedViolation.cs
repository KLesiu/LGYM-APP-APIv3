namespace LgymApi.ArchitectureTests;

public sealed record ModuleBoundaryObservedViolation(
    string GuardId,
    string SourceModule,
    string TargetModule,
    string SourceSymbolOrPath,
    string TargetSymbolOrPath)
{
    public string IdentityKey => string.Join(
        "|",
        $"guard:{NormalizeRequiredValue(GuardId, nameof(GuardId))}",
        $"source-module:{NormalizeRequiredValue(SourceModule, nameof(SourceModule))}",
        $"target-module:{NormalizeRequiredValue(TargetModule, nameof(TargetModule))}",
        $"source:{NormalizeRequiredPathOrSymbol(SourceSymbolOrPath, nameof(SourceSymbolOrPath))}",
        $"target:{NormalizeRequiredPathOrSymbol(TargetSymbolOrPath, nameof(TargetSymbolOrPath))}");

    public static string DescribeAll(IEnumerable<ModuleBoundaryObservedViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);

        var observed = violations
            .OrderBy(violation => violation.IdentityKey, StringComparer.Ordinal)
            .ToArray();

        return observed.Length == 0
            ? "Expected zero module-boundary violations."
            : $"Expected zero module-boundary violations, but observed {observed.Length}:{Environment.NewLine}" +
              string.Join(Environment.NewLine + Environment.NewLine, observed.Select(violation => violation.ToString()));
    }

    public override string ToString()
    {
        return $"Rule: {GuardId}{Environment.NewLine}" +
               $"Source module: {SourceModule}{Environment.NewLine}" +
               $"Target module: {TargetModule}{Environment.NewLine}" +
               $"Source symbol/file: {SourceSymbolOrPath}{Environment.NewLine}" +
               $"Target symbol/file: {TargetSymbolOrPath}";
    }

    private static string NormalizeRequiredPathOrSymbol(string value, string paramName)
        => NormalizeRequiredValue(ArchitectureTestHelpers.NormalizePath(value), paramName);

    private static string NormalizeRequiredValue(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value.Trim();
    }
}
