namespace LgymApi.Application.Platform.ReferenceData.Units;

public static class UnitValueComparer
{
    public const double DefaultEpsilon = 1e-9;

    public static int Compare<TUnit>(
        double leftValue,
        TUnit leftUnit,
        double rightValue,
        TUnit rightUnit,
        Func<double, TUnit, double> toBase,
        double epsilon = DefaultEpsilon)
        where TUnit : struct, Enum
    {
        var leftBase = toBase(leftValue, leftUnit);
        var rightBase = toBase(rightValue, rightUnit);
        var diff = leftBase - rightBase;

        if (Math.Abs(diff) <= epsilon)
        {
            return 0;
        }

        return diff > 0 ? 1 : -1;
    }

    public static bool AreEqual<TUnit>(
        double leftValue,
        TUnit leftUnit,
        double rightValue,
        TUnit rightUnit,
        Func<double, TUnit, double> toBase,
        double epsilon = DefaultEpsilon)
        where TUnit : struct, Enum
    {
        return Compare(leftValue, leftUnit, rightValue, rightUnit, toBase, epsilon) == 0;
    }

    public static (double Value, TUnit Unit) GetGreater<TUnit>(
        double leftValue,
        TUnit leftUnit,
        double rightValue,
        TUnit rightUnit,
        Func<double, TUnit, double> toBase,
        double epsilon = DefaultEpsilon)
        where TUnit : struct, Enum
    {
        var comparison = Compare(leftValue, leftUnit, rightValue, rightUnit, toBase, epsilon);
        return comparison >= 0 ? (leftValue, leftUnit) : (rightValue, rightUnit);
    }
}
