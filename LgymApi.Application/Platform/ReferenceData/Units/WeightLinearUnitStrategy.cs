using LgymApi.Domain.Enums;

namespace LgymApi.Application.Platform.ReferenceData.Units;

public sealed class WeightLinearUnitStrategy : ILinearUnitStrategy<WeightUnits>
{
    private static readonly IReadOnlyDictionary<WeightUnits, double> ToBaseFactors =
        new Dictionary<WeightUnits, double>
        {
            [WeightUnits.Kilograms] = 1d,
            [WeightUnits.Pounds] = 0.45359237d
        };

    public WeightUnits BaseUnit => WeightUnits.Kilograms;

    public double ConvertTo(double value, WeightUnits unit)
    {
        if (!ToBaseFactors.TryGetValue(unit, out var factor))
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unsupported weight unit.");
        }

        return value * factor;
    }

    public double ConvertFrom(double value, WeightUnits unit)
    {
        if (!ToBaseFactors.TryGetValue(unit, out var factor))
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unsupported weight unit.");
        }

        return value / factor;
    }
}
