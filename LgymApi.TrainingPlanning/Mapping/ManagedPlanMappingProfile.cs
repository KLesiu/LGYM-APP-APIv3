using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.TrainingPlanning.Contracts;
using PlanEntity = LgymApi.Domain.Entities.Plan;

namespace LgymApi.Application.TrainingPlanning.Mapping;

public sealed class ManagedPlanMappingProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<PlanEntity, ManagedPlanReadModel>((source, _) =>
            new ManagedPlanReadModel(source.Id.Rebind<PlanReference>(), source.Name, source.IsActive, source.CreatedAt));
    }
}
