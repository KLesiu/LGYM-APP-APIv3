using LgymApi.Api.Features.Enum.Contracts;
using LgymApi.Api.Features.Measurements.Contracts;
using LgymApi.Application.Features.Measurements.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;

namespace LgymApi.Api.Mapping.Profiles;

public sealed class MeasurementProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<Measurement, MeasurementResponseDto>((source, context) => new MeasurementResponseDto
        {
            UserId = source.UserId.ToString(),
            BodyPart = context!.Map<BodyParts, EnumLookupDto>(source.BodyPart),
            Unit = context.Map<MeasurementUnits, EnumLookupDto>(ParseUnit(source.Unit)),
            Value = source.Value,
            CreatedAt = source.CreatedAt.UtcDateTime,
            UpdatedAt = source.UpdatedAt.UtcDateTime
        });

        configuration.CreateMap<MeasurementReadModel, MeasurementResponseDto>((source, context) => new MeasurementResponseDto
        {
            UserId = source.UserId.ToString(),
            BodyPart = context!.Map<BodyParts, EnumLookupDto>(source.BodyPart),
            Unit = context.Map<MeasurementUnits, EnumLookupDto>(source.Unit),
            Value = source.Value,
            CreatedAt = source.CreatedAt.UtcDateTime,
            UpdatedAt = source.UpdatedAt.UtcDateTime
        });

        configuration.CreateMap<List<Measurement>, MeasurementsHistoryDto>((source, context) => new MeasurementsHistoryDto
        {
            Measurements = context!.MapList<Measurement, MeasurementResponseDto>(source)
        });

        configuration.CreateMap<List<MeasurementReadModel>, MeasurementsHistoryDto>((source, context) => new MeasurementsHistoryDto
        {
            Measurements = context!.MapList<MeasurementReadModel, MeasurementResponseDto>(source)
        });

        configuration.CreateMap<List<Measurement>, MeasurementsListDto>((source, context) => new MeasurementsListDto
        {
            Measurements = context!.MapList<Measurement, MeasurementResponseDto>(source)
        });

        configuration.CreateMap<List<MeasurementReadModel>, MeasurementsListDto>((source, context) => new MeasurementsListDto
        {
            Measurements = context!.MapList<MeasurementReadModel, MeasurementResponseDto>(source)
        });

        configuration.CreateMap<List<MeasurementTrendResult>, MeasurementTrendsDto>((source, context) => new MeasurementTrendsDto
        {
            Trends = context!.MapList<MeasurementTrendResult, MeasurementTrendDto>(source)
        });

        configuration.CreateMap<MeasurementTrendResult, MeasurementTrendDto>((source, context) => new MeasurementTrendDto
        {
            BodyPart = context!.Map<BodyParts, EnumLookupDto>(source.BodyPart),
            Unit = context.Map<MeasurementUnits, EnumLookupDto>(source.Unit),
            FirstMeasurementValue = source.FirstMeasurementValue,
            FirstMeasurementDate = source.FirstMeasurementDate?.UtcDateTime,
            LastMeasurementValue = source.LastMeasurementValue,
            LastMeasurementDate = source.LastMeasurementDate?.UtcDateTime,
            Difference = source.Difference,
            StartValue = source.StartValue,
            CurrentValue = source.CurrentValue,
            Change = source.Change,
            ChangePercentage = source.ChangePercentage,
            Direction = source.Direction,
            Points = source.Points
        });

        configuration.CreateMap<List<MeasurementTrendReadModel>, MeasurementTrendsDto>((source, context) => new MeasurementTrendsDto
        {
            Trends = context!.MapList<MeasurementTrendReadModel, MeasurementTrendDto>(source)
        });

        configuration.CreateMap<MeasurementTrendReadModel, MeasurementTrendDto>((source, context) => new MeasurementTrendDto
        {
            BodyPart = context!.Map<BodyParts, EnumLookupDto>(source.BodyPart),
            Unit = context.Map<MeasurementUnits, EnumLookupDto>(source.Unit),
            FirstMeasurementValue = source.FirstMeasurementValue,
            FirstMeasurementDate = source.FirstMeasurementDate?.UtcDateTime,
            LastMeasurementValue = source.LastMeasurementValue,
            LastMeasurementDate = source.LastMeasurementDate?.UtcDateTime,
            Difference = source.Difference,
            StartValue = source.StartValue,
            CurrentValue = source.CurrentValue,
            Change = source.Change,
            ChangePercentage = source.ChangePercentage,
            Direction = source.Direction,
            Points = source.Points
        });
    }

    private static MeasurementUnits ParseUnit(string? unit)
    {
        if (!string.IsNullOrWhiteSpace(unit) && Enum.TryParse(unit, true, out MeasurementUnits parsed))
        {
            return parsed;
        }

        return MeasurementUnits.Unknown;
    }
}
