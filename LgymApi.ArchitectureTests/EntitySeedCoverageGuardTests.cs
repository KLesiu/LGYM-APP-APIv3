using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class EntitySeedCoverageGuardTests
{
    [Test]
    public void Every_Entity_Should_Have_A_Corresponding_Seeder()
    {
        var repoRoot = ResolveRepositoryRoot();
        var entitiesRoot = Path.Combine(repoRoot, "LgymApi.Domain", "Entities");
        var seedersRoot = Path.Combine(repoRoot, "LgymApi.DataSeeder", "Seeders");

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(entitiesRoot), Is.True, $"Entities root '{entitiesRoot}' not found.");
            Assert.That(Directory.Exists(seedersRoot), Is.True, $"Seeders root '{seedersRoot}' not found.");
        });

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var entityFiles = Directory
            .EnumerateFiles(entitiesRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => !IsInBuildArtifacts(path))
            .ToList();

        Assert.That(entityFiles, Is.Not.Empty, "No entity files found in Domain/Entities.");

        var entities = entityFiles
            .SelectMany(path => ExtractEntityNames(path, parseOptions))
            .Where(name => !string.Equals(name, "EntityBase", StringComparison.Ordinal))
            // CommandEnvelope, ActionExecutionLog, and ApiIdempotencyRecord are runtime orchestration artifacts, not fixture data
            .Where(name => !string.Equals(name, "CommandEnvelope", StringComparison.Ordinal)
                       && !string.Equals(name, "ActionExecutionLog", StringComparison.Ordinal)
                       && !string.Equals(name, "ApiIdempotencyRecord", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(entities, Is.Not.Empty, "No entities detected in Domain/Entities.");

        var seederFiles = Directory
            .EnumerateFiles(seedersRoot, "*Seeder.cs", SearchOption.TopDirectoryOnly)
            .Where(path => !IsInBuildArtifacts(path))
            .ToList();

        Assert.That(seederFiles, Is.Not.Empty, "No seeders detected in DataSeeder/Seeders.");

        var seederNames = seederFiles
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name != null && name.EndsWith("Seeder", StringComparison.Ordinal))
            .Select(name => name![..^"Seeder".Length])
            .ToHashSet(StringComparer.Ordinal);

        var missing = entities
            .Where(entity => !seederNames.Contains(entity))
            .OrderBy(entity => entity, StringComparer.Ordinal)
            .ToList();

        Assert.That(
            missing,
            Is.Empty,
            "Every entity must have a corresponding seeder class. Missing: " +
            string.Join(", ", missing));
    }

    private static IEnumerable<string> ExtractEntityNames(string path, CSharpParseOptions parseOptions)
    {
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path);
        var root = tree.GetCompilationUnitRoot();
        return root
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Where(type => !type.Modifiers.Any(modifier => Microsoft.CodeAnalysis.CSharpExtensions.IsKind(modifier, SyntaxKind.AbstractKeyword)))
            .Where(type => type.BaseList != null
                           && type.BaseList.Types.Any(baseType => baseType.ToString().Contains("EntityBase", StringComparison.Ordinal)))
            .Select(type => type.Identifier.ValueText);
    }

    private static bool IsInBuildArtifacts(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LgymApi.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}
