using LgymApi.Application.Identity.Compatibility.Task7.Contracts;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;

namespace LgymApi.Application.Identity.Compatibility.Task7.Mapping;

public sealed class Task7AccountCompatibilityMappingProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<PlanCreateAccountCommand, LgymApi.Application.TrainingPlanning.Plan.CreatePlan.CreatePlanCommand>((source, _) => new(source.CurrentAccountId, source.RouteAccountId, source.Name));
        configuration.CreateMap<PlanUpdateAccountCommand, LgymApi.Application.TrainingPlanning.Plan.UpdatePlan.UpdatePlanCommand>((source, _) => new(source.CurrentAccountId, source.RouteAccountId, source.PlanId.Rebind<LgymApi.TrainingPlanning.Contracts.PlanReference>(), source.Name));
        configuration.CreateMap<PlanGetConfigAccountQuery, LgymApi.Application.TrainingPlanning.Plan.GetPlanConfig.GetPlanConfigQuery>((source, _) => new(source.CurrentAccountId, source.RouteAccountId));
        configuration.CreateMap<PlanHasAccountQuery, LgymApi.Application.TrainingPlanning.Plan.CheckIsUserHavePlan.CheckIsUserHavePlanQuery>((source, _) => new(source.CurrentAccountId, source.RouteAccountId));
        configuration.CreateMap<PlanGetListAccountQuery, LgymApi.Application.TrainingPlanning.Plan.GetPlansList.GetPlansListQuery>((source, _) => new(source.CurrentAccountId, source.RouteAccountId));
        configuration.CreateMap<PlanSetActiveAccountCommand, LgymApi.Application.TrainingPlanning.Plan.SetActivePlan.SetActivePlanCommand>((source, _) => new(source.CurrentAccountId, source.RouteAccountId, source.PlanId.Rebind<LgymApi.TrainingPlanning.Contracts.PlanReference>()));
        configuration.CreateMap<PlanCopyAccountCommand, LgymApi.Application.TrainingPlanning.Plan.CopyPlan.CopyPlanCommand>((source, _) => new(source.CurrentAccountId, source.ShareCode));
        configuration.CreateMap<PlanGenerateShareCodeAccountCommand, LgymApi.Application.TrainingPlanning.Plan.GenerateShareCode.GenerateShareCodeCommand>((source, _) => new(source.CurrentAccountId, source.PlanId.Rebind<LgymApi.TrainingPlanning.Contracts.PlanReference>()));
        configuration.CreateMap<PlanDeleteAccountCommand, LgymApi.Application.TrainingPlanning.Plan.DeletePlan.DeletePlanCommand>((source, _) => new(source.CurrentAccountId, source.PlanId.Rebind<LgymApi.TrainingPlanning.Contracts.PlanReference>()));

        configuration.CreateMap<ManagedPlanListAccountQuery, LgymApi.Application.Coaching.ManagedPlans.List.ListManagedPlansQuery>((source, _) => new(source.TrainerId, source.TraineeId));
        configuration.CreateMap<ManagedPlanCreateAccountCommand, LgymApi.Application.Coaching.ManagedPlans.Create.CreateTraineeManagedPlanCommand>((source, _) => new(source.TrainerId, source.TraineeId, source.Name));
        configuration.CreateMap<ManagedPlanUpdateAccountCommand, LgymApi.Application.Coaching.ManagedPlans.Update.UpdateTraineeManagedPlanCommand>((source, _) => new(source.TrainerId, source.TraineeId, source.PlanId.Rebind<LgymApi.TrainingPlanning.Contracts.PlanReference>(), source.Name));
        configuration.CreateMap<ManagedPlanDeleteAccountCommand, LgymApi.Application.Coaching.ManagedPlans.Delete.DeleteTraineeManagedPlanCommand>((source, _) => new(source.TrainerId, source.TraineeId, source.PlanId.Rebind<LgymApi.TrainingPlanning.Contracts.PlanReference>()));
        configuration.CreateMap<ManagedPlanAssignAccountCommand, LgymApi.Application.Coaching.ManagedPlans.Assign.AssignTraineeManagedPlanCommand>((source, _) => new(source.TrainerId, source.TraineeId, source.PlanId.Rebind<LgymApi.TrainingPlanning.Contracts.PlanReference>()));
        configuration.CreateMap<ManagedPlanUnassignAccountCommand, LgymApi.Application.Coaching.ManagedPlans.Unassign.UnassignTraineeManagedPlanCommand>((source, _) => new(source.TrainerId, source.TraineeId));

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
