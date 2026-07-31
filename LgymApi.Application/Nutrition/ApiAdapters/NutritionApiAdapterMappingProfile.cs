using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;

namespace LgymApi.Application.Nutrition.ApiAdapters;

public sealed class NutritionApiAdapterMappingProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<DietPlanListAccountQuery, LgymApi.Application.Nutrition.DietPlans.GetTraineePlans.GetTraineeDietPlansQuery>((source, _) => new(source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>()));
        configuration.CreateMap<DietPlanGetAccountQuery, LgymApi.Application.Nutrition.DietPlans.GetTraineePlan.GetTraineeDietPlanQuery>((source, _) => new(source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>(), source.DietPlanId));
        configuration.CreateMap<DietPlanCreateAccountCommand, LgymApi.Application.Nutrition.DietPlans.CreateTraineePlan.CreateTraineeDietPlanCommand>((source, _) => new(source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>(), source.Data));
        configuration.CreateMap<DietPlanUpdateAccountCommand, LgymApi.Application.Nutrition.DietPlans.UpdateTraineePlan.UpdateTraineeDietPlanCommand>((source, _) => new(source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>(), source.DietPlanId, source.Data));
        configuration.CreateMap<DietPlanActivateAccountCommand, LgymApi.Application.Nutrition.DietPlans.ActivateTraineePlan.Models.ActivateTraineeDietPlanCommand>((source, _) => new(source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>(), source.DietPlanId));
        configuration.CreateMap<DietPlanDeleteAccountCommand, LgymApi.Application.Nutrition.DietPlans.DeleteTraineePlan.Models.DeleteTraineeDietPlanCommand>((source, _) => new(source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>(), source.DietPlanId));
        configuration.CreateMap<DietPlanHistoryAccountQuery, LgymApi.Application.Nutrition.DietPlans.GetTraineePlanHistory.Models.GetTraineeDietPlanHistoryQuery>((source, _) => new(source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>(), source.DietPlanId));
        configuration.CreateMap<DietPlanCurrentAccountQuery, LgymApi.Application.Nutrition.DietPlans.GetCurrentDietPlans.GetCurrentDietPlansQuery>((source, _) => new(source.TraineeId.Rebind<User>()));
        configuration.CreateMap<DietPlanCurrentAccountQuery, LgymApi.Application.Nutrition.DietPlans.GetCurrentDietPlan.GetCurrentDietPlanQuery>((source, _) => new(source.TraineeId.Rebind<User>()));
        configuration.CreateMap<SupplementPlanListAccountQuery, LgymApi.Application.Nutrition.Supplementation.GetTraineePlans.GetTraineeSupplementPlansQuery>((source, _) => new(source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>()));
        configuration.CreateMap<SupplementPlanCreateAccountCommand, LgymApi.Application.Nutrition.Supplementation.CreateTraineePlan.CreateTraineeSupplementPlanCommand>((source, _) => new(source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>(), source.Data));
        configuration.CreateMap<SupplementPlanUpdateAccountCommand, LgymApi.Application.Nutrition.Supplementation.UpdateTraineePlan.UpdateTraineeSupplementPlanCommand>((source, _) => new(source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>(), source.PlanId, source.Data));
        configuration.CreateMap<SupplementPlanDeleteAccountCommand, LgymApi.Application.Nutrition.Supplementation.DeleteTraineePlan.Models.DeleteTraineeSupplementPlanCommand>((source, _) => new(source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>(), source.PlanId));
        configuration.CreateMap<SupplementPlanAssignAccountCommand, LgymApi.Application.Nutrition.Supplementation.AssignTraineePlan.AssignTraineeSupplementPlanCommand>((source, _) => new(source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>(), source.PlanId));
        configuration.CreateMap<SupplementPlanUnassignAccountCommand, LgymApi.Application.Nutrition.Supplementation.UnassignTraineePlan.UnassignTraineeSupplementPlanCommand>((source, _) => new(source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>()));
        configuration.CreateMap<SupplementComplianceAccountQuery, LgymApi.Application.Nutrition.Supplementation.GetComplianceSummary.GetSupplementComplianceSummaryQuery>((source, _) => new(source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>(), source.FromDate, source.ToDate));
        configuration.CreateMap<SupplementScheduleAccountQuery, LgymApi.Application.Nutrition.Supplementation.GetSchedule.Models.GetSupplementScheduleQuery>((source, _) => new(source.TraineeId.Rebind<User>(), source.IntakeDate));
        configuration.CreateMap<SupplementCheckOffAccountCommand, LgymApi.Application.Nutrition.Supplementation.CheckOffIntake.Models.CheckOffSupplementIntakeCommand>((source, _) => new(source.TraineeId.Rebind<User>(), source.PlanItemId, source.IntakeDate, source.TakenAt));
    }
}
