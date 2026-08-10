using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("Boundary")]
public sealed class StandaloneBoundaryTests
{
    private const string E2ETestsProjectName = "LgymApi.E2ETests";
    private const string E2ETestsProjectPath = "LgymApi.E2ETests/LgymApi.E2ETests.csproj";
    private const string MainSolutionName = "LgymApi.sln";
    private const string StandaloneSolutionName = "LgymApi.E2ETests.sln";

    private static readonly string[] PrivateArtifactSamples =
    [
        ".e2e-private/runs/example.txt",
        "LgymApi.E2ETests/.playwright/state.json",
        "LgymApi.E2ETests/ms-playwright/chromium",
        "LgymApi.E2ETests/playwright-report/index.html",
        "LgymApi.E2ETests/test-results/result.xml",
        "LgymApi.E2ETests/traces/trace.zip",
        "LgymApi.E2ETests/screenshots/failure.png",
        "LgymApi.E2ETests/reports/report.html",
        "LgymApi.E2ETests/runtime/api.log"
    ];

    [Test]
    public void Repository_root_must_contain_both_solution_files()
    {
        var repositoryRoot = RepositoryRoot.Find();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(repositoryRoot, MainSolutionName)), Is.True);
            Assert.That(File.Exists(Path.Combine(repositoryRoot, StandaloneSolutionName)), Is.True);
        });
    }

    [Test]
    public void Standalone_solution_must_contain_only_the_E2E_project()
    {
        var repositoryRoot = RepositoryRoot.Find();

        AssertStandaloneSolution(
            repositoryRoot,
            Path.Combine(repositoryRoot, StandaloneSolutionName));
    }

    [Test]
    public void Standalone_project_must_remain_package_only_and_target_net10()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var projectPath = Path.Combine(repositoryRoot, ToHostPath(E2ETestsProjectPath));
        var project = ParseProject(projectPath);
        var evaluatedProjectReferences = ParseEvaluatedProjectReferences(
            Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "project.assets.json"));

        Assert.Multiple(() =>
        {
            Assert.That(project.TargetFrameworks, Is.EqualTo(new[] { "net10.0" }));
            Assert.That(project.ProjectReferences, Is.Empty, "Standalone project must have zero direct ProjectReference items.");
            Assert.That(
                evaluatedProjectReferences,
                Is.Empty,
                "Standalone project must have zero evaluated ProjectReference items.");
            Assert.That(project.InlinePackageVersions, Is.Empty, "Standalone project must not define inline package versions.");
        });
    }

    [Test]
    public void Main_solution_must_not_include_the_E2E_project()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var mainSolutionProjects = ParseSolutionProjects(Path.Combine(repositoryRoot, MainSolutionName));
        var relativeProjectPaths = mainSolutionProjects
            .Select(projectPath => NormalizePath(Path.GetRelativePath(repositoryRoot, projectPath)))
            .ToArray();

        Assert.That(relativeProjectPaths, Does.Not.Contain(E2ETestsProjectPath));
    }

    [Test]
    public void Main_solution_topology_must_remain_at_18_projects_and_90_direct_edges()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var mainSolutionProjects = ParseSolutionProjects(Path.Combine(repositoryRoot, MainSolutionName));
        var directEdges = mainSolutionProjects
            .SelectMany(projectPath => ParseProject(projectPath).ProjectReferences)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(mainSolutionProjects.Count, Is.EqualTo(18));
            Assert.That(mainSolutionProjects.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(18));
            Assert.That(directEdges.Length, Is.EqualTo(90));
        });
    }

    [Test]
    public void Standalone_solution_parser_rejects_a_second_project()
    {
        using var fixture = new TemporaryFixture();
        var solutionPath = fixture.Write(
            "standalone.sln",
            CreateSolution(E2ETestsProjectPath, "Another.Tests/Another.Tests.csproj"));

        var exception = Assert.Throws<MultipleAssertException>(() => AssertStandaloneSolution(fixture.Path, solutionPath));

        Assert.That(exception!.Message, Does.Contain("exactly one project"));
    }

    [Test]
    public void Standalone_solution_parser_rejects_a_duplicate_project()
    {
        using var fixture = new TemporaryFixture();
        var solutionPath = fixture.Write(
            "standalone.sln",
            CreateSolution(E2ETestsProjectPath, E2ETestsProjectPath));

        var exception = Assert.Throws<MultipleAssertException>(() => AssertStandaloneSolution(fixture.Path, solutionPath));

        Assert.That(exception!.Message, Does.Contain("exactly one project"));
    }

    [Test]
    public void Project_parser_rejects_a_ProjectReference()
    {
        using var fixture = new TemporaryFixture();
        var projectPath = fixture.Write(
            "fixture.csproj",
            "<Project><ItemGroup><ProjectReference Include=\"../Other/Other.csproj\" /></ItemGroup></Project>");
        var project = ParseProject(projectPath);

        var exception = Assert.Throws<AssertionException>(() =>
            Assert.That(project.ProjectReferences, Is.Empty, "Standalone project must have zero direct ProjectReference items."));

        Assert.That(exception!.Message, Does.Contain("zero direct ProjectReference"));
    }

    [Test]
    public void Main_solution_membership_parser_rejects_the_E2E_project()
    {
        using var fixture = new TemporaryFixture();
        var solutionPath = fixture.Write("main.sln", CreateSolution(E2ETestsProjectPath));
        var projectPaths = ParseSolutionProjects(solutionPath)
            .Select(projectPath => NormalizePath(Path.GetRelativePath(fixture.Path, projectPath)))
            .ToArray();

        var exception = Assert.Throws<AssertionException>(() =>
            Assert.That(projectPaths, Does.Not.Contain(E2ETestsProjectPath)));

        Assert.That(exception!.Message, Does.Contain(E2ETestsProjectPath));
    }

    [Test]
    public void Project_parser_rejects_malformed_project_XML()
    {
        using var fixture = new TemporaryFixture();
        var projectPath = fixture.Write("malformed.csproj", "<Project><PropertyGroup></Project>");

        var exception = Assert.Throws<XmlException>(() => ParseProject(projectPath));

        Assert.That(exception!.Message, Is.Not.Empty);
    }

    [Test]
    public void Project_parser_rejects_an_inline_package_version()
    {
        using var fixture = new TemporaryFixture();
        var projectPath = fixture.Write(
            "inline-version.csproj",
            "<Project><ItemGroup><PackageReference Include=\"NUnit\" Version=\"4.0.0\" /></ItemGroup></Project>");
        var project = ParseProject(projectPath);

        var exception = Assert.Throws<AssertionException>(() =>
            Assert.That(project.InlinePackageVersions, Is.Empty, "Standalone project must not define inline package versions."));

        Assert.That(exception!.Message, Does.Contain("inline package versions"));
    }

    [Test]
    public void Solution_parser_rejects_malformed_project_declarations()
    {
        using var fixture = new TemporaryFixture();
        var solutionPath = fixture.Write("malformed.sln", "Project(\"type\") = \"only-two-fields\"");

        var exception = Assert.Throws<InvalidOperationException>(() => ParseSolutionProjects(solutionPath));

        Assert.That(exception!.Message, Does.Contain("Malformed solution project declaration"));
        Assert.That(exception.Message, Does.Contain(solutionPath));
    }

    [Test]
    public void E2E_safe_configuration_file_must_be_trackable()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var gitIgnore = File.ReadAllText(Path.Combine(repositoryRoot, ".gitignore"));

        Assert.That(
            IsIgnored(gitIgnore, "LgymApi.E2ETests/appsettings.E2E.json"),
            Is.False,
            "Safe E2E configuration must be trackable.");
    }

    [Test]
    public void E2E_private_browser_and_report_artifacts_must_be_ignored()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var gitIgnore = File.ReadAllText(Path.Combine(repositoryRoot, ".gitignore"));
        var unignoredSamples = PrivateArtifactSamples
            .Where(path => !IsIgnored(gitIgnore, path))
            .ToArray();

        Assert.That(
            unignoredSamples,
            Is.Empty,
            "Private E2E browser and report artifacts must be ignored:" + Environment.NewLine +
            string.Join(Environment.NewLine, unignoredSamples));
    }

    [Test]
    public void Root_build_policy_must_classify_E2E_as_a_test_project()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var rootBuildPolicy = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Build.props"));
        var classifiesE2ETests = rootBuildPolicy
            .Descendants()
            .Where(element => element.Name.LocalName == "IsTestProject")
            .Where(element => element.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("Condition")?.Value)
            .Any(condition => condition?.Contains(E2ETestsProjectName, StringComparison.Ordinal) == true);

        Assert.That(
            classifiesE2ETests,
            Is.True,
            "Root build policy must classify LgymApi.E2ETests as a test project.");
    }

    private static void AssertStandaloneSolution(string repositoryRoot, string solutionPath)
    {
        var projectPaths = ParseSolutionProjects(solutionPath);
        var relativeProjectPaths = projectPaths
            .Select(projectPath => NormalizePath(Path.GetRelativePath(repositoryRoot, projectPath)))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(projectPaths, Has.Count.EqualTo(1), "Standalone solution must contain exactly one project.");
            Assert.That(relativeProjectPaths, Is.EqualTo(new[] { E2ETestsProjectPath }));
        });
    }

    private static IReadOnlyList<string> ParseSolutionProjects(string solutionPath)
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath)
            ?? throw new InvalidOperationException($"Solution path '{solutionPath}' has no parent directory.");
        var projectPaths = new List<string>();
        var lineNumber = 0;

        foreach (var line in File.ReadLines(solutionPath))
        {
            lineNumber++;
            var trimmedLine = line.TrimStart();

            if (!trimmedLine.StartsWith("Project(", StringComparison.Ordinal))
            {
                continue;
            }

            var fields = trimmedLine.Split('"');
            if (fields.Length <= 5 || !fields[5].EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Malformed solution project declaration at '{solutionPath}' line {lineNumber}.");
            }

            projectPaths.Add(Path.GetFullPath(Path.Combine(solutionDirectory, ToHostPath(fields[5]))));
        }

        return projectPaths;
    }

    private static ParsedProject ParseProject(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        var targetFrameworks = document
            .Descendants()
            .Where(element => element.Name.LocalName == "TargetFramework")
            .Select(element => element.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var projectReferences = document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? "<missing Include>")
            .ToArray();
        var inlinePackageVersions = document
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Where(HasInlineVersion)
            .Select(element => element.Attribute("Include")?.Value ?? "<unknown package>")
            .ToArray();

        return new ParsedProject(targetFrameworks, projectReferences, inlinePackageVersions);
    }

    private static bool HasInlineVersion(XElement packageReference)
    {
        return !string.IsNullOrWhiteSpace(packageReference.Attribute("Version")?.Value) ||
               packageReference.Elements().Any(element =>
                   element.Name.LocalName == "Version" && !string.IsNullOrWhiteSpace(element.Value));
    }

    private static IReadOnlyList<string> ParseEvaluatedProjectReferences(string assetsPath)
    {
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        if (!assets.RootElement.TryGetProperty("libraries", out var libraries))
        {
            throw new InvalidOperationException($"NuGet assets file '{assetsPath}' has no libraries object.");
        }

        return libraries
            .EnumerateObject()
            .Where(library =>
                library.Value.TryGetProperty("type", out var type) &&
                type.GetString() == "project")
            .Select(library => library.Name)
            .ToArray();
    }

    private static bool IsIgnored(string gitIgnore, string relativePath)
    {
        var ignored = false;

        foreach (var rule in ParseIgnoreRules(gitIgnore))
        {
            if (Matches(rule, relativePath))
            {
                ignored = !rule.IsNegated;
            }
        }

        return ignored;
    }

    private static IEnumerable<IgnoreRule> ParseIgnoreRules(string gitIgnore)
    {
        using var reader = new StringReader(gitIgnore);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            var value = line.Trim();
            if (value.Length == 0 || value.StartsWith('#'))
            {
                continue;
            }

            var isNegated = value.StartsWith('!');
            var pattern = (isNegated ? value[1..] : value).Replace('\\', '/');
            if (pattern.Length != 0)
            {
                yield return new IgnoreRule(pattern, isNegated);
            }
        }
    }

    private static bool Matches(IgnoreRule rule, string relativePath)
    {
        var path = NormalizePath(relativePath);
        var pattern = rule.Pattern.TrimStart('/');
        var isDirectoryPattern = pattern.EndsWith('/');
        pattern = pattern.TrimEnd('/');

        if (pattern.Length == 0)
        {
            return false;
        }

        if (isDirectoryPattern)
        {
            if (pattern.Contains('/'))
            {
                return path.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith(pattern + "/", StringComparison.OrdinalIgnoreCase);
            }

            return path.Split('/').Any(segment => GlobMatches(pattern, segment));
        }

        return pattern.Contains('/')
            ? GlobMatches(pattern, path)
            : GlobMatches(pattern, Path.GetFileName(path));
    }

    private static bool GlobMatches(string pattern, string value)
    {
        var expression = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(value, expression, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static string CreateSolution(params string[] projectPaths)
    {
        var projectEntries = projectPaths
            .Select((projectPath, index) =>
                $"Project(\"type\") = \"project-{index}\", \"{projectPath.Replace('/', '\\')}\", \"id-{index}\"{Environment.NewLine}EndProject");

        return string.Join(Environment.NewLine, projectEntries);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static string ToHostPath(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    private sealed record ParsedProject(
        IReadOnlyList<string> TargetFrameworks,
        IReadOnlyList<string> ProjectReferences,
        IReadOnlyList<string> InlinePackageVersions);

    private sealed record IgnoreRule(string Pattern, bool IsNegated);

    private sealed class TemporaryFixture : IDisposable
    {
        public TemporaryFixture()
        {
            Path = Directory.CreateTempSubdirectory("lgym-e2e-boundary-").FullName;
        }

        public string Path { get; }

        public string Write(string relativePath, string content)
        {
            var path = System.IO.Path.Combine(Path, ToHostPath(relativePath));
            var directory = System.IO.Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException($"Fixture path '{relativePath}' has no parent directory.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
