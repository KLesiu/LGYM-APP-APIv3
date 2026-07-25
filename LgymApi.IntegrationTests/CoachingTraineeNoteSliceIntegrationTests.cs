using FluentAssertions;
using LgymApi.Application.Coaching.TraineeNotes.Create;
using LgymApi.Application.Coaching.TraineeNotes.Delete;
using LgymApi.Application.Coaching.TraineeNotes.History;
using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.Coaching.TraineeNotes.TrainerList;
using LgymApi.Application.Coaching.TraineeNotes.Update;
using LgymApi.Application.Coaching.TraineeNotes.VisibleList;
using LgymApi.Application.Coaching.TraineeNotes.VisibleSingle;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Data.SeedData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LgymApi.Resources;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class CoachingTraineeNoteSliceIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task TrainerNoteSlices_PreserveListCreateUpdateUnshareDeleteHistoryAndNotificationBehavior()
    {
        var trainer = await SeedUserAsync("slice-note-trainer", "slice-note-trainer@example.test");
        var trainee = await SeedUserAsync("slice-note-trainee", "slice-note-trainee@example.test");
        await SeedRelationshipAsync(trainer.Id, trainee.Id);

        Id<TraineeNote> noteId;
        using (var createScope = Factory.Services.CreateScope())
        {
            var result = await createScope.ServiceProvider.GetRequiredService<ICreateTraineeNoteUseCase>().ExecuteAsync(
                new CreateTraineeNoteCommand(
                    trainer.Id,
                    trainee.Id,
                    new TraineeNoteUpsertData("   ", "  Initial content  ", true, true)));
            result.IsSuccess.Should().BeTrue();
            result.Value.Title.Should().BeNull();
            result.Value.Content.Should().Be("Initial content");
            result.Value.VisibleToTrainee.Should().BeTrue();
            result.Value.IsPinned.Should().BeTrue();
            noteId = result.Value.Id;
        }

        using (var listScope = Factory.Services.CreateScope())
        {
            var result = await listScope.ServiceProvider.GetRequiredService<IListTrainerNotesUseCase>()
                .ExecuteAsync(new ListTrainerNotesQuery(trainer.Id, trainee.Id));
            result.Value.Should().ContainSingle(note => note.Id == noteId);
        }

        using (var updateScope = Factory.Services.CreateScope())
        {
            var result = await updateScope.ServiceProvider.GetRequiredService<IUpdateTraineeNoteUseCase>().ExecuteAsync(
                new UpdateTraineeNoteCommand(
                    trainer.Id,
                    trainee.Id,
                    noteId,
                    new TraineeNoteUpsertData("  Updated title  ", "  Updated content  ", false, false)));
            result.IsSuccess.Should().BeTrue();
            result.Value.Title.Should().Be("Updated title");
            result.Value.Content.Should().Be("Updated content");
            result.Value.VisibleToTrainee.Should().BeFalse();
        }

        using (var historyScope = Factory.Services.CreateScope())
        {
            var result = await historyScope.ServiceProvider.GetRequiredService<IGetTraineeNoteHistoryUseCase>()
                .ExecuteAsync(new GetTraineeNoteHistoryQuery(trainer.Id, trainee.Id, noteId));
            result.IsSuccess.Should().BeTrue();
            result.Value.Select(entry => entry.ChangeType).Should().Equal("Updated", "Created");
            result.Value.Single(entry => entry.ChangeType == "Updated").Should().Match<TraineeNoteHistoryReadModel>(entry =>
                entry.PreviousContent == "Initial content" && entry.NewContent == "Updated content");
        }

        using (var deleteScope = Factory.Services.CreateScope())
        {
            var result = await deleteScope.ServiceProvider.GetRequiredService<IDeleteTraineeNoteUseCase>()
                .ExecuteAsync(new DeleteTraineeNoteCommand(trainer.Id, trainee.Id, noteId));
            result.IsSuccess.Should().BeTrue();
        }

        using var verificationScope = Factory.Services.CreateScope();
        var database = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await database.TraineeNotes.IgnoreQueryFilters().SingleAsync(note => note.Id == noteId);
        persisted.IsDeleted.Should().BeTrue();
        persisted.VisibleToTrainee.Should().BeFalse();
        persisted.Content.Should().Be("Updated content");
        persisted.LastUpdatedByUserId.Should().Be(trainer.Id);
        var history = await database.TraineeNoteHistories
            .Where(entry => entry.TraineeNoteId == noteId)
            .OrderBy(entry => entry.ChangedAt)
            .ToListAsync();
        history.Select(entry => entry.ChangeType).Should().Equal("Created", "Updated", "Deleted");
        history.Last().PreviousContent.Should().Be("Updated content");
        history.Last().NewContent.Should().Be("Updated content");
        (await database.CommandEnvelopes.CountAsync(envelope =>
            envelope.CommandTypeFullName.Contains("TraineeNoteUpdatedInAppNotificationCommand"))).Should().Be(2);
    }

    [Test]
    public async Task TrainerNoteSlices_RejectForeignRelationshipForeignNoteEmptyContentAndInvalidNoteId()
    {
        var trainer = await SeedUserAsync("slice-note-access-trainer", "slice-note-access-trainer@example.test");
        var otherTrainer = await SeedUserAsync("slice-note-access-other", "slice-note-access-other@example.test");
        var trainee = await SeedUserAsync("slice-note-access-trainee", "slice-note-access-trainee@example.test");
        await SeedTrainerRoleAsync(trainer.Id);
        await SeedTrainerRoleAsync(otherTrainer.Id);

        using (var foreignRelationshipScope = Factory.Services.CreateScope())
        {
            var result = await foreignRelationshipScope.ServiceProvider.GetRequiredService<ICreateTraineeNoteUseCase>().ExecuteAsync(
                new CreateTraineeNoteCommand(
                    trainer.Id,
                    trainee.Id,
                    new TraineeNoteUpsertData(null, "Foreign", false, false)));
            result.Error.Should().BeOfType<NotFoundError>();
        }

        await SeedRelationshipAsync(trainer.Id, trainee.Id, addTrainerRole: false);
        Id<TraineeNote> foreignNoteId;
        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var foreignNote = new TraineeNote
            {
                Id = Id<TraineeNote>.New(),
                TrainerId = otherTrainer.Id,
                TraineeId = trainee.Id,
                Content = "Other trainer",
                LastUpdatedByUserId = otherTrainer.Id,
                LastUpdatedAt = DateTimeOffset.UtcNow
            };
            database.TraineeNotes.Add(foreignNote);
            await database.SaveChangesAsync();
            foreignNoteId = foreignNote.Id;
        }

        using var actionScope = Factory.Services.CreateScope();
        var services = actionScope.ServiceProvider;
        var foreignNoteResult = await services.GetRequiredService<IUpdateTraineeNoteUseCase>().ExecuteAsync(
            new UpdateTraineeNoteCommand(
                trainer.Id,
                trainee.Id,
                foreignNoteId,
                new TraineeNoteUpsertData(null, "Nope", false, false)));
        var emptyContent = await services.GetRequiredService<ICreateTraineeNoteUseCase>().ExecuteAsync(
            new CreateTraineeNoteCommand(
                trainer.Id,
                trainee.Id,
                new TraineeNoteUpsertData(null, " ", false, false)));
        var invalidNoteId = await services.GetRequiredService<IDeleteTraineeNoteUseCase>()
            .ExecuteAsync(new DeleteTraineeNoteCommand(trainer.Id, trainee.Id, Id<TraineeNote>.Empty));

        foreignNoteResult.Error.Should().BeOfType<NotFoundError>();
        emptyContent.Error.Should().BeOfType<BadRequestError>();
        invalidNoteId.Error.Should().BeOfType<BadRequestError>();
        (await services.GetRequiredService<AppDbContext>().TraineeNoteHistories.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task TraineeVisibleNoteSlices_PreserveOwnershipVisibilityGlobalFilterOrderAndNoTrackingReads()
    {
        var trainer = await SeedUserAsync("visible-note-trainer", "visible-note-trainer@example.test");
        var trainee = await SeedUserAsync("visible-note-trainee", "visible-note-trainee@example.test");
        var otherTrainee = await SeedUserAsync("visible-note-other", "visible-note-other@example.test");
        var baseline = DateTimeOffset.UtcNow.AddHours(-1);
        var pinned = VisibleNote(trainer.Id, trainee.Id, "Pinned", baseline, isPinned: true);
        var recent = VisibleNote(trainer.Id, trainee.Id, "Recent", baseline.AddMinutes(20));
        var older = VisibleNote(trainer.Id, trainee.Id, "Older", baseline.AddMinutes(10));
        var privateNote = VisibleNote(trainer.Id, trainee.Id, "Private", baseline.AddMinutes(30), visible: false);
        var deleted = VisibleNote(trainer.Id, trainee.Id, "Deleted", baseline.AddMinutes(40), isDeleted: true);
        var foreign = VisibleNote(trainer.Id, otherTrainee.Id, "Foreign", baseline.AddMinutes(50));

        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TraineeNotes.AddRange(pinned, recent, older, privateNote, deleted, foreign);
            await database.SaveChangesAsync();
        }

        using var readScope = Factory.Services.CreateScope();
        var services = readScope.ServiceProvider;
        var list = await services.GetRequiredService<IListVisibleTraineeNotesUseCase>()
            .ExecuteAsync(new ListVisibleTraineeNotesQuery(trainee.Id));
        var detail = services.GetRequiredService<IGetVisibleTraineeNoteUseCase>();
        var visibleResult = await detail.ExecuteAsync(new GetVisibleTraineeNoteQuery(trainee.Id, recent.Id));
        var privateResult = await detail.ExecuteAsync(new GetVisibleTraineeNoteQuery(trainee.Id, privateNote.Id));
        var deletedResult = await detail.ExecuteAsync(new GetVisibleTraineeNoteQuery(trainee.Id, deleted.Id));
        var foreignResult = await detail.ExecuteAsync(new GetVisibleTraineeNoteQuery(trainee.Id, foreign.Id));
        var missingResult = await detail.ExecuteAsync(
            new GetVisibleTraineeNoteQuery(trainee.Id, Id<TraineeNote>.New()));
        var invalidResult = await detail.ExecuteAsync(
            new GetVisibleTraineeNoteQuery(trainee.Id, Id<TraineeNote>.Empty));

        list.IsSuccess.Should().BeTrue();
        list.Value.Select(note => note.Id).Should().Equal(pinned.Id, recent.Id, older.Id);
        visibleResult.IsSuccess.Should().BeTrue();
        visibleResult.Value.Content.Should().Be("Recent");
        privateResult.Error.Should().BeOfType<NotFoundError>();
        deletedResult.Error.Should().BeOfType<NotFoundError>();
        foreignResult.Error.Should().BeOfType<NotFoundError>();
        missingResult.Error.Should().BeOfType<NotFoundError>();
        missingResult.Error.Message.Should().Be(Messages.DidntFind);
        invalidResult.Error.Should().BeOfType<BadRequestError>();
        invalidResult.Error.Message.Should().Be(Messages.FieldRequired);

        var readDatabase = services.GetRequiredService<AppDbContext>();
        readDatabase.ChangeTracker.Entries().Should().BeEmpty();
        (await readDatabase.TraineeNotes.IgnoreQueryFilters().CountAsync(note =>
            note.Id == pinned.Id
            || note.Id == recent.Id
            || note.Id == older.Id
            || note.Id == privateNote.Id
            || note.Id == deleted.Id
            || note.Id == foreign.Id)).Should().Be(6);
    }

    private async Task SeedRelationshipAsync(
        Id<User> trainerId,
        Id<User> traineeId,
        bool addTrainerRole = true)
    {
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (addTrainerRole)
        {
            database.UserRoles.Add(new UserRole
            {
                UserId = trainerId,
                RoleId = RoleSeedDataConfiguration.TrainerRoleSeedId
            });
        }

        database.TrainerTraineeLinks.Add(new TrainerTraineeLink
        {
            Id = Id<TrainerTraineeLink>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId
        });
        await database.SaveChangesAsync();
    }

    private async Task SeedTrainerRoleAsync(Id<User> trainerId)
    {
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.UserRoles.Add(new UserRole
        {
            UserId = trainerId,
            RoleId = RoleSeedDataConfiguration.TrainerRoleSeedId
        });
        await database.SaveChangesAsync();
    }

    private static TraineeNote VisibleNote(
        Id<User> trainerId,
        Id<User> traineeId,
        string content,
        DateTimeOffset lastUpdatedAt,
        bool visible = true,
        bool isPinned = false,
        bool isDeleted = false)
        => new()
        {
            Id = Id<TraineeNote>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            Content = content,
            VisibleToTrainee = visible,
            IsPinned = isPinned,
            LastUpdatedByUserId = trainerId,
            LastUpdatedAt = lastUpdatedAt,
            IsDeleted = isDeleted
        };
}
