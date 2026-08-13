using System.Text.RegularExpressions;
using LgymApi.E2ETests.Harness;

namespace LgymApi.E2ETests.PublicHttpGiven;

[TestFixture]
[Category("WebHarness")]
public sealed class PublicHttpGivenSourcePolicyTests
{
    private static readonly BoundaryRule[] ForbiddenRules =
    [
        new("product namespace", @"(?:using\s+|global::)LgymApi\.(Api|Application|Domain|Infrastructure|Identity|Platform|TrainingPlanning|Notifications|BackgroundWorker)"),
        new("Entity Framework", @"(?:using\s+|global::)?Microsoft\.EntityFrameworkCore|\bDbContext\b"),
        new("Npgsql", @"(?:using\s+|global::)?Npgsql|\bNpgsqlConnection\b"),
        new("repository", @"\b\w*Repository\b"),
        new("in-process host", @"\bWebApplicationFactory\b"),
        new("container persistence", @"\bTestcontainers\b"),
        new("SQL", @"\b(SELECT|INSERT|UPDATE|DELETE|ALTER|DROP|CREATE)\s+", RegexOptions.IgnoreCase),
        new("test endpoint", @"(?:api/(?:internal|test)|proof/|test-only)", RegexOptions.IgnoreCase)
    ];

    [Test]
    public void Given_source_uses_only_package_owned_public_HTTP_boundaries()
    {
        // Given
        var givenDirectory = Path.Combine(RepositoryRoot.Find(), "LgymApi.E2ETests", "Given");

        // When
        var violations = Directory.EnumerateFiles(givenDirectory, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => FindViolations(path, File.ReadAllText(path)))
            .ToArray();

        // Then
        Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
    }

    [TestCase("using LgymApi.Api;")]
    [TestCase("global::LgymApi.Infrastructure")]
    [TestCase("using Microsoft.EntityFrameworkCore;")]
    [TestCase("global::Microsoft.EntityFrameworkCore.DbContext")]
    [TestCase("using Npgsql;")]
    [TestCase("AccountRepository")]
    [TestCase("WebApplicationFactory")]
    [TestCase("Testcontainers")]
    [TestCase("SELECT * FROM users")]
    [TestCase("api/internal/example")]
    public void Given_source_policy_rejects_each_forbidden_boundary_category(string unsafeFixture)
    {
        // Given
        var violations = FindViolations("unsafe.cs", unsafeFixture).ToArray();

        // Then
        Assert.That(violations, Is.Not.Empty);
    }

    private static IEnumerable<string> FindViolations(string path, string source) =>
        ForbiddenRules
            .Where(rule => rule.Pattern.IsMatch(source))
            .Select(rule => $"{Path.GetFileName(path)} violates public-HTTP boundary rule '{rule.Name}'.");

    private sealed record BoundaryRule
    {
        internal BoundaryRule(string name, string pattern, RegexOptions options = RegexOptions.None)
        {
            Name = name;
            Pattern = new Regex(pattern, RegexOptions.CultureInvariant | options);
        }

        internal string Name { get; }

        internal Regex Pattern { get; }
    }
}
