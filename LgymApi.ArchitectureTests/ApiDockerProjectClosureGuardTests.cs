using System.Text.Json;
using System.Text.RegularExpressions;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ApiDockerProjectClosureGuardTests
{
    [Test]
    [Category("Issue427Fixture")]
    public void ClosureResolver_Should_Include_Root_Direct_And_Transitive_Projects()
    {
        var fixture = CreateProjectFixture(
            ("App/App.csproj", Project("../Direct/Direct.csproj")),
            ("Direct/Direct.csproj", Project("../Transitive/Transitive.csproj")),
            ("Transitive/Transitive.csproj", Project()));

        var closure = ResolveProjectClosure(fixture.RepositoryRoot, "App/App.csproj", fixture.ReadProject);

        Assert.That(closure, Is.EqualTo(new[]
        {
            "App/App.csproj",
            "Direct/Direct.csproj",
            "Transitive/Transitive.csproj"
        }));
    }

    [Test]
    [Category("Issue427Fixture")]
    public void ClosureResolver_Should_Normalize_Windows_And_Unix_ProjectReference_Separators()
    {
        var fixture = CreateProjectFixture(
            ("App/App.csproj", Project("..\\Shared\\Shared.csproj", "../Shared/Shared.csproj")),
            ("Shared/Shared.csproj", Project()));

        var closure = ResolveProjectClosure(fixture.RepositoryRoot, "App\\App.csproj", fixture.ReadProject);

        Assert.That(closure, Is.EqualTo(new[] { "App/App.csproj", "Shared/Shared.csproj" }));
    }

    [Test]
    [Category("Issue427Fixture")]
    public void ClosureResolver_Should_Reject_Cycles_Missing_Files_And_OutOfRoot_References()
    {
        var cycleFixture = CreateProjectFixture(
            ("A/A.csproj", Project("../B/B.csproj")),
            ("B/B.csproj", Project("../A/A.csproj")));
        var missingFixture = CreateProjectFixture(("A/A.csproj", Project("../Missing/Missing.csproj")));
        var outsideFixture = CreateProjectFixture(("A/A.csproj", Project("../../Outside/Outside.csproj")));
        var malformedFixture = CreateProjectFixture(("A/A.csproj", "<Project><ItemGroup><ProjectReference"));

        var cycle = Assert.Throws<InvalidDataException>(() =>
            ResolveProjectClosure(cycleFixture.RepositoryRoot, "A/A.csproj", cycleFixture.ReadProject));
        var missing = Assert.Throws<InvalidDataException>(() =>
            ResolveProjectClosure(missingFixture.RepositoryRoot, "A/A.csproj", missingFixture.ReadProject));
        var outside = Assert.Throws<InvalidDataException>(() =>
            ResolveProjectClosure(outsideFixture.RepositoryRoot, "A/A.csproj", outsideFixture.ReadProject));
        var malformed = Assert.Throws<InvalidDataException>(() =>
            ResolveProjectClosure(malformedFixture.RepositoryRoot, "A/A.csproj", malformedFixture.ReadProject));

        Assert.Multiple(() =>
        {
            Assert.That(cycle!.Message, Does.Contain("Project-reference cycle"));
            Assert.That(missing!.Message, Does.Contain("Project file does not exist"));
            Assert.That(outside!.Message, Does.Contain("outside the repository"));
            Assert.That(malformed!.Message, Does.Contain("Malformed project file"));
        });
    }

    [Test]
    [Category("Issue427Fixture")]
    public void DockerfileParser_Should_Accept_Current_Shell_And_Json_Copy_Forms()
    {
        var projects = FixtureProjects();
        var rootDockerfile = """
            FROM sdk AS build
            WORKDIR /src
            COPY Api/*.csproj Api/
            COPY Direct/Direct.csproj Direct/
            COPY Transitive/*.csproj Transitive/
            RUN dotnet restore "Api/Api.csproj"
            COPY . .
            RUN dotnet publish "Api/Api.csproj" \
                --configuration Release
            FROM runtime AS runtime
            COPY --from=build /app/publish/ ./
            """;
        var alternateDockerfile = """
            FROM sdk AS build
            WORKDIR /src
            COPY ["Api/Api.csproj", "Api/"]
            COPY ["Direct/Direct.csproj", "Direct/"]
            COPY ["Transitive/Transitive.csproj", "Transitive/"]
            RUN dotnet restore "Api/Api.csproj"
            COPY ["Api/", "Api/"]
            COPY ["Direct/", "Direct/"]
            COPY ["Transitive/", "Transitive/"]
            RUN dotnet publish "Api/Api.csproj"
            FROM runtime AS final
            COPY --from=build /app/publish .
            """;

        var rootViolations = ValidateDockerfile(rootDockerfile, DockerSurface.Root, projects, projects);
        var alternateViolations = ValidateDockerfile(alternateDockerfile, DockerSurface.Alternate, projects, projects);

        Assert.Multiple(() =>
        {
            Assert.That(rootViolations, Is.Empty);
            Assert.That(alternateViolations, Is.Empty);
        });
    }

    [Test]
    [Category("Issue427Fixture")]
    public void DockerfileParser_Should_Reject_Direct_And_Transitive_Omissions()
    {
        var projects = FixtureProjects();
        var dockerfile = """
            FROM sdk AS build
            WORKDIR /src
            COPY Api/Api.csproj Api/
            RUN dotnet restore "Api/Api.csproj"
            COPY . .
            RUN dotnet publish "Api/Api.csproj"
            """;

        var violations = ValidateDockerfile(dockerfile, DockerSurface.Root, projects, projects);

        Assert.Multiple(() =>
        {
            Assert.That(violations, Does.Contain("root-pre-restore|missing|Direct/Direct.csproj"));
            Assert.That(violations, Does.Contain("root-pre-restore|missing|Transitive/Transitive.csproj"));
        });
    }

    [Test]
    [Category("Issue427Fixture")]
    public void DockerfileParser_Should_Reject_Wrong_Stage_Phase_And_Destinations()
    {
        var projects = FixtureProjects();
        var wrongStage = """
            FROM sdk AS preparation
            WORKDIR /src
            COPY Api/Api.csproj Api/
            COPY Direct/Direct.csproj Direct/
            COPY Transitive/Transitive.csproj Transitive/
            FROM preparation AS build
            WORKDIR /src
            RUN dotnet restore "Api/Api.csproj"
            COPY . .
            RUN dotnet publish "Api/Api.csproj"
            """;
        var wrongPhase = """
            FROM sdk AS build
            WORKDIR /src
            COPY Api/Api.csproj Api/
            COPY Transitive/Transitive.csproj Transitive/
            RUN dotnet restore "Api/Api.csproj"
            COPY Direct/Direct.csproj Direct/
            COPY . .
            RUN dotnet publish "Api/Api.csproj"
            """;
        var wrongDestination = """
            FROM sdk AS build
            WORKDIR /src
            COPY Api/Api.csproj Wrong/
            COPY Direct/Direct.csproj Direct/
            COPY Transitive/Transitive.csproj Transitive/
            RUN dotnet restore "Api/Api.csproj"
            COPY . .
            RUN dotnet publish "Api/Api.csproj"
            """;

        var wrongStageViolations = ValidateDockerfile(wrongStage, DockerSurface.Root, projects, projects);
        var wrongPhaseViolations = ValidateDockerfile(wrongPhase, DockerSurface.Root, projects, projects);
        var wrongDestinationViolations = ValidateDockerfile(wrongDestination, DockerSurface.Root, projects, projects);

        Assert.Multiple(() =>
        {
            Assert.That(wrongStageViolations, Does.Contain("root-pre-restore|missing|Api/Api.csproj"));
            Assert.That(wrongPhaseViolations, Does.Contain("root-pre-restore|missing|Direct/Direct.csproj"));
            Assert.That(wrongDestinationViolations.Any(violation => violation.Contains("wrong-destination|Api/Api.csproj", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    [Category("Issue427Fixture")]
    public void DockerfileParser_Should_Reject_Duplicates_Stale_Projects_And_Unsupported_Constructs()
    {
        var closure = FixtureProjects();
        var repositoryProjects = closure.Append("Outside/Outside.csproj").ToArray();
        var dockerfile = """
            FROM sdk AS build
            WORKDIR /src
            COPY Api/Api.csproj Api/
            COPY Api/Api.csproj Api/
            COPY Direct/Direct.csproj Direct/
            COPY Transitive/Transitive.csproj Transitive/
            COPY Outside/Outside.csproj Outside/
            COPY --chown=1000 Direct/Direct.csproj Direct/
            COPY ${PROJECT}/Project.csproj Project/
            ADD archive.tar.gz /src
            RUN --mount=type=cache dotnet restore "Api/Api.csproj"
            RUN dotnet restore "Api/Api.csproj"
            COPY . .
            RUN dotnet publish "Api/Api.csproj"
            """;

        var violations = ValidateDockerfile(dockerfile, DockerSurface.Root, closure, repositoryProjects);

        Assert.Multiple(() =>
        {
            Assert.That(violations.Any(violation => violation.Contains("duplicate|Api/Api.csproj", StringComparison.Ordinal)), Is.True);
            Assert.That(violations.Any(violation => violation.Contains("stale-project|Outside/Outside.csproj", StringComparison.Ordinal)), Is.True);
            Assert.That(violations.Any(violation => violation.Contains("unsupported-copy-option", StringComparison.Ordinal)), Is.True);
            Assert.That(violations.Any(violation => violation.Contains("variable-bearing-copy", StringComparison.Ordinal)), Is.True);
            Assert.That(violations.Any(violation => violation.Contains("unsupported-add", StringComparison.Ordinal)), Is.True);
            Assert.That(violations.Any(violation => violation.Contains("unsupported-run-mount", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    [Category("Issue427Fixture")]
    public void DockerfileParser_Should_Ignore_Runtime_Stage_Transfers()
    {
        var projects = FixtureProjects();
        var dockerfile = """
            FROM sdk AS build
            WORKDIR /src
            COPY Api/Api.csproj Api/
            COPY Direct/Direct.csproj Direct/
            COPY Transitive/Transitive.csproj Transitive/
            RUN dotnet restore "Api/Api.csproj"
            COPY . .
            RUN dotnet publish "Api/Api.csproj"
            FROM runtime AS final
            WORKDIR /app
            COPY --from=build /app/publish/ ./
            """;

        var violations = ValidateDockerfile(dockerfile, DockerSurface.Root, projects, projects);

        Assert.That(violations, Is.Empty);
    }

    [Test]
    [Category("Issue427Fixture")]
    public void DockerIgnoreMatcher_Should_Honor_Last_Match_And_DockerfileSpecific_Precedence()
    {
        var rootIgnore = """
            **
            !Api/**
            Api/**
            !Api/Api.csproj
            """;
        var dockerfileSpecificIgnore = """
            **
            !Direct/**
            Direct/Secret/**
            """;

        Assert.Multiple(() =>
        {
            Assert.That(IsIncludedByDockerIgnore(rootIgnore, null, "Api/Api.csproj"), Is.True);
            Assert.That(IsIncludedByDockerIgnore(rootIgnore, null, "Api/Source.cs"), Is.False);
            Assert.That(IsIncludedByDockerIgnore(rootIgnore, dockerfileSpecificIgnore, "Api/Api.csproj"), Is.False);
            Assert.That(IsIncludedByDockerIgnore(rootIgnore, dockerfileSpecificIgnore, "Direct/Direct.csproj"), Is.True);
            Assert.That(IsIncludedByDockerIgnore(rootIgnore, dockerfileSpecificIgnore, "Direct/Secret/Token.cs"), Is.False);
        });
    }

    [Test]
    [Category("Issue427Fixture")]
    public void DockerIgnoreMatcher_Should_Reject_Missing_And_ClosureExternal_Projects()
    {
        var closure = new[] { "Api/Api.csproj", "Direct/Direct.csproj" };
        var repositoryProjects = closure.Append("Outside/Outside.csproj").ToArray();
        var dockerIgnore = """
            **
            !Api/**
            !Outside/**
            """;

        var violations = ValidateDockerIgnore(dockerIgnore, null, closure, repositoryProjects);

        Assert.Multiple(() =>
        {
            Assert.That(violations, Does.Contain("effective-context|missing|Direct/Direct.csproj"));
            Assert.That(violations, Does.Contain("effective-context|closure-external|Outside/Outside.csproj"));
        });
    }

    [Test]
    [Category("Issue427Fixture")]
    public void DockerIgnoreMatcher_Should_Require_Project_And_SourceProbe_Inclusion()
    {
        var projects = new[] { "Api/Api.csproj" };
        var dockerIgnore = """
            **
            !Api/Api.csproj
            """;

        var violations = ValidateDockerIgnore(dockerIgnore, null, projects, projects);

        Assert.That(violations, Does.Contain("effective-context|source-probe-missing|Api/__issue427_source_probe__.cs"));
    }

    [Test]
    [Category("Issue427Fixture")]
    public void AlternateSourceCoverage_Should_Reject_Omitted_Project()
    {
        var projects = FixtureProjects();
        var dockerfile = """
            FROM sdk AS build
            WORKDIR /src
            COPY ["Api/Api.csproj", "Api/"]
            COPY ["Direct/Direct.csproj", "Direct/"]
            COPY ["Transitive/Transitive.csproj", "Transitive/"]
            RUN dotnet restore "Api/Api.csproj"
            COPY ["Api/", "Api/"]
            COPY ["Transitive/", "Transitive/"]
            RUN dotnet publish "Api/Api.csproj"
            """;

        var violations = ValidateDockerfile(dockerfile, DockerSurface.Alternate, projects, projects);

        Assert.That(violations, Does.Contain("alternate-post-restore-source|missing|Direct/Direct.csproj"));
    }

    [Test]
    [Category("Issue427Repository")]
    public void Repository_Docker_Surfaces_Should_Match_Api_ProjectReference_Closure()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var closure = ResolveProjectClosure(
            repositoryRoot,
            "LgymApi.Api/LgymApi.Api.csproj",
            path => File.Exists(ArchitectureTestHelpers.ToHostPath(path))
                ? File.ReadAllText(ArchitectureTestHelpers.ToHostPath(path))
                : null);
        Assert.That(closure, Has.Count.EqualTo(12), "The API project-reference closure must contain exactly 12 projects.");

        var repositoryProjects = Directory
            .EnumerateFiles(repositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(path))
            .Select(path => GetRepositoryRelativePath(NormalizeFullPath(repositoryRoot).TrimEnd('/'), NormalizeFullPath(path)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var rootDockerfile = File.ReadAllText(Path.Combine(repositoryRoot, "Dockerfile"));
        var alternateDockerfilePath = Path.Combine(repositoryRoot, "LgymApi.Api", "Dockerfile");
        var alternateDockerfile = File.ReadAllText(alternateDockerfilePath);
        var rootDockerIgnore = File.ReadAllText(Path.Combine(repositoryRoot, ".dockerignore"));
        var violations = new List<string>();

        violations.AddRange(ValidateDockerfile(
            rootDockerfile,
            DockerSurface.Root,
            closure,
            repositoryProjects,
            "LgymApi.Api/LgymApi.Api.csproj"));
        violations.AddRange(ValidateDockerfile(
            alternateDockerfile,
            DockerSurface.Alternate,
            closure,
            repositoryProjects,
            "LgymApi.Api/LgymApi.Api.csproj"));
        violations.AddRange(ValidateDockerIgnore(
            rootDockerIgnore,
            ReadDockerfileSpecificIgnore(Path.Combine(repositoryRoot, "Dockerfile")),
            closure,
            repositoryProjects));
        violations.AddRange(ValidateDockerIgnore(
            rootDockerIgnore,
            ReadDockerfileSpecificIgnore(alternateDockerfilePath),
            closure,
            repositoryProjects));

        var issueTuples = violations
            .Distinct(StringComparer.Ordinal)
            .OrderBy(violation => violation, StringComparer.Ordinal)
            .Select(violation => $"issue427|{violation}")
            .ToArray();
        Assert.That(
            issueTuples.Length,
            Is.EqualTo(0),
            "API Docker project-closure drift detected:" + Environment.NewLine + string.Join(Environment.NewLine, issueTuples));
    }

    private static IReadOnlyList<string> ResolveProjectClosure(
        string repositoryRoot,
        string rootProjectPath,
        Func<string, string?> readProject)
    {
        var normalizedRoot = NormalizeFullPath(repositoryRoot).TrimEnd('/');
        var rootPath = Path.IsPathRooted(ArchitectureTestHelpers.ToHostPath(rootProjectPath))
            ? NormalizeFullPath(rootProjectPath)
            : NormalizeFullPath(Path.Combine(repositoryRoot, ArchitectureTestHelpers.ToHostPath(rootProjectPath)));
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var traversal = new List<string>();
        var closure = new List<string>();

        Visit(rootPath);
        return closure.OrderBy(path => path, StringComparer.Ordinal).ToArray();

        void Visit(string projectPath)
        {
            var normalizedPath = NormalizeFullPath(projectPath);
            var relativePath = GetRepositoryRelativePath(normalizedRoot, normalizedPath);

            if (active.Contains(normalizedPath))
            {
                var cycleStart = traversal.FindIndex(path =>
                    string.Equals(path, normalizedPath, StringComparison.OrdinalIgnoreCase));
                var cycle = traversal.Skip(cycleStart)
                    .Append(normalizedPath)
                    .Select(path => GetRepositoryRelativePath(normalizedRoot, path));
                throw new InvalidDataException($"Project-reference cycle: {string.Join(" -> ", cycle)}");
            }

            if (visited.Contains(normalizedPath))
            {
                return;
            }

            var projectXml = readProject(normalizedPath);
            if (projectXml == null)
            {
                throw new InvalidDataException($"Project file does not exist: {relativePath}");
            }

            active.Add(normalizedPath);
            traversal.Add(normalizedPath);
            IReadOnlyList<ProjectReferenceEdge> references;
            try
            {
                references = ArchitectureTestHelpers.ParseProjectReferences(normalizedPath, projectXml);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.Xml.XmlException)
            {
                throw new InvalidDataException($"Malformed project file '{relativePath}': {exception.Message}", exception);
            }

            foreach (var reference in references.OrderBy(edge => edge.TargetProjectPath, StringComparer.Ordinal))
            {
                Visit(reference.TargetProjectPath);
            }

            traversal.RemoveAt(traversal.Count - 1);
            active.Remove(normalizedPath);
            visited.Add(normalizedPath);
            closure.Add(relativePath);
        }
    }

    private static IReadOnlyList<string> ValidateDockerfile(
        string dockerfile,
        DockerSurface surface,
        IReadOnlyCollection<string> closureProjects,
        IReadOnlyCollection<string> repositoryProjects,
        string? rootProjectPath = null)
    {
        var closure = closureProjects.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var repositoryProjectSet = repositoryProjects.ToHashSet(StringComparer.Ordinal);
        var closureSet = closure.ToHashSet(StringComparer.Ordinal);
        var rootProject = rootProjectPath ?? closure[0];
        var model = ParseDockerfile(dockerfile);
        var candidates = model.Stages
            .Select(stage => FindBuildStage(stage, rootProject))
            .Where(candidate => candidate != null)
            .Cast<BuildStage>()
            .ToArray();
        var violations = new List<string>(model.Violations);

        if (candidates.Length != 1)
        {
            violations.Add($"dockerfile|api-build-stage-count|{candidates.Length}");
            return violations;
        }

        var build = candidates[0];
        foreach (var command in build.Stage.Commands)
        {
            if (command.Keyword == "ADD")
            {
                violations.Add($"dockerfile|unsupported-add|line-{command.Line}");
            }
            else if (command.Keyword == "RUN" && command.Arguments.TrimStart().StartsWith("--mount", StringComparison.Ordinal))
            {
                violations.Add($"dockerfile|unsupported-run-mount|line-{command.Line}");
            }
            else if (command.Keyword == "ONBUILD" &&
                command.Arguments.TrimStart().StartsWith("COPY", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"dockerfile|unsupported-source-construct|line-{command.Line}");
            }

            if (command.Copy != null)
            {
                violations.AddRange(command.Copy.Issues.Select(issue => $"dockerfile|{issue}|line-{command.Line}"));
            }
        }

        ValidatePreRestoreCopies(
            build,
            surface,
            closure,
            closureSet,
            repositoryProjectSet,
            violations);

        if (surface == DockerSurface.Root)
        {
            ValidateRootPostRestoreCopy(build, violations);
        }
        else
        {
            ValidateAlternatePostRestoreCopies(
                build,
                closure,
                closureSet,
                repositoryProjectSet,
                violations);
        }

        return violations.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static bool IsIncludedByDockerIgnore(
        string rootDockerIgnore,
        string? dockerfileSpecificIgnore,
        string path)
    {
        var patterns = (dockerfileSpecificIgnore ?? rootDockerIgnore)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        var normalizedPath = NormalizeRepositoryPath(path).TrimStart('/');
        var ignored = false;

        foreach (var rawPattern in patterns)
        {
            var pattern = rawPattern.Trim();
            if (pattern.Length == 0 || pattern.StartsWith('#'))
            {
                continue;
            }

            var negated = pattern.StartsWith('!');
            if (negated)
            {
                pattern = pattern[1..];
            }

            if (pattern.Length == 0)
            {
                continue;
            }

            if (DockerIgnorePatternMatches(pattern, normalizedPath))
            {
                ignored = !negated;
            }
        }

        return !ignored;
    }

    private static IReadOnlyList<string> ValidateDockerIgnore(
        string rootDockerIgnore,
        string? dockerfileSpecificIgnore,
        IReadOnlyCollection<string> closureProjects,
        IReadOnlyCollection<string> repositoryProjects)
    {
        var closureSet = closureProjects.ToHashSet(StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var project in closureProjects.OrderBy(path => path, StringComparer.Ordinal))
        {
            if (!IsIncludedByDockerIgnore(rootDockerIgnore, dockerfileSpecificIgnore, project))
            {
                violations.Add($"effective-context|missing|{project}");
                continue;
            }

            var sourceProbe = $"{GetProjectDirectory(project)}/__issue427_source_probe__.cs";
            if (!IsIncludedByDockerIgnore(rootDockerIgnore, dockerfileSpecificIgnore, sourceProbe))
            {
                violations.Add($"effective-context|source-probe-missing|{sourceProbe}");
            }
        }

        foreach (var project in repositoryProjects
            .Where(project => !closureSet.Contains(project))
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            if (IsIncludedByDockerIgnore(rootDockerIgnore, dockerfileSpecificIgnore, project))
            {
                violations.Add($"effective-context|closure-external|{project}");
            }
        }

        return violations;
    }

    private static void ValidatePreRestoreCopies(
        BuildStage build,
        DockerSurface surface,
        IReadOnlyCollection<string> closure,
        IReadOnlySet<string> closureSet,
        IReadOnlySet<string> repositoryProjects,
        ICollection<string> violations)
    {
        var prefix = surface == DockerSurface.Root ? "root-pre-restore" : "alternate-pre-restore";
        var copied = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var copy in build.Stage.Commands
            .Where(command => command.Index < build.RestoreIndex)
            .Select(command => command.Copy)
            .Where(copy => copy is { IsStageTransfer: false, Issues.Count: 0 })
            .Cast<DockerCopy>())
        {
            foreach (var project in ExpandProjectCopySources(copy.Sources, repositoryProjects, violations))
            {
                if (!closureSet.Contains(project))
                {
                    violations.Add($"{prefix}|stale-project|{project}");
                    continue;
                }

                var expectedDestination = $"/src/{GetProjectDirectory(project)}";
                var actualDestination = ResolveContainerPath(copy.Workdir, copy.Destination);
                if (!IsCanonicalProjectDestination(copy.Destination, project) ||
                    !string.Equals(actualDestination, expectedDestination, StringComparison.Ordinal))
                {
                    violations.Add($"{prefix}|wrong-destination|{project}|{actualDestination}");
                    continue;
                }

                copied[project] = copied.GetValueOrDefault(project) + 1;
            }

            if (copy.Sources.Any(source => NormalizeCopySource(source) == "."))
            {
                violations.Add($"{prefix}|unsupported-broad-source|.");
            }
        }

        foreach (var duplicate in copied.Where(item => item.Value > 1).Select(item => item.Key))
        {
            violations.Add($"{prefix}|duplicate|{duplicate}");
        }

        foreach (var missing in closure.Where(project => !copied.ContainsKey(project)))
        {
            violations.Add($"{prefix}|missing|{missing}");
        }
    }

    private static void ValidateRootPostRestoreCopy(BuildStage build, ICollection<string> violations)
    {
        var broadCopies = build.Stage.Commands
            .Where(command => command.Index > build.RestoreIndex && command.Index < build.PublishIndex)
            .Select(command => command.Copy)
            .Where(copy => copy is { IsStageTransfer: false, Issues.Count: 0 })
            .Cast<DockerCopy>()
            .Where(copy => copy.Sources.Count == 1 && NormalizeCopySource(copy.Sources[0]) == ".")
            .ToArray();
        var validCopies = broadCopies.Count(copy =>
            string.Equals(copy.Workdir, "/src", StringComparison.Ordinal) &&
            string.Equals(copy.Destination, ".", StringComparison.Ordinal) &&
            string.Equals(ResolveContainerPath(copy.Workdir, copy.Destination), "/src", StringComparison.Ordinal));

        if (validCopies == 0)
        {
            violations.Add("root-post-restore-source|missing|.");
        }
        else if (validCopies > 1)
        {
            violations.Add("root-post-restore-source|duplicate|.");
        }

        if (broadCopies.Length > validCopies)
        {
            violations.Add("root-post-restore-source|wrong-destination|.");
        }
    }

    private static void ValidateAlternatePostRestoreCopies(
        BuildStage build,
        IReadOnlyCollection<string> closure,
        IReadOnlySet<string> closureSet,
        IReadOnlySet<string> repositoryProjects,
        ICollection<string> violations)
    {
        const string prefix = "alternate-post-restore-source";
        var copied = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var copy in build.Stage.Commands
            .Where(command => command.Index > build.RestoreIndex && command.Index < build.PublishIndex)
            .Select(command => command.Copy)
            .Where(copy => copy is { IsStageTransfer: false, Issues.Count: 0 })
            .Cast<DockerCopy>())
        {
            foreach (var source in copy.Sources)
            {
                var normalizedSource = NormalizeCopySource(source).TrimEnd('/');
                var project = repositoryProjects.SingleOrDefault(candidate =>
                    string.Equals(GetProjectDirectory(candidate), normalizedSource, StringComparison.Ordinal));
                if (project == null)
                {
                    violations.Add($"{prefix}|unsupported-source|{normalizedSource}");
                    continue;
                }

                if (!closureSet.Contains(project))
                {
                    violations.Add($"{prefix}|stale-project|{project}");
                    continue;
                }

                var expectedDestination = $"/src/{GetProjectDirectory(project)}";
                var actualDestination = ResolveContainerPath(copy.Workdir, copy.Destination);
                if (!IsCanonicalProjectDestination(copy.Destination, project) ||
                    !string.Equals(actualDestination, expectedDestination, StringComparison.Ordinal))
                {
                    violations.Add($"{prefix}|wrong-destination|{project}|{actualDestination}");
                    continue;
                }

                copied[project] = copied.GetValueOrDefault(project) + 1;
            }
        }

        foreach (var duplicate in copied.Where(item => item.Value > 1).Select(item => item.Key))
        {
            violations.Add($"{prefix}|duplicate|{duplicate}");
        }

        foreach (var missing in closure.Where(project => !copied.ContainsKey(project)))
        {
            violations.Add($"{prefix}|missing|{missing}");
        }
    }

    private static IEnumerable<string> ExpandProjectCopySources(
        IReadOnlyList<string> sources,
        IReadOnlySet<string> repositoryProjects,
        ICollection<string> violations)
    {
        foreach (var source in sources)
        {
            var normalizedSource = NormalizeCopySource(source);
            if (normalizedSource.EndsWith("/*.csproj", StringComparison.Ordinal) &&
                normalizedSource.Count(character => character == '*') == 1)
            {
                var directory = normalizedSource[..^"/*.csproj".Length];
                var matches = repositoryProjects
                    .Where(project => string.Equals(GetProjectDirectory(project), directory, StringComparison.Ordinal))
                    .OrderBy(project => project, StringComparer.Ordinal)
                    .ToArray();
                if (matches.Length == 0)
                {
                    violations.Add($"dockerfile|project-glob-without-match|{normalizedSource}");
                }

                foreach (var match in matches)
                {
                    yield return match;
                }
            }
            else if (normalizedSource.EndsWith(".csproj", StringComparison.Ordinal))
            {
                if (!repositoryProjects.Contains(normalizedSource))
                {
                    violations.Add($"dockerfile|project-copy-source-missing|{normalizedSource}");
                    continue;
                }

                yield return normalizedSource;
            }
            else if (normalizedSource.Contains('*'))
            {
                violations.Add($"dockerfile|unsupported-project-glob|{normalizedSource}");
            }
        }
    }

    private static DockerfileModel ParseDockerfile(string dockerfile)
    {
        var stages = new List<DockerStage>();
        var violations = new List<string>();
        DockerStage? currentStage = null;
        var commandIndex = 0;

        foreach (var logicalLine in JoinLogicalLines(dockerfile))
        {
            var separator = logicalLine.Text.IndexOfAny([' ', '\t']);
            var keyword = (separator < 0 ? logicalLine.Text : logicalLine.Text[..separator]).ToUpperInvariant();
            var arguments = separator < 0 ? string.Empty : logicalLine.Text[(separator + 1)..].Trim();

            if (keyword == "FROM")
            {
                var tokens = TokenizeShell(arguments, out var complete);
                if (!complete || tokens.Count == 0)
                {
                    violations.Add($"dockerfile|malformed-from|line-{logicalLine.Line}");
                    currentStage = null;
                    continue;
                }

                var aliasIndex = tokens.FindIndex(token => string.Equals(token, "AS", StringComparison.OrdinalIgnoreCase));
                var name = aliasIndex >= 0 && aliasIndex + 1 < tokens.Count
                    ? tokens[aliasIndex + 1]
                    : $"stage-{stages.Count}";
                var inheritedWorkdir = stages
                    .LastOrDefault(stage => string.Equals(stage.Name, tokens[0], StringComparison.OrdinalIgnoreCase))
                    ?.Workdir ?? "/";
                currentStage = new DockerStage(stages.Count, name, inheritedWorkdir);
                stages.Add(currentStage);
                continue;
            }

            if (currentStage == null)
            {
                if (keyword is "COPY" or "ADD" or "RUN" or "WORKDIR")
                {
                    violations.Add($"dockerfile|instruction-before-from|{keyword}|line-{logicalLine.Line}");
                }
                continue;
            }

            DockerCopy? copy = null;
            if (keyword == "WORKDIR")
            {
                if (ContainsVariable(arguments))
                {
                    violations.Add($"dockerfile|variable-bearing-workdir|line-{logicalLine.Line}");
                }
                else
                {
                    currentStage.Workdir = ResolveContainerPath(currentStage.Workdir, arguments);
                }
            }
            else if (keyword == "COPY")
            {
                copy = ParseCopy(arguments, currentStage.Workdir);
            }

            currentStage.Commands.Add(new DockerCommand(
                commandIndex++,
                logicalLine.Line,
                keyword,
                arguments,
                copy));
        }

        return new DockerfileModel(stages, violations);
    }

    private static BuildStage? FindBuildStage(DockerStage stage, string rootProject)
    {
        var restore = stage.Commands.FirstOrDefault(command =>
            command.Keyword == "RUN" && ContainsDotnetProjectCommand(command.Arguments, "restore", rootProject));
        if (restore == null)
        {
            return null;
        }

        var publish = stage.Commands.FirstOrDefault(command =>
            command.Index > restore.Index && command.Keyword == "RUN" &&
            ContainsDotnetProjectCommand(command.Arguments, "publish", rootProject));
        return publish == null ? null : new BuildStage(stage, restore.Index, publish.Index);
    }

    private static bool ContainsDotnetProjectCommand(string arguments, string verb, string project)
    {
        var normalized = arguments.Replace('\\', '/');
        var pattern = $@"\bdotnet\s+{verb}\s+[""']?{Regex.Escape(project)}(?=[""'\s]|$)";
        return Regex.IsMatch(normalized, pattern, RegexOptions.CultureInvariant);
    }

    private static DockerCopy ParseCopy(string arguments, string workdir)
    {
        var payload = arguments.Trim();
        var issues = new List<string>();
        var stageTransfer = false;

        while (payload.StartsWith("--", StringComparison.Ordinal))
        {
            var separator = payload.IndexOfAny([' ', '\t']);
            var option = separator < 0 ? payload : payload[..separator];
            payload = separator < 0 ? string.Empty : payload[(separator + 1)..].TrimStart();
            if (option.StartsWith("--from=", StringComparison.Ordinal) && option.Length > "--from=".Length)
            {
                stageTransfer = true;
            }
            else
            {
                issues.Add($"unsupported-copy-option|{option}");
            }
        }

        IReadOnlyList<string> values;
        if (payload.StartsWith('['))
        {
            try
            {
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.ValueKind != JsonValueKind.Array ||
                    document.RootElement.GetArrayLength() < 2 ||
                    document.RootElement.EnumerateArray().Any(element => element.ValueKind != JsonValueKind.String))
                {
                    issues.Add("unsupported-json-copy");
                    values = [];
                }
                else
                {
                    values = document.RootElement.EnumerateArray().Select(element => element.GetString()!).ToArray();
                }
            }
            catch (JsonException)
            {
                issues.Add("malformed-json-copy");
                values = [];
            }
        }
        else
        {
            values = TokenizeShell(payload, out var complete);
            if (!complete || values.Count < 2)
            {
                issues.Add("malformed-shell-copy");
                values = [];
            }
        }

        var sources = values.Count >= 2 ? values.Take(values.Count - 1).ToArray() : [];
        var destination = values.Count >= 2 ? values[^1] : string.Empty;
        if (sources.Any(ContainsVariable) || ContainsVariable(destination))
        {
            issues.Add("variable-bearing-copy");
        }

        if (sources.Any(source => source.StartsWith('/') || NormalizeCopySource(source).StartsWith("../", StringComparison.Ordinal)))
        {
            issues.Add("out-of-context-copy-source");
        }

        return new DockerCopy(sources, destination, workdir, stageTransfer, issues);
    }

    private static List<string> TokenizeShell(string value, out bool complete)
    {
        var tokens = new List<string>();
        var index = 0;
        complete = true;

        while (index < value.Length)
        {
            while (index < value.Length && char.IsWhiteSpace(value[index]))
            {
                index++;
            }

            if (index == value.Length)
            {
                break;
            }

            var quote = value[index] is '"' or '\'' ? value[index++] : '\0';
            var start = index;
            if (quote == '\0')
            {
                while (index < value.Length && !char.IsWhiteSpace(value[index]))
                {
                    index++;
                }
            }
            else
            {
                while (index < value.Length && value[index] != quote)
                {
                    index++;
                }

                if (index == value.Length)
                {
                    complete = false;
                    return tokens;
                }
            }

            tokens.Add(value[start..index]);
            if (quote != '\0')
            {
                index++;
            }
        }

        return tokens;
    }

    private static IEnumerable<LogicalLine> JoinLogicalLines(string dockerfile)
    {
        var pending = string.Empty;
        var startLine = 0;
        var lines = dockerfile.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (pending.Length == 0 && (line.Length == 0 || line.StartsWith('#')))
            {
                continue;
            }

            if (pending.Length == 0)
            {
                startLine = index + 1;
            }

            var continues = line.EndsWith('\\');
            var content = continues ? line[..^1].TrimEnd() : line;
            pending = pending.Length == 0 ? content : $"{pending} {content}";
            if (!continues)
            {
                yield return new LogicalLine(startLine, pending);
                pending = string.Empty;
            }
        }

        if (pending.Length > 0)
        {
            throw new InvalidDataException($"Dockerfile logical continuation is incomplete at line {startLine}.");
        }
    }

    private static bool DockerIgnorePatternMatches(string pattern, string path)
    {
        var normalizedPattern = NormalizeRepositoryPath(pattern).TrimStart('/');
        if (normalizedPattern == "**")
        {
            return true;
        }

        var directoryPattern = normalizedPattern.EndsWith('/');
        normalizedPattern = normalizedPattern.TrimEnd('/');
        if (directoryPattern)
        {
            normalizedPattern += "/**";
        }

        if (!normalizedPattern.Contains('/'))
        {
            normalizedPattern = $"**/{normalizedPattern}";
        }

        var expression = new System.Text.StringBuilder("^");
        for (var index = 0; index < normalizedPattern.Length; index++)
        {
            var character = normalizedPattern[index];
            if (character == '*' && index + 1 < normalizedPattern.Length && normalizedPattern[index + 1] == '*')
            {
                var followedBySlash = index + 2 < normalizedPattern.Length && normalizedPattern[index + 2] == '/';
                expression.Append(followedBySlash ? "(?:.*/)?" : ".*");
                index += followedBySlash ? 2 : 1;
            }
            else if (character == '*')
            {
                expression.Append("[^/]*");
            }
            else if (character == '?')
            {
                expression.Append("[^/]");
            }
            else
            {
                expression.Append(Regex.Escape(character.ToString()));
            }
        }

        expression.Append('$');
        return Regex.IsMatch(path, expression.ToString(), RegexOptions.CultureInvariant);
    }

    private static string ResolveContainerPath(string workdir, string path)
    {
        var candidate = path.Replace('\\', '/');
        if (!candidate.StartsWith('/'))
        {
            candidate = $"{workdir.TrimEnd('/')}/{candidate}";
        }

        var segments = new List<string>();
        foreach (var segment in candidate.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }
                continue;
            }

            segments.Add(segment);
        }

        return "/" + string.Join('/', segments);
    }

    private static string GetRepositoryRelativePath(string normalizedRoot, string normalizedPath)
    {
        var relativePath = NormalizeRepositoryPath(Path.GetRelativePath(
            ArchitectureTestHelpers.ToHostPath(normalizedRoot),
            ArchitectureTestHelpers.ToHostPath(normalizedPath)));
        if (relativePath == ".." || relativePath.StartsWith("../", StringComparison.Ordinal) ||
            Path.IsPathRooted(ArchitectureTestHelpers.ToHostPath(relativePath)))
        {
            throw new InvalidDataException($"Project reference is outside the repository: {normalizedPath}");
        }

        return relativePath;
    }

    private static string GetProjectDirectory(string project) =>
        project[..project.LastIndexOf('/')];

    private static bool IsCanonicalProjectDestination(string destination, string project) =>
        string.Equals(
            NormalizeRepositoryPath(destination).TrimEnd('/'),
            GetProjectDirectory(project),
            StringComparison.Ordinal);

    private static string NormalizeCopySource(string source)
    {
        var normalized = NormalizeRepositoryPath(source);
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.Length == 0 ? "." : normalized;
    }

    private static string NormalizeRepositoryPath(string path) => path.Replace('\\', '/');

    private static bool ContainsVariable(string value) => value.Contains('$');

    private static string? ReadDockerfileSpecificIgnore(string dockerfilePath)
    {
        var specificIgnorePath = $"{dockerfilePath}.dockerignore";
        return File.Exists(specificIgnorePath) ? File.ReadAllText(specificIgnorePath) : null;
    }

    private static bool IsBuildArtifact(string path)
    {
        var normalized = NormalizeRepositoryPath(path);
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static ProjectFixture CreateProjectFixture(params (string Path, string Xml)[] projects)
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), "issue427-project-fixture");
        var projectXmlByPath = projects.ToDictionary(
            project => NormalizeFullPath(Path.Combine(repositoryRoot, ArchitectureTestHelpers.ToHostPath(project.Path))),
            project => project.Xml,
            StringComparer.OrdinalIgnoreCase);

        return new ProjectFixture(
            repositoryRoot,
            path => projectXmlByPath.GetValueOrDefault(NormalizeFullPath(path)));
    }

    private static string Project(params string[] references)
    {
        var items = string.Join(Environment.NewLine, references.Select(reference =>
            $"    <ProjectReference Include=\"{reference}\" />"));
        return $"<Project><ItemGroup>{Environment.NewLine}{items}{Environment.NewLine}</ItemGroup></Project>";
    }

    private static string[] FixtureProjects() =>
    [
        "Api/Api.csproj",
        "Direct/Direct.csproj",
        "Transitive/Transitive.csproj"
    ];

    private static string NormalizeFullPath(string path) =>
        Path.GetFullPath(path).Replace('\\', '/');

    private enum DockerSurface
    {
        Root,
        Alternate
    }

    private sealed record ProjectFixture(
        string RepositoryRoot,
        Func<string, string?> ReadProject);

    private sealed record LogicalLine(int Line, string Text);

    private sealed record DockerfileModel(
        IReadOnlyList<DockerStage> Stages,
        IReadOnlyList<string> Violations);

    private sealed class DockerStage(int index, string name, string workdir)
    {
        public int Index { get; } = index;

        public string Name { get; } = name;

        public string Workdir { get; set; } = workdir;

        public List<DockerCommand> Commands { get; } = [];
    }

    private sealed record DockerCommand(
        int Index,
        int Line,
        string Keyword,
        string Arguments,
        DockerCopy? Copy);

    private sealed record DockerCopy(
        IReadOnlyList<string> Sources,
        string Destination,
        string Workdir,
        bool IsStageTransfer,
        IReadOnlyList<string> Issues);

    private sealed record BuildStage(
        DockerStage Stage,
        int RestoreIndex,
        int PublishIndex);
}
