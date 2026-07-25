using LgymApi.Domain.Enums;

namespace LgymApi.Application.Platform.ReferenceData.Units;

public sealed class HeightLinearUnitStrategy : ILinearUnitStrategy<HeightUnits>
{
    private static readonly IReadOnlyDictionary<HeightUnits, double> ToBaseFactors =
        new Dictionary<HeightUnits, double>
        {
            [HeightUnits.Meters] = 1d,
            [HeightUnits.Centimeters] = 0.01d,
            [HeightUnits.Millimeters] = 0.001d
        };

    public HeightUnits BaseUnit => HeightUnits.Meters;

    public double ConvertTo(double value, HeightUnits unit)
    {
        if (!ToBaseFactors.TryGetValue(unit, out var factor))
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unsupported height unit.");
        }

        return value * factor;
    }

    public double ConvertFrom(double value, HeightUnits unit)
    {
        if (!ToBaseFactors.TryGetValue(unit, out var factor))
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unsupported height unit.");
        }

        return value / factor;
    }
}
