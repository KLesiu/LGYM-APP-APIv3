using System.Xml;
using System.Xml.Linq;
using static LgymApi.E2ETests.Harness.StandaloneBoundaryPolicy;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("Boundary")]
public sealed class StandaloneBoundaryTests
{
    private const string E2ETestsProjectName = "LgymApi.E2ETests";
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

}
