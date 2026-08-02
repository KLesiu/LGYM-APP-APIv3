using System.Reflection;
using LgymApi.Application.Services;
using LgymApi.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class Issue395MigrationLedgerTests
{
    private static readonly RemovedAggregateRow[] RemovedApplicationDependencyAggregates =
    [
        RemovedApplication("LgymApi.Application.WorkoutProgress.TrainingExecution.ITrainingHistoryReadServiceDependencies", "WorkoutProgress/TrainingExecution/ITrainingHistoryReadServiceDependencies.cs", "WorkoutProgress/ServiceCollectionExtensions.cs"),
        RemovedApplication("LgymApi.Application.WorkoutProgress.TrainingExecution.ICompleteTrainingUseCaseDependencies", "WorkoutProgress/TrainingExecution/ICompleteTrainingUseCaseDependencies.cs", "WorkoutProgress/ServiceCollectionExtensions.cs"),
        RemovedApplication("LgymApi.Application.WorkoutProgress.ProgressData.WorkoutProgressReadWriteServiceDependencies", "WorkoutProgress/ProgressData/WorkoutProgressReadWriteServiceDependencies.cs", "WorkoutProgress/ServiceCollectionExtensions.cs"),
        RemovedApplication("LgymApi.Application.Features.Reporting.IRecurringReportAssignmentServiceDependencies", "Features/Reporting/IRecurringReportAssignmentServiceDependencies.cs", "Reporting/ServiceCollectionExtensions.cs"),
        RemovedApplication("LgymApi.Application.Features.Reporting.IReportingServiceDependencies", "Features/Reporting/IReportingServiceDependencies.cs", "Reporting/ServiceCollectionExtensions.cs"),
        RemovedApplication("LgymApi.Application.Features.Measurements.IMeasurementsServiceDependencies", "Measurements/IMeasurementsServiceDependencies.cs", "WorkoutProgress/ServiceCollectionExtensions.cs"),
        RemovedApplication("LgymApi.Application.Features.Training.ITrainingServiceDependencies", "Training/ITrainingServiceDependencies.cs", "WorkoutProgress/ServiceCollectionExtensions.cs")
    ];

    private static readonly RemovedAggregateRow[] RemovedIdentityDependencyAggregates =
    [
        RemovedIdentity("LgymApi.Application.Identity.Profile.UserProfileServiceDependencies", "Profile/UserProfileServiceDependencies.cs"),
        RemovedIdentity("LgymApi.Application.Identity.Sessions.UserSessionTerminationServiceDependencies", "Sessions/UserSessionTerminationServiceDependencies.cs"),
        RemovedIdentity("LgymApi.Application.Identity.Registration.UserRegistrationServiceDependencies", "Registration/UserRegistrationServiceDependencies.cs"),
        RemovedIdentity("LgymApi.Application.Features.PasswordReset.PasswordResetServiceDependencies", "Features/PasswordReset/PasswordResetServiceDependencies.cs"),
        RemovedIdentity("LgymApi.Application.Identity.Authentication.UserCredentialLoginServiceDependencies", "Authentication/UserCredentialLoginServiceDependencies.cs")
    ];

    private static readonly RemovedApiWrapperRow[] RemovedWorkoutProgressApiWrappers =
    [
        RemovedWorkoutProgressApiWrapper("LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.IGymApiCompatibilityService", "Task7ApiCompatibility/WorkoutProgress/GymApiCompatibility.cs"),
        RemovedWorkoutProgressApiWrapper("LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.IMeasurementsApiCompatibilityService", "Task7ApiCompatibility/WorkoutProgress/MeasurementsApiCompatibility.cs"),
        RemovedWorkoutProgressApiWrapper("LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.IExerciseScoresApiCompatibilityService", "Task7ApiCompatibility/WorkoutProgress/ExerciseScoresApiCompatibility.cs"),
        RemovedWorkoutProgressApiWrapper("LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.ITrainingApiCompatibilityService", "Task7ApiCompatibility/WorkoutProgress/TrainingApiCompatibility.cs"),
        RemovedWorkoutProgressApiWrapper("LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.IEloRegistryApiCompatibilityService", "Task7ApiCompatibility/WorkoutProgress/EloRegistryApiCompatibility.cs")
    ];

    private static readonly RelocatedApiAdapterRow[] RelocatedWorkoutProgressApiAdapters =
    [
        RelocatedWorkoutProgressApiAdapter("LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.IExerciseApiCompatibilityService", "Task7ApiCompatibility/WorkoutProgress/ExerciseApiCompatibility.cs", "LgymApi.Application.WorkoutProgress.ApiAdapters.IExerciseApiAdapter", "WorkoutProgress/ApiAdapters/ExerciseApiAdapter.cs"),
        RelocatedWorkoutProgressApiAdapter("LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.IMainRecordsApiCompatibilityService", "Task7ApiCompatibility/WorkoutProgress/MainRecordsApiCompatibility.cs", "LgymApi.Application.WorkoutProgress.ApiAdapters.IMainRecordsApiAdapter", "WorkoutProgress/ApiAdapters/MainRecordsApiAdapter.cs")
    ];

    private static readonly string[] RemovedTodo14SourcePaths =
    [
        "Coaching/Compatibility/CoachingApiCompatibilityPorts.cs",
        "Coaching/Compatibility/CoachingApiCompatibilityMappingProfile.cs",
        "Coaching/Compatibility/TrainerInvitationAndRelationshipApiAdapters.cs",
        "Coaching/Compatibility/TrainerDashboardProgressApiAdapter.cs",
        "Coaching/Compatibility/TraineeNotesApiAdapters.cs",
        "Reporting/Compatibility/ReportingApiCompatibilityPorts.cs",
        "Reporting/Compatibility/ReportingApiCompatibilityMappingProfile.cs",
        "Reporting/Compatibility/ReportTemplateAndRequestApiAdapters.cs",
        "Reporting/Compatibility/ReportPhotoAndRecurringApiAdapters.cs"
    ];

    private static readonly string[] Todo14OwnerSourcePaths =
    [
        "Coaching/ApiAdapters/CoachingApiAdapterContracts.cs",
        "Coaching/ApiAdapters/CoachingApiAdapterMappingProfile.cs",
        "Coaching/ApiAdapters/TrainerInvitationAndRelationshipApiAdapters.cs",
        "Coaching/ApiAdapters/TrainerDashboardProgressApiAdapter.cs",
        "Coaching/ApiAdapters/TraineeNotesApiAdapters.cs",
        "Reporting/ApiAdapters/ReportingApiAdapterContracts.cs",
        "Reporting/ApiAdapters/ReportingApiAdapterMappingProfile.cs",
        "Reporting/ApiAdapters/ReportTemplateAndRequestApiAdapters.cs",
        "Reporting/ApiAdapters/ReportPhotoAndRecurringApiAdapters.cs"
    ];

    private static readonly string[] Todo14OwnerAdapterContracts =
    [
        "LgymApi.Application.Coaching.ApiAdapters.ITrainerInvitationApiPort",
        "LgymApi.Application.Coaching.ApiAdapters.ITrainerDashboardProgressApiPort",
        "LgymApi.Application.Coaching.ApiAdapters.ITrainerTraineeNotesApiPort",
        "LgymApi.Application.Coaching.ApiAdapters.ITraineeNotesApiPort",
        "LgymApi.Application.Coaching.ApiAdapters.ITraineeRelationshipApiPort",
        "LgymApi.Application.Reporting.ApiAdapters.ITrainerReportTemplateApiPort",
        "LgymApi.Application.Reporting.ApiAdapters.ITrainerReportRequestApiPort",
        "LgymApi.Application.Reporting.ApiAdapters.ITraineeReportRequestApiPort",
        "LgymApi.Application.Reporting.ApiAdapters.ITrainerReportPhotoApiPort",
        "LgymApi.Application.Reporting.ApiAdapters.ITraineeReportPhotoApiPort",
        "LgymApi.Application.Reporting.ApiAdapters.IRecurringReportAssignmentApiPort"
    ];

    private static readonly string[] Todo14OwnerImplementationTypes =
    [
        "LgymApi.Application.Coaching.ApiAdapters.TrainerInvitationApiAdapter",
        "LgymApi.Application.Coaching.ApiAdapters.TrainerDashboardProgressApiAdapter",
        "LgymApi.Application.Coaching.ApiAdapters.TrainerTraineeNotesApiAdapter",
        "LgymApi.Application.Coaching.ApiAdapters.TraineeNotesApiAdapter",
        "LgymApi.Application.Coaching.ApiAdapters.TraineeRelationshipApiAdapter",
        "LgymApi.Application.Reporting.ApiAdapters.TrainerReportTemplateApiAdapter",
        "LgymApi.Application.Reporting.ApiAdapters.TrainerReportRequestApiAdapter",
        "LgymApi.Application.Reporting.ApiAdapters.TraineeReportRequestApiAdapter",
        "LgymApi.Application.Reporting.ApiAdapters.TrainerReportPhotoApiAdapter",
        "LgymApi.Application.Reporting.ApiAdapters.TraineeReportPhotoApiAdapter",
        "LgymApi.Application.Reporting.ApiAdapters.RecurringReportAssignmentApiAdapter"
    ];

    private static readonly OwnerExportRow[] Todo14OwnerExports =
    [
        OwnerExports("LgymApi.Application.Coaching.ApiAdapters", "ITrainerInvitationApiPort", "ITrainerDashboardProgressApiPort", "ITrainerTraineeNotesApiPort", "ITraineeNotesApiPort", "ITraineeRelationshipApiPort", "CoachingApiAdapterMappingProfile"),
        OwnerExports("LgymApi.Application.Reporting.ApiAdapters", "ITrainerReportTemplateApiPort", "ITrainerReportRequestApiPort", "ITraineeReportRequestApiPort", "ITrainerReportPhotoApiPort", "ITraineeReportPhotoApiPort", "IRecurringReportAssignmentApiPort", "ReportingApiAdapterMappingProfile")
    ];

    private static readonly string[] RemovedTodo13SourcePaths =
    [
        "Task7ApiCompatibility/AppConfigApiCompatibility.cs",
        "Task7ApiCompatibility/Identity/IdentityAdministrationApiCompatibilityAdapters.cs",
        "Task7ApiCompatibility/Identity/IdentityApiCompatibilityAdapters.cs",
        "Task7ApiCompatibility/Identity/IdentityApiCompatibilityContracts.cs",
        "Task7ApiCompatibility/Identity/IdentityApiCompatibilityMappingProfile.cs",
        "Task7ApiCompatibility/PlanningNutrition/Adapters/DietPlanAccountCompatibilityAdapter.cs",
        "Task7ApiCompatibility/PlanningNutrition/Adapters/ManagedPlanAccountCompatibilityAdapter.cs",
        "Task7ApiCompatibility/PlanningNutrition/Adapters/PlanAccountCompatibilityAdapter.cs",
        "Task7ApiCompatibility/PlanningNutrition/Adapters/SupplementationAccountCompatibilityAdapter.cs",
        "Task7ApiCompatibility/PlanningNutrition/Contracts/DietPlanAccountCompatibilityContracts.cs",
        "Task7ApiCompatibility/PlanningNutrition/Contracts/ManagedPlanAccountCompatibilityContracts.cs",
        "Task7ApiCompatibility/PlanningNutrition/Contracts/PlanAccountCompatibilityContracts.cs",
        "Task7ApiCompatibility/PlanningNutrition/Contracts/SupplementationAccountCompatibilityContracts.cs",
        "Task7ApiCompatibility/PlanningNutrition/Mapping/Task7AccountCompatibilityMappingProfile.cs",
        "Task7ApiCompatibility/ServiceCollectionExtensions.cs"
    ];

    private static readonly string[] Todo13OwnerSourcePaths =
    [
        "ApiAdapters/ServiceCollectionExtensions.cs",
        "Coaching/ApiAdapters/ManagedPlanApiAdapter.cs",
        "Coaching/ApiAdapters/ManagedPlanApiAdapterContracts.cs",
        "Coaching/ApiAdapters/ManagedPlanApiAdapterMappingProfile.cs",
        "Identity/ApiAdapters/IdentityAdministrationApiAdapters.cs",
        "Identity/ApiAdapters/IdentityApiAdapterContracts.cs",
        "Identity/ApiAdapters/IdentityApiAdapterMappingProfile.cs",
        "Identity/ApiAdapters/IdentityApiAdapters.cs",
        "Nutrition/ApiAdapters/DietPlanApiAdapter.cs",
        "Nutrition/ApiAdapters/DietPlanApiAdapterContracts.cs",
        "Nutrition/ApiAdapters/NutritionApiAdapterMappingProfile.cs",
        "Nutrition/ApiAdapters/SupplementationApiAdapter.cs",
        "Nutrition/ApiAdapters/SupplementationApiAdapterContracts.cs",
        "Platform/ReferenceData/ApiAdapters/AppConfigApiAdapter.cs",
        "TrainingPlanning/ApiAdapters/PlanApiAdapter.cs",
        "TrainingPlanning/ApiAdapters/PlanApiAdapterContracts.cs",
        "TrainingPlanning/ApiAdapters/PlanApiAdapterMappingProfile.cs"
    ];

    private static readonly OwnerExportRow[] Todo13OwnerExports =
    [
        OwnerExports("LgymApi.Application", "ApplicationApiAdapterServiceCollectionExtensions"),
        OwnerExports("LgymApi.Application.Coaching.ApiAdapters", "IManagedPlanAccountApiAdapter", "ManagedPlanAssignAccountCommand", "ManagedPlanCreateAccountCommand", "ManagedPlanDeleteAccountCommand", "ManagedPlanListAccountQuery", "ManagedPlanUnassignAccountCommand", "ManagedPlanUpdateAccountCommand", "ManagedPlanApiAdapterMappingProfile"),
        OwnerExports("LgymApi.Application.Identity.ApiAdapters", "AccountProfileProjection", "AccountRankProjection", "AdminAccountProjection", "ExternalLoginProjection", "IAccountAccessApiAdapter", "IAccountEloApiAdapter", "IAccountExternalLoginApiAdapter", "IAccountTutorialApiAdapter", "IAdminAccountManagementApiAdapter", "IAuthenticatedAccountApiAdapter", "IRoleManagementApiAdapter", "PermissionClaimProjection", "RoleProjection", "TutorialProgressProjection", "IdentityApiAdapterMappingProfile"),
        OwnerExports("LgymApi.Application.Nutrition.ApiAdapters", "DietPlanActivateAccountCommand", "DietPlanCreateAccountCommand", "DietPlanCurrentAccountQuery", "DietPlanDeleteAccountCommand", "DietPlanGetAccountQuery", "DietPlanHistoryAccountQuery", "DietPlanListAccountQuery", "DietPlanUpdateAccountCommand", "IDietPlanAccountApiAdapter", "ISupplementationApiAdapter", "SupplementCheckOffAccountCommand", "SupplementComplianceAccountQuery", "SupplementPlanAssignAccountCommand", "SupplementPlanCreateAccountCommand", "SupplementPlanDeleteAccountCommand", "SupplementPlanListAccountQuery", "SupplementPlanUnassignAccountCommand", "SupplementPlanUpdateAccountCommand", "SupplementScheduleAccountQuery", "NutritionApiAdapterMappingProfile"),
        OwnerExports("LgymApi.Application.Platform.ReferenceData.ApiAdapters", "IAppConfigApiAdapter"),
        OwnerExports("LgymApi.Application.TrainingPlanning.ApiAdapters", "IPlanAccountApiAdapter", "PlanCopyAccountCommand", "PlanCreateAccountCommand", "PlanDeleteAccountCommand", "PlanGenerateShareCodeAccountCommand", "PlanGetConfigAccountQuery", "PlanGetListAccountQuery", "PlanHasAccountQuery", "PlanSetActiveAccountCommand", "PlanUpdateAccountCommand", "PlanApiAdapterMappingProfile")
    ];

    private static readonly string[] Todo13OwnerAdapterContracts =
    [
        "LgymApi.Application.Coaching.ApiAdapters.IManagedPlanAccountApiAdapter",
        "LgymApi.Application.Identity.ApiAdapters.IAccountAccessApiAdapter",
        "LgymApi.Application.Identity.ApiAdapters.IAccountEloApiAdapter",
        "LgymApi.Application.Identity.ApiAdapters.IAccountExternalLoginApiAdapter",
        "LgymApi.Application.Identity.ApiAdapters.IAccountTutorialApiAdapter",
        "LgymApi.Application.Identity.ApiAdapters.IAdminAccountManagementApiAdapter",
        "LgymApi.Application.Identity.ApiAdapters.IAuthenticatedAccountApiAdapter",
        "LgymApi.Application.Identity.ApiAdapters.IRoleManagementApiAdapter",
        "LgymApi.Application.Nutrition.ApiAdapters.IDietPlanAccountApiAdapter",
        "LgymApi.Application.Nutrition.ApiAdapters.ISupplementationApiAdapter",
        "LgymApi.Application.Platform.ReferenceData.ApiAdapters.IAppConfigApiAdapter",
        "LgymApi.Application.TrainingPlanning.ApiAdapters.IPlanAccountApiAdapter"
    ];

    private static readonly string[] Todo13OwnerImplementationTypes =
    [
        "LgymApi.Application.Coaching.ApiAdapters.ManagedPlanApiAdapter",
        "LgymApi.Application.Identity.ApiAdapters.AccountAccessApiAdapter",
        "LgymApi.Application.Identity.ApiAdapters.AccountEloApiAdapter",
        "LgymApi.Application.Identity.ApiAdapters.AccountExternalLoginApiAdapter",
        "LgymApi.Application.Identity.ApiAdapters.AccountTutorialApiAdapter",
        "LgymApi.Application.Identity.ApiAdapters.AdminAccountManagementApiAdapter",
        "LgymApi.Application.Identity.ApiAdapters.AuthenticatedAccountApiAdapter",
        "LgymApi.Application.Identity.ApiAdapters.RoleManagementApiAdapter",
        "LgymApi.Application.Nutrition.ApiAdapters.DietPlanApiAdapter",
        "LgymApi.Application.Nutrition.ApiAdapters.SupplementationApiAdapter",
        "LgymApi.Application.Platform.ReferenceData.ApiAdapters.AppConfigApiAdapter",
        "LgymApi.Application.TrainingPlanning.ApiAdapters.PlanApiAdapter"
    ];

    private static readonly PartialRow[] PartialContributors =
    [
        Partial("Application", "LgymApi.Application.Features.Reporting.ReportingService", "Features/Reporting", "ReportingService.cs", "ReportingService.Templates.cs", "ReportingService.Submissions.cs", "ReportingService.Submissions.Read.cs", "ReportingService.Submissions.PhotoValidation.cs", "ReportingService.Submissions.Helpers.cs", "ReportingService.Requests.cs", "ReportingService.Photos.cs", "ReportingService.Photos.Support.cs", "ReportingService.Photos.Completion.cs", "ReportingService.Photos.Read.cs"),
        Partial("Application", "LgymApi.Application.Features.Reporting.RecurringReportAssignmentService", "Features/Reporting", "RecurringReportAssignmentService.cs", "RecurringReportAssignmentService.Support.cs", "RecurringReportAssignmentService.Processing.cs", "RecurringReportAssignmentService.RequestNow.cs"),
        Partial("Application", "LgymApi.Application.WorkoutProgress.ProgressData.WorkoutProgressReadWriteService", "WorkoutProgress/ProgressData", "WorkoutProgressReadWriteService.cs", "WorkoutProgressReadWriteService.Measurements.cs", "WorkoutProgressReadWriteService.MainRecords.cs"),
        Partial("Application", "LgymApi.Application.Features.Exercise.ExerciseService", "Exercise", "ExerciseService.cs", "ExerciseService.Scores.cs", "ExerciseService.Queries.cs", "ExerciseService.Management.cs"),
        Partial("Application", "LgymApi.Application.Features.Training.TrainingService", "Training", "TrainingService.cs", "TrainingService.Queries.cs", "TrainingService.AddTraining.cs"),
        Partial("Worker", "LgymApi.BackgroundWorker.BackgroundActionOrchestratorService", "", "BackgroundActionOrchestratorService.cs", "BackgroundActionOrchestratorService.HandlerInvocation.cs")
    ];

    [Test]
    public void ApplicationDependencyAggregateCutover_Removes_The_Exact_Seven_Aggregates()
    {
        Assert.That(RemovedApplicationDependencyAggregates, Has.Length.EqualTo(7));
        ValidateRemovedApplicationAggregateRows(RemovedApplicationDependencyAggregates);
    }

    [Test]
    public void IdentityDependencyAggregateCutover_Removes_The_Exact_Five_Aggregates()
    {
        Assert.That(RemovedIdentityDependencyAggregates, Has.Length.EqualTo(5));
        ValidateRemovedIdentityAggregateRows(RemovedIdentityDependencyAggregates);
    }

    [Test]
    public void WorkoutProgressApiHandoff_RemovesFivePureWrappersAndRetainsTwoOwnerAdapters()
    {
        Assert.That(RemovedWorkoutProgressApiWrappers, Has.Length.EqualTo(5));
        Assert.That(RelocatedWorkoutProgressApiAdapters, Has.Length.EqualTo(2));

        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var applicationAssembly = Assembly.Load("LgymApi.Application");

        foreach (var wrapper in RemovedWorkoutProgressApiWrappers)
        {
            Assert.That(File.Exists(Path.Combine(root, "LgymApi.Application", wrapper.SourcePath)), Is.False, wrapper.SourcePath);
            Assert.That(applicationAssembly.GetType(wrapper.MetadataName, throwOnError: false), Is.Null, wrapper.MetadataName);
        }

        foreach (var adapter in RelocatedWorkoutProgressApiAdapters)
        {
            Assert.That(File.Exists(Path.Combine(root, "LgymApi.Application", adapter.PreviousSourcePath)), Is.False, adapter.PreviousSourcePath);
            Assert.That(applicationAssembly.GetType(adapter.PreviousMetadataName, throwOnError: false), Is.Null, adapter.PreviousMetadataName);
            Assert.That(File.Exists(Path.Combine(root, "LgymApi.Application", adapter.SourcePath)), Is.True, adapter.SourcePath);
            Assert.That(applicationAssembly.GetType(adapter.MetadataName, throwOnError: false), Is.Not.Null, adapter.MetadataName);
        }
    }

    [Test]
    public void CoachingAndReportingApiAdapterRelocation_RemovesCompatibilityNamespacesAndPublishesElevenOwnerPorts()
    {
        Assert.That(RemovedTodo14SourcePaths, Has.Length.EqualTo(9));
        Assert.That(Todo14OwnerSourcePaths, Has.Length.EqualTo(9));
        Assert.That(Todo14OwnerAdapterContracts, Has.Length.EqualTo(11));
        Assert.That(Todo14OwnerImplementationTypes, Has.Length.EqualTo(11));

        EnsureUnique(RemovedTodo14SourcePaths, "removed Todo 14 source path");
        EnsureUnique(Todo14OwnerSourcePaths, "Todo 14 owner source path");
        EnsureUnique(Todo14OwnerAdapterContracts, "Todo 14 owner adapter contract");
        EnsureUnique(Todo14OwnerImplementationTypes, "Todo 14 owner implementation type");

        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var applicationRoot = Path.Combine(root, "LgymApi.Application");
        var applicationAssembly = Assembly.Load("LgymApi.Application");
        var exportedTypeNames = applicationAssembly.GetExportedTypes()
            .Select(type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var sourcePath in RemovedTodo14SourcePaths)
        {
            Assert.That(File.Exists(Path.Combine(applicationRoot, sourcePath)), Is.False, sourcePath);
        }

        Assert.That(
            applicationAssembly.GetTypes().Where(type => type.FullName?.StartsWith("LgymApi.Application.Coaching.Compatibility.", StringComparison.Ordinal) == true
                || type.FullName?.StartsWith("LgymApi.Application.Reporting.Compatibility.", StringComparison.Ordinal) == true).Select(type => type.FullName).ToArray(),
            Is.Empty,
            "A removed Coaching or Reporting Compatibility metadata identity still compiles.");

        foreach (var sourcePath in Todo14OwnerSourcePaths)
        {
            Assert.That(File.Exists(Path.Combine(applicationRoot, sourcePath)), Is.True, sourcePath);
        }

        foreach (var contract in Todo14OwnerAdapterContracts)
        {
            var contractType = applicationAssembly.GetType(contract, throwOnError: false);
            Assert.That(contractType?.IsInterface, Is.True, contract);
            Assert.That(exportedTypeNames, Does.Contain(contract), contract);
        }

        foreach (var implementation in Todo14OwnerImplementationTypes)
        {
            var implementationType = applicationAssembly.GetType(implementation, throwOnError: false);
            Assert.That(implementationType, Is.Not.Null, implementation);
            Assert.That(implementationType!.IsPublic, Is.False, implementation);
        }

        var expectedExports = Todo14OwnerExports
            .SelectMany(row => row.TypeNames.Select(typeName => $"{row.Namespace}.{typeName}"))
            .ToArray();
        EnsureUnique(expectedExports, "Todo 14 owner export");
        Assert.That(expectedExports, Has.Length.EqualTo(13));
        Assert.That(expectedExports.Where(exportedTypeNames.Contains).ToArray(), Has.Length.EqualTo(13));
    }

    [Test]
    public void Todo13ApiAdapterRelocation_RemovesEveryOldIdentityAndPublishesEveryOwnerExport()
    {
        Assert.That(RemovedTodo13SourcePaths, Has.Length.EqualTo(15));
        Assert.That(Todo13OwnerSourcePaths, Has.Length.EqualTo(17));
        Assert.That(Todo13OwnerAdapterContracts, Has.Length.EqualTo(12));
        Assert.That(Todo13OwnerImplementationTypes, Has.Length.EqualTo(12));

        EnsureUnique(RemovedTodo13SourcePaths, "removed Todo 13 source path");
        EnsureUnique(Todo13OwnerSourcePaths, "Todo 13 owner source path");
        EnsureUnique(Todo13OwnerAdapterContracts, "Todo 13 owner adapter contract");
        EnsureUnique(Todo13OwnerImplementationTypes, "Todo 13 owner implementation type");

        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var applicationRoot = Path.Combine(root, "LgymApi.Application");
        var applicationAssembly = Assembly.Load("LgymApi.Application");
        var compiledTypes = applicationAssembly.GetTypes();
        var exportedTypeNames = applicationAssembly.GetExportedTypes()
            .Select(type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var sourcePath in RemovedTodo13SourcePaths)
        {
            Assert.That(File.Exists(Path.Combine(applicationRoot, sourcePath)), Is.False, sourcePath);
        }

        Assert.That(Directory.Exists(Path.Combine(applicationRoot, "Task7ApiCompatibility")), Is.False);
        Assert.That(
            compiledTypes.Where(type => IsRemovedTodo13Identity(type.FullName)).Select(type => type.FullName).ToArray(),
            Is.Empty,
            "A removed Task7, ApiCompatibility, or Compatibility.Task7 metadata identity still compiles.");

        foreach (var sourcePath in Todo13OwnerSourcePaths)
        {
            Assert.That(File.Exists(Path.Combine(applicationRoot, sourcePath)), Is.True, sourcePath);
        }

        var expectedExports = Todo13OwnerExports
            .SelectMany(row => row.TypeNames.Select(typeName => $"{row.Namespace}.{typeName}"))
            .ToArray();
        EnsureUnique(expectedExports, "Todo 13 owner export");
        Assert.That(expectedExports, Has.Length.EqualTo(56));
        Assert.That(expectedExports.Where(exportedTypeNames.Contains).ToArray(), Has.Length.EqualTo(56));

        foreach (var contract in Todo13OwnerAdapterContracts)
        {
            var contractType = applicationAssembly.GetType(contract, throwOnError: false);
            Assert.That(contractType, Is.Not.Null, contract);
            Assert.That(contractType!.IsInterface, Is.True, contract);
            Assert.That(exportedTypeNames, Does.Contain(contract), contract);
        }

        foreach (var implementation in Todo13OwnerImplementationTypes)
        {
            var implementationType = applicationAssembly.GetType(implementation, throwOnError: false);
            Assert.That(implementationType, Is.Not.Null, implementation);
            Assert.That(implementationType!.IsPublic, Is.False, implementation);
        }

        Assert.That(
            applicationAssembly.GetType("LgymApi.Application.ApiAdapterServiceCollectionExtensions", throwOnError: false),
            Is.Null,
            "The transitional Application API-adapter facade type must not compile.");
        var facadeType = applicationAssembly.GetType(
            "LgymApi.Application.ApplicationApiAdapterServiceCollectionExtensions",
            throwOnError: true)!;
        Assert.That(
            facadeType.GetMethod("AddTask7ApiCompatibility", BindingFlags.Public | BindingFlags.Static),
            Is.Null,
            "The transitional Application API-adapter facade method must not compile.");
        Assert.That(
            facadeType.GetMethod("AddApplicationApiAdapters", BindingFlags.Public | BindingFlags.Static),
            Is.Not.Null,
            "The owner-neutral Application API-adapter facade method must compile.");
    }

    [Test]
    public void PartialServiceLedger_Resolves_All_Approved_Compiled_Contributors()
    {
        Assert.That(PartialContributors.Sum(row => row.Files.Length), Is.EqualTo(27));
        Assert.That(PartialContributors.Select(row => row.Files.Length), Is.EqualTo(new[] { 11, 4, 3, 4, 3, 2 }));
        ValidatePartialRows(PartialContributors);
    }

    [Test]
    public void LegacyPasswordServiceFactory_Is_Absent_While_The_Live_Password_Service_Remains_Scoped()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var factoryPath = Path.Combine(root, "LgymApi.Identity", "Services", "LegacyPasswordServiceFactory.cs");

        Assert.That(File.Exists(factoryPath), Is.False, "The retired factory source must remain absent.");
        Assert.That(typeof(ILegacyPasswordService).Assembly.GetType("LgymApi.Application.Services.LegacyPasswordServiceFactory", throwOnError: false), Is.Null);

        var services = new ServiceCollection();
        services.AddIdentityModule();
        var descriptor = services.Single(candidate => candidate.ServiceType == typeof(ILegacyPasswordService));

        Assert.That(descriptor.Lifetime, Is.EqualTo(ServiceLifetime.Scoped));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.That(scope.ServiceProvider.GetRequiredService<ILegacyPasswordService>().GetType().FullName, Is.EqualTo("LgymApi.Infrastructure.Services.LegacyPasswordService"));
    }

    [Test]
    public void CompiledModuleExportInventory_Has_The_PostApplicationCutover_Baseline()
    {
        var expectedCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["LgymApi.Platform"] = 69,
            ["LgymApi.Identity"] = 68,
            ["LgymApi.TrainingPlanning"] = 69,
            ["LgymApi.Notifications"] = 76,
            ["LgymApi.Application"] = 476
        };

        var inventory = expectedCounts.ToDictionary(
            pair => pair.Key,
            pair => Assembly.Load(pair.Key).GetExportedTypes().Select(type => type.FullName!).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);

        foreach (var expected in expectedCounts)
        {
            Assert.That(inventory[expected.Key], Has.Length.EqualTo(expected.Value), expected.Key);
        }

        TestContext.Progress.WriteLine(string.Join(Environment.NewLine, inventory.Select(pair => $"{pair.Key}={pair.Value.Length}")));
    }

    [Test]
    public void RemovalLedgerFixtures_Reject_Duplicate_Present_And_Stale_Rows_With_Precise_Diagnostics()
    {
        var duplicate = RemovedApplicationDependencyAggregates.Append(RemovedApplicationDependencyAggregates[0]).ToArray();
        var duplicateError = Assert.Throws<InvalidOperationException>(() => ValidateRemovedApplicationAggregateRows(duplicate));
        Assert.That(duplicateError!.Message, Does.Contain("Duplicate removed application dependency aggregate ledger key"));

        var present = RemovedApplicationDependencyAggregates.Select(row => row with { SourcePath = "WorkoutProgress/ServiceCollectionExtensions.cs" }).ToArray();
        var presentError = Assert.Throws<AssertionException>(() => ValidateRemovedApplicationAggregateRows(present));
        Assert.That(presentError!.Message, Does.Contain("Removed dependency aggregate source still exists"));

        var stale = RemovedApplicationDependencyAggregates.Select((row, index) => index == 0 ? row with { DependencyType = "LgymApi.Application.Features.Reporting.ReportingService" } : row).ToArray();
        var staleError = Assert.Throws<AssertionException>(() => ValidateRemovedApplicationAggregateRows(stale));
        Assert.That(staleError!.Message, Does.Contain("Removed dependency aggregate still compiles"));
    }

    private static void ValidateRemovedApplicationAggregateRows(IEnumerable<RemovedAggregateRow> rows)
    {
        var materialized = rows.ToArray();
        EnsureUnique(materialized.Select(row => row.DependencyType), "removed application dependency aggregate ledger key");
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var applicationAssembly = Assembly.Load("LgymApi.Application");
        var (_, _, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Application");

        foreach (var row in materialized)
        {
            var sourcePath = Path.Combine(root, "LgymApi.Application", row.SourcePath);
            Assert.That(File.Exists(sourcePath), Is.False, $"Removed dependency aggregate source still exists: {row.SourcePath}");
            Assert.That(applicationAssembly.GetType(row.DependencyType), Is.Null, $"Removed dependency aggregate still compiles: {row.DependencyType}");

            var aggregateName = row.DependencyType[(row.DependencyType.LastIndexOf('.') + 1)..];
            var references = syntaxTrees
                .Where(tree => tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.SimpleNameSyntax>()
                    .Any(name => name.Identifier.ValueText == aggregateName))
                .Select(tree => Path.GetRelativePath(root, tree.FilePath))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.That(references, Is.Empty, $"Removed dependency aggregate '{aggregateName}' is still referenced by: {string.Join(", ", references)}");

            var facadePath = Path.Combine(root, "LgymApi.Application", row.FacadePath);
            Assert.That(File.Exists(facadePath), Is.True, $"Missing application facade: {row.FacadePath}");
            Assert.That(File.ReadAllText(facadePath), Does.Not.Contain(aggregateName), $"Removed dependency aggregate remains registered by: {row.FacadePath}");
        }
    }

    private static void ValidateRemovedIdentityAggregateRows(IEnumerable<RemovedAggregateRow> rows)
    {
        var materialized = rows.ToArray();
        EnsureUnique(materialized.Select(row => row.DependencyType), "removed identity dependency aggregate ledger key");
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var identityAssembly = Assembly.Load("LgymApi.Identity");
        var (_, _, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Identity");

        foreach (var row in materialized)
        {
            var sourcePath = Path.Combine(root, "LgymApi.Identity", row.SourcePath);
            Assert.That(File.Exists(sourcePath), Is.False, $"Removed dependency aggregate source still exists: {row.SourcePath}");
            Assert.That(identityAssembly.GetType(row.DependencyType), Is.Null, $"Removed dependency aggregate still compiles: {row.DependencyType}");

            var aggregateName = row.DependencyType[(row.DependencyType.LastIndexOf('.') + 1)..];
            var references = syntaxTrees
                .Where(tree => tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.SimpleNameSyntax>()
                    .Any(name => name.Identifier.ValueText == aggregateName))
                .Select(tree => Path.GetRelativePath(root, tree.FilePath))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.That(references, Is.Empty, $"Removed dependency aggregate '{aggregateName}' is still referenced by: {string.Join(", ", references)}");

            var facadePath = Path.Combine(root, "LgymApi.Identity", row.FacadePath);
            Assert.That(File.Exists(facadePath), Is.True, $"Missing identity facade: {row.FacadePath}");
            Assert.That(File.ReadAllText(facadePath), Does.Not.Contain(aggregateName), $"Removed dependency aggregate remains registered by: {row.FacadePath}");
        }
    }

    private static void ValidatePartialRows(IEnumerable<PartialRow> rows)
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        EnsureUnique(rows.Select(row => row.TypeName), "partial-service ledger type");
        foreach (var row in rows)
        {
            var assembly = Assembly.Load(row.AssemblyName);
            _ = assembly.GetType(row.TypeName) ?? throw new InvalidOperationException($"Stale partial-service compiled symbol '{row.TypeName}'.");
            foreach (var file in row.Files)
            {
                EnsurePath(root, row.Project, Path.Combine(row.Directory, file), "partial-service source");
            }
        }
    }

    private static void EnsureUnique(IEnumerable<string> values, string kind)
    {
        var duplicates = values.GroupBy(value => value, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (duplicates.Length > 0) throw new InvalidOperationException($"Duplicate {kind}: {string.Join(", ", duplicates)}.");
    }

    private static void EnsurePath(string root, string project, string relativePath, string kind)
    {
        var path = Path.Combine(root, project, relativePath);
        if (!File.Exists(path)) throw new InvalidOperationException($"Missing {kind} path '{project}/{relativePath}'.");
    }

    private static RemovedAggregateRow RemovedApplication(string dependency, string source, string facade) => new(dependency, source, facade);
    private static RemovedAggregateRow RemovedIdentity(string dependency, string source) => new(dependency, source, "IdentityModule.cs");
    private static RemovedApiWrapperRow RemovedWorkoutProgressApiWrapper(string metadataName, string sourcePath) => new(metadataName, sourcePath);
    private static RelocatedApiAdapterRow RelocatedWorkoutProgressApiAdapter(string previousMetadataName, string previousSourcePath, string metadataName, string sourcePath) => new(previousMetadataName, previousSourcePath, metadataName, sourcePath);
    private static OwnerExportRow OwnerExports(string @namespace, params string[] typeNames) => new(@namespace, typeNames);
    private static PartialRow Partial(string assembly, string type, string directory, params string[] files) => new(assembly == "Worker" ? "LgymApi.BackgroundWorker" : "LgymApi.Application", assembly == "Worker" ? "LgymApi.BackgroundWorker" : "LgymApi.Application", type, directory, files);

    private static bool IsRemovedTodo13Identity(string? metadataName)
        => metadataName is not null
            && (metadataName.StartsWith("LgymApi.Application.Identity.ApiCompatibility.", StringComparison.Ordinal)
                || metadataName.StartsWith("LgymApi.Application.Identity.Compatibility.Task7.", StringComparison.Ordinal)
                || metadataName.StartsWith("LgymApi.Application.Task7ApiCompatibility.", StringComparison.Ordinal)
                || metadataName == "LgymApi.Application.Task7ApiCompatibilityServiceCollectionExtensions");

    private sealed record RemovedAggregateRow(string DependencyType, string SourcePath, string FacadePath);
    private sealed record RemovedApiWrapperRow(string MetadataName, string SourcePath);
    private sealed record RelocatedApiAdapterRow(string PreviousMetadataName, string PreviousSourcePath, string MetadataName, string SourcePath);
    private sealed record OwnerExportRow(string Namespace, string[] TypeNames);
    private sealed record PartialRow(string AssemblyName, string Project, string TypeName, string Directory, string[] Files);
}
