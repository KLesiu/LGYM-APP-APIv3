using System.Reflection;
using FluentAssertions;
using LgymApi.Api;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Pagination;
using LgymApi.Domain.Entities;
using LgymApi.Api.Features.Role.Contracts;
using LgymApi.Api.Features.Role.Controllers;
using LgymApi.Application.Features.Role;
using LgymApi.Application.Features.Role.Models;
using LgymApi.Application.Identity.ApiCompatibility;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class RoleControllerTests
{
    private static readonly List<string> ManageUserRolesClaim = ["users.roles.manage"];

    [Test]
    public async Task CreateRole_ReturnsMappedRoleDto()
    {
        var roleId = Id<RoleReference>.New();
        var fakeService = new StubRoleService
        {
            CreateRoleHandler = (_, _, _) => Task.FromResult(Result<RoleProjection, AppError>.Success(new RoleProjection(roleId, "Coach", "desc", ManageUserRolesClaim)))
        };
        var controller = new RoleController(fakeService, BuildMapper());

        var action = await controller.CreateRole(new UpsertRoleRequest
        {
            Name = "Coach",
            Description = "desc",
            PermissionClaims = ManageUserRolesClaim
        });

        var ok = action as OkObjectResult;
        ok.Should().NotBeNull();
        var dto = ok!.Value as RoleDto;

        dto.Should().NotBeNull();
        dto!.Id.Should().Be($"{roleId:N}");
        dto.Name.Should().Be("Coach");
        dto.PermissionClaims.Should().BeEquivalentTo(ManageUserRolesClaim);
    }

    private static IMapper BuildMapper()
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMapper>();
    }

    [Test]
    public async Task UpdateUserRoles_DelegatesParsedRequestAndCancellationTokenToRoleService()
    {
        var userId = Id<AccountReference>.New();
        var request = new UpdateUserRolesRequest { Roles = ["Coach", "Analyst"] };
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        var observedUserId = Id<AccountReference>.Empty;
        IReadOnlyCollection<string>? observedRoleNames = null;
        var observedCancellationToken = default(CancellationToken);
        var fakeService = new StubRoleService
        {
            UpdateUserRolesHandler = (receivedUserId, receivedRoleNames, receivedCancellationToken) =>
            {
                observedUserId = receivedUserId;
                observedRoleNames = receivedRoleNames;
                observedCancellationToken = receivedCancellationToken;
                return Task.FromResult(Result<Unit, AppError>.Success(Unit.Value));
            }
        };
        var controller = new RoleController(fakeService, BuildMapper());

        var action = await controller.UpdateUserRoles($"{userId:N}", request, cancellationToken);

        action.Should().BeOfType<OkObjectResult>();
        observedUserId.Should().Be(userId);
        observedRoleNames.Should().Equal(request.Roles);
        observedCancellationToken.Should().Be(cancellationToken);
    }

    [Test]
    public void Routes_UseGetAndPostOnly_ForRoleManagement()
    {
        var type = typeof(RoleController);
        var update = type.GetMethod(nameof(RoleController.UpdateRole), BindingFlags.Public | BindingFlags.Instance);
        var delete = type.GetMethod(nameof(RoleController.DeleteRole), BindingFlags.Public | BindingFlags.Instance);
        var updateUserRoles = type.GetMethod(nameof(RoleController.UpdateUserRoles), BindingFlags.Public | BindingFlags.Instance);

        update.Should().NotBeNull();
        delete.Should().NotBeNull();
        updateUserRoles.Should().NotBeNull();

        var controllerRoute = type.GetCustomAttribute<RouteAttribute>();
        var updateHttpPost = update!.GetCustomAttribute<HttpPostAttribute>();
        var deleteHttpPost = delete!.GetCustomAttribute<HttpPostAttribute>();
        var updateUserRolesHttpPost = updateUserRoles!.GetCustomAttribute<HttpPostAttribute>();

        controllerRoute.Should().NotBeNull();
        controllerRoute!.Template.Should().Be("api/roles");
        updateHttpPost.Should().NotBeNull();
        updateHttpPost!.Template.Should().Be("{id}/update");
        deleteHttpPost.Should().NotBeNull();
        deleteHttpPost!.Template.Should().Be("{id}/delete");
        updateUserRolesHttpPost.Should().NotBeNull();
        updateUserRolesHttpPost!.Template.Should().Be("users/{id}/roles");

        update.GetCustomAttribute<HttpPutAttribute>().Should().BeNull();
        delete.GetCustomAttribute<HttpDeleteAttribute>().Should().BeNull();
        updateUserRoles.GetCustomAttribute<HttpPutAttribute>().Should().BeNull();
    }

    private sealed class StubRoleService : IRoleManagementApiAdapter
    {
        public Func<string, string?, IReadOnlyCollection<string>, Task<Result<RoleProjection, AppError>>>? CreateRoleHandler { get; init; }
        public Func<Id<AccountReference>, IReadOnlyCollection<string>, CancellationToken, Task<Result<Unit, AppError>>>? UpdateUserRolesHandler { get; init; }

        public Task<Result<IReadOnlyList<RoleProjection>, AppError>> GetRolesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<RoleProjection>, AppError>.Success([]));

        public Task<Result<RoleProjection, AppError>> GetRoleAsync(Id<RoleReference> roleId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<RoleProjection, AppError>.Success(new RoleProjection(roleId, string.Empty, null, [])));

        public Task<Result<RoleProjection, AppError>> CreateRoleAsync(string name, string? description, IReadOnlyCollection<string> permissionClaims, CancellationToken cancellationToken = default)
            => CreateRoleHandler?.Invoke(name, description, permissionClaims)
               ?? Task.FromResult(Result<RoleProjection, AppError>.Success(new RoleProjection(Id<RoleReference>.New(), name, description, permissionClaims.ToList())));

        public Task<Result<Unit, AppError>> UpdateRoleAsync(Id<RoleReference> roleId, string name, string? description, IReadOnlyCollection<string> permissionClaims, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<Unit, AppError>.Success(Unit.Value));

        public Task<Result<Unit, AppError>> DeleteRoleAsync(Id<RoleReference> roleId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<Unit, AppError>.Success(Unit.Value));

        public IReadOnlyList<PermissionClaimProjection> GetAvailablePermissionClaims() => [];

        public Task<Result<Unit, AppError>> UpdateUserRolesAsync(Id<AccountReference> userId, IReadOnlyCollection<string> roleNames, CancellationToken cancellationToken = default)
            => UpdateUserRolesHandler?.Invoke(userId, roleNames, cancellationToken)
               ?? Task.FromResult(Result<Unit, AppError>.Success(Unit.Value));

        public Task<Result<Pagination<RoleProjection>, AppError>> GetRolesPaginatedAsync(FilterInput filterInput, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<Pagination<RoleProjection>, AppError>.Success(new Pagination<RoleProjection>()));
    }
}
