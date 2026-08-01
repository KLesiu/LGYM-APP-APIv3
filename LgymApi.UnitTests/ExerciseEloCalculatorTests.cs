using FluentAssertions;
using LgymApi.Application.WorkoutProgress.Scoring.Elo;
using LgymApi.Domain.Enums;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ExerciseEloCalculatorTests
{
    [Test]
    public void Calculators_Should_Preserve_Formula_Selection_And_Golden_Gains()
    {
        var input = new ExerciseEloCalculationInput(
            PreviousWeight: 80,
            PreviousReps: 5,
            CurrentWeight: 80,
            CurrentReps: 10);

        Assert.Multiple(() =>
        {
            new StandardExerciseEloCalculator().Formula.Should().Be(ExerciseEloFormula.Standard);
            new StandardExerciseEloCalculator().Calculate(input).Should().Be(16);
            new StrengthWeightedExerciseEloCalculator().Formula.Should().Be(ExerciseEloFormula.StrengthWeighted);
            new StrengthWeightedExerciseEloCalculator().Calculate(input).Should().Be(8);
            new VolumeWeightedExerciseEloCalculator().Formula.Should().Be(ExerciseEloFormula.VolumeWeighted);
            new VolumeWeightedExerciseEloCalculator().Calculate(input).Should().Be(17);
        });
    }

    [Test]
    public void PullupWeightedCalculator_Should_Preserve_Golden_Gains()
    {
        var calculator = new PullupWeightedExerciseEloCalculator();

        Assert.Multiple(() =>
        {
            calculator.Formula.Should().Be(ExerciseEloFormula.PullupWeighted);
            calculator.Calculate(new ExerciseEloCalculationInput(80, 8, 60, 8)).Should().Be(17);
            calculator.Calculate(new ExerciseEloCalculationInput(80, 8, 100, 8)).Should().Be(-17);
        });
    }
}
