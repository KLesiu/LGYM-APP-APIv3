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
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Features.Reporting;

public sealed partial class ReportingService
{
    private sealed record CompletePhotoUploadValidationContext(ReportRequestPersistenceModel Request, string ParsedViewType);

    private async Task<Result<CompletePhotoUploadValidationContext, AppError>> ValidateCompletePhotoUploadRequestAsync(
        AuthenticatedAccountContext currentUser,
        CompletePhotoUploadCommand command,
        CancellationToken cancellationToken)
    {
        if (command.ReportRequestId.IsEmpty || string.IsNullOrWhiteSpace(command.StorageKey))
        {
            return Result<CompletePhotoUploadValidationContext, AppError>.Failure(
                new InvalidReportingError(Messages.FieldRequired));
        }

        var request = await _requestSubmissionPersistence.FindRequestByIdAsync(command.ReportRequestId, cancellationToken);
        if (request == null || request.IsDeleted)
        {
            return Result<CompletePhotoUploadValidationContext, AppError>.Failure(
                new ReportingNotFoundError(Messages.DidntFind));
        }

        var requestStatusValidation = EnsureRequestAllowsPhotoUpload(request);
        if (requestStatusValidation.IsFailure)
        {
            return Result<CompletePhotoUploadValidationContext, AppError>.Failure(requestStatusValidation.Error);
        }

        var authCheck = await ValidatePhotoAccessAsync(currentUser, request.TraineeId, cancellationToken);
        if (authCheck.IsFailure)
        {
            return Result<CompletePhotoUploadValidationContext, AppError>.Failure(authCheck.Error);
        }

        var viewTypeValidation = TryParseViewType(command.ViewType, out var parsedViewType);
        if (viewTypeValidation.IsFailure)
        {
            return Result<CompletePhotoUploadValidationContext, AppError>.Failure(viewTypeValidation.Error);
        }

        var mimeTypeValidation = ValidateMimeType(command.MimeType);
        if (mimeTypeValidation.IsFailure)
        {
            return Result<CompletePhotoUploadValidationContext, AppError>.Failure(mimeTypeValidation.Error);
        }

        var sizeValidation = ValidateFileSize(command.SizeBytes);
        if (sizeValidation.IsFailure)
        {
            return Result<CompletePhotoUploadValidationContext, AppError>.Failure(sizeValidation.Error);
        }

        var prefixValidation = EnsureStorageKeyHasExpectedPrefix(currentUser, command.StorageKey, request.TraineeId, command.ReportRequestId, parsedViewType);
        if (prefixValidation.IsFailure)
        {
            return Result<CompletePhotoUploadValidationContext, AppError>.Failure(prefixValidation.Error);
        }

        return Result<CompletePhotoUploadValidationContext, AppError>.Success(new CompletePhotoUploadValidationContext(request, parsedViewType));
    }

    private async Task<Result<PendingPhotoUpload, AppError>> GetUploadSessionOrErrorAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        var uploadSession = await _photoPersistence.FindUploadSessionAsync(storageKey, cancellationToken);
        if (uploadSession == null)
        {
            _logger.LogWarning(
                "Photo complete-upload rejected because no pending upload-init exists for key {StorageKey}",
                storageKey);

            return Result<PendingPhotoUpload, AppError>.Failure(
                new InvalidReportingError("Upload session not found or expired"));
        }

        if (uploadSession.Status == PhotoUploadSessionStatus.Expired)
        {
            return Result<PendingPhotoUpload, AppError>.Failure(
                new InvalidReportingError("Upload session not found or expired"));
        }

        if (uploadSession.Status == PhotoUploadSessionStatus.Failed)
        {
            return Result<PendingPhotoUpload, AppError>.Failure(
                new InvalidReportingError("Upload session is no longer valid"));
        }

        return Result<PendingPhotoUpload, AppError>.Success(uploadSession);
    }

    private async Task<CompletePhotoUploadResult?> TryGetCompletedPhotoResultAsync(
        PendingPhotoUpload uploadSession,
        CancellationToken cancellationToken)
    {
        if (uploadSession.Status != PhotoUploadSessionStatus.Completed || !uploadSession.CompletedPhotoId.HasValue)
        {
            return null;
        }

        var completedPhoto = await _photoPersistence.FindByIdAsync(uploadSession.CompletedPhotoId.Value, cancellationToken);
        if (completedPhoto == null)
        {
            return null;
        }

        return new CompletePhotoUploadResult
        {
            PhotoId = completedPhoto.Id,
            UploadedAt = completedPhoto.CreatedAt
        };
    }

    private async Task<Result<PhotoMetadata?, AppError>> TryGetPhotoMetadataAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        try
        {
            return Result<PhotoMetadata?, AppError>.Success(
                await _photoStorageProvider.GetMetadataAsync(storageKey, cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Photo complete-upload metadata lookup failed for key {StorageKey} using provider {Provider}",
                storageKey,
                _photoStorageOptions.Provider);

            return Result<PhotoMetadata?, AppError>.Failure(
                new InvalidReportingError("Failed to verify uploaded photo metadata"));
        }
    }

    private async Task PersistInvalidUploadAndCleanupAsync(
        string storageKey,
        string failureReason,
        CancellationToken cancellationToken)
    {
        await CleanupInvalidUploadedObjectAsync(storageKey, cancellationToken);
        await _photoPersistence.MarkUploadFailedAsync(storageKey, failureReason, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<ReportPhotoPersistenceModel> CreateAndPersistPhotoAsync(
        AuthenticatedAccountContext currentUser,
        CompletePhotoUploadCommand command,
        PhotoMetadata metadata,
        string parsedViewType,
        Id<AccountReference> ownerUserId,
        CancellationToken cancellationToken)
    {
        var createdAt = DateTimeOffset.UtcNow;
        var photo = new NewReportPhotoPersistenceModel(
            Id<Photo>.New(),
            command.StorageKey,
            metadata.ContentType,
            metadata.SizeBytes,
            ResolveStoredChecksum(command.Checksum, metadata.ETag),
            null,
            parsedViewType,
            command.ReportRequestId,
            currentUser.Id,
            ownerUserId,
            createdAt);

        await _photoPersistence.SaveAsync(photo, cancellationToken);
        await _photoPersistence.MarkUploadCompletedAsync(command.StorageKey, photo.Id, photo.CreatedAt, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ReportPhotoPersistenceModel(
            photo.Id,
            photo.StorageKey,
            photo.MimeType,
            photo.SizeBytes,
            photo.Checksum,
            photo.ThumbnailStorageKey,
            photo.ViewType,
            photo.ReportRequestId,
            photo.UploaderAccountId,
            photo.OwnerAccountId,
            photo.CreatedAt,
            false);
    }

    private async Task TryDeleteReplacedObjectAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            await _photoStorageProvider.DeleteAsync(storageKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Photo complete-upload saved new metadata but failed to delete replaced object {StorageKey}",
                storageKey);
        }
    }
}
