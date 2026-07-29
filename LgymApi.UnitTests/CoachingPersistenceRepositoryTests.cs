using FluentAssertions;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories.Coaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class CoachingPersistenceRepositoryTests
{
    [Test]
    public async Task FactReader_ReturnsCompleteUnpagedInvitationAndDashboardFactsWithoutTracking()
    {
        await using var database = CreateDbContext();
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var linkedAt = DateTimeOffset.UtcNow.AddDays(-4);
        var invitationCreatedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var olderInvitation = CreateInvitation(
            trainerId,
            traineeId,
            invitationCreatedAt,
            ParseInvitationId("00000000-0000-0000-0000-000000000001"));
        var latestInvitation = CreateInvitation(
            trainerId,
            traineeId,
            invitationCreatedAt,
            ParseInvitationId("00000000-0000-0000-0000-000000000002"));
        var emailInvitation = CreateInvitation(trainerId, null, DateTimeOffset.UtcNow);
        database.TrainerTraineeLinks.Add(new TrainerTraineeLink
        {
            Id = Id<TrainerTraineeLink>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            CreatedAt = linkedAt,
            UpdatedAt = linkedAt
        });
        database.TrainerInvitations.AddRange(olderInvitation, latestInvitation, emailInvitation);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var reader = new CoachingFactReader(database, CreateMapper());

        var invitationFacts = await reader.GetInvitationFactsAsync(trainerId);
        var dashboardFacts = await reader.GetDashboardFactsAsync(trainerId);

        invitationFacts.Should().HaveCount(3);
        invitationFacts.Should().Contain(fact => fact.Id == emailInvitation.Id && !fact.TraineeId.HasValue);
        dashboardFacts.Should().ContainSingle();
        dashboardFacts[0].TraineeId.Should().Be(traineeId);
        dashboardFacts[0].ActiveLink!.CreatedAt.Should().Be(linkedAt);
        dashboardFacts[0].LatestInvitation!.Id.Should().Be(latestInvitation.Id);
        database.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Test]
    public async Task FocusedPersistenceRepositories_StageWritesAndReturnImmutableFacts()
    {
        await using var database = CreateDbContext();
        var mapper = CreateMapper();
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var invitationId = Id<TrainerInvitation>.New();
        var linkId = Id<TrainerTraineeLink>.New();
        var noteId = Id<TraineeNote>.New();
        var historyId = Id<TraineeNoteHistory>.New();
        var now = DateTimeOffset.UtcNow;
        var invitationRepository = new CoachingInvitationPersistenceRepository(database, mapper);
        var linkRepository = new CoachingActiveLinkPersistenceRepository(database, mapper);
        var noteRepository = new CoachingTraineeNotePersistenceRepository(database, mapper);

        await invitationRepository.AddAsync(new CoachingInvitationWriteModel(
            invitationId, trainerId, "trainee@example.test", traineeId, "code", TrainerInvitationStatus.Pending, now.AddDays(3), now, null));
        await linkRepository.AddAsync(new CoachingActiveLinkWriteModel(linkId, trainerId, traineeId));
        await noteRepository.AddNoteAsync(new CoachingTraineeNoteWriteModel(
            noteId, trainerId, traineeId, "Title", "Content", true, true, trainerId, now));
        await noteRepository.AddHistoryEntryAsync(new CoachingTraineeNoteHistoryWriteModel(
            historyId, noteId, trainerId, now, null, "Content", "Created"));

        database.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Added).Should().HaveCount(4);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        (await invitationRepository.FindByIdAsync(invitationId))!.Code.Should().Be("code");
        (await linkRepository.FindByTrainerAndTraineeAsync(trainerId, traineeId))!.Id.Should().Be(linkId);
        (await noteRepository.FindNoteByIdAsync(noteId))!.Content.Should().Be("Content");
        (await noteRepository.GetNoteHistoryAsync(noteId)).Should().ContainSingle(entry => entry.Id == historyId);
        database.ChangeTracker.Entries().Should().BeEmpty();

        await noteRepository.UpdateNoteAsync(new CoachingTraineeNoteWriteModel(
            noteId, trainerId, traineeId, null, "Updated", false, false, trainerId, now.AddMinutes(1), true));
        database.ChangeTracker.Entries<TraineeNote>().Should().ContainSingle(entry => entry.State == EntityState.Modified);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var deletedNote = await database.TraineeNotes.IgnoreQueryFilters().SingleAsync(note => note.Id == noteId);
        deletedNote.Content.Should().Be("Updated");
        deletedNote.VisibleToTrainee.Should().BeFalse();
        deletedNote.IsDeleted.Should().BeTrue();
    }

    [Test]
    public async Task InvitationResponseUpdate_StagesExpiredStateWithoutBindingTrainee()
    {
        await using var database = CreateDbContext();
        var trainerId = Id<User>.New();
        var invitation = CreateInvitation(trainerId, null, DateTimeOffset.UtcNow.AddDays(-1));
        database.TrainerInvitations.Add(invitation);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var repository = new CoachingInvitationPersistenceRepository(database, CreateMapper());
        var respondedAt = DateTimeOffset.UtcNow;

        await repository.UpdateResponseAsync(new CoachingInvitationResponseUpdateModel(
            invitation.Id,
            null,
            TrainerInvitationStatus.Expired,
            respondedAt));

        var entry = database.ChangeTracker.Entries<TrainerInvitation>().Single();
        entry.Property(candidate => candidate.Status).IsModified.Should().BeTrue();
        entry.Property(candidate => candidate.RespondedAt).IsModified.Should().BeTrue();
        entry.Property(candidate => candidate.TraineeId).IsModified.Should().BeFalse();
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var persisted = await database.TrainerInvitations.SingleAsync(candidate => candidate.Id == invitation.Id);
        persisted.Status.Should().Be(TrainerInvitationStatus.Expired);
        persisted.RespondedAt.Should().Be(respondedAt);
        persisted.TraineeId.Should().BeNull();
    }

    [Test]
    public void FocusedPersistenceContracts_AreRegisteredOnceAndMapEveryFactField()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase($"coaching-persistence-di-{Id<CoachingPersistenceRepositoryTests>.New().ToString().Replace("-", string.Empty, StringComparison.Ordinal)}"));
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);
        services.AddCoachingInfrastructure();
        var contracts = new[]
        {
            typeof(ICoachingInvitationPersistence),
            typeof(ICoachingActiveLinkPersistence),
            typeof(ICoachingFactReader),
            typeof(ICoachingTraineeNotePersistence)
        };

        foreach (var contract in contracts)
        {
            services.Count(descriptor => descriptor.ServiceType == contract).Should().Be(1);
        }

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<IMapper>();
        var invitation = CreateInvitation(Id<User>.New(), Id<User>.New(), DateTimeOffset.UtcNow);
        var activeLink = new CoachingActiveLinkFact(
            Id<TrainerTraineeLink>.New(),
            invitation.TrainerId,
            invitation.TraineeId!.Value,
            invitation.CreatedAt,
            invitation.UpdatedAt);

        scope.ServiceProvider.GetRequiredService<ICoachingInvitationPersistence>().Should().BeOfType<CoachingInvitationPersistenceRepository>();
        scope.ServiceProvider.GetRequiredService<ICoachingActiveLinkPersistence>().Should().BeOfType<CoachingActiveLinkPersistenceRepository>();
        scope.ServiceProvider.GetRequiredService<ICoachingFactReader>().Should().BeOfType<CoachingFactReader>();
        scope.ServiceProvider.GetRequiredService<ICoachingTraineeNotePersistence>().Should().BeOfType<CoachingTraineeNotePersistenceRepository>();
        mapper.Map<TrainerInvitation, CoachingInvitationFact>(invitation, mapper.CreateContext()).Should().BeEquivalentTo(new CoachingInvitationFact(
            invitation.Id,
            invitation.TrainerId,
            invitation.InviteeEmail,
            invitation.TraineeId,
            invitation.Code,
            invitation.Status,
            invitation.ExpiresAt,
            invitation.RespondedAt,
            invitation.CreatedAt,
            invitation.UpdatedAt));
        mapper.Map<CoachingDashboardSource, CoachingDashboardFact>(
            new CoachingDashboardSource(activeLink.TraineeId, activeLink, null),
            mapper.CreateContext()).Should().BeEquivalentTo(new CoachingDashboardFact(activeLink.TraineeId, activeLink, null));
    }

    private static AppDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"coaching-persistence-{Id<CoachingPersistenceRepositoryTests>.New().ToString().Replace("-", string.Empty, StringComparison.Ordinal)}")
            .Options);

    private static IMapper CreateMapper()
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);
        return services.BuildServiceProvider().GetRequiredService<IMapper>();
    }

    private static TrainerInvitation CreateInvitation(
        Id<User> trainerId,
        Id<User>? traineeId,
        DateTimeOffset createdAt,
        Id<TrainerInvitation>? invitationId = null)
        => new()
        {
            Id = invitationId ?? Id<TrainerInvitation>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            InviteeEmail = "trainee@example.test",
            Code = Id<TrainerInvitation>.New().ToString().Replace("-", string.Empty, StringComparison.Ordinal),
            Status = TrainerInvitationStatus.Pending,
            ExpiresAt = createdAt.AddDays(7),
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };

    private static Id<TrainerInvitation> ParseInvitationId(string value) =>
        Id<TrainerInvitation>.TryParse(value, out var invitationId)
            ? invitationId
            : throw new ArgumentException("Invitation ID must be valid.", nameof(value));
}
