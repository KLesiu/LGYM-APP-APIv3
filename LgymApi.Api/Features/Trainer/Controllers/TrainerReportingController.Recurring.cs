using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Domain.Entities;
using LgymApi.Identity.Contracts;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.Trainer.Controllers;

public sealed partial class TrainerReportingController
{
    [HttpPost("trainees/{traineeId}/recurring-report-assignments")]
    [ProducesResponseType(typeof(RecurringReportAssignmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRecurringReportAssignment([FromRoute] string traineeId, [FromBody] UpsertRecurringReportAssignmentRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<ReportTemplate>.TryParse(request.TemplateId, out var parsedTemplateId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var result = await _recurringAssignments.CreateAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, new UpsertRecurringReportAssignmentCommand
        {
            TemplateId = parsedTemplateId,
            IntervalValue = request.IntervalValue,
            IntervalUnit = request.IntervalUnit,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            Note = request.Note
        }, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return StatusCode(StatusCodes.Status201Created, _mapper.Map<RecurringReportAssignmentResult, RecurringReportAssignmentDto>(result.Value));
    }

    [HttpGet("trainees/{traineeId}/recurring-report-assignments")]
    [ProducesResponseType(typeof(List<RecurringReportAssignmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRecurringReportAssignments([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        var result = await _recurringAssignments.GetAllAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.MapList<RecurringReportAssignmentResult, RecurringReportAssignmentDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/recurring-report-assignments/{id}/update")]
    [ProducesResponseType(typeof(RecurringReportAssignmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateRecurringReportAssignment([FromRoute] string traineeId, [FromRoute] string id, [FromBody] UpsertRecurringReportAssignmentRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<RecurringReportAssignment>.TryParse(id, out var parsedAssignmentId) || !Id<ReportTemplate>.TryParse(request.TemplateId, out var parsedTemplateId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var result = await _recurringAssignments.UpdateAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, parsedAssignmentId, new UpsertRecurringReportAssignmentCommand
        {
            TemplateId = parsedTemplateId,
            IntervalValue = request.IntervalValue,
            IntervalUnit = request.IntervalUnit,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            Note = request.Note
        }, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<RecurringReportAssignmentResult, RecurringReportAssignmentDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/recurring-report-assignments/{id}/pause")]
    [ProducesResponseType(typeof(RecurringReportAssignmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PauseRecurringReportAssignment([FromRoute] string traineeId, [FromRoute] string id, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<RecurringReportAssignment>.TryParse(id, out var parsedAssignmentId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var result = await _recurringAssignments.PauseAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, parsedAssignmentId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<RecurringReportAssignmentResult, RecurringReportAssignmentDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/recurring-report-assignments/{id}/resume")]
    [ProducesResponseType(typeof(RecurringReportAssignmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResumeRecurringReportAssignment([FromRoute] string traineeId, [FromRoute] string id, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<RecurringReportAssignment>.TryParse(id, out var parsedAssignmentId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var result = await _recurringAssignments.ResumeAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, parsedAssignmentId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<RecurringReportAssignmentResult, RecurringReportAssignmentDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/recurring-report-assignments/{id}/delete")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteRecurringReportAssignment([FromRoute] string traineeId, [FromRoute] string id, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<RecurringReportAssignment>.TryParse(id, out var parsedAssignmentId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var result = await _recurringAssignments.DeleteAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, parsedAssignmentId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }
}
