using LgymApi.Api.Extensions;
using LgymApi.Api.Features.AdminManagement.Contracts;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Identity.ApiAdapters;
using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Pagination;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.AdminManagement.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = AuthConstants.Policies.AdminAccess)]
public sealed class AdminUserController : ControllerBase
{
    private const string InvalidUserIdMessage = "Invalid user id.";

    private readonly IAdminAccountManagementApiAdapter _adminAccountManagementApiAdapter;
    private readonly IMapper _mapper;

    public AdminUserController(IAdminAccountManagementApiAdapter adminAccountManagementApiAdapter, IMapper mapper)
    {
        _adminAccountManagementApiAdapter = adminAccountManagementApiAdapter;
        _mapper = mapper;
    }

    [HttpPost("paginated")]
    [ProducesResponseType(typeof(PaginatedAdminUserResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsersPaginated([FromBody] PaginatedUserRequest request, CancellationToken cancellationToken = default)
    {
        var filterInput = new FilterInput
        {
            Page = request.Page,
            PageSize = request.PageSize,
            FilterGroups = request.FilterGroups,
            SortDescriptors = request.SortDescriptors
        };
        var result = await _adminAccountManagementApiAdapter.GetUsersAsync(filterInput, request.IncludeDeleted, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var pagination = result.Value;
        var response = new PaginatedAdminUserResult
        {
            Items = _mapper.MapList<AdminAccountProjection, AdminUserDto>(pagination.Items),
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
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUser([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        if (!TryParseUserId(id, out var userId, out var errorResult))
        {
            return errorResult;
        }

        var result = await _adminAccountManagementApiAdapter.GetUserAsync(userId, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<AdminAccountProjection, AdminUserDto>(result.Value));
    }

    [HttpPost("{id}/update")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUser([FromRoute] string id, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryParseUserId(id, out var targetUserId, out var errorResult))
        {
            return errorResult;
        }

        var adminUserId = GetAdminUserId();
        var command = new UpdateUserCommand
        {
            Name = request.Name,
            Email = request.Email,
            ProfileRank = request.ProfileRank,
            IsVisibleInRanking = request.IsVisibleInRanking,
            Avatar = request.Avatar
        };

        var result = await _adminAccountManagementApiAdapter.UpdateUserAsync(targetUserId, adminUserId, command, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("{id}/delete")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUser([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        if (!TryParseUserId(id, out var targetUserId, out var errorResult))
        {
            return errorResult;
        }

        var adminUserId = GetAdminUserId();
        var result = await _adminAccountManagementApiAdapter.DeleteUserAsync(targetUserId, adminUserId, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }

    [HttpPost("{id}/block")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BlockUser([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        if (!TryParseUserId(id, out var targetUserId, out var errorResult))
        {
            return errorResult;
        }

        var adminUserId = GetAdminUserId();
        var result = await _adminAccountManagementApiAdapter.BlockUserAsync(targetUserId, adminUserId, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("{id}/unblock")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnblockUser([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        if (!TryParseUserId(id, out var targetUserId, out var errorResult))
        {
            return errorResult;
        }

        var result = await _adminAccountManagementApiAdapter.UnblockUserAsync(targetUserId, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    private Id<AccountReference> GetAdminUserId()
        => HttpContext.GetAuthenticatedAccountContext()?.Id ?? Id<AccountReference>.Empty;

    private bool TryParseUserId(string id, out Id<AccountReference> userId, out IActionResult errorResult)
    {
        if (Id<AccountReference>.TryParse(id, out userId))
        {
            errorResult = null!;
            return true;
        }

        errorResult = BadRequest(_mapper.Map<string, ResponseMessageDto>(InvalidUserIdMessage));
        return false;
    }
}
