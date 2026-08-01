using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class TrainingPlanningPersistenceExtractionGuardTests
{
    private static readonly string[] RepositoryFiles =
    [
        "PlanRepository.cs",
        "PlanRepository.Clone.cs",
        "PlanDayRepository.cs",
        "PlanDayExerciseRepository.cs",
        "ActivePlanPointerStore.cs"
    ];

    private static readonly string[] ConfigurationTypes =
    [
        "PlanEntityTypeConfiguration",
        "PlanDayEntityTypeConfiguration",
        "PlanDayExerciseEntityTypeConfiguration"
    ];

    private static readonly (string Service, string Implementation)[] PersistenceRegistrations =
    [
        ("IPlanRepository", "PlanRepository"),
        ("IPlanDayRepository", "PlanDayRepository"),
        ("IPlanDayExerciseRepository", "PlanDayExerciseRepository"),
        ("IActivePlanPointerStore", "ActivePlanPointerStore")
    ];

    [Test]
    public void TrainingPlanning_Persistence_Sources_Should_Be_Internal_ContextBound_And_StageOnly()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var moduleRoot = Path.Combine(root, "LgymApi.TrainingPlanning");
        var infrastructureRoot = Path.Combine(root, "LgymApi.Infrastructure");
        var sources = RepositoryFiles.Select(file => Path.Combine(moduleRoot, "Persistence", "Repositories", file)).ToArray();

        Assert.That(sources.All(File.Exists), Is.True);
        Assert.That(RepositoryFiles.Select(file => Path.Combine(infrastructureRoot, "Repositories", file)).Where(File.Exists), Is.Empty);

        foreach (var sourcePath in sources)
        {
            var source = File.ReadAllText(sourcePath);
            var rootSyntax = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();

            Assert.Multiple(() =>
            {
                Assert.That(rootSyntax.DescendantNodes().OfType<ClassDeclarationSyntax>().Any(type => type.Modifiers.Any(modifier => modifier.RawKind == (int)SyntaxKind.PublicKeyword)), Is.False, sourcePath);
                Assert.That(source, Does.Not.Contain("AppDbContext").And.Not.Contain("SaveChanges").And.Not.Contain("BeginTransaction").And.Not.Contain(".Database"), sourcePath);
                Assert.That(source, Does.Not.Contain("_context.Users").And.Not.Contain("_context.Exercises").And.Not.Contain("_context.Trainings"), sourcePath);
            });
        }

        Assert.That(sources.Where(path => !path.EndsWith(".Clone.cs", StringComparison.Ordinal)).All(path => File.ReadAllText(path).Contains("ITrainingPlanningPersistenceContext", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void TrainingPlanning_Configurations_And_Registrations_Should_Be_Exact()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var moduleRoot = Path.Combine(root, "LgymApi.TrainingPlanning");
        var configurationRoot = Path.Combine(moduleRoot, "Persistence", "Configurations");
        var registrar = File.ReadAllText(Path.Combine(moduleRoot, "Persistence", "TrainingPlanningModelConfigurationRegistrar.cs"));
        var registrations = File.ReadAllText(Path.Combine(moduleRoot, "TrainingPlanningModule.cs"));

        Assert.That(ConfigurationTypes.Select(type => Path.Combine(configurationRoot, type + ".cs")).All(File.Exists), Is.True);
        Assert.That(ExtractConfigurationTypes(registrar), Is.EqualTo(ConfigurationTypes));
        EnsureScopedRegistrations(registrations);
    }

    [Test]
    public void TrainingPlanning_Persistence_Guards_Should_Reject_Foreign_Save_Transaction_And_Registration_Fixtures()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => EnsureSafePersistenceSource("class Repository { AppDbContext context; }"), Throws.InvalidOperationException);
            Assert.That(() => EnsureSafePersistenceSource("class Repository { void SaveChangesAsync() { } }"), Throws.InvalidOperationException);
            Assert.That(() => EnsureSafePersistenceSource("class Repository { void BeginTransactionAsync() { } }"), Throws.InvalidOperationException);
            Assert.That(() => EnsureSafePersistenceSource("class Repository { void Query() { _context.Users.ToString(); } }"), Throws.InvalidOperationException);
            Assert.That(() => EnsureSafePersistenceSource("class Repository { void Query() { _context.Exercises.ToString(); } }"), Throws.InvalidOperationException);
            Assert.That(() => EnsureSafePersistenceSource("class Repository { void Query() { _context.Trainings.ToString(); } }"), Throws.InvalidOperationException);
            Assert.That(() => EnsureExactConfigurations(ConfigurationTypes[..^1]), Throws.InvalidOperationException);
            Assert.That(() => EnsureExactConfigurations([.. ConfigurationTypes, ConfigurationTypes[^1]]), Throws.InvalidOperationException);
            Assert.That(() => EnsureScopedRegistrations("services.AddSingleton<IPlanRepository, PlanRepository>();"), Throws.InvalidOperationException);
        });
    }

    private static IReadOnlyList<string> ExtractConfigurationTypes(string source)
    {
        var actual = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Select(creation => creation.Type.ToString())
            .Where(type => type.EndsWith("EntityTypeConfiguration", StringComparison.Ordinal))
            .ToArray();
        EnsureExactConfigurations(actual);
        return actual;
    }

    private static void EnsureExactConfigurations(IReadOnlyList<string> actual)
    {
        if (!actual.SequenceEqual(ConfigurationTypes, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Training Planning configuration registrar order changed.");
        }
    }

    private static void EnsureScopedRegistrations(string source)
    {
        var registrations = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax { TypeArgumentList.Arguments.Count: 2 } })
            .Select(invocation => (GenericNameSyntax)((MemberAccessExpressionSyntax)invocation.Expression).Name)
            .Select(name => (Method: name.Identifier.ValueText, Service: name.TypeArgumentList.Arguments[0].ToString(), Implementation: name.TypeArgumentList.Arguments[1].ToString()))
            .ToArray();

        foreach (var expected in PersistenceRegistrations)
        {
            var matches = registrations.Where(registration => registration.Service == expected.Service && registration.Implementation == expected.Implementation).ToArray();
            if (matches.Length != 1 || matches[0].Method != "AddScoped")
            {
                throw new InvalidOperationException($"{expected.Service} must be registered once as scoped by TrainingPlanningModule.");
            }
        }
    }

    private static void EnsureSafePersistenceSource(string source)
    {
        if (source.Contains("AppDbContext", StringComparison.Ordinal) ||
            source.Contains("SaveChanges", StringComparison.Ordinal) ||
            source.Contains("BeginTransaction", StringComparison.Ordinal) ||
            source.Contains("_context.Users", StringComparison.Ordinal) ||
            source.Contains("_context.Exercises", StringComparison.Ordinal) ||
            source.Contains("_context.Trainings", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Training Planning persistence cannot access foreign sets or own commits or transactions.");
        }
    }
}
