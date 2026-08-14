using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ExecutionPolicy")]
public sealed class ExecutionPolicyTests
{
    private const string ProjectPath = "LgymApi.E2ETests/LgymApi.E2ETests.csproj";
    private const string RunSettingsPath = "LgymApi.E2ETests/LgymApi.E2ETests.runsettings";
    private const string ReqnrollPath = "LgymApi.E2ETests/reqnroll.json";
    private const string AssemblyInfoPath = "LgymApi.E2ETests/Properties/AssemblyInfo.cs";

    [Test]
    public void Committed_runsettings_and_reqnroll_configuration_must_enforce_serial_execution()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var runSettings = ParseRunSettings(Path.Combine(repositoryRoot, RunSettingsPath));
        var reqnroll = ParseReqnrollPolicy(Path.Combine(repositoryRoot, ReqnrollPath));

        Assert.Multiple(() =>
        {
            Assert.That(runSettings.TestSessionTimeout, Is.EqualTo(900000));
            Assert.That(runSettings.NumberOfTestWorkers, Is.EqualTo(0));
            Assert.That(reqnroll.Schema, Is.EqualTo("https://schemas.reqnroll.net/reqnroll-config-latest.json"));
            Assert.That(reqnroll.FeatureLanguage, Is.EqualTo("en-US"));
            Assert.That(reqnroll.NonParallelizableTags, Does.Contain("serial"));
            Assert.That(reqnroll.MissingOrPendingStepsOutcome, Is.EqualTo("Error"));
            Assert.That(reqnroll.StopAtFirstError, Is.True);
        });
    }

    [Test]
    public void Project_must_evaluate_the_committed_runsettings_path_and_intermediate_codebehind_setting()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var projectPath = Path.Combine(repositoryRoot, ProjectPath);
        var project = ParseProjectPolicy(projectPath);
        var expectedRunSettingsPath = Path.GetFullPath(Path.Combine(repositoryRoot, RunSettingsPath));
        var evaluatedRunSettingsPath = EvaluateRunSettingsFilePath(projectPath, repositoryRoot);

        Assert.Multiple(() =>
        {
            Assert.That(project.RunSettingsFilePath, Is.EqualTo("$(MSBuildProjectDirectory)\\LgymApi.E2ETests.runsettings"));
            Assert.That(project.UseIntermediateOutputPathForCodeBehind, Is.True);
            Assert.That(evaluatedRunSettingsPath, Is.EqualTo(expectedRunSettingsPath));
        });
    }

    [Test]
    public void Compiled_assembly_must_be_nonparallel_with_a_single_level_of_parallelism()
    {
        var policy = ParseAssemblyPolicy(typeof(ExecutionPolicyTests).Assembly);

        Assert.Multiple(() =>
        {
            Assert.That(policy.LevelOfParallelism, Is.EqualTo(1));
            Assert.That(policy.IsNonParallelizable, Is.True);
        });
    }

    [Test]
    public void Codebehind_must_remain_untracked_and_outside_the_intermediate_output_directory()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var tracked = RunProcess("git", ["ls-files", "--", "*.feature.cs"], repositoryRoot);
        var generatedOutsideObj = Directory
            .EnumerateFiles(repositoryRoot, "*.feature.cs", SearchOption.AllDirectories)
            .Where(path => !IsInsideObjDirectory(repositoryRoot, path))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(tracked.ExitCode, Is.EqualTo(0), tracked.StandardError);
            Assert.That(SplitLines(tracked.StandardOutput), Is.Empty);
            Assert.That(generatedOutsideObj, Is.Empty);
        });
    }

    [Test]
    public void Runsettings_parser_rejects_a_nonzero_worker_count()
    {
        using var fixture = new TemporaryFixture();
        var path = fixture.Write(
            "LgymApi.E2ETests.runsettings",
            "<RunSettings><RunConfiguration><TestSessionTimeout>900000</TestSessionTimeout></RunConfiguration><NUnit><NumberOfTestWorkers>2</NumberOfTestWorkers></NUnit></RunSettings>");

        var exception = Assert.Throws<AssertionException>(() => AssertRunSettingsPolicy(ParseRunSettings(path)));

        Assert.That(exception!.Message, Does.Contain("NumberOfTestWorkers"));
    }

    [Test]
    public void Project_policy_rejects_a_missing_runsettings_path()
    {
        using var fixture = new TemporaryFixture();
        var path = fixture.Write(
            "fixture.csproj",
            "<Project><PropertyGroup><ReqnrollUseIntermediateOutputPathForCodeBehind>true</ReqnrollUseIntermediateOutputPathForCodeBehind></PropertyGroup></Project>");

        var exception = Assert.Throws<AssertionException>(() => AssertProjectPolicy(ParseProjectPolicy(path)));

        Assert.That(exception!.Message, Does.Contain("RunSettingsFilePath"));
    }

    [Test]
    public void Assembly_info_policy_rejects_a_missing_nonparallelizable_attribute()
    {
        using var fixture = new TemporaryFixture();
        var path = fixture.Write(
            "Properties/AssemblyInfo.cs",
            "using NUnit.Framework;\n[assembly: LevelOfParallelism(1)]\n");

        var exception = Assert.Throws<AssertionException>(() => AssertAssemblyPolicy(ParseAssemblyInfoPolicy(path)));

        Assert.That(exception!.Message, Does.Contain("NonParallelizable"));
    }

    [Test]
    public void Reqnroll_policy_rejects_an_absent_serial_tag_mapping()
    {
        using var fixture = new TemporaryFixture();
        var path = fixture.Write(
            "reqnroll.json",
            "{\"$schema\":\"https://schemas.reqnroll.net/reqnroll-config-latest.json\",\"language\":{\"feature\":\"en-US\"},\"generator\":{\"addNonParallelizableMarkerForTags\":[\"nonparallel\"]},\"runtime\":{\"missingOrPendingStepsOutcome\":\"Error\",\"stopAtFirstError\":true}}");

        var exception = Assert.Throws<AssertionException>(() => AssertReqnrollPolicy(ParseReqnrollPolicy(path)));

        Assert.That(exception!.Message, Does.Contain("serial"));
    }

    [Test]
    [Category("Lifecycle")]
    public void Compiled_test_inventory_requires_nonempty_disjoint_serial_categories_without_parallel_markers()
    {
        var inventory = DiscoverTestInventory(typeof(ExecutionPolicyTests).Assembly);

        AssertCategoryPolicy(inventory);
        Assert.That(inventory.Count(entry => entry.Categories.Contains("HarnessDocker")), Is.GreaterThan(0));
        Assert.That(inventory.Count(entry => entry.Categories.Contains("Lifecycle")), Is.GreaterThan(0));
    }

    [Test]
    [Category("Lifecycle")]
    public void Category_policy_rejects_empty_overlap_and_parallel_fixtures()
    {
        var fixtures = new[]
        {
            Array.Empty<CategoryInventoryEntry>(),
            new[] { new CategoryInventoryEntry("overlap", ["HarnessDocker", "Lifecycle"], false) },
            new[] { new CategoryInventoryEntry("parallel", ["HarnessDocker"], true) }
        };

        foreach (var fixture in fixtures)
        {
            Assert.Throws<AssertionException>(() => AssertCategoryPolicy(fixture));
        }
    }

    private static CategoryInventoryEntry[] DiscoverTestInventory(Assembly assembly) => assembly
        .GetTypes()
        .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(method => (type, method)))
        .Where(candidate => candidate.method.GetCustomAttributes(inherit: true).Any(attribute =>
            attribute is TestAttribute or TestCaseAttribute or TestCaseSourceAttribute))
        .Select(candidate => new CategoryInventoryEntry(
            candidate.type.FullName + "." + candidate.method.Name,
            candidate.method.GetCustomAttributes<CategoryAttribute>(inherit: true)
                .Select(category => category.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            candidate.method.GetCustomAttributes<ParallelizableAttribute>(inherit: true).Any(),
            candidate.type.Namespace?.Contains(".Features", StringComparison.Ordinal) == true))
        .ToArray();

    private static void AssertCategoryPolicy(IReadOnlyList<CategoryInventoryEntry> inventory)
    {
        var harnessDocker = inventory.Where(entry => entry.Categories.Contains("HarnessDocker")).ToArray();
        var lifecycle = inventory.Where(entry => entry.Categories.Contains("Lifecycle")).ToArray();

        Assert.That(harnessDocker, Is.Not.Empty, "HarnessDocker inventory must be nonempty.");
        Assert.That(lifecycle, Is.Not.Empty, "Lifecycle inventory must be nonempty.");
        Assert.That(
            harnessDocker.Select(entry => entry.Name).Intersect(lifecycle.Select(entry => entry.Name), StringComparer.Ordinal),
            Is.Empty,
            "HarnessDocker and Lifecycle inventories must be disjoint.");
        Assert.That(inventory.Where(entry => entry.Parallelizable && !entry.GeneratedReqnroll), Is.Empty,
            "No hand-written test may carry a Parallelizable marker.");
        Assert.That(harnessDocker.Where(entry => entry.Parallelizable), Is.Empty,
            "HarnessDocker tests may not carry a Parallelizable marker.");
    }

    private static RunSettingsPolicy ParseRunSettings(string path)
    {
        var document = XDocument.Load(path);
        return new RunSettingsPolicy(
            ParseInt(document, "TestSessionTimeout"),
            ParseInt(document, "NumberOfTestWorkers"));
    }

    private static ReqnrollPolicy ParseReqnrollPolicy(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var runtime = root.GetProperty("runtime");

        return new ReqnrollPolicy(
            root.GetProperty("$schema").GetString(),
            root.GetProperty("language").GetProperty("feature").GetString(),
            root.GetProperty("generator").GetProperty("addNonParallelizableMarkerForTags")
                .EnumerateArray()
                .Select(tag => tag.GetString())
                .ToArray(),
            runtime.GetProperty("missingOrPendingStepsOutcome").GetString(),
            runtime.GetProperty("stopAtFirstError").GetBoolean());
    }

    private static ProjectPolicy ParseProjectPolicy(string path)
    {
        var document = XDocument.Load(path);
        return new ProjectPolicy(
            FindProperty(document, "RunSettingsFilePath"),
            bool.TryParse(FindProperty(document, "ReqnrollUseIntermediateOutputPathForCodeBehind"), out var enabled) && enabled);
    }

    private static AssemblyPolicy ParseAssemblyPolicy(Assembly assembly)
    {
        var attributes = assembly.CustomAttributes;
        var level = attributes.SingleOrDefault(attribute => attribute.AttributeType == typeof(LevelOfParallelismAttribute));
        return new AssemblyPolicy(
            level?.ConstructorArguments.Single().Value as int?,
            attributes.Any(attribute => attribute.AttributeType == typeof(NonParallelizableAttribute)));
    }

    private static AssemblyPolicy ParseAssemblyInfoPolicy(string path)
    {
        var source = File.ReadAllText(path);
        var levelMatch = Regex.Match(source, @"LevelOfParallelism\s*\(\s*(?<level>\d+)\s*\)");
        int? level = levelMatch.Success ? int.Parse(levelMatch.Groups["level"].Value) : null;
        return new AssemblyPolicy(level, source.Contains("[assembly: NonParallelizable]", StringComparison.Ordinal));
    }

    private static void AssertRunSettingsPolicy(RunSettingsPolicy policy)
    {
        Assert.That(policy.TestSessionTimeout, Is.EqualTo(900000));
        Assert.That(policy.NumberOfTestWorkers, Is.EqualTo(0));
    }

    private static void AssertProjectPolicy(ProjectPolicy policy)
    {
        Assert.That(policy.RunSettingsFilePath, Is.EqualTo("$(MSBuildProjectDirectory)\\LgymApi.E2ETests.runsettings"));
        Assert.That(policy.UseIntermediateOutputPathForCodeBehind, Is.True);
    }

    private static void AssertAssemblyPolicy(AssemblyPolicy policy)
    {
        Assert.That(policy.LevelOfParallelism, Is.EqualTo(1));
        Assert.That(policy.IsNonParallelizable, Is.True);
    }

    private static void AssertReqnrollPolicy(ReqnrollPolicy policy)
    {
        Assert.That(policy.NonParallelizableTags, Does.Contain("serial"));
        Assert.That(policy.MissingOrPendingStepsOutcome, Is.EqualTo("Error"));
        Assert.That(policy.StopAtFirstError, Is.True);
    }

    private static string EvaluateRunSettingsFilePath(string projectPath, string repositoryRoot)
    {
        var result = RunProcess("dotnet", ["msbuild", projectPath, "-nologo", "-getProperty:RunSettingsFilePath"], repositoryRoot);
        Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);

        var output = result.StandardOutput.Trim();
        var value = output.StartsWith('{')
            ? ReadRunSettingsFilePathFromJson(output)
            : output;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("MSBuild did not emit the evaluated RunSettingsFilePath property.");
        }

        return Path.GetFullPath(value, Path.GetDirectoryName(projectPath)!);
    }

    private static string? ReadRunSettingsFilePathFromJson(string output)
    {
        using var document = JsonDocument.Parse(output);
        return document.RootElement
            .GetProperty("Properties")
            .GetProperty("RunSettingsFilePath")
            .GetString();
    }

    private static ProcessResult RunProcess(string fileName, IEnumerable<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start '{fileName}'.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private static int ParseInt(XDocument document, string elementName)
    {
        var value = document.Descendants().Single(element => element.Name.LocalName == elementName).Value;
        return int.Parse(value);
    }

    private static string? FindProperty(XDocument document, string propertyName) => document
        .Descendants()
        .SingleOrDefault(element => element.Name.LocalName == propertyName)?
        .Value
        .Trim();

    private static string[] SplitLines(string value) => value
        .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsInsideObjDirectory(string repositoryRoot, string path) => Path
        .GetRelativePath(repositoryRoot, path)
        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Contains("obj", StringComparer.OrdinalIgnoreCase);

    private sealed record RunSettingsPolicy(int TestSessionTimeout, int NumberOfTestWorkers);
    private sealed record ReqnrollPolicy(string? Schema, string? FeatureLanguage, string?[] NonParallelizableTags, string? MissingOrPendingStepsOutcome, bool StopAtFirstError);
    private sealed record ProjectPolicy(string? RunSettingsFilePath, bool UseIntermediateOutputPathForCodeBehind);
    private sealed record AssemblyPolicy(int? LevelOfParallelism, bool IsNonParallelizable);
    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
    public sealed record CategoryInventoryEntry(
        string Name,
        IReadOnlyCollection<string> Categories,
        bool Parallelizable,
        bool GeneratedReqnroll = false);

    private sealed class TemporaryFixture : IDisposable
    {
        public TemporaryFixture()
        {
            Path = Directory.CreateTempSubdirectory("lgym-e2e-execution-policy-").FullName;
        }

        public string Path { get; }

        public string Write(string relativePath, string content)
        {
            var path = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
