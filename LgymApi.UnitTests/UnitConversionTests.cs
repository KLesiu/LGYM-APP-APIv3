using FluentAssertions;
using LgymApi.Application.Platform.ReferenceData.Units;
using LgymApi.Domain.Enums;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class UnitConversionTests
{
    private static readonly IUnitConverter<WeightUnits> Converter =
        new LinearUnitConverter<WeightUnits>(new WeightLinearUnitStrategy());

    [TestCase(100d, WeightUnits.Kilograms, WeightUnits.Pounds, 220.46226218487757d)]
    [TestCase(220.46226218487757d, WeightUnits.Pounds, WeightUnits.Kilograms, 100d)]
    public void Convert_ConvertsBetweenKilogramsAndPounds(double input, WeightUnits from, WeightUnits to, double expected)
    {
        var result = Converter.Convert(input, from, to);

        result.Should().BeApproximately(expected, 1e-9);
    }

    [TestCase(0d, WeightUnits.Kilograms, WeightUnits.Pounds, 0d)]
    [TestCase(-10d, WeightUnits.Kilograms, WeightUnits.Pounds, -22.046226218487757d)]
    [TestCase(-22.046226218487757d, WeightUnits.Pounds, WeightUnits.Kilograms, -10d)]
    public void Convert_HandlesZeroAndNegativeValues(double input, WeightUnits from, WeightUnits to, double expected)
    {
        var result = Converter.Convert(input, from, to);

        result.Should().BeApproximately(expected, 1e-9);
    }

    [Test]
    public void Convert_RoundTripPreservesPrecisionWithinTolerance()
    {
        const double originalKilograms = 123.456789d;

        var pounds = Converter.Convert(originalKilograms, WeightUnits.Kilograms, WeightUnits.Pounds);
        var resultKilograms = Converter.Convert(pounds, WeightUnits.Pounds, WeightUnits.Kilograms);

        resultKilograms.Should().BeApproximately(originalKilograms, 1e-9);
    }

    [Test]
    public void ConvertTo_ThrowsForUnsupportedUnit()
    {
        var strategy = new WeightLinearUnitStrategy();

        var action = () => strategy.ConvertTo(100d, WeightUnits.Unknown);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void ConvertFrom_ThrowsForUnsupportedUnit()
    {
        var strategy = new WeightLinearUnitStrategy();

        var action = () => strategy.ConvertFrom(100d, WeightUnits.Unknown);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void Compare_UsesEpsilonForNearEqualValues()
    {
        var comparison = UnitValueComparer.Compare(
            100d,
            WeightUnits.Kilograms,
            220.46226218487757d,
            WeightUnits.Pounds,
            (value, unit) => Converter.Convert(value, unit, WeightUnits.Kilograms));

        comparison.Should().Be(0);
    }
}
