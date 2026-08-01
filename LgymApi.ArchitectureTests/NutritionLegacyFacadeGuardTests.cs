using System.Text.RegularExpressions;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class NutritionLegacyFacadeGuardTests
{
    private static readonly string[] RetiredSymbols =
    [
        "IDietPlanService",
        "DietPlanService",
        "ISupplementationService",
        "SupplementationService",
        "IDietPlanRepository",
        "DietPlanRepository",
        "ISupplementationRepository",
        "SupplementationRepository"
    ];

    [Test]
    public void Legacy_Nutrition_Facade_And_Repository_Paths_Should_Not_Exist()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var retiredDirectories = new[]
        {
            "LgymApi.Application/Features/DietPlans",
            "LgymApi.Application/Features/Supplementation"
        };

        var remainingLegacyFiles = retiredDirectories
            .Where(path => Directory.Exists(Path.Combine(root, path)))
            .SelectMany(path => Directory.EnumerateFiles(Path.Combine(root, path), "*.cs", SearchOption.AllDirectories))
            .Select(path => ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(root, path)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var references = ArchitectureTestHelpers.EnumerateProductionSourceFiles("LgymApi.Application", "LgymApi.Api", "LgymApi.Infrastructure")
            .Select(path => (Path: ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(root, path)), Source: File.ReadAllText(path)))
            .SelectMany(file => RetiredSymbols
                .Where(symbol => Regex.IsMatch(file.Source, $@"\b{symbol}\b", RegexOptions.CultureInvariant))
                .Select(symbol => $"{file.Path}: {symbol}"))
            .OrderBy(reference => reference, StringComparer.Ordinal)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(remainingLegacyFiles, Is.Empty, "Retired Nutrition feature directories must not contain compatibility facade source files.");
            Assert.That(references, Is.Empty, "Nutrition production code must use focused contracts and module-local persistence ports instead of retired facades.");
        });
    }
}
