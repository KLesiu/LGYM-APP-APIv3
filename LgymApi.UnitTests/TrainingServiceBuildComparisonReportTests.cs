using FluentAssertions;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Application.WorkoutProgress.TrainingExecution;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TrainingServiceBuildComparisonReportTests
{
    [Test]
    public void Build_ShouldPreserveExerciseOrderAndPreviousScore()
    {
        var exerciseId = Id<Exercise>.New();
        var current = new[] { new TrainingExerciseInput { ExerciseId = exerciseId, Series = 1, Reps = 8, Weight = 80, Unit = WeightUnits.Kilograms } };
        var previous = new Dictionary<string, WorkoutExerciseScorePersistenceModel>
        {
            [$"{exerciseId}-1"] = Score(exerciseId, 5, 70)
        };

        var result = TrainingComparisonReportBuilder.Build(current, previous, new Dictionary<Id<Exercise>, string> { [exerciseId] = "Bench" });

        result.Should().ContainSingle();
        result[0].ExerciseName.Should().Be("Bench");
        result[0].SeriesComparisons[0].PreviousResult!.Weight.Should().Be(70);
        result[0].SeriesComparisons[0].CurrentResult.Weight.Should().Be(80);
    }

    [Test]
    public void Build_ShouldSkipEmptyExerciseIds()
    {
        var result = TrainingComparisonReportBuilder.Build(
            [new TrainingExerciseInput { ExerciseId = Id<Exercise>.Empty, Series = 1, Unit = WeightUnits.Kilograms }],
            [],
            []);
        result.Should().BeEmpty();
    }

    private static WorkoutExerciseScorePersistenceModel Score(Id<Exercise> exerciseId, double reps, double weight)
        => new(Id<ExerciseScore>.New(), exerciseId, Id<AccountReference>.New(), reps, 1, weight, WeightUnits.Kilograms, Id<Training>.New(), 0, default, null, null);
}
