using LgymApi.Application.Coaching.ApiAdapters;
using LgymApi.Application.Identity.ApiAdapters;
using LgymApi.Application.TrainingPlanning.ApiAdapters;
using LgymApi.Application.Nutrition.ApiAdapters;
using LgymApi.Application.Platform.ReferenceData.ApiAdapters;
using LgymApi.Application.Reporting.ApiAdapters;
using LgymApi.Application.WorkoutProgress.ApiAdapters;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Application;

public static class ApplicationApiAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationApiAdapters(this IServiceCollection services)
    {
        services.AddScoped<IAuthenticatedAccountApiAdapter, AuthenticatedAccountApiAdapter>();
        services.AddScoped<IAccountAccessApiAdapter, AccountAccessApiAdapter>();
        services.AddScoped<IAccountEloApiAdapter, AccountEloApiAdapter>();
        services.AddScoped<IAccountExternalLoginApiAdapter, AccountExternalLoginApiAdapter>();
        services.AddScoped<IAccountTutorialApiAdapter, AccountTutorialApiAdapter>();
        services.AddScoped<IAdminAccountManagementApiAdapter, AdminAccountManagementApiAdapter>();
        services.AddScoped<IRoleManagementApiAdapter, RoleManagementApiAdapter>();
        services.AddScoped<IPlanAccountApiAdapter, PlanApiAdapter>();
        services.AddScoped<IManagedPlanAccountApiAdapter, ManagedPlanApiAdapter>();
        services.AddScoped<IDietPlanAccountApiAdapter, DietPlanApiAdapter>();
        services.AddScoped<ISupplementationApiAdapter, SupplementationApiAdapter>();
        services.AddScoped<IExerciseApiAdapter, ExerciseApiAdapter>();
        services.AddScoped<IMainRecordsApiAdapter, MainRecordsApiAdapter>();
        services.AddScoped<IAppConfigApiAdapter, AppConfigApiAdapter>();
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
