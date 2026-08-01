using LgymApi.Application.Repositories;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Identity.Contracts;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class WorkoutProgressPersistencePortGuardTests
{
    private sealed record PersistenceSeam(string PortFile, string RepositoryFile, Type PortType);

    private static readonly PersistenceSeam[] Seams =
    [
        new("IWorkoutExercisePersistence.cs", "WorkoutExercisePersistenceRepository.cs", typeof(IWorkoutExercisePersistence)),
        new("IWorkoutExerciseScorePersistence.cs", "WorkoutExerciseScorePersistenceRepository.cs", typeof(IWorkoutExerciseScorePersistence)),
        new("IWorkoutGymPersistence.cs", "WorkoutGymPersistenceRepository.cs", typeof(IWorkoutGymPersistence)),
        new("IWorkoutMeasurementPersistence.cs", "WorkoutMeasurementPersistenceRepository.cs", typeof(IWorkoutMeasurementPersistence)),
        new("IWorkoutMainRecordPersistence.cs", "WorkoutMainRecordPersistenceRepository.cs", typeof(IWorkoutMainRecordPersistence)),
        new("IWorkoutEloPersistence.cs", "WorkoutEloPersistenceRepository.cs", typeof(IWorkoutEloPersistence)),
        new("IWorkoutTrainingPersistence.cs", "WorkoutTrainingPersistenceRepository.cs", typeof(IWorkoutTrainingPersistence))
    ];

    [Test]
    public void WorkoutPersistencePortsAdaptersAndRegistrations_ShouldRemainExact()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var portRoot = Path.Combine(root, "LgymApi.Application", "WorkoutProgress", "Persistence");
        var adapterRoot = Path.Combine(root, "LgymApi.Infrastructure", "Repositories", "WorkoutProgress");
        var registration = File.ReadAllText(Path.Combine(root, "LgymApi.Infrastructure", "WorkoutProgressServiceCollectionExtensions.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(Directory.GetFiles(portRoot, "IWorkout*Persistence.cs"), Has.Length.EqualTo(7));
            foreach (var seam in Seams)
            {
                Assert.That(File.Exists(Path.Combine(portRoot, seam.PortFile)), Is.True, seam.PortFile);
                Assert.That(File.Exists(Path.Combine(adapterRoot, seam.RepositoryFile)), Is.True, seam.RepositoryFile);
                Assert.That(registration, Does.Contain($"AddScoped<{seam.PortType.Name}, {Path.GetFileNameWithoutExtension(seam.RepositoryFile)}>()"));
            }
        });
    }

    [Test]
    public void WorkoutPersistencePorts_ShouldExposeAccountMarkersWithoutIdentityEntitiesOrRepositories()
    {
        var exposedTypes = Seams
            .SelectMany(seam => seam.PortType.GetMethods())
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
            .SelectMany(FlattenType)
            .ToHashSet();

        Assert.Multiple(() =>
        {
            Assert.That(exposedTypes, Does.Contain(typeof(AccountReference)));
            Assert.That(exposedTypes, Does.Not.Contain(typeof(User)));
            Assert.That(exposedTypes, Does.Not.Contain(typeof(Role)));
            Assert.That(exposedTypes.Select(type => type.FullName), Does.Not.Contain("LgymApi.Application.Repositories.IUserRepository"));
            Assert.That(exposedTypes.Select(type => type.FullName), Does.Not.Contain("LgymApi.Application.Repositories.IRoleRepository"));
        });
    }

    [Test]
    public void WorkoutPersistenceAdapters_ShouldRemainStageOnlyAndCentralizeAccountIdConversion()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var adapterRoot = Path.Combine(root, "LgymApi.Infrastructure", "Repositories", "WorkoutProgress");
        var violations = new List<string>();

        foreach (var seam in Seams)
        {
            var source = File.ReadAllText(Path.Combine(adapterRoot, seam.RepositoryFile));
            violations.AddRange(CollectStageOnlyViolations(source).Select(item => $"{seam.RepositoryFile}: {item}"));
            if (source.Contains("Rebind<User>", StringComparison.Ordinal))
            {
                violations.Add($"{seam.RepositoryFile}: account ID conversion must use WorkoutPersistenceAccountIds.");
            }
        }

        var conversionSource = File.ReadAllText(Path.Combine(adapterRoot, "WorkoutPersistenceAccountIds.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
            Assert.That(conversionSource, Does.Contain("ToPersisted"));
            Assert.That(conversionSource, Does.Contain("ToContract"));
        });
    }

    [TestCase("_dbContext.SaveChangesAsync()", "SaveChangesAsync")]
    [TestCase("_dbContext.Database.BeginTransactionAsync()", "BeginTransactionAsync")]
    public void WorkoutPersistenceAdapterSyntax_ShouldRejectCommitOrTransactionOwnership(string statement, string expectedDiagnostic)
    {
        var source = $$"""public sealed class Repository { public object Execute(dynamic _dbContext) => {{statement}}; }""";

        Assert.That(CollectStageOnlyViolations(source), Is.EqualTo(new[] { expectedDiagnostic }));
    }

    [Test]
    public void WorkoutPersistencePortSyntax_ShouldRejectLegacyIdentityDependencies()
    {
        const string source = "public interface IFixture { IUserRepository Users { get; } Id<User> AccountId { get; } }";

        Assert.That(CollectLegacyIdentityReferences(source), Is.EquivalentTo(new[] { "IUserRepository", "User" }));
    }

    private static IEnumerable<Type> FlattenType(Type type)
    {
        yield return type;
        foreach (var argument in type.GetGenericArguments().SelectMany(FlattenType))
        {
            yield return argument;
        }
    }

    private static IReadOnlyList<string> CollectStageOnlyViolations(string source)
        => CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot()
            .DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Select(access => access.Name.Identifier.ValueText)
            .Where(name => name is "SaveChanges" or "SaveChangesAsync" or "BeginTransaction" or "BeginTransactionAsync" or "Commit" or "CommitAsync" or "Rollback" or "RollbackAsync")
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<string> CollectLegacyIdentityReferences(string source)
        => CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot()
            .DescendantNodes().OfType<IdentifierNameSyntax>()
            .Select(identifier => identifier.Identifier.ValueText)
            .Where(name => name is "User" or "Role" or "IUserRepository" or "IRoleRepository")
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
