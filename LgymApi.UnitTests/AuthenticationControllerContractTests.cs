using System.Reflection;
using FluentAssertions;
using LgymApi.Api.Features.Trainer.Controllers;
using LgymApi.Api.Features.User.Contracts;
using LgymApi.Api.Features.User.Controllers;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.EloRegistry;
using LgymApi.Application.Features.PasswordReset;
using LgymApi.Application.Features.User.Models;
using LgymApi.Application.Identity.Contracts.Administration;
using LgymApi.Application.Identity.Contracts.Authentication;
using LgymApi.Application.Identity.Contracts.Profile;
using LgymApi.Application.Identity.Contracts.Ranking;
using LgymApi.Application.Identity.Contracts.Sessions;
using LgymApi.Application.WorkoutProgress.Ranking;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class AuthenticationControllerContractTests
{
    private static readonly (string ActionName, string Verb, string Template)[] UserRoutes =
    [
        (nameof(UserController.Register), HttpMethods.Post, "register"),
        (nameof(UserController.Login), HttpMethods.Post, "login"),
        (nameof(UserController.IsAdmin), HttpMethods.Get, "{id}/isAdmin"),
        (nameof(UserController.CheckToken), HttpMethods.Get, "checkToken"),
        (nameof(UserController.Logout), HttpMethods.Post, "logout"),
        (nameof(UserController.GetUsersRanking), HttpMethods.Get, "getUsersRanking"),
        (nameof(UserController.GetUserElo), HttpMethods.Get, "userInfo/{id}/getUserEloPoints"),
        (nameof(UserController.DeleteAccount), HttpMethods.Get, "deleteAccount"),
        (nameof(UserController.ChangeVisibilityInRanking), HttpMethods.Post, "changeVisibilityInRanking"),
        (nameof(UserController.UpdateTimeZone), HttpMethods.Post, "updateTimeZone"),
        (nameof(UserController.ForgotPassword), HttpMethods.Post, "forgot-password"),
        (nameof(UserController.ResetPassword), HttpMethods.Post, "reset-password")
    ];

    private static readonly (string ActionName, string Verb, string Template)[] TrainerRoutes =
    [
        (nameof(TrainerAuthController.Register), HttpMethods.Post, "register"),
        (nameof(TrainerAuthController.Login), HttpMethods.Post, "login"),
        (nameof(TrainerAuthController.CheckToken), HttpMethods.Get, "checkToken")
    ];

    [Test]
    public void UserController_ExposesAllCurrentLegacyRoutes()
        => AssertRoutes<UserController>("api", UserRoutes);

    [Test]
    public void TrainerAuthController_ExposesAllCurrentLegacyRoutes()
        => AssertRoutes<TrainerAuthController>("api/trainer", TrainerRoutes);

    [Test]
    public async Task UserController_Register_RegistersNonTrainerThroughEloRegistry()
    {
        var eloRegistryService = Substitute.For<IEloRegistryService>();
        var mapper = Substitute.For<IMapper>();
        eloRegistryService.RegisterUserAsync(Arg.Any<RegisterUserInput>(), false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<Unit, AppError>.Success(Unit.Value)));
        var controller = CreateUserController(eloRegistryService, mapper);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var action = await controller.Register(new RegisterUserRequest
        {
            Name = "user",
            Email = "user@example.com",
            Password = "password123",
            ConfirmPassword = "password123"
        });

        action.Should().BeOfType<OkObjectResult>();
        await eloRegistryService.Received(1)
            .RegisterUserAsync(Arg.Any<RegisterUserInput>(), false, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TrainerAuthController_Register_RegistersTrainerThroughEloRegistry()
    {
        var eloRegistryService = Substitute.For<IEloRegistryService>();
        var mapper = Substitute.For<IMapper>();
        eloRegistryService.RegisterUserAsync(Arg.Any<RegisterUserInput>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<Unit, AppError>.Success(Unit.Value)));
        var controller = CreateTrainerAuthController(eloRegistryService, mapper);

        var action = await controller.Register(new RegisterUserRequest
        {
            Name = "trainer",
            Email = "trainer@example.com",
            Password = "password123",
            ConfirmPassword = "password123"
        });

        action.Should().BeOfType<OkObjectResult>();
        await eloRegistryService.Received(1)
            .RegisterUserAsync(Arg.Any<RegisterUserInput>(), true, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UserController_Login_PopulatesLatestEloBeforeMapping()
    {
        var userInfo = new UserInfoResult { Elo = 1000 };
        var loginResult = new LoginResult { User = userInfo };
        var userCredentialLoginService = Substitute.For<IUserCredentialLoginService>();
        var eloRegistryService = Substitute.For<IEloRegistryService>();
        var mapper = Substitute.For<IMapper>();
        userCredentialLoginService.LoginAsync("user", "password123", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<LoginResult, AppError>.Success(loginResult)));
        eloRegistryService.PopulateLatestEloAsync(userInfo, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                userInfo.Elo = 1200;
                return Task.CompletedTask;
            });
        mapper.Map<LoginResult, LoginResponseDto>(loginResult).Returns(_ =>
        {
            userInfo.Elo.Should().Be(1200);
            return new LoginResponseDto();
        });
        var controller = CreateUserController(eloRegistryService, mapper, userCredentialLoginService);

        var action = await controller.Login(new LoginRequest { Name = "user", Password = "password123" });

        action.Should().BeOfType<OkObjectResult>();
        await eloRegistryService.Received(1).PopulateLatestEloAsync(userInfo, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UserController_CheckToken_PopulatesLatestEloBeforeMapping()
    {
        var currentUser = new User();
        var userInfo = new UserInfoResult { Elo = 1000 };
        var userProfileService = Substitute.For<IUserProfileService>();
        var eloRegistryService = Substitute.For<IEloRegistryService>();
        var mapper = Substitute.For<IMapper>();
        userProfileService.CheckTokenAsync(currentUser, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<UserInfoResult, AppError>.Success(userInfo)));
        eloRegistryService.PopulateLatestEloAsync(userInfo, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                userInfo.Elo = 1200;
                return Task.CompletedTask;
            });
        mapper.Map<UserInfoResult, UserInfoDto>(userInfo).Returns(_ =>
        {
            userInfo.Elo.Should().Be(1200);
            return new UserInfoDto();
        });
        var context = new DefaultHttpContext();
        context.Items["User"] = currentUser;
        var controller = CreateUserController(eloRegistryService, mapper, userProfileService: userProfileService);
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var action = await controller.CheckToken();

        action.Should().BeOfType<OkObjectResult>();
        await eloRegistryService.Received(1).PopulateLatestEloAsync(userInfo, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TrainerAuthController_Login_PopulatesLatestEloBeforeMapping()
    {
        var userInfo = new UserInfoResult { Elo = 1000 };
        var loginResult = new LoginResult { User = userInfo };
        var userCredentialLoginService = Substitute.For<IUserCredentialLoginService>();
        var eloRegistryService = Substitute.For<IEloRegistryService>();
        var mapper = Substitute.For<IMapper>();
        userCredentialLoginService.LoginTrainerAsync("trainer", "password123", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<LoginResult, AppError>.Success(loginResult)));
        eloRegistryService.PopulateLatestEloAsync(userInfo, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                userInfo.Elo = 1200;
                return Task.CompletedTask;
            });
        mapper.Map<LoginResult, LoginResponseDto>(loginResult).Returns(_ =>
        {
            userInfo.Elo.Should().Be(1200);
            return new LoginResponseDto();
        });
        var controller = CreateTrainerAuthController(eloRegistryService, mapper, userCredentialLoginService);

        var action = await controller.Login(new LoginRequest { Name = "trainer", Password = "password123" });

        action.Should().BeOfType<OkObjectResult>();
        await eloRegistryService.Received(1).PopulateLatestEloAsync(userInfo, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TrainerAuthController_CheckToken_PopulatesLatestEloBeforeMapping()
    {
        var currentUser = new User();
        var userInfo = new UserInfoResult { Elo = 1000 };
        var userProfileService = Substitute.For<IUserProfileService>();
        var eloRegistryService = Substitute.For<IEloRegistryService>();
        var mapper = Substitute.For<IMapper>();
        userProfileService.CheckTokenAsync(currentUser, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<UserInfoResult, AppError>.Success(userInfo)));
        eloRegistryService.PopulateLatestEloAsync(userInfo, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                userInfo.Elo = 1200;
                return Task.CompletedTask;
            });
        mapper.Map<UserInfoResult, UserInfoDto>(userInfo).Returns(_ =>
        {
            userInfo.Elo.Should().Be(1200);
            return new UserInfoDto();
        });
        var context = new DefaultHttpContext();
        context.Items["User"] = currentUser;
        var controller = CreateTrainerAuthController(eloRegistryService, mapper, userProfileService: userProfileService);
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var action = await controller.CheckToken();

        action.Should().BeOfType<OkObjectResult>();
        await eloRegistryService.Received(1).PopulateLatestEloAsync(userInfo, Arg.Any<CancellationToken>());
    }

    private static UserController CreateUserController(
        IEloRegistryService eloRegistryService,
        IMapper mapper,
        IUserCredentialLoginService? userCredentialLoginService = null,
        IUserProfileService? userProfileService = null)
    {
        return new UserController(
            userCredentialLoginService ?? Substitute.For<IUserCredentialLoginService>(),
            Substitute.For<IUserSessionTerminationService>(),
            userProfileService ?? Substitute.For<IUserProfileService>(),
            Substitute.For<IUserRankingService>(),
            Substitute.For<IWorkoutProgressRankingReadService>(),
            Substitute.For<IUserAdminAccessService>(),
            eloRegistryService,
            Substitute.For<IPasswordResetService>(),
            mapper);
    }

    private static TrainerAuthController CreateTrainerAuthController(
        IEloRegistryService eloRegistryService,
        IMapper mapper,
        IUserCredentialLoginService? userCredentialLoginService = null,
        IUserProfileService? userProfileService = null)
    {
        return new TrainerAuthController(
            userCredentialLoginService ?? Substitute.For<IUserCredentialLoginService>(),
            userProfileService ?? Substitute.For<IUserProfileService>(),
            eloRegistryService,
            mapper);
    }

    private static void AssertRoutes<TController>(
        string controllerTemplate,
        IReadOnlyCollection<(string ActionName, string Verb, string Template)> routes)
    {
        var routeAttribute = typeof(TController).GetCustomAttribute<RouteAttribute>();
        routeAttribute.Should().NotBeNull();
        routeAttribute!.Template.Should().Be(controllerTemplate);

        foreach (var route in routes)
        {
            var action = typeof(TController).GetMethod(route.ActionName);
            action.Should().NotBeNull();

            var httpMethodAttribute = action!.GetCustomAttributes<HttpMethodAttribute>().SingleOrDefault();
            httpMethodAttribute.Should().NotBeNull();
            httpMethodAttribute!.HttpMethods.Should().Equal(route.Verb);
            httpMethodAttribute.Template.Should().Be(route.Template);
        }
    }
}
