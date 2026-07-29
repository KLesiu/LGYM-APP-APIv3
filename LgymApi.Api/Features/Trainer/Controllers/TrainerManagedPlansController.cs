using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Identity.Compatibility.Task7.Contracts;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanEntity = LgymApi.Domain.Entities.Plan;

namespace LgymApi.Api.Features.Trainer.Controllers;

[ApiController]
[Route("api/trainer")]
[Authorize(Policy = AuthConstants.Policies.TrainerAccess)]
public sealed class TrainerManagedPlansController : ControllerBase
{
    private readonly IManagedPlanAccountCompatibilityAdapter _plans;
    private readonly IMapper _mapper;

    public TrainerManagedPlansController(
        IManagedPlanAccountCompatibilityAdapter plans,
        IMapper mapper)
    {
        _plans = plans;
        _mapper = mapper;
    }

    [HttpGet("trainees/{traineeId}/plans")]
    [ProducesResponseType(typeof(List<TrainerManagedPlanDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTraineePlans([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<List<ManagedPlanReadModel>, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _plans.ListAsync(new ManagedPlanListAccountQuery(trainerId, parsedTraineeId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.MapList<ManagedPlanReadModel, TrainerManagedPlanDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/plans")]
    [ProducesResponseType(typeof(TrainerManagedPlanDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTraineePlan([FromRoute] string traineeId, [FromBody] TrainerPlanFormRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<ManagedPlanReadModel, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _plans.CreateAsync(new ManagedPlanCreateAccountCommand(trainerId, parsedTraineeId, request.Name), cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : StatusCode(StatusCodes.Status201Created, _mapper.Map<ManagedPlanReadModel, TrainerManagedPlanDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/plans/{planId}/update")]
    [ProducesResponseType(typeof(TrainerManagedPlanDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTraineePlan([FromRoute] string traineeId, [FromRoute] string planId, [FromBody] TrainerPlanFormRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<ManagedPlanReadModel, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        if (!Id<PlanEntity>.TryParse(planId, out var parsedPlanId))
        {
            return Result<ManagedPlanReadModel, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.FieldRequired)).ToActionResult();
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _plans.UpdateAsync(new ManagedPlanUpdateAccountCommand(trainerId, parsedTraineeId, parsedPlanId, request.Name), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<ManagedPlanReadModel, TrainerManagedPlanDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/plans/{planId}/delete")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteTraineePlan([FromRoute] string traineeId, [FromRoute] string planId, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        if (!Id<PlanEntity>.TryParse(planId, out var parsedPlanId))
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.FieldRequired)).ToActionResult();
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _plans.DeleteAsync(new ManagedPlanDeleteAccountCommand(trainerId, parsedTraineeId, parsedPlanId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }

    [HttpPost("trainees/{traineeId}/plans/{planId}/assign")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignTraineePlan([FromRoute] string traineeId, [FromRoute] string planId, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        if (!Id<PlanEntity>.TryParse(planId, out var parsedPlanId))
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.FieldRequired)).ToActionResult();
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _plans.AssignAsync(new ManagedPlanAssignAccountCommand(trainerId, parsedTraineeId, parsedPlanId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("trainees/{traineeId}/plans/unassign")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnassignTraineePlan([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _plans.UnassignAsync(new ManagedPlanUnassignAccountCommand(trainerId, parsedTraineeId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }
}
