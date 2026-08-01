using LgymApi.Application.Abstractions.Storage;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Reporting.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using LgymApi.Identity.Contracts.Accounts;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Features.Reporting;

public sealed partial class ReportingService
{
    public async Task<Result<InitiatePhotoUploadResult, AppError>> InitiatePhotoUploadAsync(
        AuthenticatedAccountContext currentUser,
        InitiatePhotoUploadCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.ReportRequestId.IsEmpty)
        {
            return Result<InitiatePhotoUploadResult, AppError>.Failure(
                new InvalidReportingError(Messages.FieldRequired));
        }

        var request = await _requestSubmissionPersistence.FindRequestByIdAsync(command.ReportRequestId, cancellationToken);
        if (request == null || request.IsDeleted)
        {
            return Result<InitiatePhotoUploadResult, AppError>.Failure(
                new ReportingNotFoundError(Messages.DidntFind));
        }

        var requestStatusValidation = EnsureRequestAllowsPhotoUpload(request);
        if (requestStatusValidation.IsFailure)
        {
            return Result<InitiatePhotoUploadResult, AppError>.Failure(requestStatusValidation.Error);
        }

        var authCheck = await ValidatePhotoAccessAsync(currentUser, request.TraineeId, cancellationToken);
        if (authCheck.IsFailure)
        {
            return authCheck.Error is ReportingForbiddenError
                ? Result<InitiatePhotoUploadResult, AppError>.Failure(new ReportingNotFoundError(Messages.DidntFind))
                : Result<InitiatePhotoUploadResult, AppError>.Failure(authCheck.Error);
        }

        var viewTypeValidation = TryParseViewType(command.ViewType, out var parsedViewType);
        if (viewTypeValidation.IsFailure)
        {
            return Result<InitiatePhotoUploadResult, AppError>.Failure(viewTypeValidation.Error);
        }

        var mimeTypeValidation = ValidateMimeType(command.MimeType);
        if (mimeTypeValidation.IsFailure)
        {
            return Result<InitiatePhotoUploadResult, AppError>.Failure(mimeTypeValidation.Error);
        }

        var sizeValidation = ValidateFileSize(command.SizeBytes);
        if (sizeValidation.IsFailure)
        {
            return Result<InitiatePhotoUploadResult, AppError>.Failure(sizeValidation.Error);
        }

        if (ShouldEnforceDevelopmentPhotoLimits())
        {
            var limitValidation = await ValidateDeveloperLimitsAsync(currentUser.Id, cancellationToken);
            if (limitValidation.IsFailure)
            {
                _logger.LogWarning(
                    "Photo upload-init rejected for user {UserId} and report request {ReportRequestId}: {Reason}",
                    currentUser.Id,
                    command.ReportRequestId,
                    limitValidation.Error.Message);

                return Result<InitiatePhotoUploadResult, AppError>.Failure(limitValidation.Error);
            }
        }

        var storageKey = GenerateStorageKey(
            request.TraineeId,
            command.ReportRequestId,
            parsedViewType,
            GetFileExtensionFromMimeType(command.MimeType));

        _logger.LogInformation(
            "Photo upload-init creating storage key with prefix {StorageKeyPrefix} for user {UserId}",
            BuildStorageKeyPrefix(request.TraineeId, command.ReportRequestId, parsedViewType),
            currentUser.Id);

        var expiresAt = DateTimeOffset.UtcNow.Add(GetSignedUploadExpiration());

        await _photoPersistence.RecordUploadInitAsync(new PendingPhotoUpload(
            Id<PhotoUploadSession>.New(),
            storageKey,
            currentUser.Id,
            request.TraineeId,
            command.ReportRequestId,
            parsedViewType,
            command.MimeType,
            command.SizeBytes,
            DateTimeOffset.UtcNow,
            expiresAt,
            null,
            null,
            PhotoUploadSessionStatus.Pending,
            null), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var uploadUrl = await _photoStorageProvider.GenerateSignedUploadUrlAsync(
            storageKey,
            command.MimeType,
            GetSignedUploadExpiration(),
            cancellationToken);

        _logger.LogInformation(
            "Photo upload-init succeeded for user {UserId}, report request {ReportRequestId}, provider {Provider}, key {StorageKey}",
            currentUser.Id,
            command.ReportRequestId,
            _photoStorageOptions.Provider,
            storageKey);

        return Result<InitiatePhotoUploadResult, AppError>.Success(new InitiatePhotoUploadResult
        {
            UploadUrl = uploadUrl,
            StorageKey = storageKey,
            ExpiresAt = expiresAt
        });
    }

    public async Task<Result<CompletePhotoUploadResult, AppError>> CompletePhotoUploadAsync(
        AuthenticatedAccountContext currentUser,
        CompletePhotoUploadCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationContext = await ValidateCompletePhotoUploadRequestAsync(currentUser, command, cancellationToken);
        if (validationContext.IsFailure)
        {
            return Result<CompletePhotoUploadResult, AppError>.Failure(validationContext.Error);
        }

        var request = validationContext.Value.Request;
        var parsedViewType = validationContext.Value.ParsedViewType;

        var uploadSessionResult = await GetUploadSessionOrErrorAsync(command.StorageKey, cancellationToken);
        if (uploadSessionResult.IsFailure)
        {
            return Result<CompletePhotoUploadResult, AppError>.Failure(uploadSessionResult.Error);
        }

        var uploadSession = uploadSessionResult.Value;

        var completedResult = await TryGetCompletedPhotoResultAsync(uploadSession, cancellationToken);
        if (completedResult != null)
        {
            return Result<CompletePhotoUploadResult, AppError>.Success(completedResult);
        }

        var pendingUploadValidation = ValidatePendingUpload(currentUser, command, request.TraineeId, parsedViewType, uploadSession);
        if (pendingUploadValidation.IsFailure)
        {
            _logger.LogWarning(
                "Photo complete-upload rejected because pending upload data mismatched for key {StorageKey}: {Reason}",
                command.StorageKey,
                pendingUploadValidation.Error.Message);

            return Result<CompletePhotoUploadResult, AppError>.Failure(pendingUploadValidation.Error);
        }

        var metadataResult = await TryGetPhotoMetadataAsync(command.StorageKey, cancellationToken);
        if (metadataResult.IsFailure)
        {
            return Result<CompletePhotoUploadResult, AppError>.Failure(metadataResult.Error);
        }

        var metadata = metadataResult.Value;

        if (metadata == null)
        {
            _logger.LogWarning(
                "Photo complete-upload rejected because object metadata was not found for key {StorageKey}",
                command.StorageKey);

            return Result<CompletePhotoUploadResult, AppError>.Failure(
                new InvalidReportingError("Uploaded photo object was not found"));
        }

        var metadataValidation = ValidateUploadedObjectMetadata(command, uploadSession, metadata);
        if (metadataValidation.IsFailure)
        {
            _logger.LogWarning(
                "Photo complete-upload metadata verification failed for key {StorageKey}: {Reason}",
                command.StorageKey,
                metadataValidation.Error.Message);

            await PersistInvalidUploadAndCleanupAsync(command.StorageKey, metadataValidation.Error.Message, cancellationToken);
            return Result<CompletePhotoUploadResult, AppError>.Failure(metadataValidation.Error);
        }

        var existingPhoto = await _photoPersistence.FindActiveByRequestAndViewAsync(
            command.ReportRequestId,
            parsedViewType,
            cancellationToken);

        var photo = await CreateAndPersistPhotoAsync(currentUser, command, metadata, parsedViewType, request.TraineeId, cancellationToken);

        if (existingPhoto != null && !string.Equals(existingPhoto.StorageKey, photo.StorageKey, StringComparison.Ordinal))
        {
            await TryDeleteReplacedObjectAsync(existingPhoto.StorageKey, cancellationToken);
        }

        _logger.LogInformation(
            "Photo complete-upload succeeded for user {UserId}, request {ReportRequestId}, photo {PhotoId}",
            currentUser.Id,
            command.ReportRequestId,
            photo.Id);

        return Result<CompletePhotoUploadResult, AppError>.Success(new CompletePhotoUploadResult
        {
            PhotoId = photo.Id,
            UploadedAt = photo.CreatedAt
        });
    }

}
