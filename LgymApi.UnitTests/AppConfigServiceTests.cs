using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Pagination;
using LgymApi.Application.Platform.ReferenceData.AppConfig;
using LgymApi.Application.Platform.ReferenceData.AppConfig.Contracts;
using LgymApi.Application.Platform.ReferenceData.Errors;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using AppConfigEntity = LgymApi.Domain.Entities.AppConfig;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class AppConfigServiceTests
{
    [Test]
    public async Task GetLatestByPlatformAsync_DoesNotAuthorizeAndReturnsLatestConfig()
    {
        var port = new RecordingAppConfigAuthorizationPort(canManage: false);
        var config = new AppConfigEntity { Id = Id<AppConfigEntity>.New(), Platform = Platforms.Android };
        var repository = new RecordingAppConfigRepository { LatestConfig = config };
        var service = CreateService(port, repository, new RecordingUnitOfWork());

        var result = await service.GetLatestByPlatformAsync(Platforms.Android);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(config);
        port.Calls.Should().BeEmpty();
        repository.LatestLookupCount.Should().Be(1);
    }

    [Test]
    public async Task GetLatestByPlatformAsync_UnknownPlatformReturnsInvalidWithoutRepositoryOrPortCall()
    {
        var port = new RecordingAppConfigAuthorizationPort(canManage: true);
        var repository = new RecordingAppConfigRepository();
        var service = CreateService(port, repository, new RecordingUnitOfWork());

        var result = await service.GetLatestByPlatformAsync(Platforms.Unknown);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidAppConfigError>();
        port.Calls.Should().BeEmpty();
        repository.LatestLookupCount.Should().Be(0);
    }

    [TestCaseSource(nameof(ForbiddenUserIds))]
    public async Task CreateNewAppVersionAsync_EmptyMissingOrDeniedUserReturnsForbiddenWithoutSave(Id<User> userId)
    {
        var port = new RecordingAppConfigAuthorizationPort(canManage: false);
        var repository = new RecordingAppConfigRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(port, repository, unitOfWork);

        var result = await service.CreateNewAppVersionAsync(userId, ValidCreateInput());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<AppConfigForbiddenError>();
        port.Calls.Should().ContainSingle().Which.Should().Be(userId);
        repository.Added.Should().BeEmpty();
        unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [TestCase(ProtectedOperation.Create)]
    [TestCase(ProtectedOperation.List)]
    [TestCase(ProtectedOperation.Get)]
    [TestCase(ProtectedOperation.Update)]
    [TestCase(ProtectedOperation.Delete)]
    public async Task ProtectedOperation_AuthorizedUserCallsPortExactlyOnce(ProtectedOperation operation)
    {
        var userId = Id<User>.New();
        var config = new AppConfigEntity { Id = Id<AppConfigEntity>.New(), Platform = Platforms.Android };
        var port = new RecordingAppConfigAuthorizationPort(canManage: true);
        var repository = new RecordingAppConfigRepository { Config = config };
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(port, repository, unitOfWork);

        await InvokeAsync(operation, service, userId, config.Id);

        port.Calls.Should().ContainSingle().Which.Should().Be(userId);
    }

    [TestCase(ProtectedOperation.Create)]
    [TestCase(ProtectedOperation.List)]
    [TestCase(ProtectedOperation.Get)]
    [TestCase(ProtectedOperation.Update)]
    [TestCase(ProtectedOperation.Delete)]
    public async Task ProtectedOperation_DeniedUserReturnsForbiddenWithoutRepositoryMutationOrSave(ProtectedOperation operation)
    {
        var port = new RecordingAppConfigAuthorizationPort(canManage: false);
        var repository = new RecordingAppConfigRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(port, repository, unitOfWork);

        await AssertForbiddenAsync(operation, service, Id<User>.New(), Id<AppConfigEntity>.New());

        port.Calls.Should().ContainSingle();
        repository.TotalCalls.Should().Be(0);
        repository.Added.Should().BeEmpty();
        repository.DeleteCount.Should().Be(0);
        unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Test]
    public async Task CreateNewAppVersionAsync_InvalidPlatformAfterAuthorizationReturnsInvalidWithoutSave()
    {
        var port = new RecordingAppConfigAuthorizationPort(canManage: true);
        var repository = new RecordingAppConfigRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(port, repository, unitOfWork);

        var result = await service.CreateNewAppVersionAsync(Id<User>.New(), ValidCreateInput(Platforms.Unknown));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidAppConfigError>();
        port.Calls.Should().ContainSingle();
        repository.Added.Should().BeEmpty();
        unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [TestCase(ProtectedOperation.Get)]
    [TestCase(ProtectedOperation.Update)]
    [TestCase(ProtectedOperation.Delete)]
    public async Task ProtectedOperation_EmptyConfigIdAfterAuthorizationReturnsInvalidWithoutSave(ProtectedOperation operation)
    {
        var port = new RecordingAppConfigAuthorizationPort(canManage: true);
        var repository = new RecordingAppConfigRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(port, repository, unitOfWork);

        await AssertInvalidConfigIdAsync(operation, service, Id<User>.New());

        port.Calls.Should().ContainSingle();
        repository.TotalCalls.Should().Be(0);
        unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Test]
    public async Task UpdateAsync_UnknownPlatformAfterAuthorizationReturnsInvalidWithoutRepositoryOrSave()
    {
        var port = new RecordingAppConfigAuthorizationPort(canManage: true);
        var repository = new RecordingAppConfigRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(port, repository, unitOfWork);

        var result = await service.UpdateAsync(Id<User>.New(), Id<AppConfigEntity>.New(), ValidUpdateInput(Platforms.Unknown));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidAppConfigError>();
        port.Calls.Should().ContainSingle();
        repository.TotalCalls.Should().Be(0);
        unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [TestCase(ProtectedOperation.Get)]
    [TestCase(ProtectedOperation.Update)]
    [TestCase(ProtectedOperation.Delete)]
    public async Task ProtectedOperation_MissingConfigReturnsNotFoundWithoutSave(ProtectedOperation operation)
    {
        var port = new RecordingAppConfigAuthorizationPort(canManage: true);
        var repository = new RecordingAppConfigRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(port, repository, unitOfWork);

        await AssertNotFoundAsync(operation, service, Id<User>.New(), Id<AppConfigEntity>.New());

        port.Calls.Should().ContainSingle();
        unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Test]
    public async Task CreateNewAppVersionAsync_AuthorizedUserStagesConfigAndCommitsOnce()
    {
        var port = new RecordingAppConfigAuthorizationPort(canManage: true);
        var repository = new RecordingAppConfigRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(port, repository, unitOfWork);

        var result = await service.CreateNewAppVersionAsync(Id<User>.New(), ValidCreateInput());

        result.IsSuccess.Should().BeTrue();
        repository.Added.Should().ContainSingle();
        unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Test]
    public async Task UpdateAndDelete_AuthorizedUserCommitOnceAfterTrackedConfigMutation()
    {
        var userId = Id<User>.New();
        var config = new AppConfigEntity { Id = Id<AppConfigEntity>.New(), Platform = Platforms.Android };
        var port = new RecordingAppConfigAuthorizationPort(canManage: true);
        var repository = new RecordingAppConfigRepository { Config = config };
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(port, repository, unitOfWork);

        var update = await service.UpdateAsync(userId, config.Id, ValidUpdateInput());
        var delete = await service.DeleteAsync(userId, config.Id);

        update.IsSuccess.Should().BeTrue();
        delete.IsSuccess.Should().BeTrue();
        repository.DeleteCount.Should().Be(1);
        unitOfWork.SaveChangesCount.Should().Be(2);
        port.Calls.Should().HaveCount(2);
    }

    [Test]
    public async Task AuthorizationPortFailure_PropagatesWithoutRepositoryOrSave()
    {
        var expected = new InvalidOperationException("Authorization port failed.");
        var port = new RecordingAppConfigAuthorizationPort(canManage: false, exception: expected);
        var repository = new RecordingAppConfigRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var service = CreateService(port, repository, unitOfWork);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.CreateNewAppVersionAsync(Id<User>.New(), ValidCreateInput()));

        exception.Should().BeSameAs(expected);
        repository.TotalCalls.Should().Be(0);
        unitOfWork.SaveChangesCount.Should().Be(0);
    }

    private static IEnumerable<TestCaseData> ForbiddenUserIds()
    {
        yield return new TestCaseData(Id<User>.Empty).SetName("CreateNewAppVersionAsync_EmptyUserId_ReturnsForbidden");
        yield return new TestCaseData(Id<User>.New()).SetName("CreateNewAppVersionAsync_MissingUser_ReturnsForbidden");
        yield return new TestCaseData(Id<User>.New()).SetName("CreateNewAppVersionAsync_DeniedUser_ReturnsForbidden");
    }

    private static AppConfigService CreateService(
        IAppConfigAuthorizationPort authorizationPort,
        IAppConfigRepository repository,
        IUnitOfWork unitOfWork) => new(authorizationPort, repository, unitOfWork);

    private static CreateAppVersionInput ValidCreateInput(Platforms platform = Platforms.Android) =>
        new(platform, "1.0", "1.1", false, "https://example.test", "notes");

    private static UpdateAppConfigInput ValidUpdateInput(Platforms platform = Platforms.Android) =>
        new(platform, "1.0", "1.1", false, "https://example.test", "notes");

    private static async Task InvokeAsync(ProtectedOperation operation, AppConfigService service, Id<User> userId, Id<AppConfigEntity> configId)
    {
        switch (operation)
        {
            case ProtectedOperation.Create:
                (await service.CreateNewAppVersionAsync(userId, ValidCreateInput())).IsSuccess.Should().BeTrue();
                break;
            case ProtectedOperation.List:
                (await service.GetPaginatedAsync(userId, new FilterInput())).IsSuccess.Should().BeTrue();
                break;
            case ProtectedOperation.Get:
                (await service.GetByIdAsync(userId, configId)).IsSuccess.Should().BeTrue();
                break;
            case ProtectedOperation.Update:
                (await service.UpdateAsync(userId, configId, ValidUpdateInput())).IsSuccess.Should().BeTrue();
                break;
            case ProtectedOperation.Delete:
                (await service.DeleteAsync(userId, configId)).IsSuccess.Should().BeTrue();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    private static async Task AssertForbiddenAsync(ProtectedOperation operation, AppConfigService service, Id<User> userId, Id<AppConfigEntity> configId)
    {
        switch (operation)
        {
            case ProtectedOperation.Create:
                (await service.CreateNewAppVersionAsync(userId, ValidCreateInput())).Error.Should().BeOfType<AppConfigForbiddenError>();
                break;
            case ProtectedOperation.List:
                (await service.GetPaginatedAsync(userId, new FilterInput())).Error.Should().BeOfType<AppConfigForbiddenError>();
                break;
            case ProtectedOperation.Get:
                (await service.GetByIdAsync(userId, configId)).Error.Should().BeOfType<AppConfigForbiddenError>();
                break;
            case ProtectedOperation.Update:
                (await service.UpdateAsync(userId, configId, ValidUpdateInput())).Error.Should().BeOfType<AppConfigForbiddenError>();
                break;
            case ProtectedOperation.Delete:
                (await service.DeleteAsync(userId, configId)).Error.Should().BeOfType<AppConfigForbiddenError>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    private static async Task AssertInvalidConfigIdAsync(ProtectedOperation operation, AppConfigService service, Id<User> userId)
    {
        switch (operation)
        {
            case ProtectedOperation.Get:
                (await service.GetByIdAsync(userId, Id<AppConfigEntity>.Empty)).Error.Should().BeOfType<InvalidAppConfigError>();
                break;
            case ProtectedOperation.Update:
                (await service.UpdateAsync(userId, Id<AppConfigEntity>.Empty, ValidUpdateInput())).Error.Should().BeOfType<InvalidAppConfigError>();
                break;
            case ProtectedOperation.Delete:
                (await service.DeleteAsync(userId, Id<AppConfigEntity>.Empty)).Error.Should().BeOfType<InvalidAppConfigError>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    private static async Task AssertNotFoundAsync(ProtectedOperation operation, AppConfigService service, Id<User> userId, Id<AppConfigEntity> configId)
    {
        switch (operation)
        {
            case ProtectedOperation.Get:
                (await service.GetByIdAsync(userId, configId)).Error.Should().BeOfType<AppConfigNotFoundError>();
                break;
            case ProtectedOperation.Update:
                (await service.UpdateAsync(userId, configId, ValidUpdateInput())).Error.Should().BeOfType<AppConfigNotFoundError>();
                break;
            case ProtectedOperation.Delete:
                (await service.DeleteAsync(userId, configId)).Error.Should().BeOfType<AppConfigNotFoundError>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    public enum ProtectedOperation
    {
        Create,
        List,
        Get,
        Update,
        Delete
    }

    private sealed class RecordingAppConfigAuthorizationPort(bool canManage, Exception? exception = null) : IAppConfigAuthorizationPort
    {
        public List<Id<User>> Calls { get; } = [];

        public Task<bool> CanManageAppConfigAsync(Id<User> userId, CancellationToken cancellationToken = default)
        {
            Calls.Add(userId);
            return exception is null ? Task.FromResult(canManage) : Task.FromException<bool>(exception);
        }
    }

    private sealed class RecordingAppConfigRepository : IAppConfigRepository
    {
        public AppConfigEntity? LatestConfig { get; init; }
        public AppConfigEntity? Config { get; init; }
        public List<AppConfigEntity> Added { get; } = [];
        public int LatestLookupCount { get; private set; }
        public int FindCount { get; private set; }
        public int FindTrackedCount { get; private set; }
        public int PaginationCount { get; private set; }
        public int DeleteCount { get; private set; }
        public int TotalCalls => LatestLookupCount + FindCount + FindTrackedCount + PaginationCount + DeleteCount + Added.Count;

        public Task<AppConfigEntity?> GetLatestByPlatformAsync(Platforms platform, CancellationToken cancellationToken = default)
        {
            LatestLookupCount++;
            return Task.FromResult(LatestConfig);
        }

        public Task AddAsync(AppConfigEntity config, CancellationToken cancellationToken = default)
        {
            Added.Add(config);
            return Task.CompletedTask;
        }

        public Task<AppConfigEntity?> FindByIdAsync(Id<AppConfigEntity> id, CancellationToken cancellationToken = default)
        {
            FindCount++;
            return Task.FromResult(Config?.Id == id ? Config : null);
        }

        public Task<AppConfigEntity?> FindByIdTrackedAsync(Id<AppConfigEntity> id, CancellationToken cancellationToken = default)
        {
            FindTrackedCount++;
            return Task.FromResult(Config?.Id == id ? Config : null);
        }

        public Task<Pagination<AppConfigEntity>> GetPaginatedAsync(FilterInput filterInput, CancellationToken cancellationToken = default)
        {
            PaginationCount++;
            return Task.FromResult(new Pagination<AppConfigEntity>());
        }

        public void Update(AppConfigEntity config) { }

        public void Delete(AppConfigEntity config)
        {
            DeleteCount++;
        }
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
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
