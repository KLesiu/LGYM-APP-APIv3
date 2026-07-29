using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.AdminManagement;
using LgymApi.Application.Features.Role;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Pagination;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Identity.ApiCompatibility;

internal sealed class AdminAccountManagementApiAdapter : IAdminAccountManagementApiAdapter
{
    private readonly IAdminUserService _adminUserService;
    private readonly IMapper _mapper;

    public AdminAccountManagementApiAdapter(IAdminUserService adminUserService, IMapper mapper)
    {
        _adminUserService = adminUserService;
        _mapper = mapper;
    }

    public async Task<Result<Pagination<AdminAccountProjection>, AppError>> GetUsersAsync(FilterInput filterInput, bool includeDeleted, CancellationToken cancellationToken = default)
    {
        var result = await _adminUserService.GetUsersAsync(filterInput, includeDeleted, cancellationToken);
        if (result.IsFailure)
        {
            return Result<Pagination<AdminAccountProjection>, AppError>.Failure(result.Error);
        }

        var pagination = result.Value;
        return Result<Pagination<AdminAccountProjection>, AppError>.Success(new Pagination<AdminAccountProjection>
        {
            Items = _mapper.MapList<Features.AdminManagement.Models.UserResult, AdminAccountProjection>(pagination.Items),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalCount = pagination.TotalCount
        });
    }

    public async Task<Result<AdminAccountProjection, AppError>> GetUserAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
    {
        var result = await _adminUserService.GetUserAsync(accountId.Rebind<User>(), cancellationToken);
        return result.IsFailure
            ? Result<AdminAccountProjection, AppError>.Failure(result.Error)
            : Result<AdminAccountProjection, AppError>.Success(_mapper.Map<Features.AdminManagement.Models.UserResult, AdminAccountProjection>(result.Value));
    }

    public Task<Result<Unit, AppError>> UpdateUserAsync(Id<AccountReference> targetAccountId, Id<AccountReference> administratorAccountId, Features.AdminManagement.Models.UpdateUserCommand command, CancellationToken cancellationToken = default)
        => _adminUserService.UpdateUserAsync(targetAccountId.Rebind<User>(), administratorAccountId.Rebind<User>(), command, cancellationToken);

    public Task<Result<Unit, AppError>> DeleteUserAsync(Id<AccountReference> targetAccountId, Id<AccountReference> administratorAccountId, CancellationToken cancellationToken = default)
        => _adminUserService.DeleteUserAsync(targetAccountId.Rebind<User>(), administratorAccountId.Rebind<User>(), cancellationToken);

    public Task<Result<Unit, AppError>> BlockUserAsync(Id<AccountReference> targetAccountId, Id<AccountReference> administratorAccountId, CancellationToken cancellationToken = default)
        => _adminUserService.BlockUserAsync(targetAccountId.Rebind<User>(), administratorAccountId.Rebind<User>(), cancellationToken);

    public Task<Result<Unit, AppError>> UnblockUserAsync(Id<AccountReference> targetAccountId, CancellationToken cancellationToken = default)
        => _adminUserService.UnblockUserAsync(targetAccountId.Rebind<User>(), cancellationToken);
}

internal sealed class RoleManagementApiAdapter : IRoleManagementApiAdapter
{
    private readonly IRoleService _roleService;
    private readonly IMapper _mapper;

    public RoleManagementApiAdapter(IRoleService roleService, IMapper mapper)
    {
        _roleService = roleService;
        _mapper = mapper;
    }

    public async Task<Result<IReadOnlyList<RoleProjection>, AppError>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _roleService.GetRolesAsync(cancellationToken);
        return result.IsFailure
            ? Result<IReadOnlyList<RoleProjection>, AppError>.Failure(result.Error)
            : Result<IReadOnlyList<RoleProjection>, AppError>.Success(_mapper.MapList<Features.Role.Models.RoleResult, RoleProjection>(result.Value));
    }

    public async Task<Result<Pagination<RoleProjection>, AppError>> GetRolesPaginatedAsync(FilterInput filterInput, CancellationToken cancellationToken = default)
    {
        var result = await _roleService.GetRolesPaginatedAsync(filterInput, cancellationToken);
        if (result.IsFailure)
        {
            return Result<Pagination<RoleProjection>, AppError>.Failure(result.Error);
        }

        var pagination = result.Value;
        return Result<Pagination<RoleProjection>, AppError>.Success(new Pagination<RoleProjection>
        {
            Items = _mapper.MapList<Features.Role.Models.RoleResult, RoleProjection>(pagination.Items),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalCount = pagination.TotalCount
        });
    }

    public async Task<Result<RoleProjection, AppError>> GetRoleAsync(Id<RoleReference> roleId, CancellationToken cancellationToken = default)
    {
        var result = await _roleService.GetRoleAsync(roleId.Rebind<Role>(), cancellationToken);
        return result.IsFailure
            ? Result<RoleProjection, AppError>.Failure(result.Error)
            : Result<RoleProjection, AppError>.Success(_mapper.Map<Features.Role.Models.RoleResult, RoleProjection>(result.Value));
    }

    public async Task<Result<RoleProjection, AppError>> CreateRoleAsync(string name, string? description, IReadOnlyCollection<string> permissionClaims, CancellationToken cancellationToken = default)
    {
        var result = await _roleService.CreateRoleAsync(name, description, permissionClaims, cancellationToken);
        return result.IsFailure
            ? Result<RoleProjection, AppError>.Failure(result.Error)
            : Result<RoleProjection, AppError>.Success(_mapper.Map<Features.Role.Models.RoleResult, RoleProjection>(result.Value));
    }

    public Task<Result<Unit, AppError>> UpdateRoleAsync(Id<RoleReference> roleId, string name, string? description, IReadOnlyCollection<string> permissionClaims, CancellationToken cancellationToken = default)
        => _roleService.UpdateRoleAsync(roleId.Rebind<Role>(), name, description, permissionClaims, cancellationToken);

    public Task<Result<Unit, AppError>> DeleteRoleAsync(Id<RoleReference> roleId, CancellationToken cancellationToken = default)
        => _roleService.DeleteRoleAsync(roleId.Rebind<Role>(), cancellationToken);

    public IReadOnlyList<PermissionClaimProjection> GetAvailablePermissionClaims()
        => _mapper.MapList<Features.Role.Models.PermissionClaimLookupResult, PermissionClaimProjection>(_roleService.GetAvailablePermissionClaims());

    public Task<Result<Unit, AppError>> UpdateUserRolesAsync(Id<AccountReference> accountId, IReadOnlyCollection<string> roleNames, CancellationToken cancellationToken = default)
        => _roleService.UpdateUserRolesAsync(accountId.Rebind<User>(), roleNames, cancellationToken);
}
