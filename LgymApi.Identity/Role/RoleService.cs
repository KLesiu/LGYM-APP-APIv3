using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Role.Models;
using LgymApi.Application.Pagination;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Domain.Security;
using LgymApi.Resources;

namespace LgymApi.Application.Features.Role;

internal sealed class RoleService : IRoleService
{
    private static readonly HashSet<string> SystemRoles =
    [
        AuthConstants.Roles.User,
        AuthConstants.Roles.Admin,
        AuthConstants.Roles.Tester
    ];

    private readonly IRoleRepository _roleRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RoleService(IRoleRepository roleRepository, IUserRepository userRepository, IUnitOfWork unitOfWork)
    {
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<List<RoleResult>, AppError>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _roleRepository.GetAllAsync(cancellationToken);
        var claimsByRole = await _roleRepository.GetPermissionClaimsByRoleIdsAsync(roles.Select(r => r.Id).ToList(), cancellationToken);

        var result = roles
            .Select(role => MapRole(
                role,
                claimsByRole.TryGetValue(role.Id, out var claims) ? claims : new List<string>()))
            .ToList();

        return Result<List<RoleResult>, AppError>.Success(result);
    }

    public async Task<Result<Pagination<RoleResult>, AppError>> GetRolesPaginatedAsync(FilterInput filterInput, CancellationToken cancellationToken = default)
    {
        var pagination = await _roleRepository.GetRolesPaginatedAsync(filterInput, cancellationToken);
        var roleIds = pagination.Items.Select(r => r.Id).ToList();
        var claimsByRole = await _roleRepository.GetPermissionClaimsByRoleIdsAsync(roleIds, cancellationToken);

        var items = pagination.Items
            .Select(role => MapRole(
                role,
                claimsByRole.TryGetValue(role.Id, out var claims) ? claims : new List<string>()))
            .ToList();

        return Result<Pagination<RoleResult>, AppError>.Success(new Pagination<RoleResult>
        {
            Items = items,
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalCount = pagination.TotalCount
        });
    }

    public async Task<Result<RoleResult, AppError>> GetRoleAsync(Id<Domain.Entities.Role> roleId, CancellationToken cancellationToken = default)
    {
        if (roleId.IsEmpty)
        {
            return Result<RoleResult, AppError>.Failure(new InvalidRoleError(Messages.FieldRequired));
        }

        var role = await _roleRepository.FindByIdAsync(roleId, cancellationToken);
        if (role == null)
        {
            return Result<RoleResult, AppError>.Failure(new RoleNotFoundError(Messages.DidntFind));
        }

        var permissionClaims = await _roleRepository.GetPermissionClaimsByRoleIdAsync(role.Id, cancellationToken);
        return Result<RoleResult, AppError>.Success(MapRole(role, permissionClaims));
    }

    public async Task<Result<RoleResult, AppError>> CreateRoleAsync(string name, string? description, IReadOnlyCollection<string> permissionClaims, CancellationToken cancellationToken = default)
    {
        var normalizedNameResult = NormalizeRoleName(name);
        if (normalizedNameResult.IsFailure)
        {
            return Result<RoleResult, AppError>.Failure(normalizedNameResult.Error);
        }

        var normalizedClaimsResult = NormalizeAndValidateClaims(permissionClaims);
        if (normalizedClaimsResult.IsFailure)
        {
            return Result<RoleResult, AppError>.Failure(normalizedClaimsResult.Error);
        }

        var normalizedName = normalizedNameResult.Value;
        var normalizedClaims = normalizedClaimsResult.Value;

        if (await _roleRepository.ExistsByNameAsync(normalizedName, cancellationToken: cancellationToken))
        {
            return Result<RoleResult, AppError>.Failure(new RoleAlreadyExistsError(Messages.RoleWithThatName));
        }

        var role = new Domain.Entities.Role
        {
            Id = Id<Domain.Entities.Role>.New(),
            Name = normalizedName,
            Description = NormalizeDescription(description)
        };

        await _roleRepository.AddRoleAsync(role, cancellationToken);
        await _roleRepository.ReplaceRolePermissionClaimsAsync(role.Id, normalizedClaims, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<RoleResult, AppError>.Success(new RoleResult
        {
            Id = role.Id,
            Name = role.Name,
            Description = role.Description,
            PermissionClaims = normalizedClaims.ToList()
        });
    }

    public async Task<Result<Unit, AppError>> UpdateRoleAsync(Id<Domain.Entities.Role> roleId, string name, string? description, IReadOnlyCollection<string> permissionClaims, CancellationToken cancellationToken = default)
    {
        if (roleId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidRoleError(Messages.FieldRequired));
        }

        var role = await _roleRepository.FindByIdAsync(roleId, cancellationToken);
        if (role == null)
        {
            return Result<Unit, AppError>.Failure(new RoleNotFoundError(Messages.DidntFind));
        }

        if (SystemRoles.Contains(role.Name))
        {
            return Result<Unit, AppError>.Failure(new RoleForbiddenError(Messages.Forbidden));
        }

        var normalizedNameResult = NormalizeRoleName(name);
        if (normalizedNameResult.IsFailure)
        {
            return Result<Unit, AppError>.Failure(normalizedNameResult.Error);
        }

        var normalizedClaimsResult = NormalizeAndValidateClaims(permissionClaims);
        if (normalizedClaimsResult.IsFailure)
        {
            return Result<Unit, AppError>.Failure(normalizedClaimsResult.Error);
        }

        var normalizedName = normalizedNameResult.Value;
        var normalizedClaims = normalizedClaimsResult.Value;

        if (await _roleRepository.ExistsByNameAsync(normalizedName, roleId, cancellationToken))
        {
            return Result<Unit, AppError>.Failure(new RoleAlreadyExistsError(Messages.RoleWithThatName));
        }

        role.Name = normalizedName;
        role.Description = NormalizeDescription(description);

        await _roleRepository.UpdateRoleAsync(role, cancellationToken);
        await _roleRepository.ReplaceRolePermissionClaimsAsync(role.Id, normalizedClaims, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<Unit, AppError>> DeleteRoleAsync(Id<Domain.Entities.Role> roleId, CancellationToken cancellationToken = default)
    {
        if (roleId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidRoleError(Messages.FieldRequired));
        }

        var role = await _roleRepository.FindByIdAsync(roleId, cancellationToken);
        if (role == null)
        {
            return Result<Unit, AppError>.Failure(new RoleNotFoundError(Messages.DidntFind));
        }

        if (SystemRoles.Contains(role.Name))
        {
            return Result<Unit, AppError>.Failure(new RoleForbiddenError(Messages.Forbidden));
        }

        await _roleRepository.DeleteRoleAsync(role, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public List<PermissionClaimLookupResult> GetAvailablePermissionClaims()
    {
        return AuthConstants.Permissions.All
            .OrderBy(claim => claim, StringComparer.Ordinal)
            .Select(claim => new PermissionClaimLookupResult
            {
                ClaimType = AuthConstants.PermissionClaimType,
                ClaimValue = claim,
                DisplayName = claim switch
                {
                    AuthConstants.Permissions.AdminAccess => Messages.PermissionAdminAccess,
                    AuthConstants.Permissions.ManageUserRoles => Messages.PermissionManageUserRoles,
                    AuthConstants.Permissions.ManageAppConfig => Messages.PermissionManageAppConfig,
                    AuthConstants.Permissions.ManageGlobalExercises => Messages.PermissionManageGlobalExercises,
                    AuthConstants.Permissions.TrainerAccess => Messages.PermissionTrainerAccess,
                    _ => claim
                }
            })
            .ToList();
    }

    public async Task<Result<Unit, AppError>> UpdateUserRolesAsync(Id<LgymApi.Domain.Entities.User> userId, IReadOnlyCollection<string> roleNames, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidRoleError(Messages.FieldRequired));
        }

        var user = await _userRepository.FindByIdAsync((Id<LgymApi.Domain.Entities.User>)userId, cancellationToken);
        if (user == null)
        {
            return Result<Unit, AppError>.Failure(new RoleNotFoundError(Messages.DidntFind));
        }

        var normalizedRoleNames = roleNames
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rolesToSet = await _roleRepository.GetByNamesAsync(normalizedRoleNames, cancellationToken);
        if (rolesToSet.Count != normalizedRoleNames.Count)
        {
            return Result<Unit, AppError>.Failure(new InvalidRoleError(Messages.InvalidRoleSelection));
        }

        await _roleRepository.ReplaceUserRolesAsync(userId, rolesToSet.Select(r => r.Id).ToList(), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    private static RoleResult MapRole(Domain.Entities.Role role, List<string> permissionClaims)
    {
        return new RoleResult
        {
            Id = role.Id,
            Name = role.Name,
            Description = role.Description,
            PermissionClaims = permissionClaims
        };
    }

    private static Result<string, AppError> NormalizeRoleName(string name)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return Result<string, AppError>.Failure(new InvalidRoleError(Messages.FieldRequired));
        }

        return Result<string, AppError>.Success(normalizedName);
    }

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static Result<List<string>, AppError> NormalizeAndValidateClaims(IReadOnlyCollection<string> permissionClaims)
    {
        var normalizedClaims = permissionClaims
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        if (normalizedClaims.Any(c => !AuthConstants.Permissions.All.Contains(c, StringComparer.Ordinal)))
        {
            return Result<List<string>, AppError>.Failure(new InvalidRoleError(Messages.InvalidPermissionClaims));
        }

        return Result<List<string>, AppError>.Success(normalizedClaims);
    }
}
