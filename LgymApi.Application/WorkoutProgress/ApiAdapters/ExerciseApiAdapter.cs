using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Exercise;
using LgymApi.Application.Features.Exercise.Models;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using ExerciseEntity = LgymApi.Domain.Entities.Exercise;

namespace LgymApi.Application.WorkoutProgress.ApiAdapters;

public interface IExerciseApiAdapter
{
    Task<Result<Unit, AppError>> AddExerciseAsync(AuthenticatedAccountContext? currentAccount, string name, BodyParts bodyPart, string? description, string? image, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddExerciseWithFormulaAsync(AuthenticatedAccountContext? currentAccount, AddExerciseWithFormulaInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddUserExerciseAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> accountId, string name, BodyParts bodyPart, string? description, string? image, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddUserExerciseWithFormulaAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> accountId, AddExerciseWithFormulaInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteExerciseAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> accountId, Id<ExerciseEntity> exerciseId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateExerciseAsync(AuthenticatedAccountContext currentAccount, UpdateExerciseInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateExerciseWithFormulaAsync(AuthenticatedAccountContext currentAccount, UpdateExerciseWithFormulaInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddGlobalTranslationAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, Id<ExerciseEntity> exerciseId, string? culture, string? name, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetAllExercisesAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> accountId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetAllUserExercisesAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> accountId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetAllGlobalExercisesAsync(AuthenticatedAccountContext? currentAccount, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetExerciseByBodyPartAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> accountId, BodyParts bodyPart, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExerciseWithTranslations, AppError>> GetExerciseAsync(AuthenticatedAccountContext? currentAccount, Id<ExerciseEntity> exerciseId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Id<ExerciseEntity>, string>> GetDisplayNamesAsync(IEnumerable<Id<ExerciseEntity>> exerciseIds, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<LastExerciseScoresResult, AppError>> GetLastExerciseScoresAsync(Id<AccountReference> routeAccountId, Id<AccountReference> currentAccountId, Id<ExerciseEntity> exerciseId, int series, Id<LgymApi.Domain.Entities.Gym>? gymId, string exerciseName, CancellationToken cancellationToken = default);
    Task<Result<List<ExerciseTrainingHistoryItem>, AppError>> GetExerciseScoresFromTrainingByExerciseAsync(Id<AccountReference> currentAccountId, Id<ExerciseEntity> exerciseId, CancellationToken cancellationToken = default);
}

internal sealed class ExerciseApiAdapter : IExerciseApiAdapter
{
    private readonly IExerciseService _exerciseService;

    public ExerciseApiAdapter(IExerciseService exerciseService)
    {
        _exerciseService = exerciseService;
    }

    public Task<Result<Unit, AppError>> AddExerciseAsync(AuthenticatedAccountContext? currentAccount, string name, BodyParts bodyPart, string? description, string? image, CancellationToken cancellationToken = default)
        => _exerciseService.AddExerciseAsync(currentAccount, name, bodyPart, description, image, cancellationToken);

    public Task<Result<Unit, AppError>> AddExerciseWithFormulaAsync(AuthenticatedAccountContext? currentAccount, AddExerciseWithFormulaInput input, CancellationToken cancellationToken = default)
        => _exerciseService.AddExerciseWithFormulaAsync(currentAccount, input, cancellationToken);

    public Task<Result<Unit, AppError>> AddUserExerciseAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> accountId, string name, BodyParts bodyPart, string? description, string? image, CancellationToken cancellationToken = default)
        => _exerciseService.AddUserExerciseAsync(currentAccount, new AddUserExerciseInput(accountId, name, bodyPart, description, image), cancellationToken);

    public Task<Result<Unit, AppError>> AddUserExerciseWithFormulaAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> accountId, AddExerciseWithFormulaInput input, CancellationToken cancellationToken = default)
        => _exerciseService.AddUserExerciseWithFormulaAsync(
            currentAccount,
            new AddUserExerciseWithFormulaInput(accountId, input.Name, input.BodyPart, input.EloFormula, input.Description, input.Image),
            cancellationToken);

    public Task<Result<Unit, AppError>> DeleteExerciseAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> accountId, Id<ExerciseEntity> exerciseId, CancellationToken cancellationToken = default)
        => _exerciseService.DeleteExerciseAsync(currentAccount, accountId, exerciseId, cancellationToken);

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

    public Task<Result<ExercisesWithTranslations, AppError>> GetAllExercisesAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> accountId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
        => _exerciseService.GetAllExercisesAsync(currentAccount, accountId, cultures, cancellationToken);

    public Task<Result<ExercisesWithTranslations, AppError>> GetAllUserExercisesAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> accountId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
        => _exerciseService.GetAllUserExercisesAsync(currentAccount, accountId, cultures, cancellationToken);

    public Task<Result<ExercisesWithTranslations, AppError>> GetAllGlobalExercisesAsync(AuthenticatedAccountContext? currentAccount, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
        => _exerciseService.GetAllGlobalExercisesAsync(currentAccount, cultures, cancellationToken);

    public Task<Result<ExercisesWithTranslations, AppError>> GetExerciseByBodyPartAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> accountId, BodyParts bodyPart, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
        => _exerciseService.GetExerciseByBodyPartAsync(currentAccount, accountId, bodyPart, cultures, cancellationToken);

    public Task<Result<ExerciseWithTranslations, AppError>> GetExerciseAsync(AuthenticatedAccountContext? currentAccount, Id<ExerciseEntity> exerciseId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
        => _exerciseService.GetExerciseAsync(currentAccount, exerciseId, cultures, cancellationToken);

    public Task<IReadOnlyDictionary<Id<ExerciseEntity>, string>> GetDisplayNamesAsync(IEnumerable<Id<ExerciseEntity>> exerciseIds, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
        => _exerciseService.GetDisplayNamesAsync(exerciseIds, cultures, cancellationToken);

    public Task<Result<LastExerciseScoresResult, AppError>> GetLastExerciseScoresAsync(Id<AccountReference> routeAccountId, Id<AccountReference> currentAccountId, Id<ExerciseEntity> exerciseId, int series, Id<LgymApi.Domain.Entities.Gym>? gymId, string exerciseName, CancellationToken cancellationToken = default)
        => _exerciseService.GetLastExerciseScoresAsync(
            new GetLastExerciseScoresInput(routeAccountId, currentAccountId, exerciseId, series, gymId, exerciseName),
            cancellationToken);

    public Task<Result<List<ExerciseTrainingHistoryItem>, AppError>> GetExerciseScoresFromTrainingByExerciseAsync(Id<AccountReference> currentAccountId, Id<ExerciseEntity> exerciseId, CancellationToken cancellationToken = default)
        => _exerciseService.GetExerciseScoresFromTrainingByExerciseAsync(currentAccountId, exerciseId, cancellationToken);
}
