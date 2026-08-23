using LgymApi.Api.AgeGate;
using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Account.Contracts;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Mapping.Core;
using LgymApi.Identity.Contracts.AdultConfirmation;
using LgymApi.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.Account.Controllers;

[ApiController]
[Route("api/account")]
[Authorize]
public sealed class AdultConfirmationController : ControllerBase
{
    private readonly IAdultConfirmationService _adultConfirmationService;
    private readonly IMapper _mapper;

    public AdultConfirmationController(IAdultConfirmationService adultConfirmationService, IMapper mapper)
    {
        _adultConfirmationService = adultConfirmationService;
        _mapper = mapper;
    }

    [HttpPost("confirm-adult")]
    [AllowAgeGated]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmAdult(
        [FromBody] ConfirmAdultRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _adultConfirmationService.ConfirmAsync(
            HttpContext.GetCurrentAccountId(),
            request.AdultConfirmed == true,
            cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }
}
