using FluentAssertions;
using LgymApi.Application.Coaching;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Invitations.Accept;
using LgymApi.Application.Coaching.Invitations.Reject;
using LgymApi.Application.Coaching.Invitations.Revoke;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.Identity.Contracts.Access;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class CoachingInvitationLifecycleSliceTests
{
    [Test]
    public async Task Accept_ExpiresMatchingEmailInvitationWithoutBindingLinkOrCommand()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var invitation = Invitation(trainerId, null, " TRAINEE@EXAMPLE.TEST ", expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var services = CreateServices(out _, out var accounts, out var invitations, out var links, out var commands, out var unitOfWork);
        invitations.FindByIdAsync(invitation.Id, Arg.Any<CancellationToken>()).Returns(invitation);
        accounts.GetByIdAsync(traineeId, Arg.Any<CancellationToken>()).Returns(Account(traineeId));

        var result = await Resolve<IAcceptInvitationUseCase>(services).ExecuteAsync(new AcceptInvitationCommand(traineeId, invitation.Id));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidTrainerRelationshipError>();
        await invitations.Received(1).UpdateResponseAsync(
            Arg.Is<CoachingInvitationResponseUpdateModel>(update => update.Status == TrainerInvitationStatus.Expired && !update.TraineeId.HasValue),
            Arg.Any<CancellationToken>());
        await links.DidNotReceive().AddAsync(Arg.Any<CoachingActiveLinkWriteModel>(), Arg.Any<CancellationToken>());
        await commands.DidNotReceive().EnqueueAsync(Arg.Any<IActionCommand>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Accept_RejectsOtherEmailWithoutBindingOrWriting()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var invitation = Invitation(trainerId, null, "invitee@example.test");
        var services = CreateServices(out _, out var accounts, out var invitations, out var links, out var commands, out var unitOfWork);
        invitations.FindByIdAsync(invitation.Id, Arg.Any<CancellationToken>()).Returns(invitation);
        accounts.GetByIdAsync(traineeId, Arg.Any<CancellationToken>()).Returns(Account(traineeId, "other@example.test"));

        var result = await Resolve<IAcceptInvitationUseCase>(services).ExecuteAsync(new AcceptInvitationCommand(traineeId, invitation.Id));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<TrainerRelationshipNotFoundError>();
        await invitations.DidNotReceive().UpdateResponseAsync(Arg.Any<CoachingInvitationResponseUpdateModel>(), Arg.Any<CancellationToken>());
        await links.DidNotReceive().AddAsync(Arg.Any<CoachingActiveLinkWriteModel>(), Arg.Any<CancellationToken>());
        await commands.DidNotReceive().EnqueueAsync(Arg.Any<IActionCommand>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Accept_BindsValidEmailCreatesLinkAndQueuesExistingCommands()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var invitation = Invitation(trainerId, null, "trainee@example.test");
        var services = CreateServices(out _, out var accounts, out var invitations, out var links, out var commands, out var unitOfWork);
        invitations.FindByIdAsync(invitation.Id, Arg.Any<CancellationToken>()).Returns(invitation);
        accounts.GetByIdAsync(traineeId, Arg.Any<CancellationToken>()).Returns(Account(traineeId));
        links.HasForTraineeAsync(traineeId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await Resolve<IAcceptInvitationUseCase>(services).ExecuteAsync(new AcceptInvitationCommand(traineeId, invitation.Id));

        result.IsSuccess.Should().BeTrue();
        await invitations.Received(1).UpdateResponseAsync(
            Arg.Is<CoachingInvitationResponseUpdateModel>(update => update.Status == TrainerInvitationStatus.Accepted && update.TraineeId == traineeId),
            Arg.Any<CancellationToken>());
        await links.Received(1).AddAsync(
            Arg.Is<CoachingActiveLinkWriteModel>(link => link.TrainerId == trainerId && link.TraineeId == traineeId),
            Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await commands.Received(1).EnqueueAsync(Arg.Is<InvitationAcceptedCommand>(command => command.InvitationId == invitation.Id));
        await commands.Received(1).EnqueueAsync(Arg.Is<TrainerInvitationAcceptedInAppNotificationCommand>(command => command.TraineeId == traineeId));
    }

    [Test]
    public async Task Accept_ReplaysAcceptedInvitationWithExistingLinkWithoutWritingOrCommand()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var invitation = Invitation(trainerId, traineeId, "trainee@example.test", TrainerInvitationStatus.Accepted);
        var services = CreateServices(out _, out _, out var invitations, out var links, out var commands, out var unitOfWork);
        invitations.FindByIdAsync(invitation.Id, Arg.Any<CancellationToken>()).Returns(invitation);
        links.FindByTrainerAndTraineeAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingActiveLinkFact(Id<TrainerTraineeLink>.New(), trainerId, traineeId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var result = await Resolve<IAcceptInvitationUseCase>(services).ExecuteAsync(new AcceptInvitationCommand(traineeId, invitation.Id));

        result.IsSuccess.Should().BeTrue();
        await invitations.DidNotReceive().UpdateResponseAsync(Arg.Any<CoachingInvitationResponseUpdateModel>(), Arg.Any<CancellationToken>());
        await links.DidNotReceive().AddAsync(Arg.Any<CoachingActiveLinkWriteModel>(), Arg.Any<CancellationToken>());
        await commands.DidNotReceive().EnqueueAsync(Arg.Any<IActionCommand>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Accept_MapsUniqueLinkConflictToExistingAlreadyLinkedError()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var invitation = Invitation(trainerId, traineeId, "trainee@example.test");
        var services = CreateServices(out _, out _, out var invitations, out var links, out var commands, out var unitOfWork);
        invitations.FindByIdAsync(invitation.Id, Arg.Any<CancellationToken>()).Returns(invitation);
        links.HasForTraineeAsync(traineeId, Arg.Any<CancellationToken>()).Returns(false, true);
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<int>(new InvalidOperationException("duplicate link")));

        var result = await Resolve<IAcceptInvitationUseCase>(services).ExecuteAsync(new AcceptInvitationCommand(traineeId, invitation.Id));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidTrainerRelationshipError>();
        await commands.DidNotReceive().EnqueueAsync(Arg.Any<IActionCommand>());
    }

    [Test]
    public async Task Reject_BindsValidEmailAndQueuesRejectedInAppCommand()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var invitation = Invitation(trainerId, null, "trainee@example.test");
        var services = CreateServices(out _, out var accounts, out var invitations, out _, out var commands, out var unitOfWork);
        invitations.FindByIdAsync(invitation.Id, Arg.Any<CancellationToken>()).Returns(invitation);
        accounts.GetByIdAsync(traineeId, Arg.Any<CancellationToken>()).Returns(Account(traineeId));

        var result = await Resolve<IRejectInvitationUseCase>(services).ExecuteAsync(new RejectInvitationCommand(traineeId, invitation.Id));

        result.IsSuccess.Should().BeTrue();
        await invitations.Received(1).UpdateResponseAsync(
            Arg.Is<CoachingInvitationResponseUpdateModel>(update => update.Status == TrainerInvitationStatus.Rejected && update.TraineeId == traineeId),
            Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await commands.Received(1).EnqueueAsync(Arg.Is<TrainerInvitationRejectedInAppNotificationCommand>(command => command.TraineeId == traineeId));
    }

    [Test]
    public async Task Revoke_StagesOwnedPendingInvitationAndQueuesExistingEmailCommand()
    {
        var trainerId = Id<User>.New();
        var invitation = Invitation(trainerId, null, "trainee@example.test");
        var services = CreateServices(out var userAccess, out _, out var invitations, out _, out var commands, out var unitOfWork);
        userAccess.IsTrainerAsync(trainerId, Arg.Any<CancellationToken>()).Returns(true);
        invitations.FindByIdAsync(invitation.Id, Arg.Any<CancellationToken>()).Returns(invitation);

        var result = await Resolve<IRevokeInvitationUseCase>(services).ExecuteAsync(new RevokeInvitationCommand(trainerId, invitation.Id));

        result.IsSuccess.Should().BeTrue();
        await invitations.Received(1).UpdateResponseAsync(
            Arg.Is<CoachingInvitationResponseUpdateModel>(update => update.Status == TrainerInvitationStatus.Revoked && !update.TraineeId.HasValue),
            Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await commands.Received(1).EnqueueAsync(Arg.Is<InvitationRevokedCommand>(command => command.InvitationId == invitation.Id));
    }

    private static ServiceCollection CreateServices(
        out IUserAccessReadService userAccess,
        out IAccountReadService accounts,
        out ICoachingInvitationPersistence invitations,
        out ICoachingActiveLinkPersistence links,
        out ICommandDispatcher commands,
        out IUnitOfWork unitOfWork)
    {
        userAccess = Substitute.For<IUserAccessReadService>();
        accounts = Substitute.For<IAccountReadService>();
        invitations = Substitute.For<ICoachingInvitationPersistence>();
        links = Substitute.For<ICoachingActiveLinkPersistence>();
        commands = Substitute.For<ICommandDispatcher>();
        unitOfWork = Substitute.For<IUnitOfWork>();
        var userAccessService = userAccess;
        var accountReadService = accounts;
        var invitationPersistence = invitations;
        var activeLinkPersistence = links;
        var commandDispatcher = commands;
        var workUnit = unitOfWork;
        var services = new ServiceCollection();
        services.AddApplicationMapping(typeof(IMappingProfile).Assembly);
        services.AddScoped(_ => userAccessService);
        services.AddScoped(_ => accountReadService);
        services.AddScoped(_ => invitationPersistence);
        services.AddScoped(_ => activeLinkPersistence);
        services.AddScoped(_ => commandDispatcher);
        services.AddScoped(_ => workUnit);
        services.AddCoachingModule();
        return services;
    }

    private static TContract Resolve<TContract>(ServiceCollection services) where TContract : notnull
    {
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TContract>();
    }

    private static AccountReadModel Account(Id<User> id, string email = "trainee@example.test")
        => new(id, "Trainee", email, null, "en", "UTC");

    private static CoachingInvitationFact Invitation(
        Id<User> trainerId,
        Id<User>? traineeId,
        string inviteeEmail,
        TrainerInvitationStatus status = TrainerInvitationStatus.Pending,
        DateTimeOffset? expiresAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new CoachingInvitationFact(
            Id<TrainerInvitation>.New(),
            trainerId,
            inviteeEmail,
            traineeId,
            "CODE00000001",
            status,
            expiresAt ?? now.AddDays(7),
            null,
            now,
            now);
    }
}
