using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Idempotency;
using LgymApi.Api.Middleware;
using LgymApi.Application.Coaching.Invitations.Create;
using LgymApi.Application.Coaching.Invitations.CreateByEmail;
using LgymApi.Application.Coaching.Invitations.ListPaginated;
using LgymApi.Application.Coaching.Invitations.Models;
using LgymApi.Application.Coaching.Invitations.Revoke;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Pagination;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TrainerInvitationEntity = LgymApi.Domain.Entities.TrainerInvitation;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Api.Features.Trainer.Controllers;

[ApiController]
[Route("api/trainer")]
[Authorize(Policy = AuthConstants.Policies.TrainerAccess)]
public sealed class TrainerInvitationController : ControllerBase
{
    private readonly ICreateInvitationUseCase _createInvitation;
    private readonly ICreateInvitationByEmailUseCase _createInvitationByEmail;
    private readonly IListPaginatedInvitationsUseCase _listPaginatedInvitations;
    private readonly IRevokeInvitationUseCase _revokeInvitation;
    private readonly IMapper _mapper;

    public TrainerInvitationController(
        ICreateInvitationUseCase createInvitation,
        ICreateInvitationByEmailUseCase createInvitationByEmail,
        IListPaginatedInvitationsUseCase listPaginatedInvitations,
        IRevokeInvitationUseCase revokeInvitation,
        IMapper mapper)
    {
        _createInvitation = createInvitation;
        _createInvitationByEmail = createInvitationByEmail;
        _listPaginatedInvitations = listPaginatedInvitations;
        _revokeInvitation = revokeInvitation;
        _mapper = mapper;
    }

    [HttpPost("invitations")]
    [ApiIdempotency("/api/trainer/invitations", ApiIdempotencyScopeSource.AuthenticatedUser)]
    [ProducesResponseType(typeof(TrainerInvitationDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateInvitation([FromBody] CreateTrainerInvitationRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(request.TraineeId, out var traineeId))
        {
            return Result<InvitationReadModel, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        var trainer = HttpContext.GetCurrentUser();
        var command = _mapper.Map<CreateTrainerInvitationRequest, CreateInvitationCommand>(request) with
        {
            TrainerId = trainer!.Id,
            TraineeId = traineeId
        };
        var result = await _createInvitation.ExecuteAsync(command, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<InvitationReadModel, TrainerInvitationDto>(result.Value));
    }

    [HttpPost("invitations/paginated")]
    [ProducesResponseType(typeof(PaginatedTrainerInvitationResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInvitationsPaginated([FromBody] PaginatedTrainerInvitationRequest request, CancellationToken cancellationToken = default)
    {
        var trainer = HttpContext.GetCurrentUser();
        var query = new ListPaginatedInvitationsQuery(
            trainer!.Id,
            _mapper.Map<PaginatedTrainerInvitationRequest, FilterInput>(request));
        var result = await _listPaginatedInvitations.ExecuteAsync(query, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var pagination = result.Value;
        var response = new PaginatedTrainerInvitationResult
        {
            Items = _mapper.MapList<InvitationReadModel, TrainerInvitationDto>(pagination.Items),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalCount = pagination.TotalCount,
            TotalPages = pagination.TotalPages,
            HasNextPage = pagination.HasNextPage,
            HasPreviousPage = pagination.HasPreviousPage
        };
        return Ok(response);
    }

    [HttpPost("invitations/by-email")]
    [ProducesResponseType(typeof(TrainerInvitationDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateInvitationByEmail([FromBody] CreateTrainerInvitationByEmailRequest request, CancellationToken cancellationToken = default)
    {
        var trainer = HttpContext.GetCurrentUser();
        var command = _mapper.Map<CreateTrainerInvitationByEmailRequest, CreateInvitationByEmailCommand>(request) with { TrainerId = trainer!.Id };
        var result = await _createInvitationByEmail.ExecuteAsync(command, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<InvitationReadModel, TrainerInvitationDto>(result.Value));
    }

    [HttpPost("invitations/{invitationId}/revoke")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeInvitation([FromRoute] string invitationId, CancellationToken cancellationToken = default)
    {
        if (!Id<TrainerInvitationEntity>.TryParse(invitationId, out var parsedInvitationId))
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.FieldRequired)).ToActionResult();
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _revokeInvitation.ExecuteAsync(new RevokeInvitationCommand(trainer!.Id, parsedInvitationId), cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }
}
