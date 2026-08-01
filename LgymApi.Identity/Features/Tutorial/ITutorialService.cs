using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Tutorial.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Features.Tutorial;

public interface ITutorialService
{
    Task<Result<Unit, AppError>> InitializeOnboardingTutorialAsync(Id<UserEntity> userId, CancellationToken cancellationToken = default);
    Task<Result<List<TutorialProgressResult>, AppError>> GetActiveTutorialsAsync(Id<UserEntity> userId, CancellationToken cancellationToken = default);
    Task<Result<TutorialProgressResult?, AppError>> GetTutorialProgressAsync(Id<UserEntity> userId, TutorialType tutorialType, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> CompleteStepAsync(Id<UserEntity> userId, TutorialType tutorialType, TutorialStep step, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> CompleteTutorialAsync(Id<UserEntity> userId, TutorialType tutorialType, CancellationToken cancellationToken = default);
    Task<bool> HasActiveTutorialsAsync(Id<UserEntity> userId, CancellationToken cancellationToken = default);
}
