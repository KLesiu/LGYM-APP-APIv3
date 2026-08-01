using LgymApi.Application.Coaching.ManagedPlans.Assign;
using LgymApi.Application.Coaching.ManagedPlans.Create;
using LgymApi.Application.Coaching.ManagedPlans.Delete;
using LgymApi.Application.Coaching.ManagedPlans.GetActive;
using LgymApi.Application.Coaching.ManagedPlans.List;
using LgymApi.Application.Coaching.ManagedPlans.Unassign;
using LgymApi.Application.Coaching.ManagedPlans.Update;
using LgymApi.Application.Mapping.Core;
using OwnerAssignCommand = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.AssignManagedPlanCommand;
using OwnerCreateCommand = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.CreateManagedPlanCommand;
using OwnerDeleteCommand = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.DeleteManagedPlanCommand;
using OwnerGetActiveQuery = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.GetActiveAssignedPlanQuery;
using OwnerListQuery = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.GetManagedPlansQuery;
using OwnerUnassignCommand = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.UnassignManagedPlanCommand;
using OwnerUpdateCommand = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.UpdateManagedPlanCommand;

namespace LgymApi.Application.Coaching.ManagedPlans;

public sealed class ManagedPlanCollaborationMappingProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<ListManagedPlansQuery, OwnerListQuery>((source, _) =>
            new OwnerListQuery(source.TraineeId));
        configuration.CreateMap<CreateTraineeManagedPlanCommand, OwnerCreateCommand>((source, _) =>
            new OwnerCreateCommand(source.TrainerId, source.TraineeId, source.Name));
        configuration.CreateMap<UpdateTraineeManagedPlanCommand, OwnerUpdateCommand>((source, _) =>
            new OwnerUpdateCommand(source.TrainerId, source.TraineeId, source.PlanId, source.Name));
        configuration.CreateMap<DeleteTraineeManagedPlanCommand, OwnerDeleteCommand>((source, _) =>
            new OwnerDeleteCommand(source.TrainerId, source.TraineeId, source.PlanId));
        configuration.CreateMap<AssignTraineeManagedPlanCommand, OwnerAssignCommand>((source, _) =>
            new OwnerAssignCommand(source.TrainerId, source.TraineeId, source.PlanId));
        configuration.CreateMap<UnassignTraineeManagedPlanCommand, OwnerUnassignCommand>((source, _) =>
            new OwnerUnassignCommand(source.TraineeId));
        configuration.CreateMap<GetActiveManagedPlanQuery, OwnerGetActiveQuery>((source, _) =>
            new OwnerGetActiveQuery(source.TraineeId));
    }
}
