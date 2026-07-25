using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Exercise.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Features.Exercise;

public sealed partial class ExerciseService : IExerciseService
{
    public async Task<Result<ExercisesWithTranslations, AppError>> GetAllExercisesAsync(Id<UserEntity> userId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new InvalidExerciseError(Messages.InvalidId));
        }

        var user = await _userRepository.FindByIdAsync((Id<LgymApi.Domain.Entities.User>)userId, cancellationToken);
        if (user == null)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
        }

        var exercises = await _exerciseRepository.GetAllForUserAsync(user.Id, cancellationToken);
        if (exercises.Count == 0)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
        }

        var translations = await GetTranslationsForExercisesAsync(exercises, cultures, cancellationToken);
        return Result<ExercisesWithTranslations, AppError>.Success(new ExercisesWithTranslations
        {
            Exercises = exercises,
            Translations = translations
        });
    }

    public async Task<Result<ExercisesWithTranslations, AppError>> GetAllUserExercisesAsync(Id<UserEntity> userId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new InvalidExerciseError(Messages.InvalidId));
        }

        var user = await _userRepository.FindByIdAsync((Id<LgymApi.Domain.Entities.User>)userId, cancellationToken);
        if (user == null)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
        }

        var exercises = await _exerciseRepository.GetUserExercisesAsync(user.Id, cancellationToken);
        if (exercises.Count == 0)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
        }

        var translations = await GetTranslationsForExercisesAsync(exercises, cultures, cancellationToken);
        return Result<ExercisesWithTranslations, AppError>.Success(new ExercisesWithTranslations
        {
            Exercises = exercises,
            Translations = translations
        });
    }

    public async Task<Result<ExercisesWithTranslations, AppError>> GetAllGlobalExercisesAsync(IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
    {
        var exercises = await _exerciseRepository.GetAllGlobalAsync(cancellationToken);
        if (exercises.Count == 0)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
        }

        var translations = await GetTranslationsForExercisesAsync(exercises, cultures, cancellationToken);
        return Result<ExercisesWithTranslations, AppError>.Success(new ExercisesWithTranslations
        {
            Exercises = exercises,
            Translations = translations
        });
    }

    public async Task<Result<ExercisesWithTranslations, AppError>> GetExerciseByBodyPartAsync(Id<UserEntity> userId, BodyParts bodyPart, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new InvalidExerciseError(Messages.InvalidId));
        }

        if (bodyPart == BodyParts.Unknown)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new InvalidExerciseError(Messages.FieldRequired));
        }

        var user = await _userRepository.FindByIdAsync((Id<LgymApi.Domain.Entities.User>)userId, cancellationToken);
        if (user == null)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
        }

        var exercises = await _exerciseRepository.GetByBodyPartAsync(user.Id, bodyPart, cancellationToken);
        if (exercises.Count == 0)
        {
            return Result<ExercisesWithTranslations, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
        }

        var translations = await GetTranslationsForExercisesAsync(exercises, cultures, cancellationToken);
        return Result<ExercisesWithTranslations, AppError>.Success(new ExercisesWithTranslations
        {
            Exercises = exercises,
            Translations = translations
        });
    }

    public async Task<Result<ExerciseWithTranslations, AppError>> GetExerciseAsync(Id<Domain.Entities.Exercise> exerciseId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
    {
        if (exerciseId.IsEmpty)
        {
            return Result<ExerciseWithTranslations, AppError>.Failure(new InvalidExerciseError(Messages.InvalidId));
        }

        var exercise = await _exerciseRepository.FindByIdAsync(exerciseId, cancellationToken);
        if (exercise == null)
        {
            return Result<ExerciseWithTranslations, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
        }

        var translations = await GetTranslationsForExercisesAsync(new List<Domain.Entities.Exercise> { exercise }, cultures, cancellationToken);
        return Result<ExerciseWithTranslations, AppError>.Success(new ExerciseWithTranslations
        {
            Exercise = exercise,
            Translations = translations
        });
    }
}
