using FluentAssertions;
using LgymApi.Application.Features.Training;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Application.WorkoutProgress.TrainingExecution;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TrainingServiceBuildComparisonReportTests
{
    [Test]
    public void Should_ReturnExercisesInCurrentExercisesOrder_When_Called()
    {
        // Arrange
        var exerciseA = Id<Exercise>.New();
        var exerciseB = Id<Exercise>.New();
        var exerciseC = Id<Exercise>.New();

        // Input order: C, A, B
        var currentExercises = new List<TrainingExerciseInput>
        {
            new() { ExerciseId = exerciseC, Series = 1, Reps = 10, Weight = 60, Unit = WeightUnits.Kilograms },
            new() { ExerciseId = exerciseA, Series = 1, Reps = 8, Weight = 80, Unit = WeightUnits.Kilograms },
            new() { ExerciseId = exerciseB, Series = 1, Reps = 6, Weight = 100, Unit = WeightUnits.Kilograms },
        };

        var previousScores = new Dictionary<string, ExerciseScore>();

        var exerciseDetails = new Dictionary<Id<Exercise>, string>
        {
            [exerciseA] = "Bench Press",
            [exerciseB] = "Squat",
            [exerciseC] = "Deadlift",
        };

        // Act
        var result = TrainingComparisonReportBuilder.Build(currentExercises, previousScores, exerciseDetails);

        // Assert
        result.Should().HaveCount(3);
        result[0].ExerciseId.Should().Be(exerciseC);
        result[1].ExerciseId.Should().Be(exerciseA);
        result[2].ExerciseId.Should().Be(exerciseB);
    }

    [Test]
    public void Should_PreserveExerciseOrder_When_MultipleSeriesPerExercise()
    {
        // Arrange
        var exerciseA = Id<Exercise>.New();
        var exerciseB = Id<Exercise>.New();

        // B appears first (series 1, 2), then A (series 1, 2)
        var currentExercises = new List<TrainingExerciseInput>
        {
            new() { ExerciseId = exerciseB, Series = 1, Reps = 5, Weight = 120, Unit = WeightUnits.Kilograms },
            new() { ExerciseId = exerciseB, Series = 2, Reps = 5, Weight = 120, Unit = WeightUnits.Kilograms },
            new() { ExerciseId = exerciseA, Series = 1, Reps = 10, Weight = 60, Unit = WeightUnits.Kilograms },
            new() { ExerciseId = exerciseA, Series = 2, Reps = 8, Weight = 65, Unit = WeightUnits.Kilograms },
        };

        var previousScores = new Dictionary<string, ExerciseScore>();

        var exerciseDetails = new Dictionary<Id<Exercise>, string>
        {
            [exerciseA] = "Bench Press",
            [exerciseB] = "Squat",
        };

        // Act
        var result = TrainingComparisonReportBuilder.Build(currentExercises, previousScores, exerciseDetails);

        // Assert
        result.Should().HaveCount(2);
        result[0].ExerciseId.Should().Be(exerciseB);
        result[0].SeriesComparisons.Should().HaveCount(2);
        result[1].ExerciseId.Should().Be(exerciseA);
        result[1].SeriesComparisons.Should().HaveCount(2);
    }

    [Test]
    public void Should_IncludePreviousResult_When_PreviousScoresExist()
    {
        // Arrange
        var exerciseId = Id<Exercise>.New();

        var currentExercises = new List<TrainingExerciseInput>
        {
            new() { ExerciseId = exerciseId, Series = 1, Reps = 10, Weight = 80, Unit = WeightUnits.Kilograms },
        };

        var previousScores = new Dictionary<string, ExerciseScore>
        {
            [$"{exerciseId}-1"] = new ExerciseScore
            {
                Id = Id<ExerciseScore>.New(),
                ExerciseId = (Id<Exercise>)exerciseId,
                Reps = 8,
                Weight = new Weight(75, WeightUnits.Kilograms),
                Series = 1,
            },
        };

        var exerciseDetails = new Dictionary<Id<Exercise>, string>
        {
            [exerciseId] = "Bench Press",
        };

        // Act
        var result = TrainingComparisonReportBuilder.Build(currentExercises, previousScores, exerciseDetails);

        // Assert
        result.Should().HaveCount(1);
        var comparison = result[0].SeriesComparisons[0];
        comparison.CurrentResult.Weight.Should().Be(80);
        comparison.CurrentResult.Reps.Should().Be(10);
        comparison.PreviousResult.Should().NotBeNull();
        comparison.PreviousResult!.Weight.Should().Be(75);
        comparison.PreviousResult.Reps.Should().Be(8);
    }

}
