using LgymApi.Api.Features.Public.Contracts;
using LgymApi.Application.Coaching.Invitations.PublicStatus;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TrainerInvitationEntity = LgymApi.Domain.Entities.TrainerInvitation;

namespace LgymApi.Api.Features.Public.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/invitations")]
public sealed class PublicInvitationController : ControllerBase
{
    private readonly IPublicInvitationStatusUseCase _getInvitationStatus;
    private readonly IMapper _mapper;

    public PublicInvitationController(IPublicInvitationStatusUseCase getInvitationStatus, IMapper mapper)
    {
        _getInvitationStatus = getInvitationStatus;
        _mapper = mapper;
    }

    [HttpGet("{invitationId}")]
    [ProducesResponseType(typeof(PublicInvitationStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvitationStatus([FromRoute] string invitationId, [FromQuery] string? code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return NotFound();
        }

        if (!Id<TrainerInvitationEntity>.TryParse(invitationId, out var parsedId))
        {
            return NotFound();
        }

        var result = await _getInvitationStatus.ExecuteAsync(new PublicInvitationStatusQuery(parsedId, code), cancellationToken);
        if (result.IsFailure)
        {
            return NotFound();
        }

        return Ok(_mapper.Map<PublicInvitationStatusReadModel, PublicInvitationStatusDto>(result.Value));
    }
}
