using System.Globalization;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Exercise.Models;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Resources;

namespace LgymApi.Application.Features.Exercise;

public sealed partial class ExerciseService : IExerciseService
{
    public Task<Result<Unit, AppError>> AddExerciseAsync(string name, BodyParts bodyPart, string? description, string? image, CancellationToken cancellationToken = default)
        => CreateExerciseAsync(name, bodyPart, ExerciseEloFormula.Standard, description, image, null, cancellationToken);

    public async Task<Result<Unit, AppError>> AddExerciseWithFormulaAsync(AddExerciseWithFormulaInput input, CancellationToken cancellationToken = default)
    {
        var (name, bodyPart, eloFormula, description, image) = input;
        return await CreateExerciseAsync(name, bodyPart, eloFormula ?? ExerciseEloFormula.Standard, description, image, null, cancellationToken);
    }

    public async Task<Result<Unit, AppError>> AddUserExerciseAsync(AddUserExerciseInput input, CancellationToken cancellationToken = default)
    {
        var (userId, name, bodyPart, description, image) = input;
        return await CreateExerciseAsync(name, bodyPart, ExerciseEloFormula.Standard, description, image, userId, cancellationToken);
    }

    public async Task<Result<Unit, AppError>> AddUserExerciseWithFormulaAsync(AddUserExerciseWithFormulaInput input, CancellationToken cancellationToken = default)
    {
        var (userId, name, bodyPart, eloFormula, description, image) = input;
        return await CreateExerciseAsync(name, bodyPart, eloFormula ?? ExerciseEloFormula.Standard, description, image, userId, cancellationToken);
    }

    private async Task<Result<Unit, AppError>> CreateExerciseAsync(
        string name,
        BodyParts bodyPart,
        ExerciseEloFormula eloFormula,
        string? description,
        string? image,
        Id<AccountReference>? userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || bodyPart == BodyParts.Unknown)
        {
            return Result<Unit, AppError>.Failure(new InvalidExerciseError(Messages.FieldRequired));
        }

        if (userId.HasValue && userId.Value.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidExerciseError(Messages.InvalidId));
        }

        if (userId.HasValue)
        {
            if (await _accountAccess.GetByIdAsync(userId.Value, cancellationToken) is null)
            {
                return Result<Unit, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
            }
        }

        var exercise = new WorkoutExerciseWriteModel(Id<Domain.Entities.Exercise>.New(), userId, name, bodyPart, eloFormula, description, image, false);

        await _exerciseRepository.AddAsync(exercise, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<Unit, AppError>> DeleteExerciseAsync(Id<AccountReference> userId, Id<Domain.Entities.Exercise> exerciseId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty || exerciseId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidExerciseError(Messages.InvalidId));
        }

        var account = await _accountAccess.GetByIdAsync(userId, cancellationToken);
        if (account == null)
        {
            return Result<Unit, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
        }

        var exercise = await _exerciseRepository.FindByIdAsync(exerciseId, cancellationToken);
        if (exercise == null)
        {
            return Result<Unit, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
        }

        if (!account.PermissionClaims.Contains(AuthConstants.Permissions.ManageGlobalExercises, StringComparer.Ordinal))
        {
            if (!exercise.OwnerId.HasValue)
            {
                return Result<Unit, AppError>.Failure(new InvalidExerciseError(Messages.Forbidden));
            }

            if (exercise.OwnerId.Value != userId)
            {
                return Result<Unit, AppError>.Failure(new ExerciseForbiddenError(Messages.Forbidden));
            }

        }

        await _exerciseRepository.UpdateAsync(new WorkoutExerciseWriteModel(exercise.Id, exercise.OwnerId, exercise.Name, exercise.BodyPart, exercise.EloFormula, exercise.Description, exercise.Image, true), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<Unit, AppError>> UpdateExerciseAsync(AuthenticatedAccountContext? currentUser, UpdateExerciseInput input, CancellationToken cancellationToken = default)
    {
        var (exerciseId, name, bodyPart, description, image) = input;
        return await UpdateExerciseCoreAsync(new UpdateExerciseRequest(currentUser, exerciseId, name, bodyPart, null, description, image), cancellationToken);
    }

    public async Task<Result<Unit, AppError>> UpdateExerciseWithFormulaAsync(AuthenticatedAccountContext? currentUser, UpdateExerciseWithFormulaInput input, CancellationToken cancellationToken = default)
    {
        var (exerciseId, name, bodyPart, eloFormula, description, image) = input;
        return await UpdateExerciseCoreAsync(new UpdateExerciseRequest(currentUser, exerciseId, name, bodyPart, eloFormula, description, image), cancellationToken);
    }

    private async Task<Result<Unit, AppError>> UpdateExerciseCoreAsync(
        UpdateExerciseRequest request,
        CancellationToken cancellationToken)
    {
        var currentUser = request.CurrentUser;
        var exerciseId = request.ExerciseId;

        if (currentUser == null || currentUser.Id.IsEmpty || exerciseId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidExerciseError(Messages.FieldRequired));
        }

        var exercise = await _exerciseRepository.FindByIdAsync(exerciseId, cancellationToken);
        if (exercise == null)
        {
            return Result<Unit, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
        }

        var account = await _accountAccess.GetByIdAsync(currentUser.Id, cancellationToken);
        var canEditExercise = exercise.OwnerId == currentUser.Id
            || account?.PermissionClaims.Contains(AuthConstants.Permissions.ManageGlobalExercises, StringComparer.Ordinal) == true;

        if (!canEditExercise)
        {
            return Result<Unit, AppError>.Failure(new ExerciseForbiddenError(Messages.Forbidden));
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            exercise = exercise with { Name = request.Name };
        }

        if (request.BodyPart != BodyParts.Unknown)
        {
            exercise = exercise with { BodyPart = request.BodyPart };
        }

        if (request.EloFormula.HasValue)
        {
            exercise = exercise with { EloFormula = request.EloFormula.Value };
        }

        exercise = exercise with { Description = request.Description, Image = request.Image };

        await _exerciseRepository.UpdateAsync(new WorkoutExerciseWriteModel(exercise.Id, exercise.OwnerId, exercise.Name, exercise.BodyPart, exercise.EloFormula, exercise.Description, exercise.Image, exercise.IsDeleted), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }

    private sealed record UpdateExerciseRequest(
        AuthenticatedAccountContext? CurrentUser,
        Id<Domain.Entities.Exercise> ExerciseId,
        string? Name,
        BodyParts BodyPart,
        ExerciseEloFormula? EloFormula,
        string? Description,
        string? Image);

    public async Task<Result<Unit, AppError>> AddGlobalTranslationAsync(AuthenticatedAccountContext? currentUser, AddGlobalTranslationInput input, CancellationToken cancellationToken = default)
    {
        var (routeUserId, exerciseId, culture, name) = input;

        if (currentUser == null)
        {
            return Result<Unit, AppError>.Failure(new ExerciseForbiddenError(Messages.Forbidden));
        }

        if (routeUserId.IsEmpty || currentUser.Id != routeUserId)
        {
            return Result<Unit, AppError>.Failure(new ExerciseForbiddenError(Messages.Forbidden));
        }

        var account = await _accountAccess.GetByIdAsync(currentUser.Id, cancellationToken);
        if (account?.PermissionClaims.Contains(AuthConstants.Permissions.ManageGlobalExercises, StringComparer.Ordinal) != true)
        {
            return Result<Unit, AppError>.Failure(new ExerciseForbiddenError(Messages.Forbidden));
        }

        var cultureInput = culture?.Trim();
        var nameInput = name?.Trim();

        if (exerciseId.IsEmpty
            || string.IsNullOrWhiteSpace(cultureInput)
            || string.IsNullOrWhiteSpace(nameInput))
        {
            return Result<Unit, AppError>.Failure(new InvalidExerciseError(Messages.FieldRequired));
        }

        if (cultureInput.Length > 16 || nameInput.Length > 200)
        {
            return Result<Unit, AppError>.Failure(new InvalidExerciseError(Messages.FieldRequired));
        }

        try
        {
            _ = CultureInfo.GetCultureInfo(cultureInput);
        }
        catch (CultureNotFoundException)
        {
            return Result<Unit, AppError>.Failure(new InvalidExerciseError(Messages.FieldRequired));
        }

        var exercise = await _exerciseRepository.FindByIdAsync(exerciseId, cancellationToken);
        if (exercise == null)
        {
            return Result<Unit, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
        }

        if (exercise.OwnerId != null)
        {
            return Result<Unit, AppError>.Failure(new ExerciseForbiddenError(Messages.Forbidden));
        }

        var normalizedCulture = cultureInput.ToLowerInvariant();
        await _exerciseRepository.UpsertTranslationAsync(exerciseId, normalizedCulture, nameInput, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
