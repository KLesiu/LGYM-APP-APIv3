using LgymApi.Api.Features.Exercise.Contracts;
using LgymApi.Api.Features.ExerciseScores.Contracts;
using LgymApi.Api.Features.Training.Contracts;
using LgymApi.Api.Features.Enum.Contracts;
using LgymApi.Application.Features.ExerciseScores.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;

namespace LgymApi.Api.Mapping.Profiles;

public sealed class ExerciseScoreProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<ExerciseScore, ExerciseScoreResponseDto>((source, context) => new ExerciseScoreResponseDto
        {
            Id = source.Id.ToString(),
            ExerciseId = source.ExerciseId.ToString(),
            Reps = source.Reps,
            Series = source.Series,
            Weight = source.Weight.Value,
            Unit = context!.Map<WeightUnits, EnumLookupDto>(source.Weight.Unit)
        });

        configuration.CreateMap<ExerciseScore, ScoreDto>((source, context) => new ScoreDto
        {
            Id = source.Id.ToString(),
            Reps = source.Reps,
            Weight = source.Weight.Value,
            Unit = context!.Map<WeightUnits, EnumLookupDto>(source.Weight.Unit)
        });

        configuration.CreateMap<ExerciseScore, ScoreWithGymDto>((source, context) =>
        {
            var score = context!.Map<ExerciseScore, ScoreDto>(source);

            return new ScoreWithGymDto
            {
                Id = score.Id,
                Reps = score.Reps,
                Weight = score.Weight,
                Unit = score.Unit,
                GymName = source.Training?.Gym?.Name
            };
        });

    }
}
