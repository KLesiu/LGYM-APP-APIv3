using LgymApi.Application.Nutrition.DietPlans.ActivateTraineePlan;
using LgymApi.Application.Nutrition.DietPlans.ActivateTraineePlan.Contracts;
using LgymApi.Application.Nutrition.DietPlans;
using LgymApi.Application.Nutrition.DietPlans.CreateTraineePlan;
using LgymApi.Application.Nutrition.DietPlans.DeleteTraineePlan;
using LgymApi.Application.Nutrition.DietPlans.DeleteTraineePlan.Contracts;
using LgymApi.Application.Nutrition.DietPlans.GetCurrentDietPlan;
using LgymApi.Application.Nutrition.DietPlans.GetCurrentDietPlans;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlan;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlanHistory;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlanHistory.Contracts;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlans;
using LgymApi.Application.Nutrition.DietPlans.GetOwnPlanHistory;
using LgymApi.Application.Nutrition.DietPlans.UpdateTraineePlan;
using LgymApi.Application.Nutrition.DietPlans.UpdateTraineePlan.Contracts;
using LgymApi.Application.Nutrition.Supplementation.AssignTraineePlan;
using LgymApi.Application.Nutrition.Supplementation.CheckOffIntake;
using LgymApi.Application.Nutrition.Supplementation.CheckOffIntake.Contracts;
using LgymApi.Application.Nutrition.Supplementation.CreateTraineePlan;
using LgymApi.Application.Nutrition.Supplementation.DeleteTraineePlan;
using LgymApi.Application.Nutrition.Supplementation.DeleteTraineePlan.Contracts;
using LgymApi.Application.Nutrition.Supplementation.GetComplianceSummary;
using LgymApi.Application.Nutrition.Supplementation.GetSchedule;
using LgymApi.Application.Nutrition.Supplementation.GetSchedule.Contracts;
using LgymApi.Application.Nutrition.Supplementation.GetTraineePlans;
using LgymApi.Application.Nutrition.Supplementation.UnassignTraineePlan;
using LgymApi.Application.Nutrition.Supplementation.UnassignTraineePlan.Contracts;
using LgymApi.Application.Nutrition.Supplementation.UpdateTraineePlan;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Application.Nutrition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNutritionModule(this IServiceCollection services)
    {
        services.AddScoped<ICreateTraineeDietPlanUseCase, CreateTraineeDietPlanUseCase>();
        services.AddScoped<IUpdateTraineeDietPlanUseCase, UpdateTraineeDietPlanUseCase>();
        services.AddScoped<IDeleteTraineeDietPlanUseCase, DeleteTraineeDietPlanUseCase>();
        services.AddScoped<IActivateTraineeDietPlanUseCase, ActivateTraineeDietPlanUseCase>();
        services.AddScoped<IGetTraineeDietPlanUseCase, GetTraineeDietPlanUseCase>();
        services.AddScoped<IGetTraineeDietPlansUseCase, GetTraineeDietPlansUseCase>();
        services.AddScoped<IGetCurrentDietPlansUseCase, GetCurrentDietPlansUseCase>();
        services.AddScoped<IGetTraineeDietPlanHistoryUseCase, GetTraineeDietPlanHistoryUseCase>();
        services.AddScoped<IGetOwnDietPlanHistoryUseCase, GetOwnDietPlanHistoryUseCase>();
        services.AddScoped<IGetCurrentDietPlanUseCase, GetCurrentDietPlanUseCase>();
        services.AddScoped<DietPlanHistorySnapshotFactory>();
        services.AddScoped<ICreateTraineeSupplementPlanUseCase, CreateTraineeSupplementPlanUseCase>();
        services.AddScoped<IUpdateTraineeSupplementPlanUseCase, UpdateTraineeSupplementPlanUseCase>();
        services.AddScoped<IDeleteTraineeSupplementPlanUseCase, DeleteTraineeSupplementPlanUseCase>();
        services.AddScoped<IAssignTraineeSupplementPlanUseCase, AssignTraineeSupplementPlanUseCase>();
        services.AddScoped<IUnassignTraineeSupplementPlanUseCase, UnassignTraineeSupplementPlanUseCase>();
        services.AddScoped<IGetTraineeSupplementPlansUseCase, GetTraineeSupplementPlansUseCase>();
        services.AddScoped<IGetSupplementScheduleUseCase, GetSupplementScheduleUseCase>();
        services.AddScoped<IGetSupplementComplianceSummaryUseCase, GetSupplementComplianceSummaryUseCase>();
        services.AddScoped<ICheckOffSupplementIntakeUseCase, CheckOffSupplementIntakeUseCase>();

        return services;
    }
}
