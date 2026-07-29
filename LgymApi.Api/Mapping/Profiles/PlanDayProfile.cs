using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Enum.Contracts;
using LgymApi.Api.Features.Exercise.Contracts;
using LgymApi.Api.Features.PlanDay.Contracts;
using LgymApi.Api.Extensions;
using LgymApi.Api.Middleware;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Domain.Enums;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Api.Mapping.Profiles;

public sealed class PlanDayProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<PlanDayExerciseInputDto, PlanDayExerciseWriteModel>((source, _) =>
            new PlanDayExerciseWriteModel(source.ExerciseId.ToIdOrEmpty<PlanExerciseReference>(), source.Series, source.Reps));

        configuration.CreateMap<PlanDayFormDto, PlanDayWriteModel>((source, context) =>
            new PlanDayWriteModel(source.Name, context!.MapList<PlanDayExerciseInputDto, PlanDayExerciseWriteModel>(source.Exercises)));

        configuration.CreateMap<PlanDayChoiceReadModel, PlanDayChooseDto>((source, _) => new PlanDayChooseDto
        {
            Id = source.Id.ToString(),
            Name = source.Name
        });

        configuration.CreateMap<PlanExerciseReadModel, ExerciseResponseDto>((source, context) => new ExerciseResponseDto
        {
            Id = source.Id.ToString(),
            Name = source.Name,
            BodyPart = context!.Map<BodyParts, EnumLookupDto>(source.BodyPart),
            EloFormula = context.Map<EnumLookupDto, LookupItemVm>(context.Map<ExerciseEloFormula, EnumLookupDto>(source.EloFormula)),
            Description = source.Description,
            Image = source.Image,
            UserId = source.OwnerId?.ToString()
        });

        configuration.CreateMap<PlanDayExerciseReadModel, PlanDayExerciseVmDto>((source, context) => new PlanDayExerciseVmDto
        {
            Series = source.Series,
            Reps = source.Reps,
            Exercise = source.Exercise is null
                ? new ExerciseResponseDto()
                : context!.Map<PlanExerciseReadModel, ExerciseResponseDto>(source.Exercise)
        });

        configuration.CreateMap<PlanDayReadModel, PlanDayVmDto>((source, context) => new PlanDayVmDto
        {
            Id = source.Id.ToString(),
            Name = source.Name,
            Exercises = context!.MapList<PlanDayExerciseReadModel, PlanDayExerciseVmDto>(source.Exercises)
        });

        configuration.CreateMap<PlanDayInfoReadModel, PlanDayBaseInfoDto>((source, _) => new PlanDayBaseInfoDto
        {
            Id = source.Id.ToString(),
            Name = source.Name,
            LastTrainingDate = source.LastTrainingDate,
            TotalNumberOfSeries = source.TotalNumberOfSeries,
            TotalNumberOfExercises = source.TotalNumberOfExercises
        });
    }
}
