using FluentAssertions;
using LgymApi.Application.Repositories;
using LgymApi.Application.Platform.ReferenceData.Units;
using LgymApi.Application.Models;
using LgymApi.BackgroundWorker.Actions;
using LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using MainRecordEntity = LgymApi.Domain.Entities.MainRecord;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class UpdateTrainingMainRecordsHandlerTests
{
    private TestMainRecordRepository _testMainRecordRepository = null!;
    private TestTrainingRepository _testTrainingRepository = null!;
    private TestTrainingExerciseScoreRepository _testTrainingExerciseScoreRepository = null!;
    private TestExerciseScoreRepository _testExerciseScoreRepository = null!;
    private TestUnitOfWork _testUnitOfWork = null!;
    private TestWeightUnitConverter _testConverter = null!;
    private TestLogger _testLogger = null!;
    private UpdateTrainingMainRecordsHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _testMainRecordRepository = new TestMainRecordRepository();
        _testTrainingRepository = new TestTrainingRepository();
        _testTrainingExerciseScoreRepository = new TestTrainingExerciseScoreRepository();
        _testExerciseScoreRepository = new TestExerciseScoreRepository();
        _testUnitOfWork = new TestUnitOfWork();
        _testConverter = new TestWeightUnitConverter();
        _testLogger = new TestLogger();
        _handler = new UpdateTrainingMainRecordsHandler(
            _testMainRecordRepository,
            _testTrainingRepository,
            _testTrainingExerciseScoreRepository,
            _testExerciseScoreRepository,
            _testUnitOfWork,
            _testConverter,
            _testLogger);
    }

    [Test]
    public async Task ExecuteAsync_WithNewPersonalRecord_CreatesMainRecordEntry()
    {
        // Arrange
        var userId = Id<User>.New();
        var exerciseId = Id<Exercise>.New();
        var trainingId = Id<Training>.New();
        var exerciseScoreId = Id<ExerciseScore>.New();
        var trainingDate = DateTimeOffset.UtcNow.AddDays(-1);

        _testTrainingRepository.TrainingToReturn = new Training
        {
            Id = trainingId,
            CreatedAt = trainingDate
        };

        _testTrainingExerciseScoreRepository.TrainingExercisesToReturn = new List<TrainingExerciseScore>
        {
            new TrainingExerciseScore
            {
                TrainingId = trainingId,
                ExerciseScoreId = exerciseScoreId
            }
        };

        _testExerciseScoreRepository.ExerciseScoresToReturn = new List<ExerciseScore>
        {
            new ExerciseScore
            {
                Id = exerciseScoreId,
                ExerciseId = exerciseId,
                Weight = 90,
                Unit = WeightUnits.Kilograms,
                Series = 3,
                Reps = 10
            }
        };

        _testMainRecordRepository.ExistingRecords = new List<MainRecordEntity>
        {
            new MainRecordEntity
            {
                Id = Id<MainRecordEntity>.New(),
                UserId = userId,
                ExerciseId = exerciseId,
                Weight = 80,
                Unit = WeightUnits.Kilograms,
                Date = trainingDate.AddDays(-30)
            }
        };

        var command = new TrainingCompletedCommand
        {
            UserId = userId,
            TrainingId = trainingId
        };

        // Act
        await _handler.ExecuteAsync(command);

        // Assert
        _testMainRecordRepository.AddedRecords.Should().HaveCount(1);
        var newRecord = _testMainRecordRepository.AddedRecords[0];
        newRecord.UserId.Should().Be(userId);
        newRecord.ExerciseId.Should().Be(exerciseId);
        newRecord.Weight.Value.Should().Be(90);
        newRecord.Unit.Should().Be(WeightUnits.Kilograms);
        newRecord.Date.Should().Be(trainingDate);
    }

    [Test]
    public async Task ExecuteAsync_WithNoImprovement_DoesNotCreateRecord()
    {
        // Arrange
        var userId = Id<User>.New();
        var exerciseId = Id<Exercise>.New();
        var trainingId = Id<Training>.New();
        var exerciseScoreId = Id<ExerciseScore>.New();

        _testTrainingRepository.TrainingToReturn = new Training
        {
            Id = trainingId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _testTrainingExerciseScoreRepository.TrainingExercisesToReturn = new List<TrainingExerciseScore>
        {
            new TrainingExerciseScore
            {
                TrainingId = trainingId,
                ExerciseScoreId = exerciseScoreId
            }
        };

        _testExerciseScoreRepository.ExerciseScoresToReturn = new List<ExerciseScore>
        {
            new ExerciseScore
            {
                Id = exerciseScoreId,
                ExerciseId = exerciseId,
                Weight = 90,
                Unit = WeightUnits.Kilograms,
                Series = 3,
                Reps = 10
            }
        };

        _testMainRecordRepository.ExistingRecords = new List<MainRecordEntity>
        {
            new MainRecordEntity
            {
                Id = Id<MainRecordEntity>.New(),
                UserId = userId,
                ExerciseId = exerciseId,
                Weight = 100,
                Unit = WeightUnits.Kilograms,
                Date = DateTimeOffset.UtcNow.AddDays(-30)
            }
        };

        var command = new TrainingCompletedCommand
        {
            UserId = userId,
            TrainingId = trainingId
        };

        // Act
        await _handler.ExecuteAsync(command);

        // Assert
        _testMainRecordRepository.AddedRecords.Should().BeEmpty();
        _testUnitOfWork.SaveChangesCalled.Should().BeFalse();
    }

    [Test]
    public async Task ExecuteAsync_WithZeroReps_DoesNotCreateRecord()
    {
        // Arrange
        var userId = Id<User>.New();
        var exerciseId = Id<Exercise>.New();
        var trainingId = Id<Training>.New();
        var exerciseScoreId = Id<ExerciseScore>.New();

        _testTrainingRepository.TrainingToReturn = new Training
        {
            Id = trainingId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _testTrainingExerciseScoreRepository.TrainingExercisesToReturn = new List<TrainingExerciseScore>
        {
            new TrainingExerciseScore { TrainingId = trainingId, ExerciseScoreId = (Domain.ValueObjects.Id<ExerciseScore>)exerciseScoreId }
        };

        _testExerciseScoreRepository.ExerciseScoresToReturn = new List<ExerciseScore>
        {
            new ExerciseScore
            {
                Id = exerciseScoreId,
                ExerciseId = exerciseId,
                Weight = 100,
                Unit = WeightUnits.Kilograms,
                Series = 3,
                Reps = 0
            }
        };

        _testMainRecordRepository.ExistingRecords = new List<MainRecordEntity>();

        var command = new TrainingCompletedCommand
        {
            UserId = userId,
            TrainingId = (LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.Training>)trainingId
        };

        // Act
        await _handler.ExecuteAsync(command);

        // Assert
        _testMainRecordRepository.AddedRecords.Should().BeEmpty();
        _testUnitOfWork.SaveChangesCalled.Should().BeFalse();
    }

    [Test]
    public async Task ExecuteAsync_WithMixedFullAndPartialReps_OnlyCreatesRecordForFullReps()
    {
        // Arrange
        var userId = Id<User>.New();
        var exerciseId1 = Id<Exercise>.New();
        var exerciseId2 = Id<Exercise>.New();
        var trainingId = Id<Training>.New();
        var scoreId1 = Id<ExerciseScore>.New();
        var scoreId2 = Id<ExerciseScore>.New();

        _testTrainingRepository.TrainingToReturn = new Training
        {
            Id = trainingId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _testTrainingExerciseScoreRepository.TrainingExercisesToReturn = new List<TrainingExerciseScore>
        {
            new TrainingExerciseScore { TrainingId = trainingId, ExerciseScoreId = (Domain.ValueObjects.Id<ExerciseScore>)scoreId1 },
            new TrainingExerciseScore { TrainingId = trainingId, ExerciseScoreId = (Domain.ValueObjects.Id<ExerciseScore>)scoreId2 }
        };

        _testExerciseScoreRepository.ExerciseScoresToReturn = new List<ExerciseScore>
        {
            new ExerciseScore
            {
                Id = scoreId1,
                ExerciseId = exerciseId1,
                Weight = 100,
                Unit = WeightUnits.Kilograms,
                Series = 3,
                Reps = 0.5
            },
            new ExerciseScore
            {
                Id = scoreId2,
                ExerciseId = exerciseId2,
                Weight = 80,
                Unit = WeightUnits.Kilograms,
                Series = 3,
                Reps = 5
            }
        };

        _testMainRecordRepository.ExistingRecords = new List<MainRecordEntity>();

        var command = new TrainingCompletedCommand
        {
            UserId = userId,
            TrainingId = (LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.Training>)trainingId
        };

        // Act
        await _handler.ExecuteAsync(command);

        // Assert
        _testMainRecordRepository.AddedRecords.Should().HaveCount(1);
        _testMainRecordRepository.AddedRecords[0].ExerciseId.Should().Be(exerciseId2);
        _testMainRecordRepository.AddedRecords[0].Weight.Value.Should().Be(80);
        _testUnitOfWork.SaveChangesCalled.Should().BeTrue();
    }

    // Test doubles
    private sealed class TestMainRecordRepository : IMainRecordRepository
    {
        public List<MainRecordEntity> AddedRecords { get; } = new();
        public List<MainRecordEntity> ExistingRecords { get; set; } = new();

        public Task<List<MainRecordEntity>> GetBestByUserGroupedByExerciseAndUnitAsync(
            Id<User> userId,
            IReadOnlyCollection<Id<Exercise>>? exerciseIds,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ExistingRecords);

        public Task AddAsync(MainRecordEntity mainRecord, CancellationToken cancellationToken = default)
        {
            AddedRecords.Add(mainRecord);
            return Task.CompletedTask;
        }

        public Task<List<MainRecordEntity>> GetByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<MainRecordEntity>> GetByUserAndExerciseAsync(Id<User> userId, Id<Exercise> exerciseId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<MainRecordEntity>> GetByUserAndExercisesAsync(Id<User> userId, IReadOnlyCollection<Id<Exercise>> exerciseIds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MainRecordEntity?> FindByIdAsync(Id<MainRecordEntity> id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(MainRecordEntity record, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateAsync(MainRecordEntity record, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MainRecordEntity?> GetLatestByUserAndExerciseAsync(Id<User> userId, Id<Exercise> exerciseId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class TestTrainingRepository : ITrainingRepository
    {
        public Training? TrainingToReturn { get; set; }

        public Task<Training?> GetByIdAsync(Id<Training> trainingId, CancellationToken cancellationToken = default)
            => Task.FromResult(TrainingToReturn);

        public Task AddAsync(Training training, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Training?> GetLastByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<Training>> GetByUserIdAndDateAsync(Id<User> userId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<DateTimeOffset>> GetDatesByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<Training>> GetByGymIdsAsync(List<Id<Gym>> gymIds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<Training>> GetByPlanDayIdsAsync(List<Id<PlanDay>> planDayIds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class TestTrainingExerciseScoreRepository : ITrainingExerciseScoreRepository
    {
        public List<TrainingExerciseScore> TrainingExercisesToReturn { get; set; } = new();

        public Task<List<TrainingExerciseScore>> GetByTrainingIdsAsync(
            List<Id<Training>> trainingIds,
            CancellationToken cancellationToken = default)
            => Task.FromResult(TrainingExercisesToReturn);

        public Task AddRangeAsync(IEnumerable<TrainingExerciseScore> trainingExerciseScores, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class TestExerciseScoreRepository : IExerciseScoreRepository
    {
        public List<ExerciseScore> ExerciseScoresToReturn { get; set; } = new();

        public Task<List<ExerciseScore>> GetByIdsAsync(
            List<Id<ExerciseScore>> exerciseScoreIds,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ExerciseScoresToReturn);

        public Task AddRangeAsync(IEnumerable<ExerciseScore> scores, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<ExerciseScore>> GetByUserAndExerciseAsync(Id<User> userId, Id<Exercise> exerciseId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<ExerciseScore>> GetByUserAndExerciseAndGymAsync(Id<User> userId, Id<Exercise> exerciseId, Id<Gym>? gymId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<ExerciseScore>> GetByUserAndExercisesAsync(Id<User> userId, List<Id<Exercise>> exerciseIds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<ExerciseScore>> GetLatestByUserExerciseSeriesAsync(Id<User> userId, Id<Exercise> exerciseId, Id<Gym>? gymId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ExerciseScore?> GetLatestByUserExerciseSeriesAsync(Id<User> userId, Id<Exercise> exerciseId, int series, Id<Gym>? gymId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ExerciseScore?> GetBestScoreAsync(Id<User> userId, Id<Exercise> exerciseId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        public bool SaveChangesCalled { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalled = true;
            return Task.FromResult(1);
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IUnitOfWorkTransaction>(new FakeTransaction());
        }

        public void DetachEntity<TEntity>(TEntity entity) where TEntity : class { }
    }

    private sealed class FakeTransaction : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestWeightUnitConverter : IUnitConverter<WeightUnits>
    {
        public double Convert(double value, WeightUnits from, WeightUnits to)
        {
            if (from == to) return value;

            // Simple conversion: 1 kg = 2.20462 lbs
            if (from == WeightUnits.Kilograms && to == WeightUnits.Pounds)
                return value * 2.20462;
            if (from == WeightUnits.Pounds && to == WeightUnits.Kilograms)
                return value / 2.20462;

            return value;
        }
    }

    private sealed class TestLogger : ILogger<UpdateTrainingMainRecordsHandler>
    {
        public List<string> WarningMessages { get; } = new();
        public List<string> InformationMessages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (logLevel == LogLevel.Warning)
                WarningMessages.Add(message);
            else if (logLevel == LogLevel.Information)
                InformationMessages.Add(message);
        }
    }
}
