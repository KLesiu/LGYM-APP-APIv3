using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Reporting.ApiAdapters;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.Trainer.Controllers;

[ApiController]
[Route("api/trainer")]
[Authorize(Policy = AuthConstants.Policies.TrainerAccess)]
public sealed partial class TrainerReportingController : ControllerBase
{
    private readonly ITrainerReportTemplateApiPort _templates;
    private readonly ITrainerReportRequestApiPort _requests;
    private readonly ITrainerReportPhotoApiPort _photos;
    private readonly IRecurringReportAssignmentApiPort _recurringAssignments;
    private readonly IMapper _mapper;

    public TrainerReportingController(ITrainerReportTemplateApiPort templates, ITrainerReportRequestApiPort requests, ITrainerReportPhotoApiPort photos, IRecurringReportAssignmentApiPort recurringAssignments, IMapper mapper)
    {
        _templates = templates;
        _requests = requests;
        _photos = photos;
        _recurringAssignments = recurringAssignments;
        _mapper = mapper;
    }

    [HttpPost("report-templates")]
    [ProducesResponseType(typeof(ReportTemplateDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTemplate([FromBody] UpsertReportTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _templates.CreateAsync(HttpContext.GetAuthenticatedAccountContext()!, new CreateReportTemplateCommand
        {
            Name = request.Name,
            Description = request.Description,
            Fields = request.Fields.Select(MapField).ToList()
        }, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return StatusCode(StatusCodes.Status201Created, _mapper.Map<ReportTemplateResult, ReportTemplateDto>(result.Value));
    }

    [HttpGet("report-templates")]
    [ProducesResponseType(typeof(List<ReportTemplateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTemplates(CancellationToken cancellationToken = default)
    {
        var result = await _templates.GetAllAsync(HttpContext.GetAuthenticatedAccountContext()!, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.MapList<ReportTemplateResult, ReportTemplateDto>(result.Value));
    }

    [HttpGet("report-templates/{templateId}")]
    [ProducesResponseType(typeof(ReportTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetTemplate([FromRoute] string templateId, CancellationToken cancellationToken = default)
    {
        if (!Id<ReportTemplate>.TryParse(templateId, out var parsedTemplateId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var result = await _templates.GetAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTemplateId, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<ReportTemplateResult, ReportTemplateDto>(result.Value));
    }

    [HttpPost("report-templates/{templateId}/update")]
    [ProducesResponseType(typeof(ReportTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateTemplate([FromRoute] string templateId, [FromBody] UpsertReportTemplateRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<ReportTemplate>.TryParse(templateId, out var parsedTemplateId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var result = await _templates.UpdateAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTemplateId, new CreateReportTemplateCommand
        {
            Name = request.Name,
            Description = request.Description,
            Fields = request.Fields.Select(MapField).ToList()
        }, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<ReportTemplateResult, ReportTemplateDto>(result.Value));
    }

    [HttpPost("report-templates/{templateId}/delete")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteTemplate([FromRoute] string templateId, CancellationToken cancellationToken = default)
    {
        if (!Id<ReportTemplate>.TryParse(templateId, out var parsedTemplateId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var result = await _templates.DeleteAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTemplateId, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }

    [HttpPost("trainees/{traineeId}/report-requests")]
    [ProducesResponseType(typeof(ReportRequestDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateReportRequest([FromRoute] string traineeId, [FromBody] CreateReportRequestRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<ReportTemplate>.TryParse(request.TemplateId, out var parsedTemplateId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var result = await _requests.CreateAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, new CreateReportRequestCommand
        {
            TemplateId = parsedTemplateId,
            DueAt = request.DueAt,
            Note = request.Note
        }, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return StatusCode(StatusCodes.Status201Created, _mapper.Map<ReportRequestResult, ReportRequestDto>(result.Value));
    }

    [HttpGet("trainees/{traineeId}/report-submissions")]
    [ProducesResponseType(typeof(List<ReportSubmissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetTraineeSubmissions([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        var result = await _requests.GetSubmissionsAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.MapList<ReportSubmissionResult, ReportSubmissionDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/report-submissions/{submissionId}/feedback")]
    [ProducesResponseType(typeof(ReportSubmissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateSubmissionFeedback([FromRoute] string traineeId, [FromRoute] string submissionId, [FromBody] UpdateReportSubmissionFeedbackRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<ReportSubmission>.TryParse(submissionId, out var parsedSubmissionId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var result = await _requests.UpdateFeedbackAsync(
            HttpContext.GetAuthenticatedAccountContext()!,
            parsedTraineeId,
            parsedSubmissionId,
            new UpdateReportSubmissionFeedbackCommand
            {
                TrainerOverallComment = request.TrainerOverallComment,
                FieldComments = new Dictionary<string, string?>(request.TrainerFieldComments ?? [], StringComparer.OrdinalIgnoreCase)
            },
            cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<ReportSubmissionResult, ReportSubmissionDto>(result.Value));
    }

    private static ReportTemplateFieldCommand MapField(ReportTemplateFieldRequest field)
    {
        return new ReportTemplateFieldCommand
        {
            Key = field.Key,
            Label = field.Label,
            Type = field.Type,
            IsRequired = field.IsRequired,
            Order = field.Order,
            ModuleConfig = field.ModuleConfig
        };
    }

}
