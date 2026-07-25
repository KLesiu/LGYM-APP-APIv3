using System.Xml.Linq;
using FluentAssertions;
using LgymApi.Domain.Enums;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class EnumResourceCoverageGuardTests
{
    private const string RepresentativeKey = "ExerciseEloFormula_PullupWeighted";

    private static readonly Type[] ExposedEnumTypes =
    [
        typeof(BodyParts),
        typeof(ExerciseEloFormula),
        typeof(MeasurementUnits),
        typeof(WeightUnits),
        typeof(HeightUnits),
        typeof(Platforms)
    ];

    [Test]
    public void Exposed_Enum_Members_Should_Have_English_And_Polish_Resource_Keys()
    {
        var missingKeys = FindMissingKeys(LoadResourceKeySets());

        missingKeys.Should().BeEmpty(
            "every exposed enum member, including hidden members, must have a conventional key in both enum resource files");
    }

    [TestCase("English")]
    [TestCase("Polish")]
    public void Coverage_Check_Should_Report_A_Missing_Culture_Key(string culture)
    {
        var keySets = LoadResourceKeySets()
            .Select(keySet => keySet.Culture == culture
                ? keySet with { Keys = keySet.Keys.Where(key => key != RepresentativeKey).ToHashSet(StringComparer.Ordinal) }
                : keySet)
            .ToArray();

        var missingKeys = FindMissingKeys(keySets);

        missingKeys.Should().ContainSingle()
            .Which.Should().Be($"{culture}: ExerciseEloFormula.PullupWeighted ({RepresentativeKey})");
    }

    private static ResourceKeySet[] LoadResourceKeySets()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var resourcesRoot = Path.Combine(repositoryRoot, "LgymApi.Resources", "Resources");

        return
        [
            LoadResourceKeySet("English", Path.Combine(resourcesRoot, "Enums.resx")),
            LoadResourceKeySet("Polish", Path.Combine(resourcesRoot, "Enums.pl.resx"))
        ];
    }

    private static ResourceKeySet LoadResourceKeySet(string culture, string path)
    {
        File.Exists(path).Should().BeTrue($"the {culture} enum resource file '{path}' must exist");

        var keys = XDocument.Load(path)
            .Descendants("data")
            .Select(element => element.Attribute("name")?.Value)
            .Where(key => !string.IsNullOrEmpty(key))
            .Select(key => key!)
            .ToHashSet(StringComparer.Ordinal);

        return new ResourceKeySet(culture, keys);
    }

    private static List<string> FindMissingKeys(IEnumerable<ResourceKeySet> resourceKeySets)
    {
        var missingKeys = new List<string>();

        foreach (var resourceKeySet in resourceKeySets)
        {
            foreach (var enumType in ExposedEnumTypes)
            {
                foreach (var memberName in System.Enum.GetNames(enumType))
                {
                    var key = $"{enumType.Name}_{memberName}";
                    if (!resourceKeySet.Keys.Contains(key))
                    {
                        missingKeys.Add($"{resourceKeySet.Culture}: {enumType.Name}.{memberName} ({key})");
                    }
                }
            }
        }

        return missingKeys;
    }

    private sealed record ResourceKeySet(string Culture, IReadOnlySet<string> Keys);
}
