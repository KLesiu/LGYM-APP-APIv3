using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Training;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Application.WorkoutProgress.TrainingExecution;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TrainingServiceFacadeTests
{
    [Test]
    public async Task AddTrainingAsync_MapsTheLegacyInputAndDelegatesToTheCompletionUseCase()
    {
        var completionUseCase = Substitute.For<ICompleteTrainingUseCase>();
        var historyReadService = Substitute.For<ITrainingHistoryReadService>();
        var service = new TrainingService(completionUseCase, historyReadService);
        var userId = Id<AccountReference>.New();
        var input = new AddTrainingInput(
            Id<Gym>.New(),
            Id<PlanDayReference>.New(),
            DateTime.UtcNow,
            [new TrainingExerciseInput { ExerciseId = Id<Exercise>.New(), Series = 1, Reps = 8, Weight = 80, Unit = WeightUnits.Kilograms }]);
        var expected = new TrainingSummaryResult { Message = "Created" };
        completionUseCase.AddTrainingAsync(
                userId,
                Arg.Any<CompleteTrainingInput>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<TrainingSummaryResult, AppError>.Success(expected));

        var result = await service.AddTrainingAsync(userId, input);

        result.Value.Should().BeSameAs(expected);
        await completionUseCase.Received(1).AddTrainingAsync(
            userId,
            Arg.Is<CompleteTrainingInput>(mapped =>
                mapped.GymId == input.GymId
                && mapped.PlanDayId == input.PlanDayId
                && mapped.CreatedAt == input.CreatedAt
                && mapped.Exercises == input.Exercises),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HistoryMethods_DelegateToTheTrainingHistoryReadService()
    {
        var completionUseCase = Substitute.For<ICompleteTrainingUseCase>();
        var historyReadService = Substitute.For<ITrainingHistoryReadService>();
        var service = new TrainingService(completionUseCase, historyReadService);
        var userId = Id<AccountReference>.New();
        var createdAt = DateTime.UtcNow;
        historyReadService.GetLastTrainingAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result<WorkoutTrainingReadModel, AppError>.Success(new WorkoutTrainingReadModel(Id<Training>.New(), Id<PlanDayReference>.New(), DateTimeOffset.UtcNow, null)));
        historyReadService.GetTrainingByDateAsync(userId, createdAt, Arg.Any<CancellationToken>())
            .Returns(Result<List<TrainingByDateDetails>, AppError>.Success([]));
        historyReadService.GetTrainingDatesAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result<List<DateTime>, AppError>.Success([]));

        await service.GetLastTrainingAsync(userId);
        await service.GetTrainingByDateAsync(userId, createdAt);
        await service.GetTrainingDatesAsync(userId);

        await historyReadService.Received(1).GetLastTrainingAsync(userId, Arg.Any<CancellationToken>());
        await historyReadService.Received(1).GetTrainingByDateAsync(userId, createdAt, Arg.Any<CancellationToken>());
        await historyReadService.Received(1).GetTrainingDatesAsync(userId, Arg.Any<CancellationToken>());
    }
}
