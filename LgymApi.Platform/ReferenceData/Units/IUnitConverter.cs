namespace LgymApi.Application.Platform.ReferenceData.Units;

public interface IUnitConverter<TUnit>
    where TUnit : struct, Enum
{
    double Convert(double value, TUnit fromUnit, TUnit toUnit);
}
