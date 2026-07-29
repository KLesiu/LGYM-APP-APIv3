using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Application.TrainingPlanning.ManagedPlans;
using LgymApi.Application.TrainingPlanning.Plan.CheckIsUserHavePlan;
using LgymApi.Application.TrainingPlanning.Plan.CopyPlan;
using LgymApi.Application.TrainingPlanning.Plan.CreatePlan;
using LgymApi.Application.TrainingPlanning.Plan.DeletePlan;
using LgymApi.Application.TrainingPlanning.Plan.GenerateShareCode;
using LgymApi.Application.TrainingPlanning.Plan.GetPlanConfig;
using LgymApi.Application.TrainingPlanning.Plan.GetPlansList;
using LgymApi.Application.TrainingPlanning.Plan.SetActivePlan;
using LgymApi.Application.TrainingPlanning.Plan.UpdatePlan;
using LgymApi.Application.TrainingPlanning.PlanDay;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Plan.ActivePlanPointer;
using LgymApi.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.TrainingPlanning;

public static class TrainingPlanningModule
{
    public static IServiceCollection AddTrainingPlanningModule(this IServiceCollection services)
    {
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<IPlanDayRepository, PlanDayRepository>();
        services.AddScoped<IPlanDayExerciseRepository, PlanDayExerciseRepository>();
        services.AddScoped<IActivePlanPointerStore, ActivePlanPointerStore>();
        services.AddScoped<ICreatePlanUseCase, CreatePlanUseCase>();
        services.AddScoped<IUpdatePlanUseCase, UpdatePlanUseCase>();
        services.AddScoped<IDeletePlanUseCase, DeletePlanUseCase>();
        services.AddScoped<IGetPlanConfigUseCase, GetPlanConfigUseCase>();
        services.AddScoped<IGetPlansListUseCase, GetPlansListUseCase>();
        services.AddScoped<ISetActivePlanUseCase, SetActivePlanUseCase>();
        services.AddScoped<ICopyPlanUseCase, CopyPlanUseCase>();
        services.AddScoped<IGenerateShareCodeUseCase, GenerateShareCodeUseCase>();
        services.AddScoped<ICheckIsUserHavePlanUseCase, CheckIsUserHavePlanUseCase>();
        services.AddScoped<IGetManagedPlansUseCase, GetManagedPlansUseCase>();
        services.AddScoped<ICreateManagedPlanUseCase, CreateManagedPlanUseCase>();
        services.AddScoped<IUpdateManagedPlanUseCase, UpdateManagedPlanUseCase>();
        services.AddScoped<IDeleteManagedPlanUseCase, DeleteManagedPlanUseCase>();
        services.AddScoped<IAssignManagedPlanUseCase, AssignManagedPlanUseCase>();
        services.AddScoped<IUnassignManagedPlanUseCase, UnassignManagedPlanUseCase>();
        services.AddScoped<IGetActiveAssignedPlanUseCase, GetActiveAssignedPlanUseCase>();
        services.AddScoped<IPlanDayService, PlanDayService>();
        services.AddScoped<IPlanDayReferenceReadService, PlanDayReferenceReadService>();

        return services;
    }
}
