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
    Task<Result<Unit, AppError>> AddExerciseAsync(string name, BodyParts bodyPart, string? description, string? image, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddExerciseWithFormulaAsync(AddExerciseWithFormulaInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddUserExerciseAsync(AddUserExerciseInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddUserExerciseWithFormulaAsync(AddUserExerciseWithFormulaInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteExerciseAsync(Id<AccountReference> accountId, Id<ExerciseEntity> exerciseId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateExerciseAsync(AuthenticatedAccountContext? currentAccount, UpdateExerciseInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateExerciseWithFormulaAsync(AuthenticatedAccountContext? currentAccount, UpdateExerciseWithFormulaInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddGlobalTranslationAsync(AuthenticatedAccountContext? currentAccount, AddGlobalTranslationInput input, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetAllExercisesAsync(Id<AccountReference> accountId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetAllUserExercisesAsync(Id<AccountReference> accountId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetAllGlobalExercisesAsync(IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetExerciseByBodyPartAsync(Id<AccountReference> accountId, BodyParts bodyPart, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExerciseWithTranslations, AppError>> GetExerciseAsync(Id<ExerciseEntity> exerciseId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<LastExerciseScoresResult, AppError>> GetLastExerciseScoresAsync(GetLastExerciseScoresInput input, CancellationToken cancellationToken = default);
    Task<Result<List<ExerciseTrainingHistoryItem>, AppError>> GetExerciseScoresFromTrainingByExerciseAsync(Id<AccountReference> currentAccountId, Id<ExerciseEntity> exerciseId, CancellationToken cancellationToken = default);
}
