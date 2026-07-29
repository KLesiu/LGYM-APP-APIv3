using LgymApi.Application.Reporting.Persistence;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Identity.Contracts;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ReportingPersistencePortGuardTests
{
    private sealed record PersistenceSeam(string PortFile, string RepositoryFile, string Registration);

    private static readonly PersistenceSeam[] Seams =
    [
        new("IReportTemplatePersistence.cs", "ReportTemplatePersistenceRepository.cs", "AddScoped<IReportTemplatePersistence, ReportTemplatePersistenceRepository>()"),
        new("IReportRequestSubmissionPersistence.cs", "ReportRequestSubmissionPersistenceRepository.cs", "AddScoped<IReportRequestSubmissionPersistence, ReportRequestSubmissionPersistenceRepository>()"),
        new("IRecurringReportAssignmentPersistence.cs", "RecurringReportAssignmentPersistenceRepository.cs", "AddScoped<IRecurringReportAssignmentPersistence, RecurringReportAssignmentPersistenceRepository>()"),
        new("IReportPhotoPersistence.cs", "ReportPhotoPersistenceRepository.cs", "AddScoped<IReportPhotoPersistence, ReportPhotoPersistenceRepository>()"),
        new("IReportingRelationshipAccessPersistence.cs", "ReportingRelationshipAccessPersistenceRepository.cs", "AddScoped<IReportingRelationshipAccessPersistence, ReportingRelationshipAccessPersistenceRepository>()")
    ];

    private static readonly Type[] PortTypes =
    [
        typeof(IReportTemplatePersistence),
        typeof(IReportRequestSubmissionPersistence),
        typeof(IRecurringReportAssignmentPersistence),
        typeof(IReportPhotoPersistence),
        typeof(IReportingRelationshipAccessPersistence)
    ];

    [Test]
    public void ReportingPersistencePortsAdaptersAndRegistrations_ShouldRemainExact()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var portRoot = Path.Combine(root, "LgymApi.Application", "Reporting", "Persistence");
        var repositoryRoot = Path.Combine(root, "LgymApi.Infrastructure", "Repositories", "Reporting");
        var registrationSource = File.ReadAllText(Path.Combine(root, "LgymApi.Infrastructure", "ReportingServiceCollectionExtensions.cs"));
        var missing = Seams
            .SelectMany(seam => new[]
            {
                Path.Combine(portRoot, seam.PortFile),
                Path.Combine(repositoryRoot, seam.RepositoryFile)
            })
            .Where(path => !File.Exists(path))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(missing, Is.Empty);
            Assert.That(Directory.GetFiles(portRoot, "I*Persistence.cs"), Has.Length.EqualTo(5));
            foreach (var seam in Seams)
            {
                Assert.That(registrationSource, Does.Contain(seam.Registration));
            }
        });
    }

    [Test]
    public void ReportingPersistencePorts_ShouldUseAccountMarkersWithoutIdentityEntitiesOrRepositories()
    {
        var exposedTypes = PortTypes
            .SelectMany(port => port.GetMethods())
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
    public void ReportingPersistenceAdapters_ShouldRemainStageOnlyAndUseNoTrackingReads()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var repositoryRoot = Path.Combine(root, "LgymApi.Infrastructure", "Repositories", "Reporting");
        var violations = new List<string>();

        foreach (var seam in Seams)
        {
            var source = File.ReadAllText(Path.Combine(repositoryRoot, seam.RepositoryFile));
            violations.AddRange(CollectStageOnlyViolations(source).Select(violation => $"{seam.RepositoryFile}: {violation}"));
            if (!source.Contains("AsNoTracking", StringComparison.Ordinal))
            {
                violations.Add($"{seam.RepositoryFile}: read queries must use AsNoTracking.");
            }
        }

        Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
    }

    [TestCase("_dbContext.SaveChangesAsync()", "SaveChangesAsync")]
    [TestCase("_dbContext.Database.BeginTransactionAsync()", "BeginTransactionAsync")]
    public void ReportingPersistenceAdapterSyntax_ShouldRejectCommitOrTransactionOwnership(string statement, string expectedDiagnostic)
    {
        var source = $$"""public sealed class Repository { public object Execute(dynamic _dbContext) => {{statement}}; }""";

        Assert.That(CollectStageOnlyViolations(source), Is.EqualTo(new[] { expectedDiagnostic }));
    }

    [Test]
    public void LegacyReportingRepositoriesAndUploadTracker_ShouldRemainAbsent()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var legacyPaths = new[]
        {
            "LgymApi.Application/Repositories/IReportingRepository.cs",
            "LgymApi.Application/Repositories/IRecurringReportAssignmentRepository.cs",
            "LgymApi.Application/Features/Reporting/IPhotoUploadInitTracker.cs",
            "LgymApi.Infrastructure/Repositories/ReportingRepository.cs",
            "LgymApi.Infrastructure/Repositories/RecurringReportAssignmentRepository.cs",
            "LgymApi.Infrastructure/Services/DbPhotoUploadInitTracker.cs",
            "LgymApi.Infrastructure/Services/InMemoryPhotoUploadInitTracker.cs"
        };

        Assert.That(legacyPaths.Where(path => File.Exists(Path.Combine(root, path))), Is.Empty);
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
}
