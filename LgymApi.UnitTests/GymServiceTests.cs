using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.Features.Gym;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using NUnit.Framework;
using GymEntity = LgymApi.Domain.Entities.Gym;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class GymServiceTests
{
    private GymService _service = null!;
    private InMemoryGymRepository _gymRepository = null!;
    private InMemoryTrainingRepository _trainingRepository = null!;
    private FakeUnitOfWork _unitOfWork = null!;

    [SetUp]
    public void SetUp()
    {
        _gymRepository = new InMemoryGymRepository();
        _trainingRepository = new InMemoryTrainingRepository();
        _unitOfWork = new FakeUnitOfWork();
        _service = new GymService(_gymRepository, _trainingRepository, _unitOfWork);
    }

    [Test]
    public async Task AddGymAsync_ReturnsInvalidGymError_WhenCurrentUserIsNull()
    {
        var routeUserId = Id<UserEntity>.New();
        var result = await _service.AddGymAsync(null!, routeUserId, "Test Gym", null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidGymError>();
    }

    [Test]
    public async Task AddGymAsync_ReturnsInvalidGymError_WhenRouteUserIdIsEmpty()
    {
        var currentUser = new UserEntity { Id = Id<UserEntity>.New(), Name = "User", Email = new Email("test@test.com") };
        var result = await _service.AddGymAsync(currentUser, Id<UserEntity>.Empty, "Test Gym", null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidGymError>();
    }

    [Test]
    public async Task DeleteGymAsync_ReturnsInvalidGymError_WhenCurrentUserIsNull()
    {
        var gymId = Id<GymEntity>.New();
        var result = await _service.DeleteGymAsync(null!, gymId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidGymError>();
    }

    [Test]
    public async Task GetGymsAsync_ReturnsInvalidGymError_WhenRouteUserIdIsEmpty()
    {
        var currentUser = new UserEntity { Id = Id<UserEntity>.New(), Name = "User", Email = new Email("test@test.com") };
        var result = await _service.GetGymsAsync(currentUser, Id<UserEntity>.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidGymError>();
    }

    [Test]
    public async Task GetGymAsync_ReturnsInvalidGymError_WhenCurrentUserIsNull()
    {
        var gymId = Id<GymEntity>.New();
        var result = await _service.GetGymAsync(null!, gymId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidGymError>();
    }

    [Test]
    public async Task UpdateGymAsync_ReturnsInvalidGymError_WhenCurrentUserIsNull()
    {
        var gymId = Id<GymEntity>.New();
        var result = await _service.UpdateGymAsync(null!, gymId, "Updated", null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidGymError>();
    }

    // Minimal stubs - validation-path tests only, no repository behavior needed
    private sealed class InMemoryGymRepository : IGymRepository
    {
        public List<GymEntity> Gyms { get; } = new();

        public Task<GymEntity?> FindByIdAsync(Id<GymEntity> id, CancellationToken cancellationToken = default)
            => Task.FromResult(Gyms.FirstOrDefault(g => g.Id == id && !g.IsDeleted));

        public Task<List<GymEntity>> GetByUserIdAsync(Id<UserEntity> userId, CancellationToken cancellationToken = default)
            => Task.FromResult(Gyms.Where(g => g.UserId == userId && !g.IsDeleted).ToList());

        public Task AddAsync(GymEntity gym, CancellationToken cancellationToken = default)
        {
            Gyms.Add(gym);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(GymEntity gym, CancellationToken cancellationToken = default)
        {
            var index = Gyms.FindIndex(g => g.Id == gym.Id);
            if (index >= 0) Gyms[index] = gym;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(GymEntity gym, CancellationToken cancellationToken = default)
        {
            Gyms.RemoveAll(g => g.Id == gym.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryTrainingRepository : ITrainingRepository
    {
        public Task<Training?> GetByIdAsync(Id<Training> id, CancellationToken cancellationToken = default)
            => Task.FromResult<Training?>(null);

        public Task<Training?> GetLastByUserIdAsync(Id<UserEntity> userId, CancellationToken cancellationToken = default)
            => Task.FromResult<Training?>(null);

        public Task<List<Training>> GetByUserIdAndDateAsync(Id<UserEntity> userId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<Training>());

        public Task<List<DateTimeOffset>> GetDatesByUserIdAsync(Id<UserEntity> userId, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<DateTimeOffset>());

        public Task<List<Training>> GetByGymIdsAsync(List<Id<GymEntity>> gymIds, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<Training>());

        public Task<List<Training>> GetByPlanDayIdsAsync(List<Id<PlanDay>> planDayIds, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<Training>());

        public Task AddAsync(Training training, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;
            return Task.FromResult(1);
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IUnitOfWorkTransaction>(new FakeTransaction());

        public void DetachEntity<TEntity>(TEntity entity) where TEntity : class { }
    }

    private sealed class FakeTransaction : IUnitOfWorkTransaction
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
