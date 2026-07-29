using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Identity.Compatibility.Task7.Contracts;
using LgymApi.Application.Nutrition.Supplementation.Models;
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
public sealed class TrainerSupplementationController : ControllerBase
{
    private readonly ISupplementationAccountCompatibilityAdapter _supplementation;
    private readonly IMapper _mapper;

    public TrainerSupplementationController(
        ISupplementationAccountCompatibilityAdapter supplementation,
        IMapper mapper)
    {
        _supplementation = supplementation;
        _mapper = mapper;
    }

    [HttpGet("trainees/{traineeId}/supplement-plans")]
    [ProducesResponseType(typeof(List<SupplementPlanDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTraineePlans([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _supplementation.GetTraineePlansAsync(
            new SupplementPlanListAccountQuery(trainerId, parsedTraineeId),
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : Ok(_mapper.MapList<SupplementPlanReadModel, SupplementPlanDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/supplement-plans")]
    [ProducesResponseType(typeof(SupplementPlanDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTraineePlan([FromRoute] string traineeId, [FromBody] UpsertSupplementPlanRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _supplementation.CreateAsync(
            new SupplementPlanCreateAccountCommand(trainerId, parsedTraineeId, _mapper.Map<UpsertSupplementPlanRequest, SupplementPlanUpsertData>(request)),
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : StatusCode(StatusCodes.Status201Created, _mapper.Map<SupplementPlanReadModel, SupplementPlanDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/supplement-plans/{planId}/update")]
    [ProducesResponseType(typeof(SupplementPlanDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTraineePlan([FromRoute] string traineeId, [FromRoute] string planId, [FromBody] UpsertSupplementPlanRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<SupplementPlan>.TryParse(planId, out var parsedPlanId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _supplementation.UpdateAsync(
            new SupplementPlanUpdateAccountCommand(trainerId, parsedTraineeId, parsedPlanId, _mapper.Map<UpsertSupplementPlanRequest, SupplementPlanUpsertData>(request)),
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : Ok(_mapper.Map<SupplementPlanReadModel, SupplementPlanDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/supplement-plans/{planId}/delete")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteTraineePlan([FromRoute] string traineeId, [FromRoute] string planId, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<SupplementPlan>.TryParse(planId, out var parsedPlanId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _supplementation.DeleteAsync(
            new SupplementPlanDeleteAccountCommand(trainerId, parsedTraineeId, parsedPlanId),
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }

    [HttpPost("trainees/{traineeId}/supplement-plans/{planId}/assign")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignTraineePlan([FromRoute] string traineeId, [FromRoute] string planId, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<SupplementPlan>.TryParse(planId, out var parsedPlanId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _supplementation.AssignAsync(
            new SupplementPlanAssignAccountCommand(trainerId, parsedTraineeId, parsedPlanId),
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("trainees/{traineeId}/supplement-plans/unassign")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnassignTraineePlan([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _supplementation.UnassignAsync(
            new SupplementPlanUnassignAccountCommand(trainerId, parsedTraineeId),
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpGet("trainees/{traineeId}/supplements/compliance")]
    [ProducesResponseType(typeof(SupplementComplianceSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetComplianceSummary([FromRoute] string traineeId, [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (fromDate is null || toDate is null)
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.DateRangeRequired));
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _supplementation.GetComplianceAsync(
            new SupplementComplianceAccountQuery(trainerId, parsedTraineeId, fromDate.Value, toDate.Value),
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : Ok(_mapper.Map<SupplementComplianceSummaryReadModel, SupplementComplianceSummaryDto>(result.Value));
    }
}
