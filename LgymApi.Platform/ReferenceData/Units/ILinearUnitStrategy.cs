namespace LgymApi.Application.Platform.ReferenceData.Units;

public interface ILinearUnitStrategy<TUnit>
    where TUnit : struct, Enum
{
    TUnit BaseUnit { get; }
    double ConvertTo(double value, TUnit unit);
    double ConvertFrom(double value, TUnit unit);
}
