using FluentAssertions;
using LgymApi.Application.Coaching;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Coaching.Relationships.DetachFromTrainer;
using LgymApi.Application.Coaching.Relationships.GetCurrentTrainer;
using LgymApi.Application.Coaching.Relationships.UnlinkTrainee;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.Identity.Contracts.Access;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class CoachingRelationshipLifecycleSliceTests
{
    [Test]
    public async Task Unlink_RemovesOwnedLinkCommitsOnceAndDoesNotEnqueueNotification()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var link = Link(trainerId, traineeId);
        var services = CreateServices(out var userAccess, out _, out var links, out var commands, out var unitOfWork);
        userAccess.IsTrainerAsync(trainerId, Arg.Any<CancellationToken>()).Returns(true);
        links.FindByTrainerAndTraineeAsync(trainerId, traineeId, Arg.Any<CancellationToken>()).Returns(link);

        var result = await Resolve<IUnlinkTraineeUseCase>(services).ExecuteAsync(
            new UnlinkTraineeCommand(trainerId, traineeId));

        result.IsSuccess.Should().BeTrue();
        await links.Received(1).RemoveAsync(link.Id, Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await commands.DidNotReceive().EnqueueAsync(Arg.Any<IActionCommand>());
    }

    [Test]
    public async Task Unlink_WhenCallerIsNotTrainerReturnsForbiddenBeforeRelationshipRead()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var services = CreateServices(out var userAccess, out _, out var links, out var commands, out var unitOfWork);
        userAccess.IsTrainerAsync(trainerId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await Resolve<IUnlinkTraineeUseCase>(services).ExecuteAsync(
            new UnlinkTraineeCommand(trainerId, traineeId));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<TrainerRelationshipForbiddenError>();
        await links.DidNotReceive().FindByTrainerAndTraineeAsync(
            Arg.Any<Id<User>>(),
            Arg.Any<Id<User>>(),
            Arg.Any<CancellationToken>());
        await links.DidNotReceive().RemoveAsync(Arg.Any<Id<TrainerTraineeLink>>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await commands.DidNotReceive().EnqueueAsync(Arg.Any<IActionCommand>());
    }

    [Test]
    public async Task Unlink_WhenTraineeIdIsEmptyReturnsBadRequestAfterTrainerCheck()
    {
        var trainerId = Id<User>.New();
        var services = CreateServices(out var userAccess, out _, out var links, out _, out var unitOfWork);
        userAccess.IsTrainerAsync(trainerId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Resolve<IUnlinkTraineeUseCase>(services).ExecuteAsync(
            new UnlinkTraineeCommand(trainerId, Id<User>.Empty));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidTrainerRelationshipError>();
        await userAccess.Received(1).IsTrainerAsync(trainerId, Arg.Any<CancellationToken>());
        await links.DidNotReceive().FindByTrainerAndTraineeAsync(
            Arg.Any<Id<User>>(),
            Arg.Any<Id<User>>(),
            Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Unlink_WhenTrainerDoesNotOwnRelationshipReturnsNotFoundWithoutWrite()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var services = CreateServices(out var userAccess, out _, out var links, out _, out var unitOfWork);
        userAccess.IsTrainerAsync(trainerId, Arg.Any<CancellationToken>()).Returns(true);
        links.FindByTrainerAndTraineeAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns((CoachingActiveLinkFact?)null);

        var result = await Resolve<IUnlinkTraineeUseCase>(services).ExecuteAsync(
            new UnlinkTraineeCommand(trainerId, traineeId));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<TrainerRelationshipNotFoundError>();
        await links.DidNotReceive().RemoveAsync(Arg.Any<Id<TrainerTraineeLink>>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Detach_RemovesLinkThenCommitsThenEnqueuesRelationshipEndedCommand()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var link = Link(trainerId, traineeId);
        var operations = new List<string>();
        var services = CreateServices(out _, out _, out var links, out var commands, out var unitOfWork);
        links.FindByTraineeAsync(traineeId, Arg.Any<CancellationToken>()).Returns(link);
        links.RemoveAsync(link.Id, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => operations.Add("remove"));
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1))
            .AndDoes(_ => operations.Add("commit"));
        commands.EnqueueAsync(Arg.Any<TrainerRelationshipEndedInAppNotificationCommand>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => operations.Add("enqueue"));

        var result = await Resolve<IDetachFromTrainerUseCase>(services).ExecuteAsync(
            new DetachFromTrainerCommand(traineeId));

        result.IsSuccess.Should().BeTrue();
        operations.Should().Equal("remove", "commit", "enqueue");
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await commands.Received(1).EnqueueAsync(
            Arg.Is<TrainerRelationshipEndedInAppNotificationCommand>(command =>
                command.TrainerId == trainerId && command.TraineeId == traineeId));
    }

    [Test]
    public async Task Detach_WhenRelationshipIsAbsentReturnsNotFoundWithoutWriteOrCommand()
    {
        var traineeId = Id<User>.New();
        var services = CreateServices(out _, out _, out var links, out var commands, out var unitOfWork);
        links.FindByTraineeAsync(traineeId, Arg.Any<CancellationToken>()).Returns((CoachingActiveLinkFact?)null);

        var result = await Resolve<IDetachFromTrainerUseCase>(services).ExecuteAsync(
            new DetachFromTrainerCommand(traineeId));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<TrainerRelationshipNotFoundError>();
        await links.DidNotReceive().RemoveAsync(Arg.Any<Id<TrainerTraineeLink>>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await commands.DidNotReceive().EnqueueAsync(Arg.Any<IActionCommand>());
    }

    [Test]
    public async Task Detach_WhenCommitFailsPropagatesAndDoesNotEnqueueCommand()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var services = CreateServices(out _, out _, out var links, out var commands, out var unitOfWork);
        links.FindByTraineeAsync(traineeId, Arg.Any<CancellationToken>()).Returns(Link(trainerId, traineeId));
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("commit failed")));

        Func<Task> act = () => Resolve<IDetachFromTrainerUseCase>(services).ExecuteAsync(
            new DetachFromTrainerCommand(traineeId));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("commit failed");
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await commands.DidNotReceive().EnqueueAsync(Arg.Any<IActionCommand>());
    }

    [Test]
    public async Task Detach_WhenDispatchFailsPropagatesAfterSingleCommit()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var services = CreateServices(out _, out _, out var links, out var commands, out var unitOfWork);
        links.FindByTraineeAsync(traineeId, Arg.Any<CancellationToken>()).Returns(Link(trainerId, traineeId));
        commands.EnqueueAsync(Arg.Any<TrainerRelationshipEndedInAppNotificationCommand>())
            .Returns(Task.FromException(new InvalidOperationException("dispatch failed")));

        Func<Task> act = () => Resolve<IDetachFromTrainerUseCase>(services).ExecuteAsync(
            new DetachFromTrainerCommand(traineeId));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("dispatch failed");
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await commands.Received(1).EnqueueAsync(Arg.Any<TrainerRelationshipEndedInAppNotificationCommand>());
    }

    [Test]
    public async Task GetCurrentTrainer_ReturnsMappedIdentityProfileAndExactLinkTimestamp()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var linkedAt = new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);
        var services = CreateServices(out _, out var accounts, out var links, out _, out var unitOfWork);
        links.FindByTraineeAsync(traineeId, Arg.Any<CancellationToken>()).Returns(Link(trainerId, traineeId, linkedAt));
        accounts.GetByIdAsync(trainerId, Arg.Any<CancellationToken>())
            .Returns(new AccountReadModel(trainerId, "Trainer", "trainer@example.test", "avatar.jpg", "pl", "Europe/Warsaw"));

        var result = await Resolve<IGetCurrentTrainerUseCase>(services).ExecuteAsync(
            new GetCurrentTrainerQuery(traineeId));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new CurrentTrainerReadModel(
            trainerId,
            "Trainer",
            "trainer@example.test",
            "avatar.jpg",
            linkedAt));
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetCurrentTrainer_WhenRelationshipIsAbsentReturnsNotFoundWithoutAccountRead()
    {
        var traineeId = Id<User>.New();
        var services = CreateServices(out _, out var accounts, out var links, out _, out _);
        links.FindByTraineeAsync(traineeId, Arg.Any<CancellationToken>()).Returns((CoachingActiveLinkFact?)null);

        var result = await Resolve<IGetCurrentTrainerUseCase>(services).ExecuteAsync(
            new GetCurrentTrainerQuery(traineeId));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<TrainerRelationshipNotFoundError>();
        await accounts.DidNotReceive().GetByIdAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetCurrentTrainer_WhenTrainerAccountIsUnavailableReturnsNotFound()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var services = CreateServices(out _, out var accounts, out var links, out _, out _);
        links.FindByTraineeAsync(traineeId, Arg.Any<CancellationToken>()).Returns(Link(trainerId, traineeId));
        accounts.GetByIdAsync(trainerId, Arg.Any<CancellationToken>()).Returns((AccountReadModel?)null);

        var result = await Resolve<IGetCurrentTrainerUseCase>(services).ExecuteAsync(
            new GetCurrentTrainerQuery(traineeId));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<TrainerRelationshipNotFoundError>();
    }

    private static ServiceCollection CreateServices(
        out IUserAccessReadService userAccess,
        out IAccountReadService accounts,
        out ICoachingActiveLinkPersistence links,
        out ICommandDispatcher commands,
        out IUnitOfWork unitOfWork)
    {
        userAccess = Substitute.For<IUserAccessReadService>();
        accounts = Substitute.For<IAccountReadService>();
        links = Substitute.For<ICoachingActiveLinkPersistence>();
        commands = Substitute.For<ICommandDispatcher>();
        unitOfWork = Substitute.For<IUnitOfWork>();
        var userAccessService = userAccess;
        var accountReadService = accounts;
        var activeLinkPersistence = links;
        var commandDispatcher = commands;
        var workUnit = unitOfWork;
        var services = new ServiceCollection();
        services.AddApplicationMapping(typeof(IMappingProfile).Assembly);
        services.AddScoped(_ => userAccessService);
        services.AddScoped(_ => accountReadService);
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

    private static CoachingActiveLinkFact Link(
        Id<User> trainerId,
        Id<User> traineeId,
        DateTimeOffset? createdAt = null)
    {
        var timestamp = createdAt ?? DateTimeOffset.UtcNow;
        return new CoachingActiveLinkFact(
            Id<TrainerTraineeLink>.New(),
            trainerId,
            traineeId,
            timestamp,
            timestamp);
    }
}
