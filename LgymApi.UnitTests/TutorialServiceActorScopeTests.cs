using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Features.Tutorial;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Platform.Contracts;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TutorialServiceActorScopeTests
{
    private readonly List<string> _calls = [];
    private IActorRowSecurityScopeFactory _actorScopeFactory = null!;
    private RecordingTutorialProgressRepository _repository = null!;
    private IUnitOfWorkTransaction _transaction = null!;
    private IUnitOfWork _unitOfWork = null!;
    private ITutorialService _service = null!;
    private Id<User> _userId;

    [SetUp]
    public void SetUp()
    {
        _calls.Clear();
        _userId = Id<User>.New();
        _repository = new RecordingTutorialProgressRepository(_calls)
        {
            Progress = CreateProgress(isCompleted: true),
            ActiveTutorials = [CreateProgress()],
            HasActiveTutorials = true
        };
        _transaction = Substitute.For<IUnitOfWorkTransaction>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _actorScopeFactory = Substitute.For<IActorRowSecurityScopeFactory>();
        _actorScopeFactory
            .BeginAsync(Arg.Any<Id<ActorReference>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                _calls.Add("scope");
                return Task.FromResult(_transaction);
            });
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        _service = CreateService();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _transaction.DisposeAsync();
    }

    [TestCase(TutorialOperation.Initialize, "find")]
    [TestCase(TutorialOperation.GetActive, "get-active")]
    [TestCase(TutorialOperation.GetProgress, "find")]
    [TestCase(TutorialOperation.CompleteStep, "find")]
    [TestCase(TutorialOperation.CompleteTutorial, "find")]
    [TestCase(TutorialOperation.HasActive, "has-active")]
    public async Task PublicOperation_BeginsReboundActorScopeBeforeFirstRepositoryAccess(
        TutorialOperation operation,
        string expectedRepositoryCall)
    {
        await InvokeAsync(operation);

        _calls.Take(2).Should().Equal("scope", expectedRepositoryCall);
        await _actorScopeFactory.Received(1).BeginAsync(_userId.Rebind<ActorReference>(), CancellationToken.None);
    }

    [TestCase(TutorialOperation.Initialize)]
    [TestCase(TutorialOperation.GetActive)]
    [TestCase(TutorialOperation.GetProgress)]
    [TestCase(TutorialOperation.CompleteStep)]
    [TestCase(TutorialOperation.CompleteTutorial)]
    [TestCase(TutorialOperation.HasActive)]
    public async Task ReadOrNoOp_DisposesWithoutSaveCommitOrRollback(TutorialOperation operation)
    {
        await InvokeAsync(operation);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await _transaction.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        await _transaction.Received(1).DisposeAsync();
    }

    [TestCase(TutorialOperation.Initialize)]
    [TestCase(TutorialOperation.CompleteStep)]
    [TestCase(TutorialOperation.CompleteTutorial)]
    public async Task ActualMutation_SavesThenCommitsOnce(TutorialOperation operation)
    {
        _repository.Progress = operation == TutorialOperation.Initialize ? null : CreateProgress();
        _unitOfWork.SaveChangesAsync(CancellationToken.None).Returns(_ =>
        {
            _calls.Add("save");
            return Task.FromResult(1);
        });
        _transaction.CommitAsync(CancellationToken.None).Returns(_ =>
        {
            _calls.Add("commit");
            return Task.CompletedTask;
        });

        await InvokeAsync(operation);

        _calls.Should().ContainInOrder("save", "commit");
        await _unitOfWork.Received(1).SaveChangesAsync(CancellationToken.None);
        await _transaction.Received(1).CommitAsync(CancellationToken.None);
        await _transaction.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        await _transaction.Received(1).DisposeAsync();
    }

    [Test]
    public async Task RepositoryFailure_DisposesWithoutSaveCommitOrRollback()
    {
        _repository.FindException = new InvalidOperationException("tutorial read failed");

        var action = async () => await _service.GetTutorialProgressAsync(_userId, TutorialType.OnboardingDemo);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("tutorial read failed");
        await AssertFailureScopeAsync();
    }

    [Test]
    public async Task RepositoryCancellation_DisposesWithoutSaveCommitOrRollback()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        _repository.FindException = new OperationCanceledException(cancellationSource.Token);

        var action = async () => await _service.GetTutorialProgressAsync(
            _userId,
            TutorialType.OnboardingDemo,
            cancellationSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        await AssertFailureScopeAsync();
    }

    [Test]
    public async Task SaveFailure_DisposesWithoutCommitOrRollback()
    {
        _repository.Progress = null;
        _unitOfWork.SaveChangesAsync(CancellationToken.None)
            .Returns(Task.FromException<int>(new InvalidOperationException("save failed")));

        var action = async () => await _service.InitializeOnboardingTutorialAsync(_userId);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("save failed");
        await _unitOfWork.Received(1).SaveChangesAsync(CancellationToken.None);
        await _transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await _transaction.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        await _transaction.Received(1).DisposeAsync();
    }

    private ITutorialService CreateService()
    {
        return (ITutorialService)Activator.CreateInstance(
            typeof(TutorialService),
            _repository,
            _unitOfWork,
            _actorScopeFactory)!;
    }

    private Task InvokeAsync(TutorialOperation operation)
    {
        return operation switch
        {
            TutorialOperation.Initialize => _service.InitializeOnboardingTutorialAsync(_userId),
            TutorialOperation.GetActive => _service.GetActiveTutorialsAsync(_userId),
            TutorialOperation.GetProgress => _service.GetTutorialProgressAsync(_userId, TutorialType.OnboardingDemo),
            TutorialOperation.CompleteStep => _service.CompleteStepAsync(_userId, TutorialType.OnboardingDemo, TutorialStep.CreateArea),
            TutorialOperation.CompleteTutorial => _service.CompleteTutorialAsync(_userId, TutorialType.OnboardingDemo),
            TutorialOperation.HasActive => _service.HasActiveTutorialsAsync(_userId),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };
    }

    private async Task AssertFailureScopeAsync()
    {
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await _transaction.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        await _transaction.Received(1).DisposeAsync();
    }

    private UserTutorialProgress CreateProgress(bool isCompleted = false)
    {
        return new UserTutorialProgress
        {
            Id = Id<UserTutorialProgress>.New(),
            UserId = _userId,
            TutorialType = TutorialType.OnboardingDemo,
            IsCompleted = isCompleted,
            CompletedAt = isCompleted ? DateTimeOffset.UtcNow : null
        };
    }

    public enum TutorialOperation
    {
        Initialize,
        GetActive,
        GetProgress,
        CompleteStep,
        CompleteTutorial,
        HasActive
    }

    private sealed class RecordingTutorialProgressRepository(List<string> calls) : ITutorialProgressRepository
    {
        public UserTutorialProgress? Progress { get; set; }
        public List<UserTutorialProgress> ActiveTutorials { get; set; } = [];
        public bool HasActiveTutorials { get; set; }
        public Exception? FindException { get; set; }

        public Task<UserTutorialProgress?> FindByUserIdAndTypeAsync(
            Id<User> userId,
            TutorialType tutorialType,
            CancellationToken cancellationToken = default)
        {
            calls.Add("find");
            return FindException is null
                ? Task.FromResult(Progress)
                : Task.FromException<UserTutorialProgress?>(FindException);
        }

        public Task<UserTutorialProgress?> FindTrackedByUserIdAndTypeAsync(
            Id<User> userId,
            TutorialType tutorialType,
            CancellationToken cancellationToken = default)
        {
            calls.Add("find-tracked");
            return Task.FromResult(Progress);
        }

        public Task<List<UserTutorialProgress>> GetActiveByUserIdAsync(
            Id<User> userId,
            CancellationToken cancellationToken = default)
        {
            calls.Add("get-active");
            return Task.FromResult(ActiveTutorials);
        }

        public Task<bool> HasActiveTutorialsAsync(Id<User> userId, CancellationToken cancellationToken = default)
        {
            calls.Add("has-active");
            return Task.FromResult(HasActiveTutorials);
        }

        public Task AddAsync(UserTutorialProgress progress, CancellationToken cancellationToken = default)
        {
            calls.Add("add");
            return Task.CompletedTask;
        }

        public Task AddStepAsync(
            Id<UserTutorialProgress> progressId,
            UserTutorialStepProgress step,
            CancellationToken cancellationToken = default)
        {
            calls.Add("add-step");
            return Task.CompletedTask;
        }

        public Task UpdateAsync(UserTutorialProgress progress, CancellationToken cancellationToken = default)
        {
            calls.Add("update");
            return Task.CompletedTask;
        }
    }
}
