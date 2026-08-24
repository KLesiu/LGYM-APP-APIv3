using FluentAssertions;
using LgymApi.Application.Coaching;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Coaching.TraineeNotes.Create;
using LgymApi.Application.Coaching.TraineeNotes.Delete;
using LgymApi.Application.Coaching.TraineeNotes.History;
using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.Coaching.TraineeNotes.TrainerList;
using LgymApi.Application.Coaching.TraineeNotes.Update;
using LgymApi.Application.Coaching.TraineeNotes.VisibleList;
using LgymApi.Application.Coaching.TraineeNotes.VisibleSingle;
using LgymApi.Application.Coaching.TraineeNotes.VisibleHistory;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories.Coaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using LgymApi.Resources;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class CoachingTraineeNoteSliceTests
{
    [Test]
    public async Task TrainerList_ReturnsMappedOwnedNotesWithoutCommit()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var dependencies = new Dependencies();
        dependencies.GrantAccess(trainerId, traineeId);
        var note = Note(trainerId, traineeId, content: "Current", isPinned: true);
        dependencies.Notes.GetNotesByTrainerAndTraineeAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns([note]);

        var result = await Resolve<IListTrainerNotesUseCase>(dependencies.CreateServices())
            .ExecuteAsync(new ListTrainerNotesQuery(trainerId, traineeId));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Should().Be(new TraineeNoteReadModel(
            note.Id,
            note.TrainerId,
            note.TraineeId,
            note.Title,
            note.Content,
            note.VisibleToTrainee,
            note.IsPinned,
            note.LastUpdatedByUserId,
            note.LastUpdatedAt,
            note.CreatedAt,
            note.UpdatedAt));
        await dependencies.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task VisibleList_PreservesPersistenceOrderAndMapsWithoutAccessOrWrites()
    {
        var traineeId = Id<User>.New();
        var trainerId = Id<User>.New();
        var first = Note(trainerId, traineeId, content: "Pinned", visibleToTrainee: true, isPinned: true);
        var second = Note(trainerId, traineeId, content: "Recent", visibleToTrainee: true);
        var dependencies = new Dependencies();
        using var cancellation = new CancellationTokenSource();
        dependencies.Notes.GetVisibleNotesByTraineeAsync(traineeId, cancellation.Token)
            .Returns([first, second]);

        var result = await Resolve<IListVisibleTraineeNotesUseCase>(dependencies.CreateServices())
            .ExecuteAsync(new ListVisibleTraineeNotesQuery(traineeId), cancellation.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(note => note.Id).Should().Equal(first.Id, second.Id);
        result.Value.Select(note => note.Content).Should().Equal("Pinned", "Recent");
        await dependencies.Notes.Received(1).GetVisibleNotesByTraineeAsync(traineeId, cancellation.Token);
        await dependencies.Access.DidNotReceive().GetAccessDecisionAsync(
            Arg.Any<Id<User>>(),
            Arg.Any<Id<User>>(),
            Arg.Any<CancellationToken>());
        await dependencies.Notes.DidNotReceive().AddNoteAsync(
            Arg.Any<CoachingTraineeNoteWriteModel>(),
            Arg.Any<CancellationToken>());
        await dependencies.Notes.DidNotReceive().UpdateNoteAsync(
            Arg.Any<CoachingTraineeNoteWriteModel>(),
            Arg.Any<CancellationToken>());
        await dependencies.Notes.DidNotReceive().AddHistoryEntryAsync(
            Arg.Any<CoachingTraineeNoteHistoryWriteModel>(),
            Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task VisibleSingle_MapsOwnedVisibleNoteAndRejectsInvalidOrUnavailableNotesWithoutWrites()
    {
        var traineeId = Id<User>.New();
        var otherTraineeId = Id<User>.New();
        var trainerId = Id<User>.New();
        var visible = Note(trainerId, traineeId, content: "Visible", visibleToTrainee: true);
        var privateNote = Note(trainerId, traineeId, content: "Private");
        var foreignNote = Note(trainerId, otherTraineeId, content: "Foreign", visibleToTrainee: true);
        var missingNoteId = Id<TraineeNote>.New();
        var dependencies = new Dependencies();
        dependencies.Notes.FindNoteByIdAsync(visible.Id, Arg.Any<CancellationToken>()).Returns(visible);
        dependencies.Notes.FindNoteByIdAsync(privateNote.Id, Arg.Any<CancellationToken>()).Returns(privateNote);
        dependencies.Notes.FindNoteByIdAsync(foreignNote.Id, Arg.Any<CancellationToken>()).Returns(foreignNote);
        dependencies.Notes.FindNoteByIdAsync(missingNoteId, Arg.Any<CancellationToken>())
            .Returns((CoachingTraineeNoteFact?)null);
        var useCase = Resolve<IGetVisibleTraineeNoteUseCase>(dependencies.CreateServices());

        var success = await useCase.ExecuteAsync(new GetVisibleTraineeNoteQuery(traineeId, visible.Id));
        var invalid = await useCase.ExecuteAsync(new GetVisibleTraineeNoteQuery(traineeId, Id<TraineeNote>.Empty));
        var missing = await useCase.ExecuteAsync(new GetVisibleTraineeNoteQuery(traineeId, missingNoteId));
        var privateResult = await useCase.ExecuteAsync(new GetVisibleTraineeNoteQuery(traineeId, privateNote.Id));
        var foreign = await useCase.ExecuteAsync(new GetVisibleTraineeNoteQuery(traineeId, foreignNote.Id));

        success.IsSuccess.Should().BeTrue();
        success.Value.Id.Should().Be(visible.Id);
        success.Value.Content.Should().Be("Visible");
        invalid.Error.Should().BeOfType<BadRequestError>();
        invalid.Error.Message.Should().Be(Messages.FieldRequired);
        missing.Error.Should().BeOfType<NotFoundError>();
        missing.Error.Message.Should().Be(Messages.DidntFind);
        privateResult.Error.Should().BeOfType<NotFoundError>();
        privateResult.Error.Message.Should().Be(Messages.DidntFind);
        foreign.Error.Should().BeOfType<NotFoundError>();
        foreign.Error.Message.Should().Be(Messages.DidntFind);
        await dependencies.Notes.DidNotReceive().FindNoteByIdAsync(
            Id<TraineeNote>.Empty,
            Arg.Any<CancellationToken>());
        await dependencies.Access.DidNotReceive().GetAccessDecisionAsync(
            Arg.Any<Id<User>>(),
            Arg.Any<Id<User>>(),
            Arg.Any<CancellationToken>());
        await dependencies.Notes.DidNotReceive().UpdateNoteAsync(
            Arg.Any<CoachingTraineeNoteWriteModel>(),
            Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task VisibleHistory_ReturnsOnlyOwnedVisibleNoteHistoryAndHidesUnavailableNotes()
    {
        var traineeId = Id<User>.New();
        var trainerId = Id<User>.New();
        var visible = Note(trainerId, traineeId, visibleToTrainee: true);
        var privateNote = Note(trainerId, traineeId);
        var changedAt = DateTimeOffset.UtcNow;
        var history = new CoachingTraineeNoteHistoryFact(
            Id<TraineeNoteHistory>.New(),
            visible.Id,
            trainerId,
            changedAt,
            null,
            "Visible content",
            "Updated",
            changedAt,
            changedAt,
            false,
            true);
        var dependencies = new Dependencies();
        dependencies.Notes.FindNoteByIdAsync(visible.Id, Arg.Any<CancellationToken>()).Returns(visible);
        dependencies.Notes.FindNoteByIdAsync(privateNote.Id, Arg.Any<CancellationToken>()).Returns(privateNote);
        dependencies.Notes.GetVisibleNoteHistoryAsync(visible.Id, Arg.Any<CancellationToken>()).Returns([history]);
        var useCase = Resolve<IGetVisibleTraineeNoteHistoryUseCase>(dependencies.CreateServices());

        var success = await useCase.ExecuteAsync(
            new GetVisibleTraineeNoteHistoryQuery(traineeId, visible.Id));
        var hidden = await useCase.ExecuteAsync(
            new GetVisibleTraineeNoteHistoryQuery(traineeId, privateNote.Id));

        success.IsSuccess.Should().BeTrue();
        success.Value.Should().ContainSingle().Which.NewContent.Should().Be("Visible content");
        hidden.Error.Should().BeOfType<NotFoundError>();
        await dependencies.Notes.Received(1).GetVisibleNoteHistoryAsync(
            visible.Id,
            Arg.Any<CancellationToken>());
        await dependencies.Notes.DidNotReceive().GetVisibleNoteHistoryAsync(
            privateNote.Id,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Create_NormalizesStagesHistoryCommitsThenNotifiesWithNullableTitle()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var operations = new List<string>();
        var dependencies = new Dependencies();
        CoachingTraineeNoteWriteModel? stagedNote = null;
        CoachingTraineeNoteHistoryWriteModel? stagedHistory = null;
        TraineeNoteUpdatedInAppNotificationCommand? notification = null;
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true))
            .AndDoes(_ => operations.Add("access"));
        dependencies.Notes.AddNoteAsync(Arg.Any<CoachingTraineeNoteWriteModel>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call =>
            {
                stagedNote = call.Arg<CoachingTraineeNoteWriteModel>();
                operations.Add("note");
            });
        dependencies.Notes.AddHistoryEntryAsync(Arg.Any<CoachingTraineeNoteHistoryWriteModel>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call =>
            {
                stagedHistory = call.Arg<CoachingTraineeNoteHistoryWriteModel>();
                operations.Add("history");
            });
        dependencies.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(2))
            .AndDoes(_ => operations.Add("commit"));
        dependencies.Commands.EnqueueAsync(Arg.Any<TraineeNoteUpdatedInAppNotificationCommand>())
            .Returns(Task.CompletedTask)
            .AndDoes(call =>
            {
                notification = call.Arg<TraineeNoteUpdatedInAppNotificationCommand>();
                operations.Add("notify");
            });
        dependencies.Notes.FindNoteByIdAsync(Arg.Any<Id<TraineeNote>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ToFact(stagedNote!))
            .AndDoes(_ => operations.Add("read"));

        var result = await Resolve<ICreateTraineeNoteUseCase>(dependencies.CreateServices()).ExecuteAsync(
            new CreateTraineeNoteCommand(
                trainerId,
                traineeId,
                new TraineeNoteUpsertData("   ", "  First note  ", true, true)));

        result.IsSuccess.Should().BeTrue();
        stagedNote.Should().NotBeNull();
        stagedNote!.Title.Should().BeNull();
        stagedNote.Content.Should().Be("First note");
        stagedNote.VisibleToTrainee.Should().BeTrue();
        stagedNote.IsPinned.Should().BeTrue();
        stagedNote.IsDeleted.Should().BeFalse();
        stagedHistory.Should().NotBeNull();
        stagedHistory!.TraineeNoteId.Should().Be(stagedNote.Id);
        stagedHistory.PreviousContent.Should().BeNull();
        stagedHistory.NewContent.Should().Be("First note");
        stagedHistory.ChangeType.Should().Be("Created");
        stagedHistory.ChangedAt.Should().BeOnOrAfter(stagedNote.LastUpdatedAt);
        notification.Should().NotBeNull();
        notification!.TraineeNoteId.Should().Be(stagedNote.Id);
        notification.TraineeId.Should().Be(traineeId);
        notification.TrainerId.Should().Be(trainerId);
        notification.NoteTitle.Should().BeNull();
        operations.Should().Equal("access", "note", "history", "commit", "notify", "read");
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Create_WithEmptyContentChecksRelationshipBeforeReturningBadRequest()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var dependencies = new Dependencies();
        dependencies.GrantAccess(trainerId, traineeId);

        var result = await Resolve<ICreateTraineeNoteUseCase>(dependencies.CreateServices()).ExecuteAsync(
            new CreateTraineeNoteCommand(
                trainerId,
                traineeId,
                new TraineeNoteUpsertData(null, "  ", false, false)));

        result.Error.Should().BeOfType<BadRequestError>();
        await dependencies.Access.Received(1).GetAccessDecisionAsync(
            trainerId,
            traineeId,
            Arg.Any<CancellationToken>());
        await dependencies.Notes.DidNotReceive().AddNoteAsync(
            Arg.Any<CoachingTraineeNoteWriteModel>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Update_UnsharesOwnedNoteAndNotifiesAfterTheAtomicCommit()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var existing = Note(trainerId, traineeId, title: "Old", content: "Old content", visibleToTrainee: true);
        var operations = new List<string>();
        var dependencies = new Dependencies();
        CoachingTraineeNoteWriteModel? stagedNote = null;
        CoachingTraineeNoteHistoryWriteModel? stagedHistory = null;
        dependencies.GrantAccess(trainerId, traineeId);
        dependencies.Notes.FindNoteByIdAsync(existing.Id, Arg.Any<CancellationToken>())
            .Returns(_ => stagedNote is null ? existing : ToFact(stagedNote));
        dependencies.Notes.UpdateNoteAsync(Arg.Any<CoachingTraineeNoteWriteModel>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call =>
            {
                stagedNote = call.Arg<CoachingTraineeNoteWriteModel>();
                operations.Add("note");
            });
        dependencies.Notes.AddHistoryEntryAsync(Arg.Any<CoachingTraineeNoteHistoryWriteModel>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call =>
            {
                stagedHistory = call.Arg<CoachingTraineeNoteHistoryWriteModel>();
                operations.Add("history");
            });
        dependencies.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(2))
            .AndDoes(_ => operations.Add("commit"));
        dependencies.Commands.EnqueueAsync(Arg.Any<TraineeNoteUpdatedInAppNotificationCommand>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => operations.Add("notify"));

        var result = await Resolve<IUpdateTraineeNoteUseCase>(dependencies.CreateServices()).ExecuteAsync(
            new UpdateTraineeNoteCommand(
                trainerId,
                traineeId,
                existing.Id,
                new TraineeNoteUpsertData("  New title  ", "  New content  ", false, true)));

        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("New title");
        result.Value.Content.Should().Be("New content");
        result.Value.VisibleToTrainee.Should().BeFalse();
        stagedHistory.Should().NotBeNull();
        stagedHistory!.PreviousContent.Should().Be("Old content");
        stagedHistory.NewContent.Should().Be("New content");
        stagedHistory.ChangeType.Should().Be("Updated");
        operations.Should().Equal("note", "history", "commit", "notify");
        await dependencies.Commands.Received(1).EnqueueAsync(
            Arg.Is<TraineeNoteUpdatedInAppNotificationCommand>(command =>
                command.TraineeNoteId == existing.Id
                && command.NoteTitle == "New title"));
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Update_InvisibleNoteRemainingInvisibleDoesNotNotify()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var existing = Note(trainerId, traineeId, visibleToTrainee: false);
        var dependencies = new Dependencies();
        CoachingTraineeNoteWriteModel? stagedNote = null;
        dependencies.GrantAccess(trainerId, traineeId);
        dependencies.Notes.FindNoteByIdAsync(existing.Id, Arg.Any<CancellationToken>())
            .Returns(_ => stagedNote is null ? existing : ToFact(stagedNote));
        dependencies.Notes.UpdateNoteAsync(Arg.Any<CoachingTraineeNoteWriteModel>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => stagedNote = call.Arg<CoachingTraineeNoteWriteModel>());

        var result = await Resolve<IUpdateTraineeNoteUseCase>(dependencies.CreateServices()).ExecuteAsync(
            new UpdateTraineeNoteCommand(
                trainerId,
                traineeId,
                existing.Id,
                new TraineeNoteUpsertData(null, "Still private", false, false)));

        result.IsSuccess.Should().BeTrue();
        await dependencies.Commands.DidNotReceive().EnqueueAsync(Arg.Any<IActionCommand>());
    }

    [Test]
    public async Task Update_WithEmptyContentRejectsBeforeRelationshipAccess()
    {
        var dependencies = new Dependencies();

        var result = await Resolve<IUpdateTraineeNoteUseCase>(dependencies.CreateServices()).ExecuteAsync(
            new UpdateTraineeNoteCommand(
                Id<User>.New(),
                Id<User>.New(),
                Id<TraineeNote>.New(),
                new TraineeNoteUpsertData(null, string.Empty, false, false)));

        result.Error.Should().BeOfType<BadRequestError>();
        await dependencies.Access.DidNotReceive().GetAccessDecisionAsync(
            Arg.Any<Id<User>>(),
            Arg.Any<Id<User>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Delete_SoftDeletesAndStagesDeletedHistoryAtOneCommitWithoutNotification()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var existing = Note(trainerId, traineeId, content: "Keep in history", visibleToTrainee: true, isPinned: true);
        var dependencies = new Dependencies();
        CoachingTraineeNoteWriteModel? deleted = null;
        CoachingTraineeNoteHistoryWriteModel? history = null;
        dependencies.GrantAccess(trainerId, traineeId);
        dependencies.Notes.FindNoteByIdAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);
        dependencies.Notes.UpdateNoteAsync(Arg.Any<CoachingTraineeNoteWriteModel>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => deleted = call.Arg<CoachingTraineeNoteWriteModel>());
        dependencies.Notes.AddHistoryEntryAsync(Arg.Any<CoachingTraineeNoteHistoryWriteModel>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => history = call.Arg<CoachingTraineeNoteHistoryWriteModel>());

        var result = await Resolve<IDeleteTraineeNoteUseCase>(dependencies.CreateServices())
            .ExecuteAsync(new DeleteTraineeNoteCommand(trainerId, traineeId, existing.Id));

        result.IsSuccess.Should().BeTrue();
        deleted.Should().NotBeNull();
        deleted!.IsDeleted.Should().BeTrue();
        deleted.VisibleToTrainee.Should().BeFalse();
        deleted.IsPinned.Should().BeTrue();
        deleted.Content.Should().Be("Keep in history");
        history.Should().NotBeNull();
        history!.PreviousContent.Should().Be("Keep in history");
        history.NewContent.Should().Be("Keep in history");
        history.ChangeType.Should().Be("Deleted");
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await dependencies.Commands.DidNotReceive().EnqueueAsync(Arg.Any<IActionCommand>());
    }

    [Test]
    public async Task History_ReturnsMappedRowsForOwnedNoteWithoutCommit()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var note = Note(trainerId, traineeId);
        var changedAt = DateTimeOffset.UtcNow;
        var history = new CoachingTraineeNoteHistoryFact(
            Id<TraineeNoteHistory>.New(),
            note.Id,
            trainerId,
            changedAt,
            "Before",
            "After",
            "Updated",
            changedAt,
            changedAt);
        var dependencies = new Dependencies();
        dependencies.GrantAccess(trainerId, traineeId);
        dependencies.Notes.FindNoteByIdAsync(note.Id, Arg.Any<CancellationToken>()).Returns(note);
        dependencies.Notes.GetNoteHistoryAsync(note.Id, Arg.Any<CancellationToken>()).Returns([history]);

        var result = await Resolve<IGetTraineeNoteHistoryUseCase>(dependencies.CreateServices())
            .ExecuteAsync(new GetTraineeNoteHistoryQuery(trainerId, traineeId, note.Id));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Should().Be(new TraineeNoteHistoryReadModel(
            history.Id,
            history.TraineeNoteId,
            history.ChangedByUserId,
            history.ChangedAt,
            history.PreviousContent,
            history.NewContent,
            history.ChangeType));
        await dependencies.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TrainerSlices_RejectForeignRelationshipForeignNoteAndEmptyNoteIdWithoutWrites()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var foreignTrainerId = Id<User>.New();
        var noteId = Id<TraineeNote>.New();
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, false));
        var services = dependencies.CreateServices();

        var foreignRelationship = await Resolve<IListTrainerNotesUseCase>(services)
            .ExecuteAsync(new ListTrainerNotesQuery(trainerId, traineeId));

        dependencies.GrantAccess(trainerId, traineeId);
        dependencies.Notes.FindNoteByIdAsync(noteId, Arg.Any<CancellationToken>())
            .Returns(Note(foreignTrainerId, traineeId, noteId: noteId));
        var foreignNote = await Resolve<IUpdateTraineeNoteUseCase>(services).ExecuteAsync(
            new UpdateTraineeNoteCommand(
                trainerId,
                traineeId,
                noteId,
                new TraineeNoteUpsertData(null, "Content", false, false)));
        var invalidId = await Resolve<IDeleteTraineeNoteUseCase>(services)
            .ExecuteAsync(new DeleteTraineeNoteCommand(trainerId, traineeId, Id<TraineeNote>.Empty));

        foreignRelationship.Error.Should().BeOfType<NotFoundError>();
        foreignNote.Error.Should().BeOfType<NotFoundError>();
        invalidId.Error.Should().BeOfType<BadRequestError>();
        await dependencies.Notes.DidNotReceive().UpdateNoteAsync(
            Arg.Any<CoachingTraineeNoteWriteModel>(),
            Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Create_WhenOwnerCommitFailsPersistsNeitherNoteNorHistoryAndDoesNotNotify()
    {
        var databaseName = $"coaching-note-atomic-{Id<CoachingTraineeNoteSliceTests>.New().ToString().Replace("-", string.Empty, StringComparison.Ordinal)}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        await using var database = new AppDbContext(options);
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var access = Substitute.For<ICoachingRelationshipAccessService>();
        var commands = Substitute.For<ICommandDispatcher>();
        access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);
        services.AddCoachingModule();
        services.AddScoped(_ => database);
        services.AddScoped<ICoachingTraineeNotePersistence, CoachingTraineeNotePersistenceRepository>();
        services.AddScoped(_ => access);
        services.AddScoped(_ => commands);
        services.AddScoped<IUnitOfWork>(_ => new ThrowingUnitOfWork());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Func<Task> act = () => scope.ServiceProvider.GetRequiredService<ICreateTraineeNoteUseCase>().ExecuteAsync(
            new CreateTraineeNoteCommand(
                trainerId,
                traineeId,
                new TraineeNoteUpsertData("Atomic", "Both or neither", true, false)));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("commit failed");
        database.ChangeTracker.Entries<TraineeNote>().Should().ContainSingle(entry => entry.State == EntityState.Added);
        database.ChangeTracker.Entries<TraineeNoteHistory>().Should().ContainSingle(entry => entry.State == EntityState.Added);
        await commands.DidNotReceive().EnqueueAsync(Arg.Any<IActionCommand>());

        await using var verificationDatabase = new AppDbContext(options);
        (await verificationDatabase.TraineeNotes.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await verificationDatabase.TraineeNoteHistories.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    private static TContract Resolve<TContract>(ServiceCollection services) where TContract : notnull
    {
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TContract>();
    }

    private static CoachingTraineeNoteFact Note(
        Id<User> trainerId,
        Id<User> traineeId,
        Id<TraineeNote>? noteId = null,
        string? title = "Title",
        string content = "Content",
        bool visibleToTrainee = false,
        bool isPinned = false)
    {
        var now = DateTimeOffset.UtcNow;
        return new CoachingTraineeNoteFact(
            noteId ?? Id<TraineeNote>.New(),
            trainerId,
            traineeId,
            title,
            content,
            visibleToTrainee,
            isPinned,
            trainerId,
            now,
            now,
            now);
    }

    private static CoachingTraineeNoteFact ToFact(CoachingTraineeNoteWriteModel note)
        => new(
            note.Id,
            note.TrainerId,
            note.TraineeId,
            note.Title,
            note.Content,
            note.VisibleToTrainee,
            note.IsPinned,
            note.LastUpdatedByUserId,
            note.LastUpdatedAt,
            note.LastUpdatedAt,
            note.LastUpdatedAt);

    private sealed class Dependencies
    {
        public ICoachingRelationshipAccessService Access { get; } = Substitute.For<ICoachingRelationshipAccessService>();
        public ICoachingTraineeNotePersistence Notes { get; } = Substitute.For<ICoachingTraineeNotePersistence>();
        public ICommandDispatcher Commands { get; } = Substitute.For<ICommandDispatcher>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

        public void GrantAccess(Id<User> trainerId, Id<User> traineeId)
            => Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
                .Returns(new CoachingRelationshipAccessDecision(true, true));

        public ServiceCollection CreateServices()
        {
            var services = new ServiceCollection();
            services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);
            services.AddCoachingModule();
            services.AddScoped(_ => Access);
            services.AddScoped(_ => Notes);
            services.AddScoped(_ => Commands);
            services.AddScoped(_ => UnitOfWork);
            return services;
        }
    }

    private sealed class ThrowingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromException<int>(new InvalidOperationException("commit failed"));

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
