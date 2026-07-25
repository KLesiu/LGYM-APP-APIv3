using FluentAssertions;
using System.Security.Claims;
using LgymApi.Api;
using LgymApi.Api.Features.AdminManagement.Contracts;
using LgymApi.Api.Features.AdminManagement.Controllers;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.AdminManagement;
using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Pagination;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class AdminUserControllerTests
{
    [Test]
    public async Task GetUser_WithInvalidId_ReturnsBadRequest()
    {
        var controller = CreateController();

        var result = await controller.GetUser("not-a-guid");

        AssertBadRequest(result);
    }

    [Test]
    public async Task UpdateUser_WithInvalidId_ReturnsBadRequest()
    {
        var controller = CreateController();

        var result = await controller.UpdateUser("not-a-guid", new UpdateUserRequest());

        AssertBadRequest(result);
    }

    [Test]
    public async Task DeleteUser_WithInvalidId_ReturnsBadRequest()
    {
        var controller = CreateController();

        var result = await controller.DeleteUser("not-a-guid");

        AssertBadRequest(result);
    }

    [Test]
    public async Task BlockUser_WithInvalidId_ReturnsBadRequest()
    {
        var controller = CreateController();

        var result = await controller.BlockUser("not-a-guid");

        AssertBadRequest(result);
    }

    [Test]
    public async Task UnblockUser_WithInvalidId_ReturnsBadRequest()
    {
        var controller = CreateController();

        var result = await controller.UnblockUser("not-a-guid");

        AssertBadRequest(result);
    }

    private static void AssertBadRequest(IActionResult result)
    {
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result;
        badRequest.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        badRequest.Value.Should().BeOfType<ResponseMessageDto>();
        ((ResponseMessageDto)badRequest.Value!).Message.Should().Be("Invalid user id.");
    }

    private static AdminUserController CreateController()
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(typeof(Program).Assembly, typeof(IMappingProfile).Assembly);
        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();
        var controller = new AdminUserController(new StubAdminUserService(), mapper)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(AuthConstants.ClaimNames.UserId, Id<User>.New().ToString())
                    ],
                    "TestAuth"))
                }
            }
        };

        return controller;
    }

    private sealed class StubAdminUserService : IAdminUserService
    {
        public Task<Result<Pagination<UserResult>, AppError>> GetUsersAsync(FilterInput filterInput, bool includeDeleted, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<UserResult, AppError>> GetUserAsync(Id<User> userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<Unit, AppError>> UpdateUserAsync(Id<User> targetUserId, Id<User> adminUserId, UpdateUserCommand command, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<Unit, AppError>> DeleteUserAsync(Id<User> targetUserId, Id<User> adminUserId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<Unit, AppError>> BlockUserAsync(Id<User> targetUserId, Id<User> adminUserId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<Unit, AppError>> UnblockUserAsync(Id<User> targetUserId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
