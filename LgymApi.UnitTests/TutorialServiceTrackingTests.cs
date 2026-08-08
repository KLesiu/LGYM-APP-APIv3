using FluentAssertions;
using LgymApi.Application.Features.Tutorial;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Infrastructure.UnitOfWork;
using LgymApi.Platform.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TutorialServiceTrackingTests
{
    [TestCase(TutorialReadOperation.GetActive)]
    [TestCase(TutorialReadOperation.GetProgress)]
    [TestCase(TutorialReadOperation.HasActive)]
    public async Task Read_PreservesStagedSessionAndDoesNotTrackTutorialEntities(TutorialReadOperation operation)
    {
        await using var dbContext = CreateContext();
        var user = await SeedTutorialAsync(dbContext);
        var stagedSession = StageSession(dbContext, user.Id);
        var service = CreateService(dbContext, new EfUnitOfWork(dbContext));

        await InvokeReadAsync(service, user.Id, operation);

        dbContext.Entry(stagedSession).State.Should().Be(EntityState.Added);
        dbContext.ChangeTracker.Entries<UserTutorialProgress>().Should().BeEmpty();
        dbContext.ChangeTracker.Entries<UserTutorialStepProgress>().Should().BeEmpty();
    }

    [TestCase(TutorialNoOpOperation.InitializeExisting)]
    [TestCase(TutorialNoOpOperation.CompleteStepAlreadyCompleted)]
    [TestCase(TutorialNoOpOperation.CompleteStepExistingStep)]
    [TestCase(TutorialNoOpOperation.CompleteTutorialAlreadyCompleted)]
    public async Task NoOp_PreservesStagedSessionAndDoesNotTrackTutorialEntities(TutorialNoOpOperation operation)
    {
        await using var dbContext = CreateContext();
        var user = await SeedTutorialAsync(
            dbContext,
            isCompleted: operation is TutorialNoOpOperation.CompleteStepAlreadyCompleted
                or TutorialNoOpOperation.CompleteTutorialAlreadyCompleted,
            completedStep: operation == TutorialNoOpOperation.CompleteStepExistingStep
                ? TutorialStep.CreateArea
                : null);
        var stagedSession = StageSession(dbContext, user.Id);
        var service = CreateService(dbContext, new EfUnitOfWork(dbContext));

        await InvokeNoOpAsync(service, user.Id, operation);

        dbContext.Entry(stagedSession).State.Should().Be(EntityState.Added);
        dbContext.ChangeTracker.Entries<UserTutorialProgress>().Should().BeEmpty();
        dbContext.ChangeTracker.Entries<UserTutorialStepProgress>().Should().BeEmpty();
    }

    [Test]
    public async Task SaveFailure_PreservesPreExistingAndTutorialStagedEntries()
    {
        await using var dbContext = CreateContext();
        var user = await SeedUserAsync(dbContext);
        var stagedSession = StageSession(dbContext, user.Id);
        var unitOfWork = new FailingSaveUnitOfWork(dbContext);
        var service = CreateService(dbContext, unitOfWork);

        var action = async () => await service.InitializeOnboardingTutorialAsync(user.Id);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("save failed");
        dbContext.Entry(stagedSession).State.Should().Be(EntityState.Added);
        dbContext.ChangeTracker.Entries<UserTutorialProgress>()
            .Should()
            .ContainSingle(entry => entry.State == EntityState.Added);
    }

    [Test]
    public async Task Cancellation_PreservesPreExistingStagedSession()
    {
        await using var dbContext = CreateContext();
        var user = await SeedTutorialAsync(dbContext);
        var stagedSession = StageSession(dbContext, user.Id);
        var service = CreateService(dbContext, new EfUnitOfWork(dbContext));
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var action = async () => await service.GetTutorialProgressAsync(
            user.Id,
            TutorialType.OnboardingDemo,
            cancellationSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        dbContext.Entry(stagedSession).State.Should().Be(EntityState.Added);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"tutorial-actor-scope-{Id<TutorialServiceTrackingTests>.New()}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<User> SeedUserAsync(AppDbContext dbContext)
    {
        var user = new User
        {
            Id = Id<User>.New(),
            Name = "tutorial-actor-user",
            Email = "tutorial-actor@example.com",
            ProfileRank = "Junior 1"
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return user;
    }

    private static async Task<User> SeedTutorialAsync(
        AppDbContext dbContext,
        bool isCompleted = false,
        TutorialStep? completedStep = null)
    {
        var user = await SeedUserAsync(dbContext);
        var progress = new UserTutorialProgress
        {
            Id = Id<UserTutorialProgress>.New(),
            UserId = user.Id,
            TutorialType = TutorialType.OnboardingDemo,
            IsCompleted = isCompleted,
            CompletedAt = isCompleted ? DateTimeOffset.UtcNow : null
        };

        if (completedStep.HasValue)
        {
            progress.CompletedSteps.Add(new UserTutorialStepProgress
            {
                Id = Id<UserTutorialStepProgress>.New(),
                UserTutorialProgressId = progress.Id,
                TutorialStep = completedStep.Value,
                CompletedAt = DateTimeOffset.UtcNow
            });
        }

        dbContext.UserTutorialProgresses.Add(progress);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        return user;
    }

    private static UserSession StageSession(AppDbContext dbContext, Id<User> userId)
    {
        var session = new UserSession
        {
            Id = Id<UserSession>.New(),
            UserId = userId,
            Jti = Id<UserSession>.New().ToString(),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1)
        };
        dbContext.UserSessions.Add(session);
        return session;
    }

    private static ITutorialService CreateService(AppDbContext dbContext, IUnitOfWork unitOfWork)
    {
        var scopeFactoryType = typeof(EfUnitOfWork).Assembly.GetType(
            "LgymApi.Infrastructure.RowSecurity.EfActorRowSecurityScopeFactory",
            throwOnError: true)!;
        var scopeFactory = (IActorRowSecurityScopeFactory)Activator.CreateInstance(
            scopeFactoryType,
            dbContext,
            unitOfWork)!;

        return (ITutorialService)Activator.CreateInstance(
            typeof(TutorialService),
            new TutorialProgressRepository(dbContext),
            unitOfWork,
            scopeFactory)!;
    }

    private static async Task InvokeReadAsync(
        ITutorialService service,
        Id<User> userId,
        TutorialReadOperation operation)
    {
        switch (operation)
        {
            case TutorialReadOperation.GetActive:
                await service.GetActiveTutorialsAsync(userId);
                break;
            case TutorialReadOperation.GetProgress:
                await service.GetTutorialProgressAsync(userId, TutorialType.OnboardingDemo);
                break;
            case TutorialReadOperation.HasActive:
                await service.HasActiveTutorialsAsync(userId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    private static async Task InvokeNoOpAsync(
        ITutorialService service,
        Id<User> userId,
        TutorialNoOpOperation operation)
    {
        switch (operation)
        {
            case TutorialNoOpOperation.InitializeExisting:
                await service.InitializeOnboardingTutorialAsync(userId);
                break;
            case TutorialNoOpOperation.CompleteStepAlreadyCompleted:
            case TutorialNoOpOperation.CompleteStepExistingStep:
                await service.CompleteStepAsync(userId, TutorialType.OnboardingDemo, TutorialStep.CreateArea);
                break;
            case TutorialNoOpOperation.CompleteTutorialAlreadyCompleted:
                await service.CompleteTutorialAsync(userId, TutorialType.OnboardingDemo);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    public enum TutorialReadOperation
    {
        GetActive,
        GetProgress,
        HasActive
    }

    public enum TutorialNoOpOperation
    {
        InitializeExisting,
        CompleteStepAlreadyCompleted,
        CompleteStepExistingStep,
        CompleteTutorialAlreadyCompleted
    }

    private sealed class FailingSaveUnitOfWork(AppDbContext dbContext) : IUnitOfWork
    {
        private readonly EfUnitOfWork _inner = new(dbContext);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromException<int>(new InvalidOperationException("save failed"));
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            return _inner.BeginTransactionAsync(cancellationToken);
        }
    }
}
