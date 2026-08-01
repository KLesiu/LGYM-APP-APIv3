using FluentAssertions;
using LgymApi.Application.Features.AdminManagement.Models;
using System.Net;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.Features.Role;
using LgymApi.Application.Models;
using LgymApi.Application.Pagination;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class RoleServiceTests
{
    private static readonly string[] ManageGlobalExercisesClaim = [AuthConstants.Permissions.ManageGlobalExercises];

    private RoleService _service = null!;
    private InMemoryRoleRepository _roleRepository = null!;
    private InMemoryUserRepository _userRepository = null!;
    private FakeUnitOfWork _unitOfWork = null!;

    [SetUp]
    public void SetUp()
    {
        _roleRepository = new InMemoryRoleRepository();
        _userRepository = new InMemoryUserRepository();
        _unitOfWork = new FakeUnitOfWork();
        _service = new RoleService(_roleRepository, _userRepository, _unitOfWork);
    }

    [Test]
    public async Task GetRolesAsync_ReturnsRolesWithClaims()
    {
        var roleId = Id<Role>.New();
        _roleRepository.Roles.Add(new Role { Id = (Domain.ValueObjects.Id<Role>)roleId, Name = "Coach", Description = "Role" });
        _roleRepository.RoleClaims[roleId] =
        [
            AuthConstants.Permissions.ManageAppConfig,
            AuthConstants.Permissions.ManageUserRoles
        ];

        var result = await _service.GetRolesAsync();
        var roles = result.Value;

        roles.Should().HaveCount(1);
        roles[0].Name.Should().Be("Coach");
        roles[0].PermissionClaims.Should().BeEquivalentTo(_roleRepository.RoleClaims[_roleRepository.Roles.First().Id]);
    }

    [Test]
    public async Task GetRoleAsync_ReturnsFailure_WhenRoleIdEmpty()
    {
        var emptyId = default(Id<Role>);
        var result = await _service.GetRoleAsync(emptyId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidRoleError>();
        result.Error.HttpStatusCode.Should().Be((int)HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateRoleAsync_NormalizesAndStoresClaims()
    {
        var roleResult = await _service.CreateRoleAsync(
            "  Coach  ",
            "  Training tools  ",
            [
                AuthConstants.Permissions.ManageAppConfig,
                AuthConstants.Permissions.ManageUserRoles,
                AuthConstants.Permissions.ManageAppConfig
            ]);

        var result = roleResult.Value;

        result.Name.Should().Be("Coach");
        result.Description.Should().Be("Training tools");
        result.PermissionClaims.Should().Equal(new[]
        {
             AuthConstants.Permissions.ManageAppConfig,
             AuthConstants.Permissions.ManageUserRoles
         });
        _roleRepository.Roles.Any(r => r.Id == result.Id).Should().BeTrue();
        _unitOfWork.SaveChangesCalls.Should().Be(1);
    }

    [Test]
    public async Task CreateRoleAsync_ReturnsFailure_WhenClaimInvalid()
    {
        var result = await _service.CreateRoleAsync("Coach", null, ["invalid.claim"]);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidRoleError>();
        result.Error.HttpStatusCode.Should().Be((int)HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UpdateRoleAsync_ReturnsFailure_ForSystemRole()
    {
        var adminRole = new Role { Id = Id<Role>.New(), Name = AuthConstants.Roles.Admin };
        _roleRepository.Roles.Add(adminRole);

        var result = await _service.UpdateRoleAsync(
            adminRole.Id,
            "AdminUpdated",
            null,
            [AuthConstants.Permissions.ManageUserRoles]);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<RoleForbiddenError>();
        result.Error.HttpStatusCode.Should().Be((int)HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task UpdateRoleAsync_UpdatesNameDescriptionAndClaims()
    {
        var roleId = Id<Role>.New();
        _roleRepository.Roles.Add(new Role { Id = (Domain.ValueObjects.Id<Role>)roleId, Name = "Coach", Description = "Old" });

        await _service.UpdateRoleAsync(
            (Id<Role>)roleId,
            "  Senior Coach  ",
            "  New desc  ",
            ManageGlobalExercisesClaim);

        var updated = _roleRepository.Roles.Single(r => r.Id == roleId);
        updated.Name.Should().Be("Senior Coach");
        updated.Description.Should().Be("New desc");
        _roleRepository.RoleClaims[updated.Id].Should().Equal(ManageGlobalExercisesClaim);
        _unitOfWork.SaveChangesCalls.Should().Be(1);
    }

    [Test]
    public async Task DeleteRoleAsync_ReturnsFailure_ForSystemRole()
    {
        var userRole = new Role { Id = Id<Role>.New(), Name = AuthConstants.Roles.User };
        _roleRepository.Roles.Add(userRole);

        var result = await _service.DeleteRoleAsync(userRole.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<RoleForbiddenError>();
        result.Error.HttpStatusCode.Should().Be((int)HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task DeleteRoleAsync_RemovesNonSystemRole()
    {
        var role = new Role { Id = Id<Role>.New(), Name = "Coach" };
        _roleRepository.Roles.Add(role);

        await _service.DeleteRoleAsync(role.Id);

        _roleRepository.Roles.Any(r => r.Id == role.Id).Should().BeFalse();
        _unitOfWork.SaveChangesCalls.Should().Be(1);
    }

    [Test]
    public async Task UpdateUserRolesAsync_ReplacesUserRolesUsingDistinctNames()
    {
        var userId = Id<User>.New();
        _userRepository.Users.Add(new User { Id = (Domain.ValueObjects.Id<User>)userId, Name = "u", Email = "u@x.com" });

        var coach = new Role { Id = Id<Role>.New(), Name = "Coach" };
        var analyst = new Role { Id = Id<Role>.New(), Name = "Analyst" };
        _roleRepository.Roles.AddRange([coach, analyst]);

        await _service.UpdateUserRolesAsync((Id<User>)userId, [" coach ", "ANALYST", "coach"]);

        _roleRepository.UserRoleAssignments.TryGetValue(userId, out var roles).Should().BeTrue();
        roles!.Should().BeEquivalentTo(new[] { coach.Id, analyst.Id });
        _unitOfWork.SaveChangesCalls.Should().Be(1);
    }

    [Test]
    public async Task UpdateUserRolesAsync_ReturnsFailure_WhenUserMissing()
    {
        var result = await _service.UpdateUserRolesAsync(Id<User>.New(), ["Coach"]);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<RoleNotFoundError>();
        result.Error.HttpStatusCode.Should().Be((int)HttpStatusCode.NotFound);
        _unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public async Task UpdateUserRolesAsync_ReturnsFailure_WhenRoleMissing()
    {
        var userId = Id<User>.New();
        _userRepository.Users.Add(new User { Id = (Domain.ValueObjects.Id<User>)userId, Name = "u", Email = "u@x.com" });

        var result = await _service.UpdateUserRolesAsync((Id<User>)userId, ["UnknownRole"]);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidRoleError>();
        result.Error.HttpStatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        _unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public void GetAvailablePermissionClaims_ReturnsLocalizedCatalog()
    {
        var claims = _service.GetAvailablePermissionClaims();

        claims.Select(c => c.ClaimValue).Should().Equal(AuthConstants.Permissions.All.OrderBy(c => c, StringComparer.Ordinal).ToList());
        claims.All(c => c.ClaimType == AuthConstants.PermissionClaimType).Should().BeTrue();
        claims.All(c => !string.IsNullOrWhiteSpace(c.DisplayName)).Should().BeTrue();
    }

    private sealed class InMemoryUserRepository : IUserRepository
    {
        public List<User> Users { get; } = new();

        public Task<User?> FindByIdAsync(Id<LgymApi.Domain.Entities.User> id, CancellationToken cancellationToken = default)
            => Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

        public Task<User?> FindByIdIncludingDeletedAsync(Id<LgymApi.Domain.Entities.User> id, CancellationToken cancellationToken = default)
            => Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

        public Task<User?> FindByIdWithRolesAsync(Id<LgymApi.Domain.Entities.User> id, CancellationToken cancellationToken = default)
            => Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

        public Task<User?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult(Users.FirstOrDefault(u => u.Name == name));

        public Task<User?> FindByEmailAsync(Email email, CancellationToken cancellationToken = default)
            => Task.FromResult(Users.FirstOrDefault(u => u.Email == email));

        public Task<User?> FindByNameOrEmailAsync(string name, string email, CancellationToken cancellationToken = default)
            => Task.FromResult(Users.FirstOrDefault(u => u.Name == name || u.Email == email));

        public Task<List<LgymApi.Application.Models.UserRankingEntry>> GetRankingAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<LgymApi.Application.Models.UserRankingEntry>());

        public Task AddAsync(User user, CancellationToken cancellationToken = default)
        {
            Users.Add(user);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(User user, CancellationToken cancellationToken = default)
        {
            var index = Users.FindIndex(u => u.Id == user.Id);
            if (index >= 0)
            {
                Users[index] = user;
            }

            return Task.CompletedTask;
        }

        public Task<Pagination<UserResult>> GetUsersPaginatedAsync(FilterInput filterInput, bool includeDeleted, CancellationToken cancellationToken = default)
            => Task.FromResult(new Pagination<UserResult>());
    }

    private sealed class InMemoryRoleRepository : IRoleRepository
    {
        public List<Role> Roles { get; } = new();
        public Dictionary<Id<Role>, List<string>> RoleClaims { get; } = new();
        public Dictionary<Id<User>, List<Id<Role>>> UserRoleAssignments { get; } = new();

        public Task<List<Role>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Roles.OrderBy(r => r.Name).ToList());

        public Task<Role?> FindByIdAsync(Id<Role> roleId, CancellationToken cancellationToken = default)
            => Task.FromResult(Roles.FirstOrDefault(r => r.Id == roleId));

        public Task<Role?> FindByNameAsync(string roleName, CancellationToken cancellationToken = default)
            => Task.FromResult(Roles.FirstOrDefault(r => string.Equals(r.Name, roleName, StringComparison.Ordinal)));

        public Task<List<Role>> GetByNamesAsync(IReadOnlyCollection<string> roleNames, CancellationToken cancellationToken = default)
        {
            var normalized = roleNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(Roles.Where(r => normalized.Contains(r.Name)).ToList());
        }

        public Task<bool> ExistsByNameAsync(string roleName, Id<Role>? excludeRoleId = null, CancellationToken cancellationToken = default)
        {
            var exists = Roles.Any(r =>
                string.Equals(r.Name, roleName, StringComparison.OrdinalIgnoreCase) &&
                (!excludeRoleId.HasValue || r.Id != excludeRoleId.Value));
            return Task.FromResult(exists);
        }

        public Task<List<string>> GetRoleNamesByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
        {
            if (!UserRoleAssignments.TryGetValue(userId, out var roleIds))
            {
                return Task.FromResult(new List<string>());
            }

            return Task.FromResult(Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Name).OrderBy(n => n).ToList());
        }

        public Task<List<string>> GetPermissionClaimsByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
        {
            if (!UserRoleAssignments.TryGetValue(userId, out var roleIds))
            {
                return Task.FromResult(new List<string>());
            }

            var claims = roleIds
                .Where(RoleClaims.ContainsKey)
                .SelectMany(roleId => RoleClaims[roleId])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(c => c)
                .ToList();
            return Task.FromResult(claims);
        }

        public Task<List<string>> GetPermissionClaimsByRoleIdAsync(Id<Role> roleId, CancellationToken cancellationToken = default)
            => Task.FromResult(RoleClaims.TryGetValue(roleId, out var claims)
                ? claims.OrderBy(c => c).ToList()
                : new List<string>());

        public Task<Dictionary<Id<Role>, List<string>>> GetPermissionClaimsByRoleIdsAsync(IReadOnlyCollection<Id<Role>> roleIds, CancellationToken cancellationToken = default)
        {
            var result = roleIds
                .Distinct()
                .Where(roleId => RoleClaims.ContainsKey(roleId))
                .ToDictionary(
                    roleId => roleId,
                    roleId => RoleClaims[roleId].OrderBy(c => c).ToList());
            return Task.FromResult(result);
        }

        public Task<bool> UserHasRoleAsync(Id<User> userId, string roleName, CancellationToken cancellationToken = default)
        {
            if (!UserRoleAssignments.TryGetValue(userId, out var roleIds))
            {
                return Task.FromResult(false);
            }

            var hasRole = Roles.Any(r => roleIds.Contains(r.Id) && string.Equals(r.Name, roleName, StringComparison.Ordinal));
            return Task.FromResult(hasRole);
        }

        public Task<bool> UserHasPermissionAsync(Id<User> userId, string permission, CancellationToken cancellationToken = default)
        {
            if (!UserRoleAssignments.TryGetValue(userId, out var roleIds))
            {
                return Task.FromResult(false);
            }

            var hasPermission = roleIds
                .Where(RoleClaims.ContainsKey)
                .SelectMany(roleId => RoleClaims[roleId])
                .Any(claim => string.Equals(claim, permission, StringComparison.Ordinal));

            return Task.FromResult(hasPermission);
        }

        public Task AddRoleAsync(Role role, CancellationToken cancellationToken = default)
        {
            Roles.Add(role);
            return Task.CompletedTask;
        }

        public Task UpdateRoleAsync(Role role, CancellationToken cancellationToken = default)
        {
            var index = Roles.FindIndex(r => r.Id == role.Id);
            if (index >= 0)
            {
                Roles[index] = role;
            }

            return Task.CompletedTask;
        }

        public Task DeleteRoleAsync(Role role, CancellationToken cancellationToken = default)
        {
            Roles.RemoveAll(r => r.Id == role.Id);
            RoleClaims.Remove(role.Id);
            foreach (var assignment in UserRoleAssignments.Values)
            {
                assignment.RemoveAll(roleId => roleId == role.Id);
            }

            return Task.CompletedTask;
        }

        public Task ReplaceRolePermissionClaimsAsync(Id<Role> roleId, IReadOnlyCollection<string> permissionClaims, CancellationToken cancellationToken = default)
        {
            RoleClaims[roleId] = permissionClaims
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(c => c)
                .ToList();
            return Task.CompletedTask;
        }

        public Task AddUserRolesAsync(Id<User> userId, IReadOnlyCollection<Id<Role>> roleIds, CancellationToken cancellationToken = default)
        {
            var userIdGuid = userId;
            if (!UserRoleAssignments.TryGetValue(userIdGuid, out var assignments))
            {
                assignments = new List<Id<Role>>();
                UserRoleAssignments[userIdGuid] = assignments;
            }

            foreach (var roleId in roleIds.Where(roleId => !assignments.Contains(roleId)))
            {
                assignments.Add(roleId);
            }

            return Task.CompletedTask;
        }

        public Task ReplaceUserRolesAsync(Id<User> userId, IReadOnlyCollection<Id<Role>> roleIds, CancellationToken cancellationToken = default)
        {
            UserRoleAssignments[userId] = roleIds.Distinct().ToList();
            return Task.CompletedTask;
        }

        public Task<Dictionary<Id<User>, List<string>>> GetRoleNamesByUserIdsAsync(IReadOnlyCollection<Id<User>> userIds, CancellationToken cancellationToken = default)
            => Task.FromResult(new Dictionary<Id<User>, List<string>>());

        public Task<Pagination<Role>> GetRolesPaginatedAsync(FilterInput filterInput, CancellationToken cancellationToken = default)
            => Task.FromResult(new Pagination<Role>());
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;
            return Task.FromResult(1);
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IUnitOfWorkTransaction>(new FakeUnitOfWorkTransaction());

        public void DetachEntity<TEntity>(TEntity entity) where TEntity : class { }
    }

    private sealed class FakeUnitOfWorkTransaction : IUnitOfWorkTransaction
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
