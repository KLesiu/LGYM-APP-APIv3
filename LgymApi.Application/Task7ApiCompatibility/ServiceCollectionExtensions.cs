using LgymApi.Application.Coaching.Compatibility;
using LgymApi.Application.Identity.ApiCompatibility;
using LgymApi.Application.Identity.Compatibility.Task7.Adapters;
using LgymApi.Application.Identity.Compatibility.Task7.Contracts;
using LgymApi.Application.Reporting.Compatibility;
using LgymApi.Application.Task7ApiCompatibility;
using LgymApi.Application.Task7ApiCompatibility.WorkoutProgress;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Application;

public static class Task7ApiCompatibilityServiceCollectionExtensions
{
    public static IServiceCollection AddTask7ApiCompatibility(this IServiceCollection services)
    {
        services.AddScoped<IAuthenticatedAccountApiAdapter, AuthenticatedAccountApiAdapter>();
        services.AddScoped<IAccountAccessApiAdapter, AccountAccessApiAdapter>();
        services.AddScoped<IAccountEloApiAdapter, AccountEloApiAdapter>();
        services.AddScoped<IAccountExternalLoginApiAdapter, AccountExternalLoginApiAdapter>();
        services.AddScoped<IAccountTutorialApiAdapter, AccountTutorialApiAdapter>();
        services.AddScoped<IAdminAccountManagementApiAdapter, AdminAccountManagementApiAdapter>();
        services.AddScoped<IRoleManagementApiAdapter, RoleManagementApiAdapter>();
        services.AddScoped<IPlanAccountCompatibilityAdapter, PlanAccountCompatibilityAdapter>();
        services.AddScoped<IManagedPlanAccountCompatibilityAdapter, ManagedPlanAccountCompatibilityAdapter>();
        services.AddScoped<IDietPlanAccountCompatibilityAdapter, DietPlanAccountCompatibilityAdapter>();
        services.AddScoped<ISupplementationAccountCompatibilityAdapter, SupplementationAccountCompatibilityAdapter>();
        services.AddScoped<IGymApiCompatibilityService, GymApiCompatibilityService>();
        services.AddScoped<IMeasurementsApiCompatibilityService, MeasurementsApiCompatibilityService>();
        services.AddScoped<IExerciseApiCompatibilityService, ExerciseApiCompatibilityService>();
        services.AddScoped<IExerciseScoresApiCompatibilityService, ExerciseScoresApiCompatibilityService>();
        services.AddScoped<ITrainingApiCompatibilityService, TrainingApiCompatibilityService>();
        services.AddScoped<IMainRecordsApiCompatibilityService, MainRecordsApiCompatibilityService>();
        services.AddScoped<IEloRegistryApiCompatibilityService, EloRegistryApiCompatibilityService>();
        services.AddScoped<IAppConfigApiCompatibilityAdapter, AppConfigApiCompatibilityAdapter>();
        services.AddScoped<ITrainerInvitationApiPort, TrainerInvitationApiAdapter>();
        services.AddScoped<ITrainerDashboardProgressApiPort, TrainerDashboardProgressApiAdapter>();
        services.AddScoped<ITrainerTraineeNotesApiPort, TrainerTraineeNotesApiAdapter>();
        services.AddScoped<ITraineeNotesApiPort, TraineeNotesApiAdapter>();
        services.AddScoped<ITraineeRelationshipApiPort, TraineeRelationshipApiAdapter>();
        services.AddScoped<ITrainerReportTemplateApiPort, TrainerReportTemplateApiAdapter>();
        services.AddScoped<ITrainerReportRequestApiPort, TrainerReportRequestApiAdapter>();
        services.AddScoped<ITraineeReportRequestApiPort, TraineeReportRequestApiAdapter>();
        services.AddScoped<ITrainerReportPhotoApiPort, TrainerReportPhotoApiAdapter>();
        services.AddScoped<ITraineeReportPhotoApiPort, TraineeReportPhotoApiAdapter>();
        services.AddScoped<IRecurringReportAssignmentApiPort, RecurringReportAssignmentApiAdapter>();

        return services;
    }
}
