using LgymApi.Api.Features.ExerciseScores.Contracts;
using LgymApi.Application.Features.ExerciseScores.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;

namespace LgymApi.Api.Mapping.Profiles;

public sealed class ExerciseScoresProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<ExerciseScoresChartData, ExerciseScoresChartDataDto>((source, _) => new ExerciseScoresChartDataDto
        {
            Id = source.Id,
            Value = source.Value,
            Date = source.Date,
            ExerciseName = source.ExerciseName,
            ExerciseId = source.ExerciseId.ToString()
        });

        configuration.CreateMap<ExerciseScoreChartPoint, ExerciseScoresChartDataDto>((source, _) => new ExerciseScoresChartDataDto
        {
            Id = source.Id,
            Value = source.Value,
            Date = source.Date,
            ExerciseName = source.ExerciseName,
            ExerciseId = source.ExerciseId.ToString()
        });
    }
}
