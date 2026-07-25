using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Coaching.ManagedPlans.Assign;
using LgymApi.Application.Coaching.ManagedPlans.Create;
using LgymApi.Application.Coaching.ManagedPlans.Delete;
using LgymApi.Application.Coaching.ManagedPlans.List;
using LgymApi.Application.Coaching.ManagedPlans.Unassign;
using LgymApi.Application.Coaching.ManagedPlans.Update;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanEntity = LgymApi.Domain.Entities.Plan;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Api.Features.Trainer.Controllers;

[ApiController]
[Route("api/trainer")]
[Authorize(Policy = AuthConstants.Policies.TrainerAccess)]
public sealed class TrainerManagedPlansController : ControllerBase
{
    private readonly IListManagedPlansUseCase _listPlans;
    private readonly ICreateTraineeManagedPlanUseCase _createPlan;
    private readonly IUpdateTraineeManagedPlanUseCase _updatePlan;
    private readonly IDeleteTraineeManagedPlanUseCase _deletePlan;
    private readonly IAssignTraineeManagedPlanUseCase _assignPlan;
    private readonly IUnassignTraineeManagedPlanUseCase _unassignPlan;
    private readonly IMapper _mapper;

    public TrainerManagedPlansController(
        IListManagedPlansUseCase listPlans,
        ICreateTraineeManagedPlanUseCase createPlan,
        IUpdateTraineeManagedPlanUseCase updatePlan,
        IDeleteTraineeManagedPlanUseCase deletePlan,
        IAssignTraineeManagedPlanUseCase assignPlan,
        IUnassignTraineeManagedPlanUseCase unassignPlan,
        IMapper mapper)
    {
        _listPlans = listPlans;
        _createPlan = createPlan;
        _updatePlan = updatePlan;
        _deletePlan = deletePlan;
        _assignPlan = assignPlan;
        _unassignPlan = unassignPlan;
        _mapper = mapper;
    }

    [HttpGet("trainees/{traineeId}/plans")]
    [ProducesResponseType(typeof(List<TrainerManagedPlanDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTraineePlans([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<List<ManagedPlanReadModel>, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _listPlans.ExecuteAsync(new ListManagedPlansQuery(trainer!.Id, parsedTraineeId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.MapList<ManagedPlanReadModel, TrainerManagedPlanDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/plans")]
    [ProducesResponseType(typeof(TrainerManagedPlanDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTraineePlan([FromRoute] string traineeId, [FromBody] TrainerPlanFormRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<ManagedPlanReadModel, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        var trainer = HttpContext.GetCurrentUser();
        var command = _mapper.Map<TrainerPlanFormRequest, CreateTraineeManagedPlanCommand>(request) with { TrainerId = trainer!.Id, TraineeId = parsedTraineeId };
        var result = await _createPlan.ExecuteAsync(command, cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : StatusCode(StatusCodes.Status201Created, _mapper.Map<ManagedPlanReadModel, TrainerManagedPlanDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/plans/{planId}/update")]
    [ProducesResponseType(typeof(TrainerManagedPlanDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTraineePlan([FromRoute] string traineeId, [FromRoute] string planId, [FromBody] TrainerPlanFormRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<ManagedPlanReadModel, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        if (!Id<PlanEntity>.TryParse(planId, out var parsedPlanId))
        {
            return Result<ManagedPlanReadModel, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.FieldRequired)).ToActionResult();
        }

        var trainer = HttpContext.GetCurrentUser();
        var command = _mapper.Map<TrainerPlanFormRequest, UpdateTraineeManagedPlanCommand>(request) with
        {
            TrainerId = trainer!.Id,
            TraineeId = parsedTraineeId,
            PlanId = parsedPlanId
        };
        var result = await _updatePlan.ExecuteAsync(command, cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<ManagedPlanReadModel, TrainerManagedPlanDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/plans/{planId}/delete")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteTraineePlan([FromRoute] string traineeId, [FromRoute] string planId, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        if (!Id<PlanEntity>.TryParse(planId, out var parsedPlanId))
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.FieldRequired)).ToActionResult();
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _deletePlan.ExecuteAsync(new DeleteTraineeManagedPlanCommand(trainer!.Id, parsedTraineeId, parsedPlanId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }

    [HttpPost("trainees/{traineeId}/plans/{planId}/assign")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignTraineePlan([FromRoute] string traineeId, [FromRoute] string planId, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        if (!Id<PlanEntity>.TryParse(planId, out var parsedPlanId))
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.FieldRequired)).ToActionResult();
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _assignPlan.ExecuteAsync(new AssignTraineeManagedPlanCommand(trainer!.Id, parsedTraineeId, parsedPlanId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("trainees/{traineeId}/plans/unassign")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnassignTraineePlan([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _unassignPlan.ExecuteAsync(new UnassignTraineeManagedPlanCommand(trainer!.Id, parsedTraineeId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }
}
