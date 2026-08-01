using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.Pagination;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;

namespace LgymApi.Application.Features.AdminManagement;

internal sealed class AdminUserService : IAdminUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IUserSessionStore _userSessionStore;
    private readonly IUnitOfWork _unitOfWork;

    public AdminUserService(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IUserSessionStore userSessionStore,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _userSessionStore = userSessionStore;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Pagination<UserResult>, AppError>> GetUsersAsync(FilterInput filterInput, bool includeDeleted, CancellationToken cancellationToken = default)
    {
        var pagination = await _userRepository.GetUsersPaginatedAsync(filterInput, includeDeleted, cancellationToken);

        var userIds = pagination.Items.Select(x => x.Id).ToList();
        var rolesByUser = await _roleRepository.GetRoleNamesByUserIdsAsync(userIds, cancellationToken);

        var items = pagination.Items.Select(x => new UserResult
        {
            Id = x.Id,
            Name = x.Name,
            Email = x.Email,
            Avatar = x.Avatar,
            ProfileRank = x.ProfileRank,
            IsVisibleInRanking = x.IsVisibleInRanking,
            IsBlocked = x.IsBlocked,
            IsDeleted = x.IsDeleted,
            CreatedAt = x.CreatedAt,
            Roles = rolesByUser.TryGetValue(x.Id, out var roles) ? roles : new List<string>()
        }).ToList();

        return Result<Pagination<UserResult>, AppError>.Success(new Pagination<UserResult>
        {
            Items = items,
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalCount = pagination.TotalCount
        });
    }

    public async Task<Result<UserResult, AppError>> GetUserAsync(Id<global::LgymApi.Domain.Entities.User> userId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return Result<UserResult, AppError>.Failure(new InvalidAdminUserError(Messages.DidntFind));
        }

        var user = await _userRepository.FindByIdIncludingDeletedAsync(userId, cancellationToken);
        if (user == null)
        {
            return Result<UserResult, AppError>.Failure(new NotFoundError(Messages.DidntFind));
        }

        var roles = await _roleRepository.GetRoleNamesByUserIdAsync(userId, cancellationToken);

        return Result<UserResult, AppError>.Success(new UserResult
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Avatar = user.Avatar,
            ProfileRank = user.ProfileRank,
            IsVisibleInRanking = user.IsVisibleInRanking,
            IsBlocked = user.IsBlocked,
            IsDeleted = user.IsDeleted,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            Roles = roles
        });
    }

    public async Task<Result<Unit, AppError>> UpdateUserAsync(Id<global::LgymApi.Domain.Entities.User> targetUserId, Id<global::LgymApi.Domain.Entities.User> adminUserId, UpdateUserCommand command, CancellationToken cancellationToken = default)
    {
        if (targetUserId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidAdminUserError(Messages.DidntFind));
        }

        var user = await _userRepository.FindByIdIncludingDeletedAsync(targetUserId, cancellationToken);
        if (user == null)
        {
            return Result<Unit, AppError>.Failure(new NotFoundError(Messages.DidntFind));
        }

        var email = new Email(command.Email);
        var existingUserWithEmail = await _userRepository.FindByEmailAsync(email, cancellationToken);
        if (existingUserWithEmail != null && existingUserWithEmail.Id != targetUserId)
        {
            return Result<Unit, AppError>.Failure(new ConflictError(Messages.UserWithThatEmail));
        }

        user.Name = command.Name;
        user.Email = email;
        user.ProfileRank = command.ProfileRank;
        user.IsVisibleInRanking = command.IsVisibleInRanking;
        user.Avatar = command.Avatar;

        await _userRepository.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<Unit, AppError>> DeleteUserAsync(Id<global::LgymApi.Domain.Entities.User> targetUserId, Id<global::LgymApi.Domain.Entities.User> adminUserId, CancellationToken cancellationToken = default)
    {
        if (targetUserId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidAdminUserError(Messages.DidntFind));
        }

        if (targetUserId == adminUserId)
        {
            return Result<Unit, AppError>.Failure(new ForbiddenError(Messages.CannotDeleteSelf));
        }

        var user = await _userRepository.FindByIdAsync(targetUserId, cancellationToken);
        if (user == null)
        {
            return Result<Unit, AppError>.Failure(new NotFoundError(Messages.DidntFind));
        }

        user.IsDeleted = true;
        await _userRepository.UpdateAsync(user, cancellationToken);
        await _userSessionStore.RevokeAllUserSessionsAsync(targetUserId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<Unit, AppError>> BlockUserAsync(Id<global::LgymApi.Domain.Entities.User> targetUserId, Id<global::LgymApi.Domain.Entities.User> adminUserId, CancellationToken cancellationToken = default)
    {
        if (targetUserId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidAdminUserError(Messages.DidntFind));
        }

        if (targetUserId == adminUserId)
        {
            return Result<Unit, AppError>.Failure(new ForbiddenError(Messages.CannotBlockSelf));
        }

        var user = await _userRepository.FindByIdAsync(targetUserId, cancellationToken);
        if (user == null)
        {
            return Result<Unit, AppError>.Failure(new NotFoundError(Messages.DidntFind));
        }

        user.IsBlocked = true;
        await _userRepository.UpdateAsync(user, cancellationToken);
        await _userSessionStore.RevokeAllUserSessionsAsync(targetUserId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<Unit, AppError>> UnblockUserAsync(Id<global::LgymApi.Domain.Entities.User> targetUserId, CancellationToken cancellationToken = default)
    {
        if (targetUserId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidAdminUserError(Messages.DidntFind));
        }

        var user = await _userRepository.FindByIdAsync(targetUserId, cancellationToken);
        if (user == null)
        {
            return Result<Unit, AppError>.Failure(new NotFoundError(Messages.DidntFind));
        }

        user.IsBlocked = false;
        await _userRepository.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
