using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Domain.Enums;
using LgymApi.Resources;
using MeasurementEntity = LgymApi.Domain.Entities.Measurement;

namespace LgymApi.Application.Features.Measurements;

internal static class MeasurementListProjector
{
    public static Result<List<MeasurementEntity>, AppError> ProjectMeasurements(
        IEnumerable<MeasurementEntity> ordered,
        MeasurementUnits? targetUnit,
        Func<double, MeasurementUnits, MeasurementUnits, BodyParts, double> convertValue)
    {
        var result = new List<MeasurementEntity>();
        foreach (var measurement in ordered)
        {
            var parsedUnit = MeasurementTrendCalculator.ParseMeasurementUnit(measurement.Unit);
            if (parsedUnit == MeasurementUnits.Unknown)
            {
                return Result<List<MeasurementEntity>, AppError>.Failure(new InvalidMeasurementError(Messages.UnitRequired));
            }

            var resolvedTargetUnit = targetUnit ?? parsedUnit;
            if (!MeasurementUnitResolver.IsUnitAllowedForBodyPart(measurement.BodyPart, resolvedTargetUnit))
            {
                return Result<List<MeasurementEntity>, AppError>.Failure(new InvalidMeasurementError(Messages.UnitRequired));
            }

            var convertedValue = convertValue(measurement.Value, parsedUnit, resolvedTargetUnit, measurement.BodyPart);
            result.Add(new MeasurementEntity
            {
                Id = measurement.Id,
                UserId = measurement.UserId,
                BodyPart = measurement.BodyPart,
                Unit = resolvedTargetUnit.ToString(),
                Value = Math.Round(convertedValue, 2),
                CreatedAt = measurement.CreatedAt,
                UpdatedAt = measurement.UpdatedAt
            });
        }

        return Result<List<MeasurementEntity>, AppError>.Success(result);
    }
}
