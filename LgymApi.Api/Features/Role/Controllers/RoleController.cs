using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Role.Contracts;
using LgymApi.Application.Identity.ApiAdapters;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Pagination;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.Role.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize(Policy = AuthConstants.Policies.ManageUserRoles)]
public sealed class RoleController : ControllerBase
{
    private readonly IRoleManagementApiAdapter _roleManagementApiAdapter;
    private readonly IMapper _mapper;

    public RoleController(IRoleManagementApiAdapter roleManagementApiAdapter, IMapper mapper)
    {
        _roleManagementApiAdapter = roleManagementApiAdapter;
        _mapper = mapper;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<RoleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoles(CancellationToken cancellationToken = default)
    {
        var result = await _roleManagementApiAdapter.GetRolesAsync(cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.MapList<RoleProjection, RoleDto>(result.Value));
    }

    [HttpPost("paginated")]
    [ProducesResponseType(typeof(PaginatedRoleResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRolesPaginated([FromBody] PaginatedRoleRequest request, CancellationToken cancellationToken = default)
    {
        var filterInput = new FilterInput
        {
            Page = request.Page,
            PageSize = request.PageSize,
            FilterGroups = request.FilterGroups,
            SortDescriptors = request.SortDescriptors
        };
        var result = await _roleManagementApiAdapter.GetRolesPaginatedAsync(filterInput, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var pagination = result.Value;
        var response = new PaginatedRoleResult
        {
            Items = _mapper.MapList<RoleProjection, RoleDto>(pagination.Items),
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
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRole([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var roleId = Id<RoleReference>.TryParse(id, out var parsedRoleId) ? parsedRoleId : Id<RoleReference>.Empty;
        var result = await _roleManagementApiAdapter.GetRoleAsync(roleId, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<RoleProjection, RoleDto>(result.Value));
    }

    [HttpGet("permission-claims")]
    [ProducesResponseType(typeof(List<PermissionClaimLookupDto>), StatusCodes.Status200OK)]
    public IActionResult GetPermissionClaims()
    {
        var claims = _roleManagementApiAdapter.GetAvailablePermissionClaims();
        return Ok(_mapper.MapList<PermissionClaimProjection, PermissionClaimLookupDto>(claims));
    }

    [HttpPost]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRole([FromBody] UpsertRoleRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _roleManagementApiAdapter.CreateRoleAsync(request.Name, request.Description, request.PermissionClaims, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<RoleProjection, RoleDto>(result.Value));
    }

    [HttpPost("{id}/update")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRole([FromRoute] string id, [FromBody] UpsertRoleRequest request, CancellationToken cancellationToken = default)
    {
        var roleId = Id<RoleReference>.TryParse(id, out var parsedRoleId) ? parsedRoleId : Id<RoleReference>.Empty;
        var result = await _roleManagementApiAdapter.UpdateRoleAsync(roleId, request.Name, request.Description, request.PermissionClaims, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("{id}/delete")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRole([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var roleId = Id<RoleReference>.TryParse(id, out var parsedRoleId) ? parsedRoleId : Id<RoleReference>.Empty;
        var result = await _roleManagementApiAdapter.DeleteRoleAsync(roleId, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }

    [HttpPost("users/{id}/roles")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUserRoles([FromRoute] string id, [FromBody] UpdateUserRolesRequest request, CancellationToken cancellationToken = default)
    {
        var userId = Id<AccountReference>.TryParse(id, out var parsedUserId) ? parsedUserId : Id<AccountReference>.Empty;
        var result = await _roleManagementApiAdapter.UpdateUserRolesAsync(userId, request.Roles, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

}
