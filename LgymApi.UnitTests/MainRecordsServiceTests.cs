using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.Features.MainRecords;
using LgymApi.Application.Features.MainRecords.Models;
using LgymApi.Application.Models;
using LgymApi.Application.Pagination;
using LgymApi.Application.Repositories;
using LgymApi.Application.Platform.ReferenceData.Units;
using LgymApi.Application.WorkoutProgress.ProgressData;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using NUnit.Framework;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class MainRecordsServiceTests
{
    [Test]
    public async Task AddNewRecordAsync_WithEmptyUserId_ReturnsInvalidMainRecordsError()
    {
        var service = CreateService();
        var input = new AddMainRecordInput(Id<User>.Empty, Id<Exercise>.New(), 100, WeightUnits.Kilograms, DateTime.UtcNow);

        var result = await service.AddNewRecordAsync(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidMainRecordsError>();
    }

    [Test]
    public async Task GetMainRecordsHistoryAsync_WithEmptyUserId_ReturnsInvalidMainRecordsError()
    {
        var service = CreateService();

        var result = await service.GetMainRecordsHistoryAsync(Id<User>.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidMainRecordsError>();
    }

    [Test]
    public async Task GetLastMainRecordsAsync_WithEmptyUserId_ReturnsInvalidMainRecordsError()
    {
        var service = CreateService();

        var result = await service.GetLastMainRecordsAsync(Id<User>.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidMainRecordsError>();
    }

    [Test]
    public async Task DeleteMainRecordAsync_WithEmptyCurrentUserId_ReturnsInvalidMainRecordsError()
    {
        var service = CreateService();

        var result = await service.DeleteMainRecordAsync(Id<User>.Empty, Id<MainRecord>.New());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidMainRecordsError>();
    }

    [Test]
    public async Task UpdateMainRecordAsync_WithEmptyRouteUserId_ReturnsInvalidMainRecordsError()
    {
        var service = CreateService();
        var input = new UpdateMainRecordInput(Id<User>.Empty, Id<User>.New(), Id<MainRecord>.New(), Id<Exercise>.New(), 100, WeightUnits.Kilograms, DateTime.UtcNow);

        var result = await service.UpdateMainRecordAsync(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidMainRecordsError>();
    }

    [Test]
    public async Task GetRecordOrPossibleRecordInExerciseAsync_WithEmptyUserId_ReturnsInvalidMainRecordsError()
    {
        var service = CreateService();

        var result = await service.GetRecordOrPossibleRecordInExerciseAsync(Id<User>.Empty, Id<Exercise>.New());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidMainRecordsError>();
    }

    private static MainRecordsService CreateService()
    {
        var progress = NSubstitute.Substitute.For<IWorkoutProgressReadWriteService>();
        progress.AddMainRecordAsync(NSubstitute.Arg.Any<LgymApi.Application.WorkoutProgress.ProgressData.Models.MainRecordCreateWriteModel>(), NSubstitute.Arg.Any<CancellationToken>()).Returns(LgymApi.Application.BuildingBlocks.Results.Result<Unit, AppError>.Failure(new InvalidMainRecordsError("invalid")));
        progress.GetMainRecordHistoryAsync(Id<User>.Empty, NSubstitute.Arg.Any<CancellationToken>()).Returns(LgymApi.Application.BuildingBlocks.Results.Result<List<LgymApi.Application.WorkoutProgress.ProgressData.Models.MainRecordReadModel>, AppError>.Failure(new InvalidMainRecordsError("invalid")));
        progress.GetBestMainRecordsAsync(Id<User>.Empty, NSubstitute.Arg.Any<CancellationToken>()).Returns(LgymApi.Application.BuildingBlocks.Results.Result<List<LgymApi.Application.WorkoutProgress.ProgressData.Models.MainRecordBestReadModel>, AppError>.Failure(new InvalidMainRecordsError("invalid")));
        progress.DeleteMainRecordAsync(Id<User>.Empty, NSubstitute.Arg.Any<Id<MainRecord>>(), NSubstitute.Arg.Any<CancellationToken>()).Returns(LgymApi.Application.BuildingBlocks.Results.Result<Unit, AppError>.Failure(new InvalidMainRecordsError("invalid")));
        progress.UpdateMainRecordAsync(NSubstitute.Arg.Any<LgymApi.Application.WorkoutProgress.ProgressData.Models.MainRecordUpdateWriteModel>(), NSubstitute.Arg.Any<CancellationToken>()).Returns(LgymApi.Application.BuildingBlocks.Results.Result<Unit, AppError>.Failure(new InvalidMainRecordsError("invalid")));
        progress.GetRecordOrPossibleRecordAsync(Id<User>.Empty, NSubstitute.Arg.Any<Id<Exercise>>(), NSubstitute.Arg.Any<CancellationToken>()).Returns(LgymApi.Application.BuildingBlocks.Results.Result<LgymApi.Application.WorkoutProgress.ProgressData.Models.PossibleRecordReadModel, AppError>.Failure(new InvalidMainRecordsError("invalid")));
        return new MainRecordsService(progress);
    }

    private sealed class NoOpMainRecordsServiceDependencies : IMainRecordsServiceDependencies
    {
        public IUserRepository UserRepository => new NoOpUserRepository();
        public IExerciseRepository ExerciseRepository => new NoOpExerciseRepository();
        public IMainRecordRepository MainRecordRepository => new NoOpMainRecordRepository();
        public IExerciseScoreRepository ExerciseScoreRepository => new NoOpExerciseScoreRepository();
        public IUnitConverter<WeightUnits> WeightUnitConverter => new NoOpWeightUnitConverter();
        public IUnitOfWork UnitOfWork => new NoOpUnitOfWork();
    }

    private sealed class NoOpUserRepository : IUserRepository
    {
        public Task<User?> FindByIdAsync(Id<User> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByIdIncludingDeletedAsync(Id<User> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByIdWithRolesAsync(Id<User> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByNameAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByEmailAsync(Email email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByNameOrEmailAsync(string name, string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<UserRankingEntry>> GetRankingAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Pagination<UserResult>> GetUsersPaginatedAsync(FilterInput filterInput, bool includeDeleted, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoOpExerciseRepository : IExerciseRepository
    {
        public Task<Exercise?> FindByIdAsync(Id<Exercise> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Exercise>> GetAllForUserAsync(Id<User> userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Exercise>> GetAllGlobalAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Exercise>> GetByIdsAsync(List<Id<Exercise>> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Exercise>> GetByBodyPartAsync(Id<User> userId, BodyParts bodyPart, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Exercise>> GetUserExercisesAsync(Id<User> userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Dictionary<Id<Exercise>, string>> GetTranslationsAsync(IEnumerable<Id<Exercise>> exerciseIds, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertTranslationAsync(Id<Exercise> exerciseId, string culture, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(Exercise exercise, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(Exercise exercise, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoOpMainRecordRepository : IMainRecordRepository
    {
        public Task<MainRecord?> FindByIdAsync(Id<MainRecord> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<MainRecord>> GetByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<MainRecord>> GetByUserAndExerciseAsync(Id<User> userId, Id<Exercise> exerciseId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<MainRecord>> GetByUserAndExercisesAsync(Id<User> userId, IReadOnlyCollection<Id<Exercise>> exerciseIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<MainRecord>> GetBestByUserGroupedByExerciseAndUnitAsync(Id<User> userId, IReadOnlyCollection<Id<Exercise>>? exerciseIds = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MainRecord?> GetLatestByUserAndExerciseAsync(Id<User> userId, Id<Exercise> exerciseId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(MainRecord record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(MainRecord record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(MainRecord record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoOpExerciseScoreRepository : IExerciseScoreRepository
    {
        public Task AddRangeAsync(IEnumerable<ExerciseScore> scores, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ExerciseScore>> GetByIdsAsync(List<Id<ExerciseScore>> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ExerciseScore>> GetByUserAndExerciseAsync(Id<User> userId, Id<Exercise> exerciseId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ExerciseScore>> GetByUserAndExerciseAndGymAsync(Id<User> userId, Id<Exercise> exerciseId, Id<Gym>? gymId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ExerciseScore>> GetByUserAndExercisesAsync(Id<User> userId, List<Id<Exercise>> exerciseIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ExerciseScore>> GetLatestByUserExerciseSeriesAsync(Id<User> userId, Id<Exercise> exerciseId, Id<Gym>? gymId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExerciseScore?> GetLatestByUserExerciseSeriesAsync(Id<User> userId, Id<Exercise> exerciseId, int series, Id<Gym>? gymId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExerciseScore?> GetBestScoreAsync(Id<User> userId, Id<Exercise> exerciseId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoOpWeightUnitConverter : IUnitConverter<WeightUnits>
    {
        public double Convert(double value, WeightUnits from, WeightUnits to) => throw new NotSupportedException();
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
