using LgymApi.Api.Features.Gym.Contracts;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.ValueObjects;
using LgymApi.TrainingPlanning.Contracts;
using GymEntity = LgymApi.Domain.Entities.Gym;

namespace LgymApi.Api.Mapping.Profiles;

public sealed class GymProfile : IMappingProfile
{
    internal static class Keys
    {
        internal static readonly ContextKey<IReadOnlyDictionary<Id<GymEntity>, WorkoutTrainingPersistenceModel>> LastTrainingMap = new("Gym.LastTrainingMap");
        internal static readonly ContextKey<IReadOnlyDictionary<Id<PlanDayReference>, PlanDayReferenceReadModel>> PlanDayMap = new("Gym.PlanDayMap");
    }

    public void Configure(MappingConfiguration configuration)
    {
        configuration.AllowContextKey(Keys.LastTrainingMap);
        configuration.AllowContextKey(Keys.PlanDayMap);

        configuration.CreateMap<WorkoutTrainingPersistenceModel, LastTrainingGymInfoDto>((source, context) =>
        {
            var planDay = context?.Get(Keys.PlanDayMap)?.GetValueOrDefault(source.TypePlanDayId);
            return new LastTrainingGymInfoDto
            {
                Id = source.Id.ToString(),
                CreatedAt = source.CreatedAt.UtcDateTime,
                Type = planDay is not { Exists: true, IsDeleted: false } ? null : new LastTrainingGymPlanDayInfoDto { Id = planDay.PlanDayId.ToString(), Name = planDay.Name },
                Name = planDay is { Exists: true, IsDeleted: false } ? planDay.Name : null
            };
        });

        configuration.CreateMap<WorkoutGymPersistenceModel, GymFormDto>((source, _) => new GymFormDto
        {
            Id = source.Id.ToString(),
            Name = source.Name,
            Address = source.AddressId?.ToString()
        });

        configuration.CreateMap<GymEntity, GymFormDto>((source, _) => new GymFormDto { Id = source.Id.ToString(), Name = source.Name, Address = source.AddressId?.ToString() });

        configuration.CreateMap<WorkoutGymPersistenceModel, GymChoiceInfoDto>((source, context) =>
        {
            var lastTrainingMap = context?.Get(Keys.LastTrainingMap);
            WorkoutTrainingPersistenceModel? training = null;
            if (lastTrainingMap != null && lastTrainingMap.TryGetValue(source.Id, out var resolvedTraining))
            {
                training = resolvedTraining;
            }

            return new GymChoiceInfoDto
            {
                Id = source.Id.ToString(),
                Name = source.Name,
                Address = source.AddressId?.ToString(),
                LastTrainingInfo = training == null ? null : context!.Map<WorkoutTrainingPersistenceModel, LastTrainingGymInfoDto>(training)
            };
        });

        configuration.CreateMap<GymEntity, GymChoiceInfoDto>((source, _) => new GymChoiceInfoDto { Id = source.Id.ToString(), Name = source.Name, Address = source.AddressId?.ToString() });
    }
}
