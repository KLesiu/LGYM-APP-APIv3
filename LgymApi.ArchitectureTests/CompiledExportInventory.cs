using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LgymApi.ArchitectureTests;

internal static class CompiledExportInventory
{
    internal const int SchemaVersion = 1;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static CompiledExportInventoryDocument Create(IEnumerable<System.Reflection.Assembly> assemblies)
    {
        var entries = assemblies
            .Select(assembly => new CompiledExportAssembly(
                assembly.GetName().Name ?? throw new InvalidOperationException("Compiled assembly has no simple name."),
                assembly.GetExportedTypes()
                    .Select(type => type.FullName ?? throw new InvalidOperationException($"Exported type in '{assembly.FullName}' has no metadata name."))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray()))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();

        var duplicateAssemblies = entries.GroupBy(entry => entry.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateAssemblies.Length > 0)
        {
            throw new InvalidOperationException($"Duplicate compiled export inventory assembly names: {string.Join(", ", duplicateAssemblies)}.");
        }

        return new CompiledExportInventoryDocument(SchemaVersion, entries);
    }

    internal static string Serialize(CompiledExportInventoryDocument inventory)
        => JsonSerializer.Serialize(inventory, JsonOptions);

    internal static CompiledExportInventoryDocument Deserialize(string json)
        => JsonSerializer.Deserialize<CompiledExportInventoryDocument>(json, JsonOptions)
           ?? throw new InvalidOperationException("Compiled export inventory JSON was empty.");

    internal static void AssertExactExports(
        CompiledExportAssembly observed,
        IEnumerable<string> expectedExports)
    {
        var expected = expectedExports.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var missing = expected.Except(observed.ExportedTypes, StringComparer.Ordinal).ToArray();
        var unlisted = observed.ExportedTypes.Except(expected, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0 || unlisted.Length > 0)
        {
            throw new InvalidOperationException(
                $"Compiled export inventory mismatch for '{observed.Name}': missing=[{string.Join(", ", missing)}]; "
                + $"unlisted=[{string.Join(", ", unlisted)}].");
        }
    }

    internal static void AssertNoForbiddenExportIdentities(CompiledExportAssembly assembly)
    {
        var violations = assembly.ExportedTypes
            .Where(metadataName => IsForbiddenExportIdentity(assembly.Name, metadataName))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (violations.Length > 0)
        {
            throw new InvalidOperationException(
                $"Compiled export inventory contains forbidden implementation, provider, persistence, Worker, Task7, or Compatibility identities in '{assembly.Name}': "
                + string.Join(", ", violations));
        }
    }

    private static bool IsForbiddenExportIdentity(string assemblyName, string metadataName)
        => (assemblyName != "LgymApi.Platform"
               && metadataName.StartsWith("LgymApi.Infrastructure.", StringComparison.Ordinal))
           || metadataName.StartsWith("LgymApi.BackgroundWorker.", StringComparison.Ordinal)
           || metadataName.StartsWith("LgymApi.BackgroundWorker.Common.", StringComparison.Ordinal)
           || metadataName.Contains(".Task7", StringComparison.Ordinal)
           || metadataName.Contains(".Compatibility.", StringComparison.Ordinal)
           || metadataName.Contains(".Providers.", StringComparison.Ordinal)
           || (metadataName.EndsWith("Repository", StringComparison.Ordinal)
               && !metadataName[(metadataName.LastIndexOf('.') + 1)..].StartsWith("I", StringComparison.Ordinal))
           || metadataName.EndsWith("PersistenceRepository", StringComparison.Ordinal)
           || metadataName.EndsWith("ServiceDependencies", StringComparison.Ordinal)
           || metadataName == "LgymApi.Application.Services.LegacyPasswordServiceFactory";

    internal sealed record CompiledExportInventoryDocument(
        int SchemaVersion,
        IReadOnlyList<CompiledExportAssembly> Assemblies)
    {
        public int TotalExportedTypeCount => Assemblies.Sum(assembly => assembly.ExportedTypes.Count);
    }

    internal sealed record CompiledExportAssembly(string Name, IReadOnlyList<string> ExportedTypes)
    {
        public int ExportedTypeCount => ExportedTypes.Count;

        public string Sha256 => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", ExportedTypes) + "\n"))).ToLowerInvariant();
    }
}
