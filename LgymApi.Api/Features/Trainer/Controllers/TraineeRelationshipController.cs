using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Coaching.ApiAdapters;
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
    private readonly ITraineeRelationshipApiPort _relationship;
    private readonly IMapper _mapper;

    public TraineeRelationshipController(
        ITraineeRelationshipApiPort relationship,
        IMapper mapper)
    {
        _relationship = relationship;
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

        var result = await _relationship.AcceptInvitationAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedInvitationId, cancellationToken);
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

        var result = await _relationship.RejectInvitationAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedInvitationId, cancellationToken);
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
        var result = await _relationship.DetachAsync(HttpContext.GetAuthenticatedAccountContext()!, cancellationToken);
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
        var result = await _relationship.GetCurrentTrainerAsync(HttpContext.GetAuthenticatedAccountContext()!, cancellationToken);
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
        var result = await _relationship.GetActivePlanAsync(HttpContext.GetAuthenticatedAccountContext()!, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<ManagedPlanReadModel, TrainerManagedPlanDto>(result.Value));
    }
}
