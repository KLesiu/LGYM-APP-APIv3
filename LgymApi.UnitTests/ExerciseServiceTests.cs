using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.Features.Exercise;
using LgymApi.Application.Features.Exercise.Models;
using LgymApi.Application.Models;
using LgymApi.Application.Pagination;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ExerciseServiceTests
{
    [Test]
    public async Task AddExerciseAsync_WithBlankName_ReturnsInvalidExerciseError()
    {
        var service = new ExerciseService(
            new NoOpUserRepository(),
            new NoOpRoleRepository(),
            new NoOpExerciseRepository(),
            new NoOpExerciseScoreRepository(),
            new NoOpUnitOfWork());

        var result = await service.AddExerciseAsync("   ", BodyParts.Chest, null, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidExerciseError>();
    }

    [Test]
    public async Task AddExerciseAsync_WithUnknownBodyPart_ReturnsInvalidExerciseError()
    {
        var service = new ExerciseService(
            new NoOpUserRepository(),
            new NoOpRoleRepository(),
            new NoOpExerciseRepository(),
            new NoOpExerciseScoreRepository(),
            new NoOpUnitOfWork());

        var result = await service.AddExerciseAsync("Bench Press", BodyParts.Unknown, null, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidExerciseError>();
    }

    [Test]
    public async Task AddUserExerciseAsync_WithEmptyUserId_ReturnsInvalidExerciseError()
    {
        var service = new ExerciseService(
            new NoOpUserRepository(),
            new NoOpRoleRepository(),
            new NoOpExerciseRepository(),
            new NoOpExerciseScoreRepository(),
            new NoOpUnitOfWork());

        var input = new AddUserExerciseInput(Id<User>.Empty, "Bench Press", BodyParts.Chest, null, null);
        var result = await service.AddUserExerciseAsync(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidExerciseError>();
    }

    [Test]
    public async Task DeleteExerciseAsync_WithEmptyUserId_ReturnsInvalidExerciseError()
    {
        var exerciseId = Id<Exercise>.New();
        var service = new ExerciseService(
            new NoOpUserRepository(),
            new NoOpRoleRepository(),
            new NoOpExerciseRepository(),
            new NoOpExerciseScoreRepository(),
            new NoOpUnitOfWork());

        var result = await service.DeleteExerciseAsync(Id<User>.Empty, exerciseId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidExerciseError>();
    }

    [Test]
    public async Task DeleteExerciseAsync_WithEmptyExerciseId_ReturnsInvalidExerciseError()
    {
        var userId = Id<User>.New();
        var service = new ExerciseService(
            new NoOpUserRepository(),
            new NoOpRoleRepository(),
            new NoOpExerciseRepository(),
            new NoOpExerciseScoreRepository(),
            new NoOpUnitOfWork());

        var result = await service.DeleteExerciseAsync(userId, Id<Exercise>.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidExerciseError>();
    }

    [Test]
    public async Task UpdateExerciseAsync_WithEmptyExerciseId_ReturnsInvalidExerciseError()
    {
        var service = new ExerciseService(
            new NoOpUserRepository(),
            new NoOpRoleRepository(),
            new NoOpExerciseRepository(),
            new NoOpExerciseScoreRepository(),
            new NoOpUnitOfWork());

        var currentUser = new User { Id = Id<User>.New(), Name = "CurrentUser", Email = "current@example.com" };
        var input = new UpdateExerciseInput(Id<Exercise>.Empty, "Bench Press", BodyParts.Chest, null, null);
        var result = await service.UpdateExerciseAsync(currentUser, input);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidExerciseError>();
    }

    [Test]
    public async Task UpdateExerciseWithFormulaAsync_WhenUserOwnsExercise_UpdatesExerciseAndFormula()
    {
        var userId = Id<User>.New();
        var exercise = new Exercise
        {
            Id = Id<Exercise>.New(),
            UserId = userId,
            Name = "Pull-up",
            BodyPart = BodyParts.Back,
            EloFormula = ExerciseEloFormula.Standard,
            Description = "before",
            Image = "before.png"
        };

        var unitOfWork = new CountingUnitOfWork();
        var service = new ExerciseService(
            new NoOpUserRepository(),
            new NoOpRoleRepository(),
            new MutableExerciseRepository(exercise),
            new NoOpExerciseScoreRepository(),
            unitOfWork);

        var currentUser = new User { Id = userId, Name = "CurrentUser", Email = "current@example.com" };
        var input = new UpdateExerciseWithFormulaInput(
            exercise.Id,
            "Weighted pull-up",
            BodyParts.Shoulders,
            ExerciseEloFormula.PullupWeighted,
            "after",
            "after.png");

        var result = await service.UpdateExerciseWithFormulaAsync(currentUser, input);

        result.IsSuccess.Should().BeTrue();
        exercise.Name.Should().Be("Weighted pull-up");
        exercise.BodyPart.Should().Be(BodyParts.Shoulders);
        exercise.EloFormula.Should().Be(ExerciseEloFormula.PullupWeighted);
        exercise.Description.Should().Be("after");
        exercise.Image.Should().Be("after.png");
        unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Test]
    public async Task GetLastExerciseScoresAsync_WithSeriesAboveLimit_ClampsToThirty()
    {
        var exerciseId = Id<Exercise>.New();
        var userId = Id<User>.New();

        var scores = new List<ExerciseScore>
        {
            new() { Id = Id<ExerciseScore>.New(), ExerciseId = exerciseId, UserId = userId, Series = 1, Reps = 8, Weight = 80, Unit = WeightUnits.Kilograms },
            new() { Id = Id<ExerciseScore>.New(), ExerciseId = exerciseId, UserId = userId, Series = 2, Reps = 6, Weight = 90, Unit = WeightUnits.Kilograms }
        };

        var service = new ExerciseService(
            new NoOpUserRepository(),
            new NoOpRoleRepository(),
            new NoOpExerciseRepository(),
            new StubExerciseScoreRepository(scores),
            new NoOpUnitOfWork());

        var input = new GetLastExerciseScoresInput(userId, userId, exerciseId, 100, null, "Bench press");
        var result = await service.GetLastExerciseScoresAsync(input);
        result.IsSuccess.Should().BeTrue();
        var value = result.Value;

        value.SeriesScores.Should().HaveCount(30);
        value.SeriesScores[0].Score.Should().NotBeNull();
        value.SeriesScores[1].Score.Should().NotBeNull();
        value.SeriesScores[^1].Series.Should().Be(30);
    }

    private sealed class NoOpExerciseScoreRepository : IExerciseScoreRepository
    {
        public Task<List<ExerciseScore>> GetLatestByUserExerciseSeriesAsync(Id<User> userId, Id<Exercise> exerciseId, Id<Gym>? gymId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddRangeAsync(IEnumerable<ExerciseScore> scores, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ExerciseScore>> GetByIdsAsync(List<Id<ExerciseScore>> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ExerciseScore>> GetByUserAndExerciseAsync(Id<User> userId, Id<Exercise> exerciseId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ExerciseScore>> GetByUserAndExerciseAndGymAsync(Id<User> userId, Id<Exercise> exerciseId, Id<Gym>? gymId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ExerciseScore>> GetByUserAndExercisesAsync(Id<User> userId, List<Id<Exercise>> exerciseIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExerciseScore?> GetLatestByUserExerciseSeriesAsync(Id<User> userId, Id<Exercise> exerciseId, int series, Id<Gym>? gymId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExerciseScore?> GetBestScoreAsync(Id<User> userId, Id<Exercise> exerciseId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubExerciseScoreRepository : IExerciseScoreRepository
    {
        private readonly List<ExerciseScore> _scores;

        public StubExerciseScoreRepository(List<ExerciseScore> scores)
        {
            _scores = scores;
        }

        public Task<List<ExerciseScore>> GetLatestByUserExerciseSeriesAsync(Id<User> userId, Id<Exercise> exerciseId, Id<Gym>? gymId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_scores);
        }

        public Task AddRangeAsync(IEnumerable<ExerciseScore> scores, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ExerciseScore>> GetByIdsAsync(List<Id<ExerciseScore>> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ExerciseScore>> GetByUserAndExerciseAsync(Id<User> userId, Id<Exercise> exerciseId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ExerciseScore>> GetByUserAndExerciseAndGymAsync(Id<User> userId, Id<Exercise> exerciseId, Id<Gym>? gymId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ExerciseScore>> GetByUserAndExercisesAsync(Id<User> userId, List<Id<Exercise>> exerciseIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExerciseScore?> GetLatestByUserExerciseSeriesAsync(Id<User> userId, Id<Exercise> exerciseId, int series, Id<Gym>? gymId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExerciseScore?> GetBestScoreAsync(Id<User> userId, Id<Exercise> exerciseId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoOpUserRepository : IUserRepository
    {
        public Task<User?> FindByIdAsync(Id<LgymApi.Domain.Entities.User> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByIdIncludingDeletedAsync(Id<LgymApi.Domain.Entities.User> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByIdWithRolesAsync(Id<LgymApi.Domain.Entities.User> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByNameAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByEmailAsync(Email email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByNameOrEmailAsync(string name, string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<LgymApi.Application.Models.UserRankingEntry>> GetRankingAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Pagination<UserResult>> GetUsersPaginatedAsync(FilterInput filterInput, bool includeDeleted, CancellationToken cancellationToken = default)
            => Task.FromResult(new Pagination<UserResult>());
    }

    private sealed class NoOpExerciseRepository : IExerciseRepository
    {
        public Task<Exercise?> FindByIdAsync(Id<Exercise> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Exercise>> GetAllForUserAsync(Id<User> userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Exercise>> GetAllGlobalAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Exercise>> GetUserExercisesAsync(Id<User> userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Exercise>> GetByBodyPartAsync(Id<User> userId, BodyParts bodyPart, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Exercise>> GetByIdsAsync(List<Id<Exercise>> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Dictionary<Id<Exercise>, string>> GetTranslationsAsync(IEnumerable<Id<Exercise>> exerciseIds, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertTranslationAsync(Id<Exercise> exerciseId, string culture, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(Exercise exercise, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(Exercise exercise, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class MutableExerciseRepository : IExerciseRepository
    {
        private readonly Exercise _exercise;

        public MutableExerciseRepository(Exercise exercise)
        {
            _exercise = exercise;
        }

        public Task<Exercise?> FindByIdAsync(Id<Exercise> id, CancellationToken cancellationToken = default)
            => Task.FromResult(id == _exercise.Id ? _exercise : null);

        public Task UpdateAsync(Exercise exercise, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<List<Exercise>> GetAllForUserAsync(Id<User> userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Exercise>> GetAllGlobalAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Exercise>> GetUserExercisesAsync(Id<User> userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Exercise>> GetByBodyPartAsync(Id<User> userId, BodyParts bodyPart, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Exercise>> GetByIdsAsync(List<Id<Exercise>> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Dictionary<Id<Exercise>, string>> GetTranslationsAsync(IEnumerable<Id<Exercise>> exerciseIds, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertTranslationAsync(Id<Exercise> exerciseId, string culture, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(Exercise exercise, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoOpRoleRepository : IRoleRepository
    {
        public Task<List<Role>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Role?> FindByIdAsync(Id<Role> roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Role?> FindByNameAsync(string roleName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Role>> GetByNamesAsync(IReadOnlyCollection<string> roleNames, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByNameAsync(string roleName, Id<Role>? excludeRoleId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<string>> GetRoleNamesByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<string>> GetPermissionClaimsByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<string>> GetPermissionClaimsByRoleIdAsync(Id<Role> roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Dictionary<Id<Role>, List<string>>> GetPermissionClaimsByRoleIdsAsync(IReadOnlyCollection<Id<Role>> roleIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UserHasRoleAsync(Id<User> userId, string roleName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UserHasPermissionAsync(Id<User> userId, string permission, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task AddRoleAsync(Role role, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateRoleAsync(Role role, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteRoleAsync(Role role, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReplaceRolePermissionClaimsAsync(Id<Role> roleId, IReadOnlyCollection<string> permissionClaims, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddUserRolesAsync(Id<User> userId, IReadOnlyCollection<Id<Role>> roleIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReplaceUserRolesAsync(Id<User> userId, IReadOnlyCollection<Id<Role>> roleIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Dictionary<Id<User>, List<string>>> GetRoleNamesByUserIdsAsync(IReadOnlyCollection<Id<User>> userIds, CancellationToken cancellationToken = default)
            => Task.FromResult(new Dictionary<Id<User>, List<string>>());
        public Task<Pagination<Role>> GetRolesPaginatedAsync(FilterInput filterInput, CancellationToken cancellationToken = default)
            => Task.FromResult(new Pagination<Role>());
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void DetachEntity<TEntity>(TEntity entity) where TEntity : class => throw new NotSupportedException();
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void DetachEntity<TEntity>(TEntity entity) where TEntity : class => throw new NotSupportedException();
    }
}
