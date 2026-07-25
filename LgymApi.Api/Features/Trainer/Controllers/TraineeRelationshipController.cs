using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Coaching.Invitations.Accept;
using LgymApi.Application.Coaching.Invitations.Reject;
using LgymApi.Application.Coaching.ManagedPlans.GetActive;
using LgymApi.Application.Coaching.Relationships.DetachFromTrainer;
using LgymApi.Application.Coaching.Relationships.GetCurrentTrainer;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TrainerInvitationEntity = LgymApi.Domain.Entities.TrainerInvitation;

namespace LgymApi.Api.Features.Trainer.Controllers;

[ApiController]
[Route("api/trainee")]
[Authorize]
public sealed class TraineeRelationshipController : ControllerBase
{
    private readonly IAcceptInvitationUseCase _acceptInvitation;
    private readonly IRejectInvitationUseCase _rejectInvitation;
    private readonly IDetachFromTrainerUseCase _detachFromTrainer;
    private readonly IGetCurrentTrainerUseCase _getCurrentTrainer;
    private readonly IGetActiveManagedPlanUseCase _getActiveManagedPlan;
    private readonly IMapper _mapper;

    public TraineeRelationshipController(
        IAcceptInvitationUseCase acceptInvitation,
        IRejectInvitationUseCase rejectInvitation,
        IDetachFromTrainerUseCase detachFromTrainer,
        IGetCurrentTrainerUseCase getCurrentTrainer,
        IGetActiveManagedPlanUseCase getActiveManagedPlan,
        IMapper mapper)
    {
        _acceptInvitation = acceptInvitation;
        _rejectInvitation = rejectInvitation;
        _detachFromTrainer = detachFromTrainer;
        _getCurrentTrainer = getCurrentTrainer;
        _getActiveManagedPlan = getActiveManagedPlan;
        _mapper = mapper;
    }

    [HttpPost("invitations/{invitationId}/accept")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AcceptInvitation([FromRoute] string invitationId, CancellationToken cancellationToken = default)
    {
        if (!Id<TrainerInvitationEntity>.TryParse(invitationId, out var parsedInvitationId))
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.FieldRequired)).ToActionResult();
        }

        var trainee = HttpContext.GetCurrentUser();
        var result = await _acceptInvitation.ExecuteAsync(
            new AcceptInvitationCommand(trainee!.Id, parsedInvitationId),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("invitations/{invitationId}/reject")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RejectInvitation([FromRoute] string invitationId, CancellationToken cancellationToken = default)
    {
        if (!Id<TrainerInvitationEntity>.TryParse(invitationId, out var parsedInvitationId))
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.FieldRequired)).ToActionResult();
        }

        var trainee = HttpContext.GetCurrentUser();
        var result = await _rejectInvitation.ExecuteAsync(
            new RejectInvitationCommand(trainee!.Id, parsedInvitationId),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("trainer/detach")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DetachFromTrainer(CancellationToken cancellationToken = default)
    {
        var trainee = HttpContext.GetCurrentUser();
        var result = await _detachFromTrainer.ExecuteAsync(new DetachFromTrainerCommand(trainee!.Id), cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpGet("trainer")]
    [ProducesResponseType(typeof(TraineeTrainerProfileDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentTrainer(CancellationToken cancellationToken = default)
    {
        var trainee = HttpContext.GetCurrentUser();
        var result = await _getCurrentTrainer.ExecuteAsync(new GetCurrentTrainerQuery(trainee!.Id), cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<CurrentTrainerReadModel, TraineeTrainerProfileDto>(result.Value));
    }

    [HttpGet("plan/active")]
    [ProducesResponseType(typeof(TrainerManagedPlanDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveAssignedPlan(CancellationToken cancellationToken = default)
    {
        var trainee = HttpContext.GetCurrentUser();
        var result = await _getActiveManagedPlan.ExecuteAsync(new GetActiveManagedPlanQuery(trainee!.Id), cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<ManagedPlanReadModel, TrainerManagedPlanDto>(result.Value));
    }
}
