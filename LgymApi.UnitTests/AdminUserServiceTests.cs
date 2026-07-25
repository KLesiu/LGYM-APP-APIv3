using System.Net;
using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.Features.AdminManagement;
using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.Models;
using LgymApi.Application.Pagination;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class AdminUserServiceTests
{
    private AdminUserService _service = null!;
    private IUserRepository _userRepository = null!;
    private IRoleRepository _roleRepository = null!;
    private IUserSessionStore _sessionStore = null!;
    private IUnitOfWork _unitOfWork = null!;
    private List<Id<User>> _revokedAllUserIds = null!;
    private int _saveChangesCalls;

    [SetUp]
    public void SetUp()
    {
        _revokedAllUserIds = new List<Id<User>>();

        _userRepository = Substitute.For<IUserRepository>();
        _roleRepository = Substitute.For<IRoleRepository>();
        _sessionStore = Substitute.For<IUserSessionStore>();
        _unitOfWork = Substitute.For<IUnitOfWork>();

        _sessionStore.CreateSessionAsync(Arg.Any<Id<User>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(new UserSession
            {
                Id = Id<UserSession>.New(),
                UserId = ci.Arg<Id<User>>(),
                Jti = Id<UserSession>.New().ToString(),
                ExpiresAtUtc = ci.Arg<DateTimeOffset>(),
                RevokedAtUtc = null
            }));
        _sessionStore.RevokeAllUserSessionsAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _sessionStore.When(x => x.RevokeAllUserSessionsAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>()))
            .Do(ci => _revokedAllUserIds.Add(ci.Arg<Id<User>>()));

        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                _saveChangesCalls++;
                return Task.FromResult(1);
            });

        _service = new AdminUserService(_userRepository, _roleRepository, _sessionStore, _unitOfWork);
    }

    [Test]
    public async Task Should_ReturnUserWithRoles_When_UserExists()
    {
        var userId = Id<User>.New();
        var roleId = Id<Role>.New();
        var user = new User { Id = (Domain.ValueObjects.Id<User>)userId, Name = "Test", Email = new Email("test@test.com") };
        var role = new Role { Id = (Domain.ValueObjects.Id<Role>)roleId, Name = "Admin" };

        _userRepository.FindByIdIncludingDeletedAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        _roleRepository.GetRoleNamesByUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new List<string> { role.Name });

        var result = await _service.GetUserAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Test");
        result.Value.Roles.Should().HaveCount(1);
        result.Value.Roles[0].Should().Be("Admin");
    }

    [Test]
    public async Task Should_ReturnFailure_When_UserNotFound()
    {
        var result = await _service.GetUserAsync(Id<User>.New());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<NotFoundError>();
    }

    [Test]
    public async Task Should_BlockUserAndRevokeAllSessions_When_Called()
    {
        var userId = Id<User>.New();
        var adminId = Id<User>.New();
        var user = new User { Id = (Domain.ValueObjects.Id<User>)userId, Name = "Test", Email = new Email("test@test.com") };
        _userRepository.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);

        var result = await _service.BlockUserAsync(userId, adminId);

        result.IsSuccess.Should().BeTrue();
        user.IsBlocked.Should().BeTrue();
        _revokedAllUserIds.Should().Contain(userId);
        _saveChangesCalls.Should().Be(1);
    }

    [Test]
    public async Task Should_ReturnFailure_When_BlockingSelf()
    {
        var userId = Id<User>.New();
        _userRepository.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new User { Id = (Domain.ValueObjects.Id<User>)userId, Name = "Test", Email = new Email("test@test.com") });

        var result = await _service.BlockUserAsync(userId, userId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ForbiddenError>();
    }

    [Test]
    public async Task Should_UnblockUser_When_Called()
    {
        var userId = Id<User>.New();
        var user = new User { Id = (Domain.ValueObjects.Id<User>)userId, Name = "Test", Email = new Email("test@test.com"), IsBlocked = true };
        _userRepository.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);

        var result = await _service.UnblockUserAsync(userId);

        result.IsSuccess.Should().BeTrue();
        user.IsBlocked.Should().BeFalse();
    }

    [Test]
    public async Task Should_SoftDeleteUserAndRevokeAllSessions_When_Called()
    {
        var userId = Id<User>.New();
        var adminId = Id<User>.New();
        var user = new User { Id = (Domain.ValueObjects.Id<User>)userId, Name = "Test", Email = new Email("test@test.com") };
        _userRepository.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);

        var result = await _service.DeleteUserAsync(userId, adminId);

        result.IsSuccess.Should().BeTrue();
        user.IsDeleted.Should().BeTrue();
        _revokedAllUserIds.Should().Contain(userId);
    }

    [Test]
    public async Task Should_ReturnFailure_When_DeletingSelf()
    {
        var userId = Id<User>.New();
        _userRepository.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new User { Id = (Domain.ValueObjects.Id<User>)userId, Name = "Test", Email = new Email("test@test.com") });

        var result = await _service.DeleteUserAsync(userId, userId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ForbiddenError>();
    }

    [Test]
    public async Task Should_UpdateFields_When_Called()
    {
        var userId = Id<User>.New();
        var user = new User { Id = (Domain.ValueObjects.Id<User>)userId, Name = "Old", Email = new Email("old@test.com") };
        _userRepository.FindByIdIncludingDeletedAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        _userRepository.FindByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var command = new UpdateUserCommand { Name = "New", Email = "new@test.com", IsVisibleInRanking = false };
        var result = await _service.UpdateUserAsync(userId, Id<User>.New(), command);

        result.IsSuccess.Should().BeTrue();
        user.Name.Should().Be("New");
        user.IsVisibleInRanking.Should().BeFalse();
    }

    [Test]
    public async Task Should_ReturnConflict_When_EmailAlreadyTaken()
    {
        var userId = Id<User>.New();
        var otherId = Id<User>.New();
        var user = new User { Id = (Domain.ValueObjects.Id<User>)userId, Name = "Test", Email = new Email("test@test.com") };
        var otherUser = new User { Id = (Domain.ValueObjects.Id<User>)otherId, Name = "Other", Email = new Email("other@test.com") };

        _userRepository.FindByIdIncludingDeletedAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        _userRepository.FindByEmailAsync(new Email("other@test.com"), Arg.Any<CancellationToken>()).Returns(otherUser);

        var command = new UpdateUserCommand { Name = "Test", Email = "other@test.com" };
        var result = await _service.UpdateUserAsync(userId, Id<User>.New(), command);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ConflictError>();
    }

    [Test]
    public async Task Should_ReturnPaginatedResults_When_Called()
    {
        var user1 = new User { Id = (Domain.ValueObjects.Id<User>)Id<User>.New(), Name = "User1", Email = new Email("u1@test.com") };
        var user2 = new User { Id = (Domain.ValueObjects.Id<User>)Id<User>.New(), Name = "User2", Email = new Email("u2@test.com") };

        _userRepository.GetUsersPaginatedAsync(Arg.Any<FilterInput>(), false, Arg.Any<CancellationToken>()).Returns(new Pagination<UserResult>
        {
            Items = new List<UserResult>
            {
                new() { Id = user1.Id, Name = user1.Name, Email = user1.Email, Avatar = user1.Avatar, ProfileRank = user1.ProfileRank, IsVisibleInRanking = user1.IsVisibleInRanking, IsBlocked = user1.IsBlocked, IsDeleted = user1.IsDeleted, CreatedAt = user1.CreatedAt },
                new() { Id = user2.Id, Name = user2.Name, Email = user2.Email, Avatar = user2.Avatar, ProfileRank = user2.ProfileRank, IsVisibleInRanking = user2.IsVisibleInRanking, IsBlocked = user2.IsBlocked, IsDeleted = user2.IsDeleted, CreatedAt = user2.CreatedAt }
            },
            Page = 1,
            PageSize = 10,
            TotalCount = 2
        });
        _roleRepository.GetRoleNamesByUserIdsAsync(Arg.Any<IReadOnlyCollection<Id<User>>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Id<User>, List<string>>());

        var result = await _service.GetUsersAsync(new FilterInput { Page = 1, PageSize = 10 }, includeDeleted: false);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.TotalCount.Should().Be(2);
    }

    [Test]
    public async Task Should_ReturnDeletedUsers_When_IncludeDeletedIsTrue()
    {
        var activeUser = new User { Id = (Domain.ValueObjects.Id<User>)Id<User>.New(), Name = "Active", Email = new Email("active@test.com"), IsDeleted = false };
        var deletedUser = new User { Id = (Domain.ValueObjects.Id<User>)Id<User>.New(), Name = "Deleted", Email = new Email("deleted@test.com"), IsDeleted = true };

        _userRepository.GetUsersPaginatedAsync(Arg.Any<FilterInput>(), true, Arg.Any<CancellationToken>()).Returns(new Pagination<UserResult>
        {
            Items = new List<UserResult>
            {
                new() { Id = activeUser.Id, Name = activeUser.Name, Email = activeUser.Email, Avatar = activeUser.Avatar, ProfileRank = activeUser.ProfileRank, IsVisibleInRanking = activeUser.IsVisibleInRanking, IsBlocked = activeUser.IsBlocked, IsDeleted = activeUser.IsDeleted, CreatedAt = activeUser.CreatedAt },
                new() { Id = deletedUser.Id, Name = deletedUser.Name, Email = deletedUser.Email, Avatar = deletedUser.Avatar, ProfileRank = deletedUser.ProfileRank, IsVisibleInRanking = deletedUser.IsVisibleInRanking, IsBlocked = deletedUser.IsBlocked, IsDeleted = deletedUser.IsDeleted, CreatedAt = deletedUser.CreatedAt }
            },
            Page = 1,
            PageSize = 10,
            TotalCount = 2
        });
        _roleRepository.GetRoleNamesByUserIdsAsync(Arg.Any<IReadOnlyCollection<Id<User>>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Id<User>, List<string>>());

        var resultWithDeleted = await _service.GetUsersAsync(new FilterInput { Page = 1, PageSize = 10 }, includeDeleted: true);

        resultWithDeleted.IsSuccess.Should().BeTrue();
        resultWithDeleted.Value.Items.Should().HaveCount(2);
    }

    [Test]
    public async Task Should_ReturnDeletedUser_When_IncludeDeletedIsTrue()
    {
        var userId = Id<User>.New();
        var user = new User { Id = (Domain.ValueObjects.Id<User>)userId, Name = "Deleted", Email = new Email("deleted@test.com"), IsDeleted = true };
        _userRepository.FindByIdIncludingDeletedAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        _roleRepository.GetRoleNamesByUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new List<string>());

        var result = await _service.GetUserAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsDeleted.Should().BeTrue();
    }

    [Test]
    public async Task Should_ReturnNotFound_When_UserNotFoundForUpdate()
    {
        var command = new UpdateUserCommand { Name = "Test", Email = "test@test.com" };
        var result = await _service.UpdateUserAsync(Id<User>.New(), Id<User>.New(), command);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<NotFoundError>();
    }

    [Test]
    public async Task Should_ReturnNotFound_When_UserNotFoundForDelete()
    {
        var result = await _service.DeleteUserAsync(Id<User>.New(), Id<User>.New());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<NotFoundError>();
    }

    [Test]
    public async Task Should_ReturnNotFound_When_UserNotFoundForBlock()
    {
        var result = await _service.BlockUserAsync(Id<User>.New(), Id<User>.New());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<NotFoundError>();
    }

    [Test]
    public async Task Should_ReturnNotFound_When_UserNotFoundForUnblock()
    {
        var result = await _service.UnblockUserAsync(Id<User>.New());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<NotFoundError>();
    }

    [Test]
    public async Task Should_ReturnInvalidAdminUserError_When_TargetUserIdIsEmpty()
    {
        var command = new UpdateUserCommand { Name = "Test", Email = "test@test.com" };
        var result = await _service.UpdateUserAsync(Id<User>.Empty, Id<User>.New(), command);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidAdminUserError>();
    }

    private static UserResult ToUserResult(User user) => new()
    {
        Id = user.Id,
        Name = user.Name,
        Email = user.Email,
        Avatar = user.Avatar,
        ProfileRank = user.ProfileRank,
        IsVisibleInRanking = user.IsVisibleInRanking,
        IsBlocked = user.IsBlocked,
        IsDeleted = user.IsDeleted,
        CreatedAt = user.CreatedAt
    };
}
