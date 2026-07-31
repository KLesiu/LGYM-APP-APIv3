using LgymApi.Application.Mapping.Core;

namespace LgymApi.Application.Coaching.ApiAdapters;

public sealed class ManagedPlanApiAdapterMappingProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<ManagedPlanListAccountQuery, LgymApi.Application.Coaching.ManagedPlans.List.ListManagedPlansQuery>((source, _) => new(source.TrainerId, source.TraineeId));
        configuration.CreateMap<ManagedPlanCreateAccountCommand, LgymApi.Application.Coaching.ManagedPlans.Create.CreateTraineeManagedPlanCommand>((source, _) => new(source.TrainerId, source.TraineeId, source.Name));
        configuration.CreateMap<ManagedPlanUpdateAccountCommand, LgymApi.Application.Coaching.ManagedPlans.Update.UpdateTraineeManagedPlanCommand>((source, _) => new(source.TrainerId, source.TraineeId, source.PlanId.Rebind<LgymApi.TrainingPlanning.Contracts.PlanReference>(), source.Name));
        configuration.CreateMap<ManagedPlanDeleteAccountCommand, LgymApi.Application.Coaching.ManagedPlans.Delete.DeleteTraineeManagedPlanCommand>((source, _) => new(source.TrainerId, source.TraineeId, source.PlanId.Rebind<LgymApi.TrainingPlanning.Contracts.PlanReference>()));
        configuration.CreateMap<ManagedPlanAssignAccountCommand, LgymApi.Application.Coaching.ManagedPlans.Assign.AssignTraineeManagedPlanCommand>((source, _) => new(source.TrainerId, source.TraineeId, source.PlanId.Rebind<LgymApi.TrainingPlanning.Contracts.PlanReference>()));
        configuration.CreateMap<ManagedPlanUnassignAccountCommand, LgymApi.Application.Coaching.ManagedPlans.Unassign.UnassignTraineeManagedPlanCommand>((source, _) => new(source.TrainerId, source.TraineeId));
    }
}
