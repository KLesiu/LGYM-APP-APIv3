using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace LgymApi.ArchitectureTests;

[TestFixture]
[NonParallelizable]
public sealed class IndependentExportedSurfaceTests
{
    private static readonly string[] AssemblyNames =
    [
        "LgymApi.Platform",
        "LgymApi.Identity",
        "LgymApi.TrainingPlanning",
        "LgymApi.Notifications",
        "LgymApi.Application"
    ];

    private static readonly string[] RequiredExports =
    [
        "LgymApi.Application.ApplicationApiAdapterServiceCollectionExtensions",
        "LgymApi.Application.Identity.ApiAdapters.IAuthenticatedAccountApiAdapter",
        "LgymApi.Application.TrainingPlanning.ApiAdapters.IPlanAccountApiAdapter",
        "LgymApi.Application.Coaching.ApiAdapters.IManagedPlanAccountApiAdapter",
        "LgymApi.Application.Nutrition.ApiAdapters.IDietPlanAccountApiAdapter",
        "LgymApi.Application.Nutrition.ApiAdapters.ISupplementationApiAdapter",
        "LgymApi.Application.WorkoutProgress.ApiAdapters.IExerciseApiAdapter",
        "LgymApi.Application.WorkoutProgress.ApiAdapters.IMainRecordsApiAdapter",
        "LgymApi.Application.Platform.ReferenceData.ApiAdapters.IAppConfigApiAdapter",
        "LgymApi.Application.Coaching.ApiAdapters.ITrainerInvitationApiPort",
        "LgymApi.Application.Reporting.ApiAdapters.ITrainerReportTemplateApiPort",
        "LgymApi.Notifications.ApiAdapters.IInAppNotificationApiAdapter",
        "LgymApi.Notifications.ApiAdapters.INotificationEventApiAdapter",
        "LgymApi.Notifications.ApiAdapters.IPushInstallationApiAdapter"
    ];

    private static readonly string[] RemovedExports =
    [
        "LgymApi.Application.ApiAdapterServiceCollectionExtensions",
        "LgymApi.Application.Task7ApiCompatibilityServiceCollectionExtensions",
        "LgymApi.Application.WorkoutProgress.TrainingExecution.ITrainingHistoryReadServiceDependencies",
        "LgymApi.Application.WorkoutProgress.TrainingExecution.ICompleteTrainingUseCaseDependencies",
        "LgymApi.Application.WorkoutProgress.ProgressData.WorkoutProgressReadWriteServiceDependencies",
        "LgymApi.Application.Features.Reporting.IRecurringReportAssignmentServiceDependencies",
        "LgymApi.Application.Features.Reporting.IReportingServiceDependencies",
        "LgymApi.Application.Features.Measurements.IMeasurementsServiceDependencies",
        "LgymApi.Application.Features.Training.ITrainingServiceDependencies",
        "LgymApi.Application.Identity.Profile.UserProfileServiceDependencies",
        "LgymApi.Application.Identity.Sessions.UserSessionTerminationServiceDependencies",
        "LgymApi.Application.Identity.Registration.UserRegistrationServiceDependencies",
        "LgymApi.Application.Features.PasswordReset.PasswordResetServiceDependencies",
        "LgymApi.Application.Identity.Authentication.UserCredentialLoginServiceDependencies",
        "LgymApi.Application.Services.LegacyPasswordServiceFactory"
    ];

    [Test]
    public void CompiledReleaseExports_MatchTheReviewedIndependentInventoryAndApprovedCutoverDelta()
    {
        var inventory = CompiledExportInventory.Create(AssemblyNames.Select(Assembly.Load));
        var serialized = CompiledExportInventory.Serialize(inventory);
        var roundTripped = CompiledExportInventory.Deserialize(serialized);
        var observedDirectory = Path.Combine(ArchitectureTestHelpers.ResolveRepositoryRoot(), "TestResults", "Issue395");
        Directory.CreateDirectory(observedDirectory);
        var observedPath = Path.Combine(observedDirectory, "issue-395-compiled-export-inventory.observed.json");

        File.WriteAllText(observedPath, serialized);
        Assert.That(CompiledExportInventory.Serialize(roundTripped), Is.EqualTo(serialized));
        Assert.That(inventory.Assemblies.Select(assembly => assembly.Name), Is.EqualTo(AssemblyNames.OrderBy(name => name, StringComparer.Ordinal)));
        Assert.That(inventory.TotalExportedTypeCount, Is.EqualTo(757));

        foreach (var assembly in inventory.Assemblies)
        {
            Assert.That(assembly.ExportedTypes, Is.EqualTo(assembly.ExportedTypes.OrderBy(name => name, StringComparer.Ordinal)));
            Assert.That(assembly.ExportedTypes.Distinct(StringComparer.Ordinal), Is.EqualTo(assembly.ExportedTypes));
            Assert.That(assembly.ExportedTypeCount, Is.GreaterThan(0));
            Assert.That(assembly.Sha256, Has.Length.EqualTo(64));
            CompiledExportInventory.AssertNoForbiddenExportIdentities(assembly);
        }

        var allExports = inventory.Assemblies.SelectMany(assembly => assembly.ExportedTypes).ToHashSet(StringComparer.Ordinal);
        Assert.That(RequiredExports.Where(allExports.Contains).ToArray(), Has.Length.EqualTo(RequiredExports.Length));
        Assert.That(RemovedExports.Where(allExports.Contains), Is.Empty);

        var approval = LoadApproval();
        Assert.That(approval.SchemaVersion, Is.EqualTo(CompiledExportInventory.SchemaVersion));
        Assert.That(approval.Assemblies.Select(assembly => assembly.Name), Is.EqualTo(inventory.Assemblies.Select(assembly => assembly.Name)));
        foreach (var observed in inventory.Assemblies)
        {
            var expected = approval.Assemblies.Single(assembly => assembly.Name == observed.Name);
            Assert.That(observed.ExportedTypeCount, Is.EqualTo(expected.ExportedTypeCount), observed.Name);
            Assert.That(observed.Sha256, Is.EqualTo(expected.Sha256), observed.Name);
        }

        TestContext.Progress.WriteLine($"Independent compiled export inventory: {observedPath}; total={inventory.TotalExportedTypeCount}.");
    }

    [Test]
    public void CompiledFixture_RejectsAnUnlistedPublicExport()
    {
        var fixture = CreateFixtureAssembly(
            "Issue395.UnlistedExportFixture",
            "LgymApi.Identity.Contracts.AllowedCompiledContract",
            "LgymApi.Identity.Contracts.UnlistedCompiledExport");
        var observed = CompiledExportInventory.Create([fixture]).Assemblies.Single();

        var exception = Assert.Throws<InvalidOperationException>(
            () => CompiledExportInventory.AssertExactExports(observed, ["LgymApi.Identity.Contracts.AllowedCompiledContract"]));

        Assert.That(exception!.Message, Does.Contain("UnlistedCompiledExport"));
        Assert.That(exception.Message, Does.Contain("unlisted"));
    }

    [Test]
    public void CompiledFixture_RejectsAPublicProviderExport()
    {
        var fixture = CreateFixtureAssembly(
            "Issue395.ProviderExportFixture",
            "LgymApi.Notifications.Providers.Fcm.PublicProviderFixture");
        var observed = CompiledExportInventory.Create([fixture]).Assemblies.Single();

        var exception = Assert.Throws<InvalidOperationException>(
            () => CompiledExportInventory.AssertNoForbiddenExportIdentities(observed));

        Assert.That(exception!.Message, Does.Contain("PublicProviderFixture"));
        Assert.That(exception.Message, Does.Contain("provider"));
    }

    private static CompiledExportApproval LoadApproval()
    {
        var path = Path.Combine(
            ArchitectureTestHelpers.ResolveRepositoryRoot(),
            "LgymApi.ArchitectureTests",
            "Inventories",
            "issue-395-compiled-export-surface.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<CompiledExportApproval>(json, CompiledExportInventory.JsonOptions)
               ?? throw new InvalidOperationException("Compiled export approval inventory was empty.");
    }

    private static Assembly CreateFixtureAssembly(string assemblyName, params string[] exportedTypeNames)
    {
        var source = string.Join(
            Environment.NewLine,
            exportedTypeNames.Select(typeName =>
            {
                var separatorIndex = typeName.LastIndexOf(".", StringComparison.Ordinal);
                return $"namespace {typeName[..separatorIndex]} {{ public sealed class {typeName[(separatorIndex + 1)..]} {{ }} }}";
            }));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            ArchitectureTestHelpers.ResolveMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Compiled export fixture failed: {string.Join(Environment.NewLine, result.Diagnostics)}");
        }

        return Assembly.Load(stream.ToArray());
    }

    private sealed record CompiledExportApproval(int SchemaVersion, IReadOnlyList<CompiledExportAssemblyApproval> Assemblies);

    private sealed record CompiledExportAssemblyApproval(string Name, int ExportedTypeCount, string Sha256);
}
