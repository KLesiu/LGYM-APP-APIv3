using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.User.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Notifications.Models;
using LgymApi.Notifications.ApiAdapters;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.User.Controllers;

[ApiController]
[Route("api/push/installations")]
public sealed class PushInstallationController : ControllerBase
{
    private readonly IPushInstallationApiAdapter _pushInstallationApiAdapter;
    private readonly IMapper _mapper;

    public PushInstallationController(IPushInstallationApiAdapter pushInstallationApiAdapter, IMapper mapper)
    {
        _pushInstallationApiAdapter = pushInstallationApiAdapter;
        _mapper = mapper;
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Register([FromBody] RegisterPushInstallationRequest request, CancellationToken cancellationToken = default)
    {
        var input = new RegisterPushInstallationInput(
            request.InstallationId,
            request.Platform,
            request.FcmToken,
            request.AppVersion,
            request.Environment,
            request.PermissionStatus);

        var accountContext = HttpContext.GetAuthenticatedAccountContext();
        var result = await _pushInstallationApiAdapter.RegisterAsync(
            accountContext?.Id,
            accountContext?.SessionId,
            input,
            cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("unregister")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Unregister([FromBody] PushInstallationActionRequest request, CancellationToken cancellationToken = default)
    {
        var accountContext = HttpContext.GetAuthenticatedAccountContext();
        var result = await _pushInstallationApiAdapter.UnregisterAsync(
            accountContext?.Id,
            accountContext?.SessionId,
            new PushInstallationActionInput(request.InstallationId),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("disassociate")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Disassociate([FromBody] PushInstallationActionRequest request, CancellationToken cancellationToken = default)
    {
        var accountContext = HttpContext.GetAuthenticatedAccountContext();
        var result = await _pushInstallationApiAdapter.DisassociateAsync(
            accountContext?.Id,
            accountContext?.SessionId,
            new PushInstallationActionInput(request.InstallationId),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }
}
