using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Enum.Contracts;
using LgymApi.Api.Features.Exercise.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Features.Exercise.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;
using LgymApi.Identity.Contracts;

namespace LgymApi.Api.Mapping.Profiles;

public sealed class ExerciseProfile : IMappingProfile
{
    internal static class Keys
    {
        internal static readonly ContextKey<IReadOnlyDictionary<Id<Exercise>, string>> Translations = new("Exercise.Translations");
        internal static readonly ContextKey<Id<AccountReference>> UserId = new("Exercise.UserId");
    }

    public void Configure(MappingConfiguration configuration)
    {
        configuration.AllowContextKey(Keys.Translations);
        configuration.AllowContextKey(Keys.UserId);

        configuration.CreateMap<ExerciseExtendedFormDto, AddExerciseWithFormulaInput>((source, _) => new AddExerciseWithFormulaInput(
            source.Name,
            source.BodyPart,
            ParseExerciseEloFormula(source.EloFormula),
            source.Description,
            source.Image));

        configuration.CreateMap<ExerciseExtendedFormDto, AddUserExerciseWithFormulaInput>((source, context) => new AddUserExerciseWithFormulaInput(
            context?.Get(Keys.UserId) ?? default,
            source.Name,
            source.BodyPart,
            ParseExerciseEloFormula(source.EloFormula),
            source.Description,
            source.Image));

        configuration.CreateMap<ExerciseExtendedFormDto, UpdateExerciseWithFormulaInput>((source, _) => new UpdateExerciseWithFormulaInput(
            source.Id.ToIdOrEmpty<Exercise>(),
            source.Name,
            source.BodyPart,
            ParseExerciseEloFormula(source.EloFormula),
            source.Description,
            source.Image));

        configuration.CreateMap<ProgressExerciseReadModel, ExerciseResponseDto>((source, context) =>
        {
            var name = source.Name;

            if (source.UserId is null)
            {
                var translations = context?.Get(Keys.Translations);
                if (translations != null && translations.TryGetValue(source.Id, out var translatedName))
                {
                    name = translatedName;
                }
            }

            return new ExerciseResponseDto
            {
                Id = source.Id.ToString(),
                Name = name,
                DisplayName = name,
                BodyPart = context!.Map<BodyParts, EnumLookupDto>(source.BodyPart),
                EloFormula = source.EloFormula == null ? null : context.Map<EnumLookupDto, LookupItemVm>(context.Map<ExerciseEloFormula, EnumLookupDto>(source.EloFormula.Value)),
                Description = source.Description,
                Image = source.Image,
                UserId = source.UserId?.ToString()
            };
        });

        configuration.CreateMap<Exercise, ExerciseResponseDto>((source, context) =>
        {
            var name = source.Name;
            if (source.UserId is null)
            {
                var translations = context?.Get(Keys.Translations);
                if (translations != null && translations.TryGetValue(source.Id, out var translatedName)) name = translatedName;
            }
            return new ExerciseResponseDto
            {
                Id = source.Id.ToString(), Name = name, DisplayName = name, BodyPart = context!.Map<BodyParts, EnumLookupDto>(source.BodyPart),
                EloFormula = context.Map<EnumLookupDto, LookupItemVm>(context.Map<ExerciseEloFormula, EnumLookupDto>(source.EloFormula)),
                Description = source.Description, Image = source.Image, UserId = source.UserId?.ToString()
            };
        });

        configuration.CreateMap<SeriesScoreResult, SeriesScoreWithGymDto>((source, context) => new SeriesScoreWithGymDto
        {
            Series = source.Series,
            Score = source.Score == null ? null : context!.Map<WorkoutExerciseScoreReadModel, ScoreWithGymDto>(source.Score)
        });

        configuration.CreateMap<LastExerciseScoresResult, LastExerciseScoresResponseDto>((source, context) => new LastExerciseScoresResponseDto
        {
            ExerciseId = source.ExerciseId.ToString(),
            ExerciseName = source.ExerciseName,
            SeriesScores = context!.MapList<SeriesScoreResult, SeriesScoreWithGymDto>(source.SeriesScores)
        });

        configuration.CreateMap<SeriesScoreResult, SeriesScoreDto>((source, context) => new SeriesScoreDto
        {
            Series = source.Series,
            Score = source.Score == null ? null : context!.Map<WorkoutExerciseScoreReadModel, ScoreDto>(source.Score)
        });

        configuration.CreateMap<ExerciseTrainingHistoryItem, ExerciseTrainingHistoryItemDto>((source, context) => new ExerciseTrainingHistoryItemDto
        {
            Id = source.Id.ToString(),
            Date = source.Date,
            GymName = source.GymName,
            TrainingName = source.TrainingName,
            SeriesScores = context!.MapList<SeriesScoreResult, SeriesScoreDto>(source.SeriesScores)
        });
    }

    private static ExerciseEloFormula? ParseExerciseEloFormula(LookupItemVm? eloFormula)
    {
        var formulaId = eloFormula?.Id;
        if (string.IsNullOrWhiteSpace(formulaId))
        {
            return null;
        }

        return global::System.Enum.TryParse<ExerciseEloFormula>(formulaId, ignoreCase: true, out var parsed)
            ? parsed
            : null;
    }

}
