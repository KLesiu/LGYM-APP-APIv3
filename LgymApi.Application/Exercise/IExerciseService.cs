using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Exercise.Models;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using ExerciseEntity = LgymApi.Domain.Entities.Exercise;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Features.Exercise;

public interface IExerciseService
{
    Task<Result<Unit, AppError>> AddExerciseAsync(string name, BodyParts bodyPart, string? description, string? image, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddExerciseWithFormulaAsync(AddExerciseWithFormulaInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddUserExerciseAsync(AddUserExerciseInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddUserExerciseWithFormulaAsync(AddUserExerciseWithFormulaInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteExerciseAsync(Id<UserEntity> userId, Id<ExerciseEntity> exerciseId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateExerciseAsync(LgymApi.Domain.Entities.User currentUser, UpdateExerciseInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateExerciseWithFormulaAsync(LgymApi.Domain.Entities.User currentUser, UpdateExerciseWithFormulaInput input, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddGlobalTranslationAsync(LgymApi.Domain.Entities.User currentUser, AddGlobalTranslationInput input, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetAllExercisesAsync(Id<UserEntity> userId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetAllUserExercisesAsync(Id<UserEntity> userId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetAllGlobalExercisesAsync(IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExercisesWithTranslations, AppError>> GetExerciseByBodyPartAsync(Id<UserEntity> userId, BodyParts bodyPart, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<ExerciseWithTranslations, AppError>> GetExerciseAsync(Id<ExerciseEntity> exerciseId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<LastExerciseScoresResult, AppError>> GetLastExerciseScoresAsync(GetLastExerciseScoresInput input, CancellationToken cancellationToken = default);
    Task<Result<List<ExerciseTrainingHistoryItem>, AppError>> GetExerciseScoresFromTrainingByExerciseAsync(Id<UserEntity> currentUserId, Id<ExerciseEntity> exerciseId, CancellationToken cancellationToken = default);
}
