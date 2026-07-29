using FluentAssertions;
using LgymApi.Application.Coaching;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Invitations.Create;
using LgymApi.Application.Coaching.Invitations.CreateByEmail;
using LgymApi.Application.Coaching.Invitations.List;
using LgymApi.Application.Coaching.Invitations.ListPaginated;
using LgymApi.Application.Coaching.Invitations.Models;
using LgymApi.Application.Coaching.Invitations.PublicStatus;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Identity.Contracts.Access;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Pagination;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class CoachingInvitationSliceTests
{
    [Test]
    public async Task Create_ReusesActivePendingInvitationWithoutWritesOrCommands()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var invitation = Invitation(trainerId, traineeId, expiresAt: DateTimeOffset.UtcNow.AddDays(1));
        var services = CreateServices(out var userAccess, out var accounts, out var invitations, out var links, out var _, out var commands, out var unitOfWork, out var _);
        userAccess.IsTrainerAsync(trainerId, Arg.Any<CancellationToken>()).Returns(true);
        accounts.GetByIdAsync(traineeId, Arg.Any<CancellationToken>()).Returns(Account(traineeId));
        links.HasForTraineeAsync(traineeId, Arg.Any<CancellationToken>()).Returns(false);
        invitations.FindPendingAsync(trainerId, traineeId, Arg.Any<CancellationToken>()).Returns(invitation);

        var result = await Resolve<ICreateInvitationUseCase>(services).ExecuteAsync(new CreateInvitationCommand(trainerId, traineeId));

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(invitation.Id);
        await invitations.DidNotReceive().AddAsync(Arg.Any<CoachingInvitationWriteModel>(), Arg.Any<CancellationToken>());
        await commands.DidNotReceive().EnqueueAsync(Arg.Any<IActionCommand>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateByEmail_NormalizesAndQueuesBothExistingCommandsForActiveAccount()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var services = CreateServices(out var userAccess, out var accounts, out var invitations, out var links, out var _, out var commands, out var unitOfWork, out var _);
        userAccess.IsTrainerAsync(trainerId, Arg.Any<CancellationToken>()).Returns(true);
        accounts.GetByIdAsync(trainerId, Arg.Any<CancellationToken>()).Returns(Account(trainerId, "trainer@example.test"));
        accounts.GetByEmailAsync("trainee@example.test", Arg.Any<CancellationToken>()).Returns(Account(traineeId, "trainee@example.test"));
        invitations.FindPendingByEmailAsync(trainerId, "trainee@example.test", Arg.Any<CancellationToken>()).Returns((CoachingInvitationFact?)null);
        links.HasForTraineeAsync(traineeId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await Resolve<ICreateInvitationByEmailUseCase>(services).ExecuteAsync(
            new CreateInvitationByEmailCommand(trainerId, " Trainee@Example.Test ", "pl", "Europe/Warsaw"));

        result.IsSuccess.Should().BeTrue();
        await invitations.Received(1).AddAsync(
            Arg.Is<CoachingInvitationWriteModel>(value => value.InviteeEmail == "trainee@example.test" && value.TraineeId == traineeId),
            Arg.Any<CancellationToken>());
        await commands.Received(1).EnqueueAsync(Arg.Any<InvitationCreatedCommand>());
        await commands.Received(1).EnqueueAsync(
            Arg.Is<TrainerInvitationCreatedInAppNotificationCommand>(value => value.TrainerId == trainerId && value.TraineeId == traineeId));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task List_ExpiresPendingRowsAndCommitsOnceBeforeReturningNewestFirst()
    {
        var trainerId = Id<User>.New();
        var expired = Invitation(trainerId, Id<User>.New(), createdAt: DateTimeOffset.UtcNow.AddDays(-2), expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var current = Invitation(trainerId, Id<User>.New(), createdAt: DateTimeOffset.UtcNow.AddDays(-1), expiresAt: DateTimeOffset.UtcNow.AddDays(1));
        var services = CreateServices(out var userAccess, out var _, out var invitations, out var _, out var facts, out var _, out var unitOfWork, out var _);
        userAccess.IsTrainerAsync(trainerId, Arg.Any<CancellationToken>()).Returns(true);
        facts.GetInvitationFactsAsync(trainerId, Arg.Any<CancellationToken>()).Returns([expired, current]);

        var result = await Resolve<IListInvitationsUseCase>(services).ExecuteAsync(new ListInvitationsQuery(trainerId));

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(value => value.Id).Should().Equal(current.Id, expired.Id);
        result.Value.Single(value => value.Id == expired.Id).Status.Should().Be(TrainerInvitationStatus.Expired);
        await invitations.Received(1).ExpireAsync(expired.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ListPaginated_BatchEnrichesBeforePassingEveryAvailableRowToPagination()
    {
        var trainerId = Id<User>.New();
        var activeTraineeId = Id<User>.New();
        var missingTraineeId = Id<User>.New();
        var active = Invitation(trainerId, activeTraineeId, createdAt: DateTimeOffset.UtcNow.AddDays(-2));
        var missing = Invitation(trainerId, missingTraineeId, createdAt: DateTimeOffset.UtcNow.AddDays(-1));
        var emailOnly = Invitation(trainerId, null, createdAt: DateTimeOffset.UtcNow);
        var services = CreateServices(out var userAccess, out var accounts, out var _, out var _, out var facts, out var _, out var _, out var pagination);
        userAccess.IsTrainerAsync(trainerId, Arg.Any<CancellationToken>()).Returns(true);
        facts.GetInvitationFactsAsync(trainerId, Arg.Any<CancellationToken>()).Returns([active, missing, emailOnly]);
        accounts.GetByIdsAsync(Arg.Any<IReadOnlyList<Id<User>>>(), Arg.Any<CancellationToken>()).Returns([Account(activeTraineeId)]);
        pagination.ExecuteAsync<InvitationReadModel>(Arg.Any<Func<IQueryable<InvitationReadModel>>>(), Arg.Any<FilterInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Result<Pagination<InvitationReadModel>, LgymApi.Application.BuildingBlocks.Errors.AppError>.Success(new Pagination<InvitationReadModel>
            {
                Items = call.Arg<Func<IQueryable<InvitationReadModel>>>()().ToList(),
                Page = 1,
                PageSize = 20,
                TotalCount = call.Arg<Func<IQueryable<InvitationReadModel>>>()().Count()
            }));

        var result = await Resolve<IListPaginatedInvitationsUseCase>(services).ExecuteAsync(
            new ListPaginatedInvitationsQuery(trainerId, new FilterInput()));

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Items.Select(value => value.Id).Should().BeEquivalentTo([active.Id, emailOnly.Id]);
        await accounts.Received(1).GetByIdsAsync(
            Arg.Is<IReadOnlyList<Id<User>>>(ids => ids.SequenceEqual(new[] { activeTraineeId, missingTraineeId })),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublicStatus_RequiresExactCodeAndUsesIdentityForEmailOnlyInvitation()
    {
        var trainerId = Id<User>.New();
        var invitationId = Id<TrainerInvitation>.New();
        var services = CreateServices(out var _, out var accounts, out var invitations, out var _, out var _, out var _, out var _, out var _);
        invitations.FindByIdAndCodeAsync(invitationId, "exact-code", Arg.Any<CancellationToken>()).Returns(
            Invitation(trainerId, null, invitationId: invitationId, inviteeEmail: "new-user@example.test"));
        accounts.GetByEmailAsync("new-user@example.test", Arg.Any<CancellationToken>()).Returns(Account(Id<User>.New(), "new-user@example.test"));

        var result = await Resolve<IPublicInvitationStatusUseCase>(services).ExecuteAsync(
            new PublicInvitationStatusQuery(invitationId, "exact-code"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(TrainerInvitationStatus.Pending);
        result.Value.UserExists.Should().BeTrue();
    }

    private static ServiceCollection CreateServices(
        out IUserAccessReadService userAccess,
        out IAccountReadService accounts,
        out ICoachingInvitationPersistence invitations,
        out ICoachingActiveLinkPersistence links,
        out ICoachingFactReader facts,
        out ICommandDispatcher commands,
        out IUnitOfWork unitOfWork,
        out IQueryPaginationService pagination)
    {
        userAccess = Substitute.For<IUserAccessReadService>();
        accounts = Substitute.For<IAccountReadService>();
        invitations = Substitute.For<ICoachingInvitationPersistence>();
        links = Substitute.For<ICoachingActiveLinkPersistence>();
        facts = Substitute.For<ICoachingFactReader>();
        commands = Substitute.For<ICommandDispatcher>();
        unitOfWork = Substitute.For<IUnitOfWork>();
        pagination = Substitute.For<IQueryPaginationService>();
        var userAccessService = userAccess;
        var accountReadService = accounts;
        var invitationPersistence = invitations;
        var activeLinkPersistence = links;
        var factReader = facts;
        var commandDispatcher = commands;
        var workUnit = unitOfWork;
        var paginationService = pagination;
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);
        services.AddScoped(_ => userAccessService);
        services.AddScoped(_ => accountReadService);
        services.AddScoped(_ => invitationPersistence);
        services.AddScoped(_ => activeLinkPersistence);
        services.AddScoped(_ => factReader);
        services.AddScoped(_ => commandDispatcher);
        services.AddScoped(_ => workUnit);
        services.AddScoped(_ => paginationService);
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
        Id<TrainerInvitation>? invitationId = null,
        string inviteeEmail = "trainee@example.test",
        DateTimeOffset? createdAt = null,
        DateTimeOffset? expiresAt = null)
    {
        var timestamp = createdAt ?? DateTimeOffset.UtcNow;
        return new CoachingInvitationFact(
            invitationId ?? Id<TrainerInvitation>.New(),
            trainerId,
            inviteeEmail,
            traineeId,
            "CODE00000001",
            TrainerInvitationStatus.Pending,
            expiresAt ?? timestamp.AddDays(7),
            null,
            timestamp,
            timestamp);
    }
}
