using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.AspNetCore.Mvc;
using PhotoEntity = LgymApi.Domain.Entities.Photo;
using ReportRequestEntity = LgymApi.Domain.Entities.ReportRequest;
using LgymApi.Identity.Contracts;

namespace LgymApi.Api.Features.Trainer.Controllers;

public sealed partial class TrainerReportingController
{
    [HttpPost("reporting/photos/upload-init")]
    [ProducesResponseType(typeof(InitiatePhotoUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> InitiatePhotoUpload([FromBody] InitiatePhotoUploadRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<ReportRequestEntity>.TryParse(request.ReportRequestId, out var parsedRequestId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var result = await _photos.InitiateAsync(
            HttpContext.GetAuthenticatedAccountContext()!,
            new InitiatePhotoUploadCommand
            {
                ReportRequestId = parsedRequestId,
                ViewType = request.ViewType,
                MimeType = request.MimeType,
                SizeBytes = request.SizeBytes
            },
            cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<InitiatePhotoUploadResult, InitiatePhotoUploadResponse>(result.Value));
    }

    [HttpGet("reporting/photos/{photoId}/signed-url")]
    [ProducesResponseType(typeof(GetSignedReadUrlResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPhotoSignedReadUrl([FromRoute] string photoId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(photoId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        if (!Id<PhotoEntity>.TryParse(photoId, out var parsedPhotoId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>("Invalid photo ID format"));
        }

        var result = await _photos.GetSignedReadUrlAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedPhotoId, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<SignedReadUrlResult, GetSignedReadUrlResponse>(result.Value));
    }

    [HttpPost("reporting/photos/complete-upload")]
    [ProducesResponseType(typeof(CompletePhotoUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CompletePhotoUpload([FromBody] CompletePhotoUploadRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<ReportRequestEntity>.TryParse(request.ReportRequestId, out var parsedRequestId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var result = await _photos.CompleteAsync(
            HttpContext.GetAuthenticatedAccountContext()!,
            new CompletePhotoUploadCommand
            {
                StorageKey = request.StorageKey,
                MimeType = request.MimeType,
                SizeBytes = request.SizeBytes,
                Checksum = request.Checksum,
                ReportRequestId = parsedRequestId,
                ViewType = request.ViewType
            },
            cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<CompletePhotoUploadResult, CompletePhotoUploadResponse>(result.Value));
    }

    [HttpGet("reporting/photos/history")]
    [ProducesResponseType(typeof(GetPhotoHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPhotoHistory([FromQuery] string? traineeId, [FromQuery] string? requestId, CancellationToken cancellationToken = default)
    {
        Id<AccountReference>? parsedTraineeId = null;
        if (!string.IsNullOrWhiteSpace(traineeId))
        {
            if (!Id<AccountReference>.TryParse(traineeId, out var tempId))
            {
                return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
            }

            parsedTraineeId = tempId;
        }

        Id<ReportRequestEntity>? parsedRequestId = null;
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            if (!Id<ReportRequestEntity>.TryParse(requestId, out var tempId))
            {
                return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
            }

            parsedRequestId = tempId;
        }

        var result = await _photos.GetHistoryAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, parsedRequestId, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(new GetPhotoHistoryResponse
        {
            Photos = _mapper.MapList<PhotoHistoryItemResult, PhotoHistoryItemResponse>(result.Value)
        });
    }
}
