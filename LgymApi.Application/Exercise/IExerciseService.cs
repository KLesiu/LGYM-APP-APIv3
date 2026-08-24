using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Exercise.Models;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using ExerciseEntity = LgymApi.Domain.Entities.Exercise;

namespace LgymApi.Application.Features.Exercise;

public interface IExerciseService
{
    Task<Result<Unit, AppError>> AddExerciseAsync(AuthenticatedAccountContext? currentAccount, string name, BodyParts bodyPart, string? description, string? image, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddExerciseWithFormulaAsync(AuthenticatedAccountContext? currentAccount, AddExerciseWithFormulaInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddUserExerciseAsync(AuthenticatedAccountContext? currentAccount, AddUserExerciseInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddUserExerciseWithFormulaAsync(AuthenticatedAccountContext? currentAccount, AddUserExerciseWithFormulaInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteExerciseAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, Id<ExerciseEntity> exerciseId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateExerciseAsync(AuthenticatedAccountContext? currentAccount, UpdateExerciseInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateExerciseWithFormulaAsync(AuthenticatedAccountContext? currentAccount, UpdateExerciseWithFormulaInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddGlobalTranslationAsync(AuthenticatedAccountContext? currentAccount, AddGlobalTranslationInput input, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetAllExercisesAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetAllUserExercisesAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetAllGlobalExercisesAsync(AuthenticatedAccountContext? currentAccount, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetExerciseByBodyPartAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, BodyParts bodyPart, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExerciseWithTranslations, AppError>> GetExerciseAsync(AuthenticatedAccountContext? currentAccount, Id<ExerciseEntity> exerciseId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Id<ExerciseEntity>, string>> GetDisplayNamesAsync(IEnumerable<Id<ExerciseEntity>> exerciseIds, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<LastExerciseScoresResult, AppError>> GetLastExerciseScoresAsync(GetLastExerciseScoresInput input, CancellationToken cancellationToken = default);
    Task<Result<List<ExerciseTrainingHistoryItem>, AppError>> GetExerciseScoresFromTrainingByExerciseAsync(Id<AccountReference> currentAccountId, Id<ExerciseEntity> exerciseId, CancellationToken cancellationToken = default);
}
