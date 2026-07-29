using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Application.WorkoutProgress.Dashboard;
using LgymApi.Application.WorkoutProgress.ProgressData;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Application.WorkoutProgress.TrainingExecution;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class WorkoutProgressDashboardReadServiceTests
{
    [Test]
    public async Task GetTrainingByDateAsync_MapsTrainingEntitiesToDashboardReadModels()
    {
        var trainingHistory = Substitute.For<ITrainingHistoryReadService>();
        var progress = Substitute.For<IWorkoutProgressReadWriteService>();
        var traineeId = Id<AccountReference>.New();
        var createdAt = DateTime.UtcNow;
        var planDayId = Id<PlanDayReference>.New();
        var exercise = new ProgressExerciseReadModel(Id<Exercise>.New(), "Bench", null, BodyParts.Chest, null, null, null);
        var score = new WorkoutExerciseScoreReadModel(Id<ExerciseScore>.New(), exercise.Id, 80, WeightUnits.Kilograms, 8, 1, null);
        var training = new TrainingByDateDetails
        {
            Id = Id<Training>.New(),
            TypePlanDayId = planDayId,
            CreatedAt = createdAt,
            PlanDay = new PlanDayReferenceReadModel(planDayId, Id<PlanReference>.New(), "Push", true, false),
            Gym = "Gym",
            Exercises =
            [
                new EnrichedExercise
                {
                    ExerciseScoreId = score.Id,
                    ExerciseDetails = exercise,
                    ScoresDetails = [score]
                }
            ]
        };
        trainingHistory.GetTrainingByDateAsync(traineeId, createdAt, Arg.Any<CancellationToken>())
            .Returns(Result<List<TrainingByDateDetails>, AppError>.Success([training]));
        var service = new WorkoutProgressDashboardReadService(trainingHistory, progress);

        var result = await service.GetTrainingByDateAsync(traineeId, createdAt);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].Id.Should().Be(training.Id.ToString());
        result.Value[0].PlanDay.Should().BeEquivalentTo(new { Id = planDayId.ToString(), Name = "Push" });
        result.Value[0].Exercises[0].ExerciseDetails.Should().BeEquivalentTo(new { Id = exercise.Id.ToString(), Name = "Bench" });
        result.Value[0].Exercises[0].ScoresDetails[0].Should().BeEquivalentTo(new { Id = score.Id.ToString(), ExerciseId = exercise.Id.ToString(), Weight = 80d, Reps = 8d, Series = 1 });
    }

    [Test]
    public async Task ProgressReads_DelegateAuthorizedTraineeIdToOwnerContracts()
    {
        var trainingHistory = Substitute.For<ITrainingHistoryReadService>();
        var progress = Substitute.For<IWorkoutProgressReadWriteService>();
        var traineeId = Id<AccountReference>.New();
        var exerciseId = Id<Exercise>.New();
        trainingHistory.GetTrainingDatesAsync(traineeId, Arg.Any<CancellationToken>()).Returns(Result<List<DateTime>, AppError>.Success([]));
        progress.GetExerciseScoreChartAsync(traineeId, exerciseId, Arg.Any<CancellationToken>()).Returns(Result<List<ExerciseScoreChartPoint>, AppError>.Success([]));
        progress.GetEloChartAsync(traineeId, Arg.Any<CancellationToken>()).Returns(Result<List<EloChartPoint>, AppError>.Success([]));
        progress.GetMainRecordHistoryAsync(traineeId, Arg.Any<CancellationToken>()).Returns(Result<List<MainRecordReadModel>, AppError>.Success([]));
        progress.GetBestMainRecordsAsync(traineeId, Arg.Any<CancellationToken>()).Returns(Result<List<MainRecordBestReadModel>, AppError>.Success([]));
        var service = new WorkoutProgressDashboardReadService(trainingHistory, progress);

        await service.GetTrainingDatesAsync(traineeId);
        await service.GetExerciseScoreChartAsync(traineeId, exerciseId.ToString());
        await service.GetEloChartAsync(traineeId);
        await service.GetMainRecordHistoryAsync(traineeId);
        await service.GetBestMainRecordsAsync(traineeId);

        await trainingHistory.Received(1).GetTrainingDatesAsync(traineeId, Arg.Any<CancellationToken>());
        await progress.Received(1).GetExerciseScoreChartAsync(traineeId, exerciseId, Arg.Any<CancellationToken>());
        await progress.Received(1).GetEloChartAsync(traineeId, Arg.Any<CancellationToken>());
        await progress.Received(1).GetMainRecordHistoryAsync(traineeId, Arg.Any<CancellationToken>());
        await progress.Received(1).GetBestMainRecordsAsync(traineeId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetTrainingByDateAsync_PreservesOwnerFailure()
    {
        var trainingHistory = Substitute.For<ITrainingHistoryReadService>();
        var progress = Substitute.For<IWorkoutProgressReadWriteService>();
        var traineeId = Id<AccountReference>.New();
        var error = new TrainingNotFoundError("missing");
        trainingHistory.GetTrainingByDateAsync(traineeId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<List<TrainingByDateDetails>, AppError>.Failure(error));
        var service = new WorkoutProgressDashboardReadService(trainingHistory, progress);

        var result = await service.GetTrainingByDateAsync(traineeId, DateTime.UtcNow);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeSameAs(error);
    }
}
