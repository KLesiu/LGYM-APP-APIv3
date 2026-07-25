namespace LgymApi.Application.Platform.ReferenceData.Units;

public sealed class LinearUnitConverter<TUnit> : IUnitConverter<TUnit>
    where TUnit : struct, Enum
{
    private readonly ILinearUnitStrategy<TUnit> _strategy;

    public LinearUnitConverter(ILinearUnitStrategy<TUnit> strategy)
    {
        _strategy = strategy;
    }

    public double Convert(double value, TUnit fromUnit, TUnit toUnit)
    {
        if (EqualityComparer<TUnit>.Default.Equals(fromUnit, toUnit))
        {
            return value;
        }

        var valueInBase = _strategy.ConvertTo(value, fromUnit);
        return _strategy.ConvertFrom(valueInBase, toUnit);
    }
}
