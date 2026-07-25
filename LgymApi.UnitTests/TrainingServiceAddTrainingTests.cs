using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.WorkoutProgress.Scoring.Elo;
using LgymApi.Application.Features.Training;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands;
using LgymApi.Application.WorkoutProgress.TrainingExecution;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Services;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TrainingServiceAddTrainingTests
{
    private ICompleteTrainingUseCaseDependencies _deps = null!;
    private IUserRepository _userRepository = null!;
    private IGymRepository _gymRepository = null!;
    private ITrainingRepository _trainingRepository = null!;
    private IExerciseRepository _exerciseRepository = null!;
    private IExerciseScoreRepository _exerciseScoreRepository = null!;
    private ITrainingExerciseScoreRepository _trainingExerciseScoreRepository = null!;
    private ICommandDispatcher _commandDispatcher = null!;
    private IEloRegistryRepository _eloRepository = null!;
    private IRankService _rankService = null!;
    private IUnitOfWork _unitOfWork = null!;
    private ICompleteTrainingUseCase _service = null!;

    [SetUp]
    public void SetUp()
    {
        _userRepository = Substitute.For<IUserRepository>();
        _gymRepository = Substitute.For<IGymRepository>();
        _trainingRepository = Substitute.For<ITrainingRepository>();
        _exerciseRepository = Substitute.For<IExerciseRepository>();
        _exerciseScoreRepository = Substitute.For<IExerciseScoreRepository>();
        _trainingExerciseScoreRepository = Substitute.For<ITrainingExerciseScoreRepository>();
        _commandDispatcher = Substitute.For<ICommandDispatcher>();
        _eloRepository = Substitute.For<IEloRegistryRepository>();
        _rankService = Substitute.For<IRankService>();
        _unitOfWork = Substitute.For<IUnitOfWork>();

        _deps = Substitute.For<ICompleteTrainingUseCaseDependencies>();
        _deps.UserRepository.Returns(_userRepository);
        _deps.GymRepository.Returns(_gymRepository);
        _deps.TrainingRepository.Returns(_trainingRepository);
        _deps.ExerciseRepository.Returns(_exerciseRepository);
        _deps.ExerciseScoreRepository.Returns(_exerciseScoreRepository);
        _deps.TrainingExerciseScoreRepository.Returns(_trainingExerciseScoreRepository);
        _deps.CommandDispatcher.Returns(_commandDispatcher);
        _deps.EloRepository.Returns(_eloRepository);
        _deps.RankService.Returns(_rankService);
        _deps.UnitOfWork.Returns(_unitOfWork);
        _deps.ExerciseEloCalculators.Returns(new IExerciseEloCalculator[]
        {
            new StandardExerciseEloCalculator(),
            new StrengthWeightedExerciseEloCalculator(),
            new VolumeWeightedExerciseEloCalculator(),
            new PullupWeightedExerciseEloCalculator()
        });

        _service = new CompleteTrainingUseCase(_deps);
    }

    [Test]
    public async Task Should_ReturnInternalServerError_When_EloEntryIsNull()
    {
        // Arrange
        var userId = Id<User>.New();
        var gymId = Id<Gym>.New();
        var planDayId = Id<PlanDay>.New();
        var exerciseId = Id<Exercise>.New();

        var user = new User
        {
            Id = userId,
            Name = "TestUser",
            Email = "test@example.com",
            ProfileRank = "Junior 1"
        };

        var gym = new Gym { Id = gymId, UserId = userId, Name = "TestGym" };

        var exercises = new List<TrainingExerciseInput>
        {
            new() { ExerciseId = exerciseId, Series = 1, Reps = 10, Weight = 80, Unit = WeightUnits.Kilograms }
        };

        var input = new AddTrainingInput(gymId, planDayId, DateTime.UtcNow, exercises);

        _userRepository.FindByIdAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>())
            .Returns(user);
        _gymRepository.FindByIdAsync(Arg.Any<Id<Gym>>(), Arg.Any<CancellationToken>())
            .Returns(gym);
        _exerciseRepository.GetByIdsAsync(Arg.Any<List<Id<Exercise>>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Exercise> { new() { Id = exerciseId, Name = "Bench Press" } });
        _exerciseScoreRepository.GetByUserAndExercisesAsync(Arg.Any<Id<User>>(), Arg.Any<List<Id<Exercise>>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ExerciseScore>());

        var transaction = Substitute.For<IUnitOfWorkTransaction>();
        _unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(transaction);

        _eloRepository.GetLatestEntryAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>())
            .Returns((EloRegistry?)null);

        // Act
        var result = await _service.AddTrainingAsync(userId, ToCompleteInput(input));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InternalServerError>();
        result.Error.Message.Should().Be(Messages.TryAgain);
    }

    [Test]
    public async Task Should_PropagateException_When_RepositoryThrowsException()
    {
        // Arrange
        var userId = Id<User>.New();
        var gymId = Id<Gym>.New();
        var planDayId = Id<PlanDay>.New();

        var input = new AddTrainingInput(gymId, planDayId, DateTime.UtcNow, new List<TrainingExerciseInput>());

        _userRepository.FindByIdAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Database connection failed"));

        // Act & Assert
        var action = () => _service.AddTrainingAsync(userId, ToCompleteInput(input));
        var ex = await action.Should().ThrowAsync<InvalidOperationException>();
        ex.And.Message.Should().Be("Database connection failed");
    }

    [Test]
    public async Task Should_ReturnInvalidTrainingDataError_When_UserIdIsEmpty()
    {
        // Arrange
        var emptyUserId = Id<User>.Empty;
        var gymId = Id<Gym>.New();
        var planDayId = Id<PlanDay>.New();

        var input = new AddTrainingInput(gymId, planDayId, DateTime.UtcNow, new List<TrainingExerciseInput>());

        // Act
        var result = await _service.AddTrainingAsync(emptyUserId, ToCompleteInput(input));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidTrainingDataError>();
    }

    [Test]
    public async Task Should_ReturnInvalidTrainingDataError_When_GymIdIsEmpty()
    {
        // Arrange
        var userId = Id<User>.New();
        var emptyGymId = Id<Gym>.Empty;
        var planDayId = Id<PlanDay>.New();

        var input = new AddTrainingInput(emptyGymId, planDayId, DateTime.UtcNow, new List<TrainingExerciseInput>());

        // Act
        var result = await _service.AddTrainingAsync(userId, ToCompleteInput(input));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidTrainingDataError>();
    }

    [Test]
    public async Task Should_ReturnInvalidTrainingDataError_When_PlanDayIdIsEmpty()
    {
        // Arrange
        var userId = Id<User>.New();
        var gymId = Id<Gym>.New();
        var emptyPlanDayId = Id<PlanDay>.Empty;

        var input = new AddTrainingInput(gymId, emptyPlanDayId, DateTime.UtcNow, new List<TrainingExerciseInput>());

        // Act
        var result = await _service.AddTrainingAsync(userId, ToCompleteInput(input));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidTrainingDataError>();
    }

    [Test]
    public async Task Should_ReturnInvalidTrainingDataError_When_ExerciseHasUnknownUnit()
    {
        // Arrange
        var userId = Id<User>.New();
        var gymId = Id<Gym>.New();
        var planDayId = Id<PlanDay>.New();
        var exerciseId = Id<Exercise>.New();

        var user = new User
        {
            Id = userId,
            Name = "TestUser",
            Email = "test@example.com",
            ProfileRank = "Junior 1"
        };

        var gym = new Gym { Id = gymId, UserId = userId, Name = "TestGym" };

        var exercises = new List<TrainingExerciseInput>
        {
            new() { ExerciseId = exerciseId, Series = 1, Reps = 10, Weight = 80, Unit = WeightUnits.Unknown }
        };

        var input = new AddTrainingInput(gymId, planDayId, DateTime.UtcNow, exercises);

        _userRepository.FindByIdAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>())
            .Returns(user);
        _gymRepository.FindByIdAsync(Arg.Any<Id<Gym>>(), Arg.Any<CancellationToken>())
            .Returns(gym);
        _exerciseRepository.GetByIdsAsync(Arg.Any<List<Id<Exercise>>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Exercise> { new() { Id = exerciseId, Name = "Bench Press" } });
        _exerciseScoreRepository.GetByUserAndExercisesAsync(Arg.Any<Id<User>>(), Arg.Any<List<Id<Exercise>>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ExerciseScore>());

        var transaction = Substitute.For<IUnitOfWorkTransaction>();
        _unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(transaction);

        // Act
        var result = await _service.AddTrainingAsync(userId, ToCompleteInput(input));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidTrainingDataError>();

        // Verify transaction was rolled back
        await transaction.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_UseDifferentEloGain_ForDifferentExerciseProfiles()
    {
        var userId = Id<User>.New();
        var gymId = Id<Gym>.New();
        var planDayId = Id<PlanDay>.New();
        var exerciseId = Id<Exercise>.New();

        var user = new User
        {
            Id = userId,
            Name = "TestUser",
            Email = "test@example.com",
            ProfileRank = "Junior 1"
        };

        var gym = new Gym { Id = gymId, UserId = userId, Name = "TestGym" };

        var exercises = new List<TrainingExerciseInput>
        {
            new() { ExerciseId = exerciseId, Series = 1, Reps = 10, Weight = 80, Unit = WeightUnits.Kilograms }
        };

        var input = new AddTrainingInput(gymId, planDayId, DateTime.UtcNow, exercises);
        var previousTraining = new Training
        {
            Id = Id<Training>.New(),
            UserId = userId,
            TypePlanDayId = planDayId,
            GymId = gymId
        };

        var previousScore = new ExerciseScore
        {
            Id = Id<ExerciseScore>.New(),
            ExerciseId = exerciseId,
            UserId = userId,
            Reps = 5,
            Series = 1,
            Weight = new Weight(80, WeightUnits.Kilograms),
            TrainingId = previousTraining.Id,
            Training = previousTraining
        };

        _userRepository.FindByIdAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>())
            .Returns(user);
        _gymRepository.FindByIdAsync(Arg.Any<Id<Gym>>(), Arg.Any<CancellationToken>())
            .Returns(gym);
        _exerciseScoreRepository.GetByUserAndExercisesAsync(Arg.Any<Id<User>>(), Arg.Any<List<Id<Exercise>>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ExerciseScore> { previousScore });
        _eloRepository.GetLatestEntryAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>())
            .Returns(new EloRegistry { Id = Id<EloRegistry>.New(), UserId = userId, Date = DateTimeOffset.UtcNow, Elo = 1000 });
        var operationOrder = new List<string>();
        _commandDispatcher.EnqueueAsync(Arg.Any<TrainingCompletedCommand>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => operationOrder.Add("enqueue"));
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1))
            .AndDoes(_ => operationOrder.Add("commit"));

        async Task<int> RunProfileAsync(ExerciseEloFormula formula)
        {
            var transaction = Substitute.For<IUnitOfWorkTransaction>();
            _unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>())
                .Returns(transaction);
            var service = new CompleteTrainingUseCase(new SuccessfulTrainingServiceDependencies(
                _userRepository,
                _gymRepository,
                _trainingRepository,
                _exerciseRepository,
                _exerciseScoreRepository,
                _trainingExerciseScoreRepository,
                _commandDispatcher,
                _eloRepository,
                new FixedRankService(),
                _unitOfWork,
                new IExerciseEloCalculator[]
                {
                    new StandardExerciseEloCalculator(),
                    new StrengthWeightedExerciseEloCalculator(),
                    new VolumeWeightedExerciseEloCalculator(),
                    new PullupWeightedExerciseEloCalculator()
                }));

            _exerciseRepository.GetByIdsAsync(Arg.Any<List<Id<Exercise>>>(), Arg.Any<CancellationToken>())
                .Returns(new List<Exercise>
                {
                    new()
                    {
                        Id = exerciseId,
                        Name = "Machine Pullup",
                        EloFormula = formula
                    }
                });

            _trainingRepository.AddAsync(Arg.Any<Training>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            _exerciseScoreRepository.AddRangeAsync(Arg.Any<IEnumerable<ExerciseScore>>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            _trainingExerciseScoreRepository.AddRangeAsync(Arg.Any<IEnumerable<TrainingExerciseScore>>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            _eloRepository.AddAsync(Arg.Any<EloRegistry>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            _userRepository.UpdateAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            var result = await service.AddTrainingAsync(userId, ToCompleteInput(input));

            result.IsSuccess.Should().BeTrue();
            await transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
            return result.Value.GainElo;
        }

        var standardGain = await RunProfileAsync(ExerciseEloFormula.Standard);
        var strengthGain = await RunProfileAsync(ExerciseEloFormula.StrengthWeighted);
        var volumeGain = await RunProfileAsync(ExerciseEloFormula.VolumeWeighted);

        strengthGain.Should().BeLessThan(standardGain);
        standardGain.Should().BeLessThan(volumeGain);
        await _commandDispatcher.Received(3).EnqueueAsync(Arg.Is<TrainingCompletedCommand>(command =>
            command.UserId == userId && !command.TrainingId.IsEmpty));
        operationOrder.Should().Equal("enqueue", "commit", "enqueue", "commit", "enqueue", "commit");
    }

    [Test]
    public void Should_RewardLowerWeight_ForPullupProfile()
    {
        var calculator = new PullupWeightedExerciseEloCalculator();

        var lowerWeightGain = calculator.Calculate(new ExerciseEloCalculationInput(
            PreviousWeight: 80,
            PreviousReps: 8,
            CurrentWeight: 60,
            CurrentReps: 8));

        var higherWeightGain = calculator.Calculate(new ExerciseEloCalculationInput(
            PreviousWeight: 80,
            PreviousReps: 8,
            CurrentWeight: 100,
            CurrentReps: 8));

        lowerWeightGain.Should().BeGreaterThan(higherWeightGain);
    }

    private sealed class FixedRankService : IRankService
    {
        public IReadOnlyList<RankDefinition> GetRanks() => [new RankDefinition { Name = "Junior 1", NeedElo = 0 }];

        public RankDefinition GetCurrentRank(Elo elo) => new RankDefinition { Name = "Junior 1", NeedElo = 0 };

        public RankDefinition? GetNextRank(string currentRankName)
            => currentRankName == "Junior 1"
                ? new RankDefinition { Name = "Junior 2", NeedElo = 1001 }
                : null;
    }

    private static CompleteTrainingInput ToCompleteInput(AddTrainingInput input)
        => new(input.GymId, input.PlanDayId, input.CreatedAt, input.Exercises);

    private sealed class SuccessfulTrainingServiceDependencies : ICompleteTrainingUseCaseDependencies
    {
        public SuccessfulTrainingServiceDependencies(
            IUserRepository userRepository,
            IGymRepository gymRepository,
            ITrainingRepository trainingRepository,
            IExerciseRepository exerciseRepository,
            IExerciseScoreRepository exerciseScoreRepository,
            ITrainingExerciseScoreRepository trainingExerciseScoreRepository,
            ICommandDispatcher commandDispatcher,
            IEloRegistryRepository eloRepository,
            IRankService rankService,
            IUnitOfWork unitOfWork,
            IReadOnlyCollection<IExerciseEloCalculator> exerciseEloCalculators)
        {
            UserRepository = userRepository;
            GymRepository = gymRepository;
            TrainingRepository = trainingRepository;
            ExerciseRepository = exerciseRepository;
            ExerciseScoreRepository = exerciseScoreRepository;
            TrainingExerciseScoreRepository = trainingExerciseScoreRepository;
            CommandDispatcher = commandDispatcher;
            EloRepository = eloRepository;
            RankService = rankService;
            UnitOfWork = unitOfWork;
            ExerciseEloCalculators = exerciseEloCalculators;
        }

        public IUserRepository UserRepository { get; }
        public IGymRepository GymRepository { get; }
        public ITrainingRepository TrainingRepository { get; }
        public IExerciseRepository ExerciseRepository { get; }
        public IExerciseScoreRepository ExerciseScoreRepository { get; }
        public ITrainingExerciseScoreRepository TrainingExerciseScoreRepository { get; }
        public ICommandDispatcher CommandDispatcher { get; }
        public IEloRegistryRepository EloRepository { get; }
        public IRankService RankService { get; }
        public IUnitOfWork UnitOfWork { get; }
        public IReadOnlyCollection<IExerciseEloCalculator> ExerciseEloCalculators { get; }
    }
}
