using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Application.WorkoutProgress.Scoring.Elo;
using LgymApi.Application.WorkoutProgress.TrainingExecution;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Services;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Resources;
using LgymApi.TrainingPlanning.Contracts;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TrainingServiceAddTrainingTests
{
    private IAccountAccessReader _accountAccess = null!;
    private IWorkoutGymPersistence _gyms = null!;
    private IWorkoutTrainingPersistence _trainings = null!;
    private IWorkoutExercisePersistence _exercises = null!;
    private IWorkoutExerciseScorePersistence _scores = null!;
    private IWorkoutEloPersistence _elo = null!;
    private IPlanDayReferenceReadService _planDayReferences = null!;
    private IUnitOfWork _unitOfWork = null!;
    private ICompleteTrainingUseCase _service = null!;

    [SetUp]
    public void SetUp()
    {
        _accountAccess = Substitute.For<IAccountAccessReader>();
        _gyms = Substitute.For<IWorkoutGymPersistence>();
        _trainings = Substitute.For<IWorkoutTrainingPersistence>();
        _exercises = Substitute.For<IWorkoutExercisePersistence>();
        _scores = Substitute.For<IWorkoutExerciseScorePersistence>();
        _elo = Substitute.For<IWorkoutEloPersistence>();
        _planDayReferences = Substitute.For<IPlanDayReferenceReadService>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _planDayReferences.GetByIdAsync(Arg.Any<Id<PlanDayReference>>(), Arg.Any<CancellationToken>())
            .Returns(call => new PlanDayReferenceReadModel(call.ArgAt<Id<PlanDayReference>>(0), Id<PlanReference>.New(), "Plan day", true, false));
        _service = new CompleteTrainingUseCase(
            _accountAccess,
            _gyms,
            _planDayReferences,
            _trainings,
            _exercises,
            _scores,
            _elo,
            new FixedRankService(),
            _unitOfWork,
            [
                new StandardExerciseEloCalculator(),
                new StrengthWeightedExerciseEloCalculator(),
                new VolumeWeightedExerciseEloCalculator(),
                new PullupWeightedExerciseEloCalculator()
            ]);
    }

    [Test]
    public async Task EmptyAccountId_ShouldReturnInvalidTrainingDataError()
    {
        var result = await _service.AddTrainingAsync(Id<AccountReference>.Empty, Input(Id<Gym>.New(), Id<PlanDayReference>.New()));
        result.Error.Should().BeOfType<InvalidTrainingDataError>();
    }

    [Test]
    public async Task MissingAccount_ShouldReturnTrainingNotFoundError()
    {
        var accountId = Id<AccountReference>.New();
        _accountAccess.GetByIdAsync(accountId, Arg.Any<CancellationToken>()).Returns((AccountAccessFacts?)null);
        var result = await _service.AddTrainingAsync(accountId, Input(Id<Gym>.New(), Id<PlanDayReference>.New()));
        result.Error.Should().BeOfType<TrainingNotFoundError>();
    }

    [Test]
    public async Task MissingEloEntry_ShouldRollbackAndReturnInternalError()
    {
        var accountId = Id<AccountReference>.New();
        var gymId = Id<Gym>.New();
        var transaction = Substitute.For<IUnitOfWorkTransaction>();
        ArrangeAccountAndGym(accountId, gymId);
        _unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(transaction);
        _elo.GetLatestEntryAsync(accountId, Arg.Any<CancellationToken>()).Returns((WorkoutEloPersistenceModel?)null);

        var result = await _service.AddTrainingAsync(accountId, Input(gymId, Id<PlanDayReference>.New()));

        result.Error.Should().BeOfType<InternalServerError>();
        result.Error.Message.Should().Be(Messages.TryAgain);
        await transaction.Received(1).RollbackAsync(CancellationToken.None);
    }

    [Test]
    public async Task SuccessfulTraining_ShouldStageAllWritesSaveOnceAndCommit()
    {
        var accountId = Id<AccountReference>.New();
        var gymId = Id<Gym>.New();
        var exerciseId = Id<Exercise>.New();
        var transaction = Substitute.For<IUnitOfWorkTransaction>();
        ArrangeAccountAndGym(accountId, gymId);
        _exercises.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Id<Exercise>>>(), Arg.Any<CancellationToken>())
            .Returns([Exercise(exerciseId, ExerciseEloFormula.Standard)]);
        _scores.GetByAccountAndExercisesAsync(accountId, Arg.Any<IReadOnlyCollection<Id<Exercise>>>(), Arg.Any<CancellationToken>())
            .Returns([Score(accountId, exerciseId, gymId)]);
        _elo.GetLatestEntryAsync(accountId, Arg.Any<CancellationToken>())
            .Returns(new WorkoutEloPersistenceModel(Id<EloRegistry>.New(), accountId, DateTimeOffset.UtcNow, 1000, null));
        _unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(transaction);

        var result = await _service.AddTrainingAsync(accountId, Input(gymId, Id<PlanDayReference>.New(), exerciseId));

        result.IsSuccess.Should().BeTrue();
        await _trainings.Received(1).AddAsync(Arg.Any<WorkoutTrainingWriteModel>(), Arg.Any<CancellationToken>());
        await _scores.Received(1).AddRangeAsync(Arg.Any<IReadOnlyCollection<WorkoutExerciseScoreWriteModel>>(), Arg.Any<CancellationToken>());
        await _trainings.Received(1).AddExerciseScoreLinksAsync(Arg.Any<IReadOnlyCollection<WorkoutTrainingExerciseScorePersistenceModel>>(), Arg.Any<CancellationToken>());
        await _elo.Received(1).AddAsync(Arg.Any<WorkoutEloWriteModel>(), Arg.Any<CancellationToken>());
        await _trainings.Received(1).UpdateAccountProfileRankAsync(accountId, "Junior 1", Arg.Any<CancellationToken>());
        await _trainings.Received(1).StageTrainingCompletedCommandAsync(accountId, Arg.Any<Id<Training>>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveFailure_ShouldRollbackAndPropagateAfterStagingWrites()
    {
        var accountId = Id<AccountReference>.New();
        var gymId = Id<Gym>.New();
        var transaction = Substitute.For<IUnitOfWorkTransaction>();
        ArrangeAccountAndGym(accountId, gymId);
        _elo.GetLatestEntryAsync(accountId, Arg.Any<CancellationToken>())
            .Returns(new WorkoutEloPersistenceModel(Id<EloRegistry>.New(), accountId, DateTimeOffset.UtcNow, 1000, null));
        _unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(transaction);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("save failed")));

        var action = () => _service.AddTrainingAsync(accountId, Input(gymId, Id<PlanDayReference>.New()));

        await action.Should().ThrowAsync<InvalidOperationException>();
        await _trainings.Received(1).AddAsync(Arg.Any<WorkoutTrainingWriteModel>(), Arg.Any<CancellationToken>());
        await transaction.Received(1).RollbackAsync(CancellationToken.None);
        await transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void PullupProfile_ShouldRewardLowerWeight()
    {
        var calculator = new PullupWeightedExerciseEloCalculator();
        calculator.Calculate(new(80, 8, 60, 8)).Should().BeGreaterThan(calculator.Calculate(new(80, 8, 100, 8)));
    }

    [Test]
    public async Task MissingPlanDay_ShouldReturnTrainingNotFoundErrorWithoutStagingWrites()
    {
        var accountId = Id<AccountReference>.New();
        var gymId = Id<Gym>.New();
        var planDayId = Id<PlanDayReference>.New();
        ArrangeAccountAndGym(accountId, gymId);
        _planDayReferences.GetByIdAsync(planDayId, Arg.Any<CancellationToken>()).Returns(new PlanDayReferenceReadModel(planDayId, Id<PlanReference>.Empty, string.Empty, false, false));

        var result = await _service.AddTrainingAsync(accountId, Input(gymId, planDayId));

        result.Error.Should().BeOfType<TrainingNotFoundError>();
        await _trainings.DidNotReceive().AddAsync(Arg.Any<WorkoutTrainingWriteModel>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    private void ArrangeAccountAndGym(Id<AccountReference> accountId, Id<Gym> gymId)
    {
        _accountAccess.GetByIdAsync(accountId, Arg.Any<CancellationToken>()).Returns(new AccountAccessFacts(accountId, false, false, [], []));
        _gyms.FindByIdAsync(gymId, Arg.Any<CancellationToken>()).Returns(new WorkoutGymPersistenceModel(gymId, accountId, "Gym", null, false, default, default));
    }

    private static CompleteTrainingInput Input(Id<Gym> gymId, Id<PlanDayReference> planDayId, Id<Exercise>? exerciseId = null)
        => new(gymId, planDayId, DateTime.UtcNow, exerciseId.HasValue ? [new() { ExerciseId = exerciseId.Value, Series = 1, Reps = 10, Weight = 80, Unit = WeightUnits.Kilograms }] : []);

    private static WorkoutExercisePersistenceModel Exercise(Id<Exercise> id, ExerciseEloFormula formula)
        => new(id, null, "Exercise", BodyParts.Chest, formula, null, null, false, default, default);

    private static WorkoutExerciseScorePersistenceModel Score(Id<AccountReference> accountId, Id<Exercise> exerciseId, Id<Gym> gymId)
        => new(Id<ExerciseScore>.New(), exerciseId, accountId, 5, 1, 70, WeightUnits.Kilograms, Id<Training>.New(), 0, DateTimeOffset.UtcNow, null,
            new WorkoutTrainingPersistenceModel(Id<Training>.New(), accountId, Id<PlanDayReference>.New(), gymId, DateTimeOffset.UtcNow, null));

    private sealed class FixedRankService : IRankService
    {
        public IReadOnlyList<RankDefinition> GetRanks() => [new() { Name = "Junior 1", NeedElo = 0 }];
        public RankDefinition GetCurrentRank(Elo elo) => new() { Name = "Junior 1", NeedElo = 0 };
        public RankDefinition? GetNextRank(string currentRankName) => new RankDefinition { Name = "Junior 2", NeedElo = 1001 };
    }
}
