using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Tutorial.Models;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Tutorials;
using LgymApi.Resources;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Features.Tutorial;

internal sealed class TutorialService : ITutorialService
{
    private readonly ITutorialProgressRepository _tutorialProgressRepository;
    private readonly IUnitOfWork _unitOfWork;

    public TutorialService(
        ITutorialProgressRepository tutorialProgressRepository,
        IUnitOfWork unitOfWork)
    {
        _tutorialProgressRepository = tutorialProgressRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> InitializeOnboardingTutorialAsync(Id<UserEntity> userId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidUserIdError(Messages.FieldRequired));
        }

        var existingProgress = await _tutorialProgressRepository.FindByUserIdAndTypeAsync(
            userId,
            TutorialType.OnboardingDemo,
            cancellationToken);

        if (existingProgress != null)
        {
            return Result<Unit, AppError>.Success(Unit.Value);
        }

        var progress = new UserTutorialProgress
        {
            Id = Id<UserTutorialProgress>.New(),
            UserId = userId,
            TutorialType = TutorialType.OnboardingDemo,
            IsCompleted = false,
            CompletedAt = null
        };

        await _tutorialProgressRepository.AddAsync(progress, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<List<TutorialProgressResult>, AppError>> GetActiveTutorialsAsync(Id<UserEntity> userId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return Result<List<TutorialProgressResult>, AppError>.Failure(new InvalidUserIdError(Messages.FieldRequired));
        }

        var activeTutorials = await _tutorialProgressRepository.GetActiveByUserIdAsync(userId, cancellationToken);
        return Result<List<TutorialProgressResult>, AppError>.Success(activeTutorials.Select(MapToResult).ToList());
    }

    public async Task<Result<TutorialProgressResult?, AppError>> GetTutorialProgressAsync(Id<UserEntity> userId, TutorialType tutorialType, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return Result<TutorialProgressResult?, AppError>.Failure(new InvalidUserIdError(Messages.FieldRequired));
        }

        if (tutorialType == TutorialType.Unknown)
        {
            return Result<TutorialProgressResult?, AppError>.Failure(new InvalidTutorialTypeError(Messages.FieldRequired));
        }

        var progress = await _tutorialProgressRepository.FindByUserIdAndTypeAsync(userId, tutorialType, cancellationToken);
        return Result<TutorialProgressResult?, AppError>.Success(progress == null ? null : MapToResult(progress));
    }

    public async Task<Result<Unit, AppError>> CompleteStepAsync(Id<UserEntity> userId, TutorialType tutorialType, TutorialStep step, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidUserIdError(Messages.FieldRequired));
        }

        if (tutorialType == TutorialType.Unknown || step == TutorialStep.Unknown)
        {
            return Result<Unit, AppError>.Failure(new InvalidTutorialTypeError(Messages.FieldRequired));
        }

        var progress = await _tutorialProgressRepository.FindByUserIdAndTypeAsync(userId, tutorialType, cancellationToken);
        if (progress == null)
        {
            return Result<Unit, AppError>.Failure(new TutorialProgressNotFoundError(Messages.DidntFind));
        }

        if (progress.IsCompleted)
        {
            return Result<Unit, AppError>.Success(Unit.Value);
        }

        var tutorialDefinition = TutorialDefinitions.GetByType(tutorialType);
        if (!tutorialDefinition.Steps.Contains(step))
        {
            return Result<Unit, AppError>.Failure(new InvalidTutorialStepError(Messages.FieldRequired));
        }

        var alreadyCompleted = progress.CompletedSteps.Any(s => s.TutorialStep == step);
        if (alreadyCompleted)
        {
            return Result<Unit, AppError>.Success(Unit.Value); // Idempotent - step already completed
        }

        var stepProgress = new UserTutorialStepProgress
        {
            Id = Id<UserTutorialStepProgress>.New(),
            UserTutorialProgressId = progress.Id,
            TutorialStep = step,
            CompletedAt = DateTimeOffset.UtcNow
        };

        await _tutorialProgressRepository.AddStepAsync(progress.Id, stepProgress, cancellationToken);
        await _tutorialProgressRepository.UpdateAsync(progress, cancellationToken);

        var allStepsCompleted = tutorialDefinition.Steps.All(requiredStep =>
            progress.CompletedSteps.Any(completed => completed.TutorialStep == requiredStep));

        if (allStepsCompleted)
        {
            progress.IsCompleted = true;
            progress.CompletedAt = DateTimeOffset.UtcNow;
            await _tutorialProgressRepository.UpdateAsync(progress, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<Unit, AppError>> CompleteTutorialAsync(Id<UserEntity> userId, TutorialType tutorialType, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidUserIdError(Messages.FieldRequired));
        }

        if (tutorialType == TutorialType.Unknown)
        {
            return Result<Unit, AppError>.Failure(new InvalidTutorialTypeError(Messages.FieldRequired));
        }

        var progress = await _tutorialProgressRepository.FindByUserIdAndTypeAsync(userId, tutorialType, cancellationToken);
        if (progress == null)
        {
            return Result<Unit, AppError>.Failure(new TutorialProgressNotFoundError(Messages.DidntFind));
        }

        if (progress.IsCompleted)
        {
            return Result<Unit, AppError>.Success(Unit.Value); // Already completed
        }

        progress.IsCompleted = true;
        progress.CompletedAt = DateTimeOffset.UtcNow;
        await _tutorialProgressRepository.UpdateAsync(progress, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public Task<bool> HasActiveTutorialsAsync(Id<UserEntity> userId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return Task.FromResult(false);
        }

        return _tutorialProgressRepository.HasActiveTutorialsAsync(userId, cancellationToken);
    }

    private static TutorialProgressResult MapToResult(UserTutorialProgress progress)
    {
        var tutorialDefinition = TutorialDefinitions.GetByType(progress.TutorialType);
        var completedSteps = progress.CompletedSteps.Select(s => s.TutorialStep).ToList();
        var remainingSteps = tutorialDefinition.Steps.Except(completedSteps).ToList();

        return new TutorialProgressResult
        {
            Id = progress.Id,
            TutorialType = progress.TutorialType,
            TutorialName = tutorialDefinition.Name,
            TutorialDescription = tutorialDefinition.Description,
            IsCompleted = progress.IsCompleted,
            CompletedAt = progress.CompletedAt?.UtcDateTime,
            CompletedSteps = completedSteps,
            RemainingSteps = remainingSteps,
            TotalSteps = tutorialDefinition.Steps.Count,
            CompletedStepsCount = completedSteps.Count
        };
    }
}
