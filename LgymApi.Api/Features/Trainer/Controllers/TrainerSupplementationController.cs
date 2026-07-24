using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.Supplementation.AssignTraineePlan;
using LgymApi.Application.Nutrition.Supplementation.CreateTraineePlan;
using LgymApi.Application.Nutrition.Supplementation.DeleteTraineePlan.Contracts;
using LgymApi.Application.Nutrition.Supplementation.DeleteTraineePlan.Models;
using LgymApi.Application.Nutrition.Supplementation.GetComplianceSummary;
using LgymApi.Application.Nutrition.Supplementation.GetTraineePlans;
using LgymApi.Application.Nutrition.Supplementation.Models;
using LgymApi.Application.Nutrition.Supplementation.UnassignTraineePlan;
using LgymApi.Application.Nutrition.Supplementation.UnassignTraineePlan.Contracts;
using LgymApi.Application.Nutrition.Supplementation.UpdateTraineePlan;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Api.Features.Trainer.Controllers;

[ApiController]
[Route("api/trainer")]
[Authorize(Policy = AuthConstants.Policies.TrainerAccess)]
public sealed class TrainerSupplementationController : ControllerBase
{
    private readonly IGetTraineeSupplementPlansUseCase _getTraineePlans;
    private readonly ICreateTraineeSupplementPlanUseCase _createTraineePlan;
    private readonly IUpdateTraineeSupplementPlanUseCase _updateTraineePlan;
    private readonly IDeleteTraineeSupplementPlanUseCase _deleteTraineePlan;
    private readonly IAssignTraineeSupplementPlanUseCase _assignTraineePlan;
    private readonly IUnassignTraineeSupplementPlanUseCase _unassignTraineePlan;
    private readonly IGetSupplementComplianceSummaryUseCase _getComplianceSummary;
    private readonly IMapper _mapper;

    public TrainerSupplementationController(
        IGetTraineeSupplementPlansUseCase getTraineePlans,
        ICreateTraineeSupplementPlanUseCase createTraineePlan,
        IUpdateTraineeSupplementPlanUseCase updateTraineePlan,
        IDeleteTraineeSupplementPlanUseCase deleteTraineePlan,
        IAssignTraineeSupplementPlanUseCase assignTraineePlan,
        IUnassignTraineeSupplementPlanUseCase unassignTraineePlan,
        IGetSupplementComplianceSummaryUseCase getComplianceSummary,
        IMapper mapper)
    {
        _getTraineePlans = getTraineePlans;
        _createTraineePlan = createTraineePlan;
        _updateTraineePlan = updateTraineePlan;
        _deleteTraineePlan = deleteTraineePlan;
        _assignTraineePlan = assignTraineePlan;
        _unassignTraineePlan = unassignTraineePlan;
        _getComplianceSummary = getComplianceSummary;
        _mapper = mapper;
    }

    [HttpGet("trainees/{traineeId}/supplement-plans")]
    [ProducesResponseType(typeof(List<SupplementPlanDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTraineePlans([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _getTraineePlans.ExecuteAsync(
            new GetTraineeSupplementPlansQuery(trainer!.Id, parsedTraineeId),
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : Ok(_mapper.MapList<SupplementPlanReadModel, SupplementPlanDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/supplement-plans")]
    [ProducesResponseType(typeof(SupplementPlanDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTraineePlan([FromRoute] string traineeId, [FromBody] UpsertSupplementPlanRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _createTraineePlan.ExecuteAsync(
            new CreateTraineeSupplementPlanCommand(
                trainer!.Id,
                parsedTraineeId,
                _mapper.Map<UpsertSupplementPlanRequest, SupplementPlanUpsertData>(request)),
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : StatusCode(StatusCodes.Status201Created, _mapper.Map<SupplementPlanReadModel, SupplementPlanDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/supplement-plans/{planId}/update")]
    [ProducesResponseType(typeof(SupplementPlanDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTraineePlan([FromRoute] string traineeId, [FromRoute] string planId, [FromBody] UpsertSupplementPlanRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<SupplementPlan>.TryParse(planId, out var parsedPlanId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _updateTraineePlan.ExecuteAsync(
            new UpdateTraineeSupplementPlanCommand(
                trainer!.Id,
                parsedTraineeId,
                parsedPlanId,
                _mapper.Map<UpsertSupplementPlanRequest, SupplementPlanUpsertData>(request)),
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : Ok(_mapper.Map<SupplementPlanReadModel, SupplementPlanDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/supplement-plans/{planId}/delete")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteTraineePlan([FromRoute] string traineeId, [FromRoute] string planId, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<SupplementPlan>.TryParse(planId, out var parsedPlanId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _deleteTraineePlan.ExecuteAsync(
            new DeleteTraineeSupplementPlanCommand(trainer!.Id, parsedTraineeId, parsedPlanId),
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }

    [HttpPost("trainees/{traineeId}/supplement-plans/{planId}/assign")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignTraineePlan([FromRoute] string traineeId, [FromRoute] string planId, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<SupplementPlan>.TryParse(planId, out var parsedPlanId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _assignTraineePlan.ExecuteAsync(
            new AssignTraineeSupplementPlanCommand(trainer!.Id, parsedTraineeId, parsedPlanId),
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("trainees/{traineeId}/supplement-plans/unassign")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnassignTraineePlan([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _unassignTraineePlan.ExecuteAsync(
            new UnassignTraineeSupplementPlanCommand(trainer!.Id, parsedTraineeId),
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpGet("trainees/{traineeId}/supplements/compliance")]
    [ProducesResponseType(typeof(SupplementComplianceSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetComplianceSummary([FromRoute] string traineeId, [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (fromDate is null || toDate is null)
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.DateRangeRequired));
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _getComplianceSummary.ExecuteAsync(
            new GetSupplementComplianceSummaryQuery(trainer!.Id, parsedTraineeId, fromDate.Value, toDate.Value),
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : Ok(_mapper.Map<SupplementComplianceSummaryReadModel, SupplementComplianceSummaryDto>(result.Value));
    }
}
