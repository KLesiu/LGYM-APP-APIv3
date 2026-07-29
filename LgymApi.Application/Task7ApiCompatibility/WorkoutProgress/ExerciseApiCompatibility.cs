using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Exercise;
using LgymApi.Application.Features.Exercise.Models;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using ExerciseEntity = LgymApi.Domain.Entities.Exercise;

namespace LgymApi.Application.Task7ApiCompatibility.WorkoutProgress;

public interface IExerciseApiCompatibilityService
{
    Task<Result<Unit, AppError>> AddExerciseAsync(string name, BodyParts bodyPart, string? description, string? image, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddExerciseWithFormulaAsync(AddExerciseWithFormulaInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddUserExerciseAsync(Id<AccountReference> accountId, string name, BodyParts bodyPart, string? description, string? image, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddUserExerciseWithFormulaAsync(Id<AccountReference> accountId, AddExerciseWithFormulaInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteExerciseAsync(Id<AccountReference> accountId, Id<ExerciseEntity> exerciseId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateExerciseAsync(AuthenticatedAccountContext currentAccount, UpdateExerciseInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateExerciseWithFormulaAsync(AuthenticatedAccountContext currentAccount, UpdateExerciseWithFormulaInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddGlobalTranslationAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, Id<ExerciseEntity> exerciseId, string? culture, string? name, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetAllExercisesAsync(Id<AccountReference> accountId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetAllUserExercisesAsync(Id<AccountReference> accountId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetAllGlobalExercisesAsync(IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetExerciseByBodyPartAsync(Id<AccountReference> accountId, BodyParts bodyPart, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExerciseWithTranslations, AppError>> GetExerciseAsync(Id<ExerciseEntity> exerciseId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<LastExerciseScoresResult, AppError>> GetLastExerciseScoresAsync(Id<AccountReference> routeAccountId, Id<AccountReference> currentAccountId, Id<ExerciseEntity> exerciseId, int series, Id<LgymApi.Domain.Entities.Gym>? gymId, string exerciseName, CancellationToken cancellationToken = default);
    Task<Result<List<ExerciseTrainingHistoryItem>, AppError>> GetExerciseScoresFromTrainingByExerciseAsync(Id<AccountReference> currentAccountId, Id<ExerciseEntity> exerciseId, CancellationToken cancellationToken = default);
}

internal sealed class ExerciseApiCompatibilityService : IExerciseApiCompatibilityService
{
    private readonly IExerciseService _exerciseService;

    public ExerciseApiCompatibilityService(IExerciseService exerciseService)
    {
        _exerciseService = exerciseService;
    }

    public Task<Result<Unit, AppError>> AddExerciseAsync(string name, BodyParts bodyPart, string? description, string? image, CancellationToken cancellationToken = default)
        => _exerciseService.AddExerciseAsync(name, bodyPart, description, image, cancellationToken);

    public Task<Result<Unit, AppError>> AddExerciseWithFormulaAsync(AddExerciseWithFormulaInput input, CancellationToken cancellationToken = default)
        => _exerciseService.AddExerciseWithFormulaAsync(input, cancellationToken);

    public Task<Result<Unit, AppError>> AddUserExerciseAsync(Id<AccountReference> accountId, string name, BodyParts bodyPart, string? description, string? image, CancellationToken cancellationToken = default)
        => _exerciseService.AddUserExerciseAsync(new AddUserExerciseInput(accountId, name, bodyPart, description, image), cancellationToken);

    public Task<Result<Unit, AppError>> AddUserExerciseWithFormulaAsync(Id<AccountReference> accountId, AddExerciseWithFormulaInput input, CancellationToken cancellationToken = default)
        => _exerciseService.AddUserExerciseWithFormulaAsync(
            new AddUserExerciseWithFormulaInput(accountId, input.Name, input.BodyPart, input.EloFormula, input.Description, input.Image),
            cancellationToken);

    public Task<Result<Unit, AppError>> DeleteExerciseAsync(Id<AccountReference> accountId, Id<ExerciseEntity> exerciseId, CancellationToken cancellationToken = default)
        => _exerciseService.DeleteExerciseAsync(accountId, exerciseId, cancellationToken);

    public async Task<Result<Unit, AppError>> UpdateExerciseAsync(AuthenticatedAccountContext currentAccount, UpdateExerciseInput input, CancellationToken cancellationToken = default)
    {
        return await _exerciseService.UpdateExerciseAsync(currentAccount, input, cancellationToken);
    }

    public async Task<Result<Unit, AppError>> UpdateExerciseWithFormulaAsync(AuthenticatedAccountContext currentAccount, UpdateExerciseWithFormulaInput input, CancellationToken cancellationToken = default)
    {
        return await _exerciseService.UpdateExerciseWithFormulaAsync(currentAccount, input, cancellationToken);
    }

    public async Task<Result<Unit, AppError>> AddGlobalTranslationAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, Id<ExerciseEntity> exerciseId, string? culture, string? name, CancellationToken cancellationToken = default)
    {
        if (currentAccount is null)
        {
            return await _exerciseService.AddGlobalTranslationAsync(null, new AddGlobalTranslationInput(routeAccountId, exerciseId, culture, name), cancellationToken);
        }

        return await _exerciseService.AddGlobalTranslationAsync(currentAccount, new AddGlobalTranslationInput(routeAccountId, exerciseId, culture, name), cancellationToken);
    }

    public Task<Result<ExercisesWithTranslations, AppError>> GetAllExercisesAsync(Id<AccountReference> accountId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
        => _exerciseService.GetAllExercisesAsync(accountId, cultures, cancellationToken);

    public Task<Result<ExercisesWithTranslations, AppError>> GetAllUserExercisesAsync(Id<AccountReference> accountId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
        => _exerciseService.GetAllUserExercisesAsync(accountId, cultures, cancellationToken);

    public Task<Result<ExercisesWithTranslations, AppError>> GetAllGlobalExercisesAsync(IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
        => _exerciseService.GetAllGlobalExercisesAsync(cultures, cancellationToken);

    public Task<Result<ExercisesWithTranslations, AppError>> GetExerciseByBodyPartAsync(Id<AccountReference> accountId, BodyParts bodyPart, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
        => _exerciseService.GetExerciseByBodyPartAsync(accountId, bodyPart, cultures, cancellationToken);

    public Task<Result<ExerciseWithTranslations, AppError>> GetExerciseAsync(Id<ExerciseEntity> exerciseId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
        => _exerciseService.GetExerciseAsync(exerciseId, cultures, cancellationToken);

    public Task<Result<LastExerciseScoresResult, AppError>> GetLastExerciseScoresAsync(Id<AccountReference> routeAccountId, Id<AccountReference> currentAccountId, Id<ExerciseEntity> exerciseId, int series, Id<LgymApi.Domain.Entities.Gym>? gymId, string exerciseName, CancellationToken cancellationToken = default)
        => _exerciseService.GetLastExerciseScoresAsync(
            new GetLastExerciseScoresInput(routeAccountId, currentAccountId, exerciseId, series, gymId, exerciseName),
            cancellationToken);

    public Task<Result<List<ExerciseTrainingHistoryItem>, AppError>> GetExerciseScoresFromTrainingByExerciseAsync(Id<AccountReference> currentAccountId, Id<ExerciseEntity> exerciseId, CancellationToken cancellationToken = default)
        => _exerciseService.GetExerciseScoresFromTrainingByExerciseAsync(currentAccountId, exerciseId, cancellationToken);
}
