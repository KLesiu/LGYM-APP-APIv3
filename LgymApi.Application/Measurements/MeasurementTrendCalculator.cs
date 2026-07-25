using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Measurements.Models;
using LgymApi.Domain.Enums;
using LgymApi.Resources;
using MeasurementEntity = LgymApi.Domain.Entities.Measurement;

namespace LgymApi.Application.Features.Measurements;

internal static class MeasurementTrendCalculator
{
    public static Result<MeasurementTrendResult, AppError> BuildTrendResult(
        IReadOnlyList<MeasurementEntity> ordered,
        BodyParts bodyPart,
        MeasurementUnits unit,
        Func<double, MeasurementUnits, MeasurementUnits, BodyParts, double> convertValue)
    {
        if (ordered.Count == 1)
        {
            var parsedSingleUnit = ParseMeasurementUnit(ordered[0].Unit);
            if (parsedSingleUnit == MeasurementUnits.Unknown)
            {
                return Result<MeasurementTrendResult, AppError>.Failure(new InvalidMeasurementError(Messages.UnitRequired));
            }

            var singleValue = convertValue(ordered[0].Value, parsedSingleUnit, unit, bodyPart);
            return Result<MeasurementTrendResult, AppError>.Success(new MeasurementTrendResult
            {
                BodyPart = bodyPart,
                Unit = unit,
                StartValue = Math.Round(singleValue, 2),
                CurrentValue = Math.Round(singleValue, 2),
                Change = 0d,
                ChangePercentage = 0d,
                FirstMeasurementValue = Math.Round(singleValue, 2),
                FirstMeasurementDate = ordered[0].CreatedAt,
                LastMeasurementValue = Math.Round(singleValue, 2),
                LastMeasurementDate = ordered[0].CreatedAt,
                Difference = 0d,
                Direction = "insufficient_data",
                Points = 1
            });
        }

        var convertedValues = new List<double>(ordered.Count);
        foreach (var measurement in ordered)
        {
            var parsedUnit = ParseMeasurementUnit(measurement.Unit);
            if (parsedUnit == MeasurementUnits.Unknown)
            {
                return Result<MeasurementTrendResult, AppError>.Failure(new InvalidMeasurementError(Messages.UnitRequired));
            }

            convertedValues.Add(convertValue(measurement.Value, parsedUnit, unit, bodyPart));
        }

        var startValue = convertedValues[0];
        var currentValue = convertedValues[^1];
        var change = currentValue - startValue;
        var roundedChange = Math.Round(change, 2);
        const double percentageEpsilon = 0.0001d;
        var changePercentage = Math.Abs(startValue) <= percentageEpsilon
            ? 0d
            : (change / startValue) * 100d;

        return Result<MeasurementTrendResult, AppError>.Success(new MeasurementTrendResult
        {
            BodyPart = bodyPart,
            Unit = unit,
            StartValue = Math.Round(startValue, 2),
            CurrentValue = Math.Round(currentValue, 2),
            Change = roundedChange,
            ChangePercentage = Math.Round(changePercentage, 2),
            FirstMeasurementValue = Math.Round(startValue, 2),
            FirstMeasurementDate = ordered[0].CreatedAt,
            LastMeasurementValue = Math.Round(currentValue, 2),
            LastMeasurementDate = ordered[^1].CreatedAt,
            Difference = Math.Round(Math.Abs(roundedChange), 2),
            Direction = ResolveDirection(roundedChange),
            Points = ordered.Count
        });
    }

    public static MeasurementTrendResult CreateInsufficientTrend(BodyParts bodyPart, MeasurementUnits unit, int points)
    {
        return new MeasurementTrendResult
        {
            BodyPart = bodyPart,
            Unit = unit,
            Direction = "insufficient_data",
            Points = points
        };
    }

    public static MeasurementUnits ResolveTrendTargetUnit(List<MeasurementEntity> measurements)
    {
        var latestUnit = ParseMeasurementUnit(measurements[^1].Unit);
        if (latestUnit != MeasurementUnits.Unknown)
        {
            return latestUnit;
        }

        return MeasurementUnitResolver.GetDefaultUnit(measurements[^1].BodyPart);
    }

    private static string ResolveDirection(double change)
    {
        const double epsilon = 0.0001d;
        if (change > epsilon)
        {
            return "up";
        }

        if (change < -epsilon)
        {
            return "down";
        }

        return "same";
    }

    internal static MeasurementUnits ParseMeasurementUnit(string? unit)
    {
        return MeasurementUnitResolver.TryParseStoredUnit(unit, out var parsedUnit)
            ? parsedUnit
            : MeasurementUnits.Unknown;
    }
}
