using FluentAssertions;
using LgymApi.Api;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Features.Trainer.Controllers;
using LgymApi.Application.Coaching.Invitations.Accept;
using LgymApi.Application.Coaching.Invitations.Reject;
using LgymApi.Application.Coaching.ManagedPlans.GetActive;
using LgymApi.Application.Coaching.Relationships.DetachFromTrainer;
using LgymApi.Application.Coaching.Relationships.GetCurrentTrainer;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TraineeRelationshipControllerTests
{
    [Test]
    public async Task AcceptInvitation_WhenFocusedUseCaseSucceeds_ForwardsTypedIdsAndReturnsLegacyMessage()
    {
        var acceptInvitation = Substitute.For<IAcceptInvitationUseCase>();
        var traineeId = Id<User>.New();
        var invitationId = Id<TrainerInvitation>.New();
        AcceptInvitationCommand? captured = null;
        acceptInvitation.ExecuteAsync(Arg.Do<AcceptInvitationCommand>(command => captured = command), Arg.Any<CancellationToken>())
            .Returns(Result.Success<Unit, AppError>(Unit.Value));
        var controller = CreateController(acceptInvitation: acceptInvitation, traineeId: traineeId);

        var result = await controller.AcceptInvitation(invitationId.ToString());

        var message = ((OkObjectResult)result).Value.Should().BeOfType<ResponseMessageDto>().Subject;
        message.Message.Should().NotBeNullOrWhiteSpace();
        captured.Should().Be(new AcceptInvitationCommand(traineeId, invitationId));
    }

    [Test]
    public async Task GetCurrentTrainer_WhenFocusedUseCaseSucceeds_MapsLegacyProfileDto()
    {
        var currentTrainer = Substitute.For<IGetCurrentTrainerUseCase>();
        var traineeId = Id<User>.New();
        var trainerId = Id<User>.New();
        currentTrainer.ExecuteAsync(Arg.Any<GetCurrentTrainerQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<CurrentTrainerReadModel, AppError>(new CurrentTrainerReadModel(
                trainerId,
                "Trainer",
                "trainer@example.test",
                "avatar.png",
                DateTimeOffset.UtcNow)));
        var controller = CreateController(currentTrainer: currentTrainer, traineeId: traineeId);

        var result = await controller.GetCurrentTrainer();

        var profile = ((OkObjectResult)result).Value.Should().BeOfType<TraineeTrainerProfileDto>().Subject;
        profile.TrainerId.Should().Be(trainerId.ToString());
        profile.Name.Should().Be("Trainer");
    }

    [Test]
    public async Task GetActiveAssignedPlan_WhenFocusedUseCaseSucceeds_MapsLegacyPlanDto()
    {
        var activePlan = Substitute.For<IGetActiveManagedPlanUseCase>();
        var traineeId = Id<User>.New();
        var plan = new ManagedPlanReadModel(Id<Plan>.New(), "Active plan", true, DateTimeOffset.UtcNow);
        activePlan.ExecuteAsync(Arg.Any<GetActiveManagedPlanQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<ManagedPlanReadModel, AppError>(plan));
        var controller = CreateController(activePlan: activePlan, traineeId: traineeId);

        var result = await controller.GetActiveAssignedPlan();

        var response = ((OkObjectResult)result).Value.Should().BeOfType<TrainerManagedPlanDto>().Subject;
        response.Id.Should().Be(plan.Id.ToString());
        response.Name.Should().Be(plan.Name);
    }

    private static TraineeRelationshipController CreateController(
        IAcceptInvitationUseCase? acceptInvitation = null,
        IRejectInvitationUseCase? rejectInvitation = null,
        IDetachFromTrainerUseCase? detachFromTrainer = null,
        IGetCurrentTrainerUseCase? currentTrainer = null,
        IGetActiveManagedPlanUseCase? activePlan = null,
        Id<User>? traineeId = null)
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(typeof(Program).Assembly, typeof(IMappingProfile).Assembly);
        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();
        var controller = new TraineeRelationshipController(
            acceptInvitation ?? Substitute.For<IAcceptInvitationUseCase>(),
            rejectInvitation ?? Substitute.For<IRejectInvitationUseCase>(),
            detachFromTrainer ?? Substitute.For<IDetachFromTrainerUseCase>(),
            currentTrainer ?? Substitute.For<IGetCurrentTrainerUseCase>(),
            activePlan ?? Substitute.For<IGetActiveManagedPlanUseCase>(),
            mapper)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.Items["User"] = new User
        {
            Id = traineeId ?? Id<User>.New(),
            Name = "Trainee",
            Email = "trainee@example.test"
        };
        return controller;
    }
}
