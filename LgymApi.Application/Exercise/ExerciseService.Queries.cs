using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Exercise.Models;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Resources;

namespace LgymApi.Application.Features.Exercise;

public sealed partial class ExerciseService : IExerciseService
{
    public async Task<Result<ExercisesWithTranslations, AppError>> GetAllExercisesAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
    {
        if (routeAccountId.IsEmpty)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new InvalidExerciseError(Messages.InvalidId));
        }

        if (currentAccount == null || currentAccount.Id.IsEmpty || currentAccount.Id != routeAccountId)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new ExerciseForbiddenError(Messages.Forbidden));
        }

        var exercises = await _exerciseRepository.GetAllForAccountAsync(currentAccount.Id, cancellationToken);
        if (exercises.Count == 0)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
        }

        var translations = await GetTranslationsForExercisesAsync(exercises, cultures, cancellationToken);
        return Result<ExercisesWithTranslations, AppError>.Success(new ExercisesWithTranslations
        {
            Exercises = exercises.Select(MapExercise).ToList(),
            Translations = translations
        });
    }

    public async Task<Result<ExercisesWithTranslations, AppError>> GetAllUserExercisesAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
    {
        if (routeAccountId.IsEmpty)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new InvalidExerciseError(Messages.InvalidId));
        }

        if (currentAccount == null || currentAccount.Id.IsEmpty || currentAccount.Id != routeAccountId)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new ExerciseForbiddenError(Messages.Forbidden));
        }

        var exercises = await _exerciseRepository.GetAccountExercisesAsync(currentAccount.Id, cancellationToken);
        if (exercises.Count == 0)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
        }

        var translations = await GetTranslationsForExercisesAsync(exercises, cultures, cancellationToken);
        return Result<ExercisesWithTranslations, AppError>.Success(new ExercisesWithTranslations
        {
            Exercises = exercises.Select(MapExercise).ToList(),
            Translations = translations
        });
    }

    public async Task<Result<ExercisesWithTranslations, AppError>> GetAllGlobalExercisesAsync(AuthenticatedAccountContext? currentAccount, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
    {
        if (currentAccount == null || currentAccount.Id.IsEmpty)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new ExerciseForbiddenError(Messages.Forbidden));
        }

        var exercises = await _exerciseRepository.GetAllGlobalAsync(cancellationToken);
        if (exercises.Count == 0)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
        }

        var translations = await GetTranslationsForExercisesAsync(exercises, cultures, cancellationToken);
        return Result<ExercisesWithTranslations, AppError>.Success(new ExercisesWithTranslations
        {
            Exercises = exercises.Select(MapExercise).ToList(),
            Translations = translations
        });
    }

    public async Task<Result<ExercisesWithTranslations, AppError>> GetExerciseByBodyPartAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, BodyParts bodyPart, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
    {
        if (routeAccountId.IsEmpty)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new InvalidExerciseError(Messages.InvalidId));
        }

        if (bodyPart == BodyParts.Unknown)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new InvalidExerciseError(Messages.FieldRequired));
        }

        if (currentAccount == null || currentAccount.Id.IsEmpty || currentAccount.Id != routeAccountId)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new ExerciseForbiddenError(Messages.Forbidden));
        }

        var exercises = await _exerciseRepository.GetByBodyPartAsync(currentAccount.Id, bodyPart, cancellationToken);
        if (exercises.Count == 0)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
        }

        var translations = await GetTranslationsForExercisesAsync(exercises, cultures, cancellationToken);
        return Result<ExercisesWithTranslations, AppError>.Success(new ExercisesWithTranslations
        {
            Exercises = exercises.Select(MapExercise).ToList(),
            Translations = translations
        });
    }

    public async Task<Result<ExerciseWithTranslations, AppError>> GetExerciseAsync(AuthenticatedAccountContext? currentAccount, Id<Domain.Entities.Exercise> exerciseId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
    {
        if (exerciseId.IsEmpty)
        {
            return Result<ExerciseWithTranslations, AppError>.Failure(new InvalidExerciseError(Messages.InvalidId));
        }

        if (currentAccount == null || currentAccount.Id.IsEmpty)
        {
            return Result<ExerciseWithTranslations, AppError>.Failure(new ExerciseForbiddenError(Messages.Forbidden));
        }

        var exercise = CanManageGlobalExercises(currentAccount)
            ? await _exerciseRepository.FindUnrestrictedByIdAsync(exerciseId, cancellationToken)
            : await _exerciseRepository.FindVisibleToAccountAsync(exerciseId, currentAccount.Id, cancellationToken);
        if (exercise == null)
        {
            return Result<ExerciseWithTranslations, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
        }

        var translations = await GetTranslationsForExercisesAsync([exercise], cultures, cancellationToken);
        return Result<ExerciseWithTranslations, AppError>.Success(new ExerciseWithTranslations
        {
            Exercise = MapExercise(exercise),
            Translations = translations
        });
    }
}
