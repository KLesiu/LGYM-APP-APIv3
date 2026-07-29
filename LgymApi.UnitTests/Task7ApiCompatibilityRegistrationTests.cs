using FluentAssertions;
using LgymApi.Application;
using LgymApi.Application.Notifications;
using LgymApi.Notifications.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class Task7ApiCompatibilityRegistrationTests
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedRegistrations =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LgymApi.Application.Identity.ApiCompatibility.IAuthenticatedAccountApiAdapter"] = "LgymApi.Application.Identity.ApiCompatibility.AuthenticatedAccountApiAdapter",
            ["LgymApi.Application.Identity.ApiCompatibility.IAccountAccessApiAdapter"] = "LgymApi.Application.Identity.ApiCompatibility.AccountAccessApiAdapter",
            ["LgymApi.Application.Identity.ApiCompatibility.IAccountEloApiAdapter"] = "LgymApi.Application.Identity.ApiCompatibility.AccountEloApiAdapter",
            ["LgymApi.Application.Identity.ApiCompatibility.IAccountExternalLoginApiAdapter"] = "LgymApi.Application.Identity.ApiCompatibility.AccountExternalLoginApiAdapter",
            ["LgymApi.Application.Identity.ApiCompatibility.IAccountPushInstallationApiAdapter"] = "LgymApi.Application.Identity.ApiCompatibility.AccountPushInstallationApiAdapter",
            ["LgymApi.Application.Identity.ApiCompatibility.IAccountTutorialApiAdapter"] = "LgymApi.Application.Identity.ApiCompatibility.AccountTutorialApiAdapter",
            ["LgymApi.Application.Identity.ApiCompatibility.IAdminAccountManagementApiAdapter"] = "LgymApi.Application.Identity.ApiCompatibility.AdminAccountManagementApiAdapter",
            ["LgymApi.Application.Identity.ApiCompatibility.IRoleManagementApiAdapter"] = "LgymApi.Application.Identity.ApiCompatibility.RoleManagementApiAdapter",
            ["LgymApi.Application.Identity.Compatibility.Task7.Contracts.IPlanAccountCompatibilityAdapter"] = "LgymApi.Application.Identity.Compatibility.Task7.Adapters.PlanAccountCompatibilityAdapter",
            ["LgymApi.Application.Identity.Compatibility.Task7.Contracts.IManagedPlanAccountCompatibilityAdapter"] = "LgymApi.Application.Identity.Compatibility.Task7.Adapters.ManagedPlanAccountCompatibilityAdapter",
            ["LgymApi.Application.Identity.Compatibility.Task7.Contracts.IDietPlanAccountCompatibilityAdapter"] = "LgymApi.Application.Identity.Compatibility.Task7.Adapters.DietPlanAccountCompatibilityAdapter",
            ["LgymApi.Application.Identity.Compatibility.Task7.Contracts.ISupplementationAccountCompatibilityAdapter"] = "LgymApi.Application.Identity.Compatibility.Task7.Adapters.SupplementationAccountCompatibilityAdapter",
            ["LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.IGymApiCompatibilityService"] = "LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.GymApiCompatibilityService",
            ["LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.IMeasurementsApiCompatibilityService"] = "LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.MeasurementsApiCompatibilityService",
            ["LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.IExerciseApiCompatibilityService"] = "LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.ExerciseApiCompatibilityService",
            ["LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.IExerciseScoresApiCompatibilityService"] = "LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.ExerciseScoresApiCompatibilityService",
            ["LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.ITrainingApiCompatibilityService"] = "LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.TrainingApiCompatibilityService",
            ["LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.IMainRecordsApiCompatibilityService"] = "LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.MainRecordsApiCompatibilityService",
            ["LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.IEloRegistryApiCompatibilityService"] = "LgymApi.Application.Task7ApiCompatibility.WorkoutProgress.EloRegistryApiCompatibilityService",
            ["LgymApi.Application.Task7ApiCompatibility.IAppConfigApiCompatibilityAdapter"] = "LgymApi.Application.Task7ApiCompatibility.AppConfigApiCompatibilityAdapter",
            ["LgymApi.Application.Task7ApiCompatibility.IInAppNotificationApiCompatibilityAdapter"] = "LgymApi.Application.Task7ApiCompatibility.InAppNotificationApiCompatibilityAdapter",
            ["LgymApi.Application.Task7ApiCompatibility.INotificationEventApiCompatibilityAdapter"] = "LgymApi.Application.Task7ApiCompatibility.NotificationEventApiCompatibilityAdapter",
            ["LgymApi.Application.Coaching.Compatibility.ITrainerInvitationApiPort"] = "LgymApi.Application.Coaching.Compatibility.TrainerInvitationApiAdapter",
            ["LgymApi.Application.Coaching.Compatibility.ITrainerDashboardProgressApiPort"] = "LgymApi.Application.Coaching.Compatibility.TrainerDashboardProgressApiAdapter",
            ["LgymApi.Application.Coaching.Compatibility.ITrainerTraineeNotesApiPort"] = "LgymApi.Application.Coaching.Compatibility.TrainerTraineeNotesApiAdapter",
            ["LgymApi.Application.Coaching.Compatibility.ITraineeNotesApiPort"] = "LgymApi.Application.Coaching.Compatibility.TraineeNotesApiAdapter",
            ["LgymApi.Application.Coaching.Compatibility.ITraineeRelationshipApiPort"] = "LgymApi.Application.Coaching.Compatibility.TraineeRelationshipApiAdapter",
            ["LgymApi.Application.Reporting.Compatibility.ITrainerReportTemplateApiPort"] = "LgymApi.Application.Reporting.Compatibility.TrainerReportTemplateApiAdapter",
            ["LgymApi.Application.Reporting.Compatibility.ITrainerReportRequestApiPort"] = "LgymApi.Application.Reporting.Compatibility.TrainerReportRequestApiAdapter",
            ["LgymApi.Application.Reporting.Compatibility.ITraineeReportRequestApiPort"] = "LgymApi.Application.Reporting.Compatibility.TraineeReportRequestApiAdapter",
            ["LgymApi.Application.Reporting.Compatibility.ITrainerReportPhotoApiPort"] = "LgymApi.Application.Reporting.Compatibility.TrainerReportPhotoApiAdapter",
            ["LgymApi.Application.Reporting.Compatibility.ITraineeReportPhotoApiPort"] = "LgymApi.Application.Reporting.Compatibility.TraineeReportPhotoApiAdapter",
            ["LgymApi.Application.Reporting.Compatibility.IRecurringReportAssignmentApiPort"] = "LgymApi.Application.Reporting.Compatibility.RecurringReportAssignmentApiAdapter"
        };

    [Test]
    public void ApiAdapterFacades_RegisterEveryTask7PortExactlyOnceAsScoped()
    {
        var services = new ServiceCollection();
        services.AddTask7ApiCompatibility();
        services.AddNotificationsApiAdapters();

        foreach (var expected in ExpectedRegistrations)
        {
            var descriptor = services.Where(candidate => candidate.ServiceType.FullName == expected.Key).Should().ContainSingle().Subject;
            descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped, expected.Key);
            descriptor.ImplementationType.Should().NotBeNull();
            descriptor.ImplementationType!.FullName.Should().Be(expected.Value);
        }

        var discoveredContracts = new[]
            {
                typeof(ServiceCollectionExtensions).Assembly,
                typeof(NotificationReference).Assembly
            }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsPublic && type.IsInterface && IsTask7CompatibilityNamespace(type.Namespace))
            .Select(type => type.FullName!)
            .OrderBy(typeName => typeName, StringComparer.Ordinal)
            .ToArray();

        discoveredContracts.Should().Equal(ExpectedRegistrations.Keys.OrderBy(typeName => typeName, StringComparer.Ordinal));
    }

    private static bool IsTask7CompatibilityNamespace(string? namespaceName)
        => namespaceName is not null
            && (namespaceName.StartsWith("LgymApi.Application.Identity.ApiCompatibility", StringComparison.Ordinal)
                || namespaceName.StartsWith("LgymApi.Application.Identity.Compatibility.Task7", StringComparison.Ordinal)
                || namespaceName.StartsWith("LgymApi.Application.Task7ApiCompatibility", StringComparison.Ordinal)
                || namespaceName.StartsWith("LgymApi.Application.Coaching.Compatibility", StringComparison.Ordinal)
                || namespaceName.StartsWith("LgymApi.Application.Reporting.Compatibility", StringComparison.Ordinal));
}
