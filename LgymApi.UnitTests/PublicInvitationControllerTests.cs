using FluentAssertions;
using LgymApi.Api;
using LgymApi.Api.Features.Public.Contracts;
using LgymApi.Api.Features.Public.Controllers;
using LgymApi.Application.Coaching.Invitations.PublicStatus;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class PublicInvitationControllerTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public async Task GetInvitationStatus_WithBlankCode_ReturnsNotFoundWithoutCallingUseCase(string? code)
    {
        var useCase = Substitute.For<IPublicInvitationStatusUseCase>();
        var controller = CreateController(useCase);

        var result = await controller.GetInvitationStatus(Id<TrainerInvitation>.New().ToString(), code);

        result.Should().BeOfType<NotFoundResult>();
        await useCase.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    [Test]
    public async Task GetInvitationStatus_WithMalformedId_ReturnsNotFoundWithoutCallingUseCase()
    {
        var useCase = Substitute.For<IPublicInvitationStatusUseCase>();
        var controller = CreateController(useCase);

        var result = await controller.GetInvitationStatus("not-a-guid", "exact-code");

        result.Should().BeOfType<NotFoundResult>();
        await useCase.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    [Test]
    public async Task GetInvitationStatus_WithSuccessfulUseCase_ForwardsQueryAndMapsLegacyDto()
    {
        var invitationId = Id<TrainerInvitation>.New();
        using var cancellation = new CancellationTokenSource();
        var useCase = Substitute.For<IPublicInvitationStatusUseCase>();
        useCase.ExecuteAsync(Arg.Any<PublicInvitationStatusQuery>(), cancellation.Token)
            .Returns(Result<PublicInvitationStatusReadModel, AppError>.Success(
                new PublicInvitationStatusReadModel(TrainerInvitationStatus.Accepted, true)));
        var controller = CreateController(useCase);

        var result = await controller.GetInvitationStatus(invitationId.ToString(), "exact-code", cancellation.Token);

        var response = result.Should().BeOfType<OkObjectResult>().Subject;
        response.Value.Should().BeEquivalentTo(new PublicInvitationStatusDto
        {
            Status = "Accepted",
            UserExists = true
        });
        await useCase.Received(1).ExecuteAsync(
            Arg.Is<PublicInvitationStatusQuery>(query => query.InvitationId == invitationId && query.Code == "exact-code"),
            cancellation.Token);
    }

    [Test]
    public async Task GetInvitationStatus_WithUseCaseFailure_ReturnsBareNotFound()
    {
        var useCase = Substitute.For<IPublicInvitationStatusUseCase>();
        useCase.ExecuteAsync(Arg.Any<PublicInvitationStatusQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<PublicInvitationStatusReadModel, AppError>.Failure(
                new TrainerRelationshipNotFoundError(Messages.DidntFind)));
        var controller = CreateController(useCase);

        var result = await controller.GetInvitationStatus(Id<TrainerInvitation>.New().ToString(), "wrong-code");

        result.Should().BeOfType<NotFoundResult>();
    }

    private static PublicInvitationController CreateController(IPublicInvitationStatusUseCase useCase)
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(typeof(Program).Assembly, typeof(IMappingProfile).Assembly);
        using var provider = services.BuildServiceProvider();
        return new PublicInvitationController(useCase, provider.GetRequiredService<IMapper>());
    }
}
