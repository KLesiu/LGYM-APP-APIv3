using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;

namespace LgymApi.Application.TrainingPlanning.ApiAdapters;

public sealed class PlanApiAdapterMappingProfile : IMappingProfile
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
    }
}
