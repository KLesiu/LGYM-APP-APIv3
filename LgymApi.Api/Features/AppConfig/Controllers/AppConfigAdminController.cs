using LgymApi.Api.Extensions;
using LgymApi.Api.Features.AppConfig.Contracts;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Platform.ReferenceData.AppConfig;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Pagination;
using LgymApi.Application.Task7ApiCompatibility;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AppConfigEntity = LgymApi.Domain.Entities.AppConfig;

namespace LgymApi.Api.Features.AppConfig.Controllers;

[ApiController]
[Route("api/appconfig")]
[Authorize(Policy = AuthConstants.Policies.ManageAppConfig)]
public sealed class AppConfigAdminController : ControllerBase
{
    private readonly IAppConfigApiCompatibilityAdapter _appConfigApiCompatibility;
    private readonly IMapper _mapper;

    public AppConfigAdminController(IAppConfigApiCompatibilityAdapter appConfigApiCompatibility, IMapper mapper)
    {
        _appConfigApiCompatibility = appConfigApiCompatibility;
        _mapper = mapper;
    }

    [HttpPost("paginated")]
    [ProducesResponseType(typeof(PaginatedAppConfigResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaginated([FromBody] PaginatedAppConfigRequest request, CancellationToken cancellationToken = default)
    {
        var filterInput = new FilterInput
        {
            Page = request.Page,
            PageSize = request.PageSize,
            FilterGroups = request.FilterGroups,
            SortDescriptors = request.SortDescriptors
        };
        var accountId = HttpContext.GetCurrentAccountId();
        var result = await _appConfigApiCompatibility.GetPaginatedAsync(accountId, filterInput, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var pagination = result.Value;
        var response = new PaginatedAppConfigResult
        {
            Items = _mapper.MapList<AppConfigEntity, AppConfigDetailDto>(pagination.Items),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalCount = pagination.TotalCount,
            TotalPages = pagination.TotalPages,
            HasNextPage = pagination.HasNextPage,
            HasPreviousPage = pagination.HasPreviousPage
        };

        return Ok(response);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(AppConfigDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var configId = Id<AppConfigEntity>.TryParse(id, out var parsedConfigId) ? parsedConfigId : Id<AppConfigEntity>.Empty;
        var accountId = HttpContext.GetCurrentAccountId();
        var result = await _appConfigApiCompatibility.GetByIdAsync(accountId, configId, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<AppConfigEntity, AppConfigDetailDto>(result.Value));
    }

    [HttpPost("{id}/update")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] UpdateAppConfigRequest request, CancellationToken cancellationToken = default)
    {
        var configId = Id<AppConfigEntity>.TryParse(id, out var parsedConfigId) ? parsedConfigId : Id<AppConfigEntity>.Empty;
        var accountId = HttpContext.GetCurrentAccountId();
        var input = new UpdateAppConfigInput(
            request.Platform,
            request.MinRequiredVersion,
            request.LatestVersion,
            request.ForceUpdate,
            request.UpdateUrl,
            request.ReleaseNotes);
        var result = await _appConfigApiCompatibility.UpdateAsync(accountId, configId, input, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("{id}/delete")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var configId = Id<AppConfigEntity>.TryParse(id, out var parsedConfigId) ? parsedConfigId : Id<AppConfigEntity>.Empty;
        var accountId = HttpContext.GetCurrentAccountId();
        var result = await _appConfigApiCompatibility.DeleteAsync(accountId, configId, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }
}
