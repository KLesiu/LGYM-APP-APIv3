using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.TrainingPlanning.Contracts;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.WorkoutProgress.Adapters;

public sealed class PlanExerciseWorkoutAdapterMappingProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<WorkoutExercisePersistenceModel, PlanExerciseCatalogItem>((source, _) => new(
            source.Id.Rebind<PlanExerciseReference>(),
            source.Name,
            source.OwnerId,
            source.BodyPart,
            source.EloFormula,
            source.Description,
            source.Image));
    }
}
