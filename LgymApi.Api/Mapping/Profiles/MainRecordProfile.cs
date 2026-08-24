using LgymApi.Api.Features.Enum.Contracts;
using LgymApi.Api.Features.Exercise.Contracts;
using LgymApi.Api.Features.MainRecords.Contracts;
using LgymApi.Application.Features.MainRecords.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Api.Mapping.Profiles;

public sealed class MainRecordProfile : IMappingProfile
{
    internal static class Keys
    {
        internal static readonly ContextKey<IReadOnlyDictionary<Id<Exercise>, Exercise>> ExerciseMap = new("MainRecord.ExerciseMap");
    }

    public void Configure(MappingConfiguration configuration)
    {
        configuration.AllowContextKey(Keys.ExerciseMap);

        configuration.CreateMap<MainRecord, MainRecordResponseDto>((source, context) => new MainRecordResponseDto
        {
            Id = source.Id.ToString(),
            ExerciseId = source.ExerciseId.ToString(),
            Weight = source.Weight.Value,
            Unit = context!.Map<WeightUnits, EnumLookupDto>(source.Weight.Unit),
            Date = source.Date.UtcDateTime
        });

        configuration.CreateMap<MainRecordReadModel, MainRecordResponseDto>((source, context) => new MainRecordResponseDto
        {
            Id = source.Id.ToString(),
            ExerciseId = source.ExerciseId.ToString(),
            Weight = source.Weight,
            Unit = context!.Map<WeightUnits, EnumLookupDto>(source.Unit),
            Date = source.Date
        });

        configuration.CreateMap<MainRecord, MainRecordsLastDto>((source, context) =>
        {
            var exerciseMap = context?.Get(Keys.ExerciseMap);
            var exercise = exerciseMap != null && exerciseMap.TryGetValue(source.ExerciseId, out var resolvedExercise)
                ? resolvedExercise
                : null;

            return new MainRecordsLastDto
            {
                Id = source.Id.ToString(),
                ExerciseId = source.ExerciseId.ToString(),
                Weight = source.Weight.Value,
                Unit = context!.Map<WeightUnits, EnumLookupDto>(source.Weight.Unit),
                Date = source.Date.UtcDateTime,
                ExerciseDetails = exercise == null
                    ? new ExerciseResponseDto()
                    : context!.Map<Exercise, ExerciseResponseDto>(exercise)
            };
        });

        configuration.CreateMap<MainRecordBestReadModel, MainRecordsLastDto>((source, context) => new MainRecordsLastDto
        {
            Id = source.Record.Id.ToString(),
            ExerciseId = source.Record.ExerciseId.ToString(),
            Weight = source.Record.Weight,
            Unit = context!.Map<WeightUnits, EnumLookupDto>(source.Record.Unit),
            Date = source.Record.Date,
            ExerciseDetails = new ExerciseResponseDto
            {
                Id = source.Exercise.Id.ToString(),
                Name = source.Exercise.Name,
                DisplayName = GetDisplayName(context, source.Exercise.Id, source.Exercise.Name),
                UserId = source.Exercise.UserId?.ToString(),
                BodyPart = context.Map<BodyParts, EnumLookupDto>(source.Exercise.BodyPart),
                EloFormula = source.Exercise.EloFormula == null
                    ? null
                    : context.Map<EnumLookupDto, LgymApi.Api.Features.Common.Contracts.LookupItemVm>(context.Map<ExerciseEloFormula, EnumLookupDto>(source.Exercise.EloFormula.Value)),
                Description = source.Exercise.Description,
                Image = source.Exercise.Image
            }
        });

        configuration.CreateMap<PossibleRecordResult, PossibleRecordForExerciseDto>((source, context) => new PossibleRecordForExerciseDto
        {
            Weight = source.Weight,
            Reps = source.Reps,
            Unit = context!.Map<WeightUnits, EnumLookupDto>(source.Unit),
            Date = source.Date
        });

        configuration.CreateMap<PossibleRecordReadModel, PossibleRecordForExerciseDto>((source, context) => new PossibleRecordForExerciseDto
        {
            Weight = source.Weight,
            Reps = source.Reps,
            Unit = context!.Map<WeightUnits, EnumLookupDto>(source.Unit),
            Date = source.Date
        });
    }

    private static string GetDisplayName(
        MappingContext context,
        Id<Exercise> exerciseId,
        string fallback)
    {
        var translations = context.Get(ExerciseProfile.Keys.Translations);
        return translations is not null && translations.TryGetValue(exerciseId, out var displayName)
            ? displayName
            : fallback;
    }
}
