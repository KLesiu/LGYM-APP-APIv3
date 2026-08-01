namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class FeatureFolderStructureGuardTests
{
    [Test]
    public void Features_With_Controllers_Should_Contain_Only_Contracts_Controllers_Validation_Leaf_Folders()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var featuresRoot = Path.Combine(repoRoot, "LgymApi.Api", "Features");

        Assert.That(
            Directory.Exists(featuresRoot),
            Is.True,
            $"Features root directory '{featuresRoot}' does not exist.");

        var featureDirectories = Directory
            .EnumerateDirectories(featuresRoot, "*", SearchOption.TopDirectoryOnly)
            .ToList();

        Assert.That(
            featureDirectories,
            Is.Not.Empty,
            $"No feature directories found in '{featuresRoot}'.");

        var expectedSubfolders = new HashSet<string>(StringComparer.Ordinal)
        {
            "Contracts",
            "Controllers",
            "Validation"
        };

        var rejectedAlternateLeafFolders = new HashSet<string>(StringComparer.Ordinal)
        {
            "Dtos",
            "Endpoints",
            "Validators"
        };

        var violations = new List<Violation>();

        foreach (var featureDirectory in featureDirectories)
        {
            var sourceFiles = Directory
                .EnumerateFiles(featureDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !ArchitectureTestHelpers.IsInBuildArtifacts(path))
                .ToList();

            var hasController = sourceFiles.Any(path => ArchitectureTestHelpers.IsApiFeatureLeafFilePath(path, "Controllers"));

            if (!hasController)
            {
                continue;
            }

            var featureDirectoriesRecursively = Directory
                .EnumerateDirectories(featureDirectory, "*", SearchOption.AllDirectories)
                .ToList();

            var actualLeafFolders = featureDirectoriesRecursively
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Where(name => expectedSubfolders.Contains(name))
                .ToHashSet(StringComparer.Ordinal);

            var nonLeafExpectedFolders = featureDirectoriesRecursively
                .Where(path => expectedSubfolders.Contains(Path.GetFileName(path) ?? string.Empty))
                .Where(path => Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly).Any())
                .Select(path => ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repoRoot, path)))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            var alternateLeafFolders = featureDirectoriesRecursively
                .Where(path => rejectedAlternateLeafFolders.Contains(Path.GetFileName(path) ?? string.Empty))
                .Select(path => ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repoRoot, path)))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            if (nonLeafExpectedFolders.Count > 0 || alternateLeafFolders.Count > 0 || actualLeafFolders.Count == 0)
            {
                var relativeFeaturePath = Path.GetRelativePath(repoRoot, featureDirectory);
                violations.Add(new Violation(relativeFeaturePath, expectedSubfolders, actualLeafFolders, nonLeafExpectedFolders, alternateLeafFolders));
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            "Features with controllers may use nested slice containers, but any API leaf folders must still be named 'Contracts', 'Controllers', or 'Validation' and remain terminal directories. Violations count: " + violations.Count + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
    }

    private sealed record Violation(
        string FeaturePath,
        HashSet<string> Expected,
        HashSet<string> Actual,
        IReadOnlyList<string> NonLeafExpectedFolders,
        IReadOnlyList<string> AlternateLeafFolders)
    {
        public override string ToString()
        {
            var expected = string.Join(", ", Expected.OrderBy(x => x, StringComparer.Ordinal));
            var actual = string.Join(", ", Actual.OrderBy(x => x, StringComparer.Ordinal));
            var nonLeafExpectedFolders = NonLeafExpectedFolders.Count == 0 ? "none" : string.Join(", ", NonLeafExpectedFolders);
            var alternateLeafFolders = AlternateLeafFolders.Count == 0 ? "none" : string.Join(", ", AlternateLeafFolders);
            return $"{FeaturePath} [expected leaf names: {expected}] [actual leaf names: {actual}] [non-leaf expected folders: {nonLeafExpectedFolders}] [alternate leaf folders: {alternateLeafFolders}]";
        }
    }
}
