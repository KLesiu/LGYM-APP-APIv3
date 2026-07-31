using LgymApi.Api.Extensions;
using LgymApi.Api.Features.AppConfig.Contracts;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Platform.ReferenceData.AppConfig;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Platform.ReferenceData.ApiAdapters;
using LgymApi.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AppConfigEntity = LgymApi.Domain.Entities.AppConfig;

namespace LgymApi.Api.Features.AppConfig.Controllers;

[ApiController]
[Route("api")]
public sealed class AppConfigController : ControllerBase
{
    private readonly IAppConfigService _appConfigService;
    private readonly IAppConfigApiAdapter _appConfigApiAdapter;
    private readonly IMapper _mapper;

    public AppConfigController(
        IAppConfigService appConfigService,
        IAppConfigApiAdapter appConfigApiAdapter,
        IMapper mapper)
    {
        _appConfigService = appConfigService;
        _appConfigApiAdapter = appConfigApiAdapter;
        _mapper = mapper;
    }

    [HttpPost("appConfig/getAppVersion")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AppConfigInfoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAppVersion([FromBody] AppConfigVersionRequestDto request, CancellationToken cancellationToken = default)
    {
        var result = await _appConfigService.GetLatestByPlatformAsync(request.Platform, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<AppConfigEntity, AppConfigInfoDto>(result.Value));
    }

    [HttpPost("appConfig/createNewAppVersion/{id}")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateNewAppVersion([FromRoute] string id, [FromBody] AppConfigInfoWithPlatformDto form, CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.ParseRouteAccountIdForCurrentAdmin(id);
        var input = new CreateAppVersionInput(
            form.Platform,
            form.MinRequiredVersion,
            form.LatestVersion,
            form.ForceUpdate,
            form.UpdateUrl,
            form.ReleaseNotes);
        var result = await _appConfigApiAdapter.CreateNewAppVersionAsync(accountId, input, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return StatusCode(StatusCodes.Status201Created, _mapper.Map<string, ResponseMessageDto>(Messages.Created));
    }
}
