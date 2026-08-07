using FluentAssertions;
using LgymApi.Api.Features.Trainer.Contracts;
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
using System.Net;
using System.Net.Http.Json;

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

    [Test]
    [Authorization.AuthorizationEvidence("GET", "/api/trainee/notes", "own", "owner-allow")]
    [Authorization.AuthorizationEvidence("GET", "/api/trainee/notes", "own", "no-client-subject")]
    public async Task TraineeNotesRoute_ReturnsOnlyVisibleNotesForAuthenticatedActor()
    {
        var trainer = await SeedUserAsync("http-note-trainer", "http-note-trainer@example.test");
        var trainee = await SeedUserAsync("http-note-trainee", "http-note-trainee@example.test");
        var otherTrainee = await SeedUserAsync("http-note-other", "http-note-other@example.test");
        var visible = VisibleNote(trainer.Id, trainee.Id, "HTTP visible", DateTimeOffset.UtcNow);
        var hidden = VisibleNote(trainer.Id, trainee.Id, "HTTP hidden", DateTimeOffset.UtcNow, visible: false);
        var deleted = VisibleNote(trainer.Id, trainee.Id, "HTTP deleted", DateTimeOffset.UtcNow, isDeleted: true);
        var foreign = VisibleNote(trainer.Id, otherTrainee.Id, "HTTP foreign", DateTimeOffset.UtcNow);

        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TraineeNotes.AddRange(visible, hidden, deleted, foreign);
            await database.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainee.Id);
        using var response = await Client.GetAsync("/api/trainee/notes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<TraineeNoteDto>>();
        body.Should().NotBeNull();
        body!.Select(note => note.Id).Should().Equal(visible.Id.ToString());
        body.Single().Content.Should().Be("HTTP visible");
        var responseText = await response.Content.ReadAsStringAsync();
        responseText.Should().NotContain(hidden.Id.ToString());
        responseText.Should().NotContain(hidden.Content);
        responseText.Should().NotContain(deleted.Id.ToString());
        responseText.Should().NotContain(deleted.Content);
        responseText.Should().NotContain(foreign.Id.ToString());
        responseText.Should().NotContain(foreign.Content);
    }

    [Test]
    [Authorization.AuthorizationEvidence("GET", "/api/trainee/notes", "own", "anonymous-denial")]
    public async Task TraineeNotesRoute_WithoutAuthentication_ReturnsUnauthorizedWithoutProtectedContent()
    {
        var trainer = await SeedUserAsync("http-anonymous-note-trainer", "http-anonymous-note-trainer@example.test");
        var trainee = await SeedUserAsync("http-anonymous-note-trainee", "http-anonymous-note-trainee@example.test");
        var visible = VisibleNote(trainer.Id, trainee.Id, "HTTP anonymous protected", DateTimeOffset.UtcNow);

        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TraineeNotes.Add(visible);
            await database.SaveChangesAsync();
        }

        ClearAuthorizationHeader();
        using var response = await Client.GetAsync("/api/trainee/notes");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var responseText = await response.Content.ReadAsStringAsync();
        responseText.Should().NotContain(visible.Content);
        responseText.Should().NotContain(visible.Id.ToString());
    }

    [Test]
    [Authorization.AuthorizationEvidence("GET", "/api/trainee/notes/{noteId}", "own", "owner-allow")]
    [Authorization.AuthorizationEvidence("GET", "/api/trainee/notes/{noteId}", "own", "no-client-subject")]
    [Authorization.AuthorizationEvidence("GET", "/api/trainee/notes/{noteId}", "own", "foreign-object-denial-no-mutation")]
    public async Task TraineeNoteDetailRoute_ReturnsVisibleNoteAndHidesProtectedNotes()
    {
        var trainer = await SeedUserAsync("http-detail-note-trainer", "http-detail-note-trainer@example.test");
        var trainee = await SeedUserAsync("http-detail-note-trainee", "http-detail-note-trainee@example.test");
        var otherTrainee = await SeedUserAsync("http-detail-note-other", "http-detail-note-other@example.test");
        var visible = VisibleNote(trainer.Id, trainee.Id, "HTTP detail visible", DateTimeOffset.UtcNow);
        var hidden = VisibleNote(trainer.Id, trainee.Id, "HTTP detail hidden", DateTimeOffset.UtcNow, visible: false);
        var deleted = VisibleNote(trainer.Id, trainee.Id, "HTTP detail deleted", DateTimeOffset.UtcNow, isDeleted: true);
        var foreign = VisibleNote(trainer.Id, otherTrainee.Id, "HTTP detail foreign", DateTimeOffset.UtcNow);

        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TraineeNotes.AddRange(visible, hidden, deleted, foreign);
            await database.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainee.Id);
        using var visibleResponse = await Client.GetAsync($"/api/trainee/notes/{visible.Id}");
        visibleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var visibleBody = await visibleResponse.Content.ReadFromJsonAsync<TraineeNoteDto>();
        visibleBody.Should().NotBeNull();
        visibleBody!.Id.Should().Be(visible.Id.ToString());
        visibleBody.Content.Should().Be(visible.Content);

        foreach (var protectedNote in new[] { hidden, deleted, foreign })
        {
            using var deniedResponse = await Client.GetAsync($"/api/trainee/notes/{protectedNote.Id}");
            deniedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var deniedText = await deniedResponse.Content.ReadAsStringAsync();
            deniedText.Should().NotContain(protectedNote.Id.ToString());
            deniedText.Should().NotContain(protectedNote.Content);
        }
    }

    [Test]
    [Authorization.AuthorizationEvidence("GET", "/api/trainee/notes/{noteId}", "own", "anonymous-denial")]
    public async Task TraineeNoteDetailRoute_WithoutAuthentication_ReturnsUnauthorizedWithoutProtectedContent()
    {
        var trainer = await SeedUserAsync("http-anonymous-detail-trainer", "http-anonymous-detail-trainer@example.test");
        var trainee = await SeedUserAsync("http-anonymous-detail-trainee", "http-anonymous-detail-trainee@example.test");
        var visible = VisibleNote(trainer.Id, trainee.Id, "HTTP anonymous detail protected", DateTimeOffset.UtcNow);

        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TraineeNotes.Add(visible);
            await database.SaveChangesAsync();
        }

        ClearAuthorizationHeader();
        using var response = await Client.GetAsync($"/api/trainee/notes/{visible.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var responseText = await response.Content.ReadAsStringAsync();
        responseText.Should().NotContain(visible.Id.ToString());
        responseText.Should().NotContain(visible.Content);
    }

    [Test]
    [Authorization.AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/notes", "trainer-shared", "active-relationship-allow")]
    [Authorization.AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/notes", "trainer-shared", "unrelated-relationship-denial")]
    [Authorization.AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/notes", "trainer-shared", "former-relationship-denial")]
    [Authorization.AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/notes", "trainer-shared", "foreign-object-denial-no-mutation")]
    [Authorization.AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/notes", "trainer-shared", "anonymous-denial")]
    public async Task TrainerTraineeNotesRoute_IsRelationshipScopedAndNonDisclosing()
    {
        var trainer = await SeedUserAsync("http-trainer-notes-trainer", "http-trainer-notes-trainer@example.test");
        var linked = await SeedUserAsync("http-trainer-notes-linked", "http-trainer-notes-linked@example.test");
        var unrelated = await SeedUserAsync("http-trainer-notes-unrelated", "http-trainer-notes-unrelated@example.test");
        var ordinaryUser = await SeedUserAsync("http-trainer-notes-ordinary", "http-trainer-notes-ordinary@example.test");
        var note = VisibleNote(trainer.Id, linked.Id, "HTTP trainer note protected", DateTimeOffset.UtcNow);

        await SeedRelationshipAsync(trainer.Id, linked.Id);
        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TraineeNotes.Add(note);
            await database.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        using var ownerResponse = await Client.GetAsync($"/api/trainer/trainees/{linked.Id}/notes");
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerNotes = await ownerResponse.Content.ReadFromJsonAsync<List<TraineeNoteDto>>();
        ownerNotes.Should().NotBeNull();
        ownerNotes!.Should().ContainSingle(item => item.Id == note.Id.ToString() && item.Content == note.Content);

        using var unrelatedResponse = await Client.GetAsync($"/api/trainer/trainees/{unrelated.Id}/notes");
        unrelatedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var unrelatedText = await unrelatedResponse.Content.ReadAsStringAsync();
        unrelatedText.Should().NotContain(note.Id.ToString());
        unrelatedText.Should().NotContain(note.Content);

        using (var unlinkScope = Factory.Services.CreateScope())
        {
            var database = unlinkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await database.TrainerTraineeLinks
                .SingleAsync(candidate => candidate.TrainerId == trainer.Id && candidate.TraineeId == linked.Id);
            database.TrainerTraineeLinks.Remove(link);
            await database.SaveChangesAsync();
        }

        using var formerResponse = await Client.GetAsync($"/api/trainer/trainees/{linked.Id}/notes");
        formerResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var formerText = await formerResponse.Content.ReadAsStringAsync();
        formerText.Should().NotContain(note.Id.ToString());
        formerText.Should().NotContain(note.Content);

        SetAuthorizationHeader(ordinaryUser.Id);
        using var ordinaryResponse = await Client.GetAsync($"/api/trainer/trainees/{linked.Id}/notes");
        ordinaryResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var ordinaryText = await ordinaryResponse.Content.ReadAsStringAsync();
        ordinaryText.Should().NotContain(note.Id.ToString());
        ordinaryText.Should().NotContain(note.Content);

        ClearAuthorizationHeader();
        using var anonymousResponse = await Client.GetAsync($"/api/trainer/trainees/{linked.Id}/notes");
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var anonymousText = await anonymousResponse.Content.ReadAsStringAsync();
        anonymousText.Should().NotContain(note.Id.ToString());
        anonymousText.Should().NotContain(note.Content);
    }

    [Test]
    [Authorization.AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/notes", "trainer-shared", "active-relationship-allow")]
    [Authorization.AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/notes", "trainer-shared", "unrelated-relationship-denial")]
    [Authorization.AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/notes", "trainer-shared", "former-relationship-denial")]
    [Authorization.AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/notes", "trainer-shared", "foreign-object-denial-no-mutation")]
    [Authorization.AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/notes", "trainer-shared", "anonymous-denial")]
    public async Task TrainerTraineeNoteCreateRoute_IsRelationshipScopedAndNonDisclosing()
    {
        var trainer = await SeedUserAsync("http-create-note-trainer", "http-create-note-trainer@example.test");
        var linked = await SeedUserAsync("http-create-note-linked", "http-create-note-linked@example.test");
        var unrelated = await SeedUserAsync("http-create-note-unrelated", "http-create-note-unrelated@example.test");
        var ordinaryUser = await SeedUserAsync("http-create-note-ordinary", "http-create-note-ordinary@example.test");
        await SeedRelationshipAsync(trainer.Id, linked.Id);

        SetAuthorizationHeader(trainer.Id);
        using var ownerResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{linked.Id}/notes", new
        {
            title = "HTTP created note",
            content = "HTTP created protected content",
            visibleToTrainee = true,
            isPinned = false
        });
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var ownerText = await ownerResponse.Content.ReadAsStringAsync();
        ownerText.Should().Contain("HTTP created protected content");

        using var unrelatedResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{unrelated.Id}/notes", new
        {
            title = "HTTP unrelated note",
            content = "HTTP unrelated protected content",
            visibleToTrainee = true,
            isPinned = false
        });
        unrelatedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await unrelatedResponse.Content.ReadAsStringAsync()).Should().NotContain("HTTP unrelated protected content");

        using (var unlinkScope = Factory.Services.CreateScope())
        {
            var database = unlinkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await database.TrainerTraineeLinks
                .SingleAsync(candidate => candidate.TrainerId == trainer.Id && candidate.TraineeId == linked.Id);
            database.TrainerTraineeLinks.Remove(link);
            await database.SaveChangesAsync();
        }

        using var formerResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{linked.Id}/notes", new
        {
            title = "HTTP former note",
            content = "HTTP former protected content",
            visibleToTrainee = true,
            isPinned = false
        });
        formerResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await formerResponse.Content.ReadAsStringAsync()).Should().NotContain("HTTP former protected content");

        SetAuthorizationHeader(ordinaryUser.Id);
        using var ordinaryResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{linked.Id}/notes", new
        {
            title = "HTTP ordinary note",
            content = "HTTP ordinary protected content",
            visibleToTrainee = true,
            isPinned = false
        });
        ordinaryResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ordinaryResponse.Content.ReadAsStringAsync()).Should().NotContain("HTTP ordinary protected content");

        ClearAuthorizationHeader();
        using var anonymousResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{linked.Id}/notes", new
        {
            title = "HTTP anonymous note",
            content = "HTTP anonymous protected content",
            visibleToTrainee = true,
            isPinned = false
        });
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymousResponse.Content.ReadAsStringAsync()).Should().NotContain("HTTP anonymous protected content");
    }

    [Test]
    [Authorization.AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/notes/{noteId}/delete", "trainer-shared", "active-relationship-allow")]
    [Authorization.AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/notes/{noteId}/delete", "trainer-shared", "unrelated-relationship-denial")]
    [Authorization.AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/notes/{noteId}/delete", "trainer-shared", "former-relationship-denial")]
    [Authorization.AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/notes/{noteId}/delete", "trainer-shared", "foreign-object-denial-no-mutation")]
    [Authorization.AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/notes/{noteId}/delete", "trainer-shared", "anonymous-denial")]
    public async Task TrainerTraineeNoteDeleteRoute_IsRelationshipScopedAndNonDisclosing()
    {
        var trainer = await SeedUserAsync("http-delete-note-trainer", "http-delete-note-trainer@example.test");
        var linked = await SeedUserAsync("http-delete-note-linked", "http-delete-note-linked@example.test");
        var unrelated = await SeedUserAsync("http-delete-note-unrelated", "http-delete-note-unrelated@example.test");
        var ordinaryUser = await SeedUserAsync("http-delete-note-ordinary", "http-delete-note-ordinary@example.test");
        await SeedRelationshipAsync(trainer.Id, linked.Id);

        SetAuthorizationHeader(trainer.Id);
        using var createResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{linked.Id}/notes", new
        {
            title = "HTTP delete note",
            content = "HTTP delete protected content",
            visibleToTrainee = true,
            isPinned = false
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<TraineeNoteDto>();
        created.Should().NotBeNull();

        using var protectedCreateResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{linked.Id}/notes", new
        {
            title = "HTTP protected note",
            content = "HTTP delete protected content",
            visibleToTrainee = true,
            isPinned = false
        });
        protectedCreateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var protectedNote = await protectedCreateResponse.Content.ReadFromJsonAsync<TraineeNoteDto>();
        protectedNote.Should().NotBeNull();

        using var ownerResponse = await Client.PostAsync($"/api/trainer/trainees/{linked.Id}/notes/{created!.Id}/delete", null);
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var unrelatedResponse = await Client.PostAsync($"/api/trainer/trainees/{unrelated.Id}/notes/{protectedNote!.Id}/delete", null);
        unrelatedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await unrelatedResponse.Content.ReadAsStringAsync()).Should().NotContain("HTTP delete protected content");

        using (var unlinkScope = Factory.Services.CreateScope())
        {
            var database = unlinkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await database.TrainerTraineeLinks
                .SingleAsync(candidate => candidate.TrainerId == trainer.Id && candidate.TraineeId == linked.Id);
            database.TrainerTraineeLinks.Remove(link);
            await database.SaveChangesAsync();
        }

        using var formerResponse = await Client.PostAsync($"/api/trainer/trainees/{linked.Id}/notes/{protectedNote.Id}/delete", null);
        formerResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        SetAuthorizationHeader(ordinaryUser.Id);
        using var ordinaryResponse = await Client.PostAsync($"/api/trainer/trainees/{linked.Id}/notes/{protectedNote.Id}/delete", null);
        ordinaryResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        ClearAuthorizationHeader();
        using var anonymousResponse = await Client.PostAsync($"/api/trainer/trainees/{linked.Id}/notes/{protectedNote.Id}/delete", null);
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    [Authorization.AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/notes/{noteId}/update", "trainer-shared", "active-relationship-allow")]
    [Authorization.AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/notes/{noteId}/update", "trainer-shared", "unrelated-relationship-denial")]
    [Authorization.AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/notes/{noteId}/update", "trainer-shared", "former-relationship-denial")]
    [Authorization.AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/notes/{noteId}/update", "trainer-shared", "foreign-object-denial-no-mutation")]
    [Authorization.AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/notes/{noteId}/update", "trainer-shared", "anonymous-denial")]
    public async Task TrainerTraineeNoteUpdateRoute_IsRelationshipScopedAndNonDisclosing()
    {
        var trainer = await SeedUserAsync("http-update-note-trainer", "http-update-note-trainer@example.test");
        var linked = await SeedUserAsync("http-update-note-linked", "http-update-note-linked@example.test");
        var unrelated = await SeedUserAsync("http-update-note-unrelated", "http-update-note-unrelated@example.test");
        var ordinaryUser = await SeedUserAsync("http-update-note-ordinary", "http-update-note-ordinary@example.test");
        await SeedRelationshipAsync(trainer.Id, linked.Id);

        SetAuthorizationHeader(trainer.Id);
        using var createResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{linked.Id}/notes", new
        {
            title = "HTTP update note",
            content = "HTTP original protected content",
            visibleToTrainee = true,
            isPinned = false
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<TraineeNoteDto>();
        created.Should().NotBeNull();
        var updatePayload = new
        {
            title = "HTTP updated note",
            content = "HTTP updated protected content",
            visibleToTrainee = false,
            isPinned = true
        };

        using var ownerResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{linked.Id}/notes/{created!.Id}/update", updatePayload);
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ownerResponse.Content.ReadAsStringAsync()).Should().Contain("HTTP updated protected content");

        using var unrelatedResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{unrelated.Id}/notes/{created.Id}/update", updatePayload);
        unrelatedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await unrelatedResponse.Content.ReadAsStringAsync()).Should().NotContain("HTTP updated protected content");

        using (var unlinkScope = Factory.Services.CreateScope())
        {
            var database = unlinkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await database.TrainerTraineeLinks
                .SingleAsync(candidate => candidate.TrainerId == trainer.Id && candidate.TraineeId == linked.Id);
            database.TrainerTraineeLinks.Remove(link);
            await database.SaveChangesAsync();
        }

        using var formerResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{linked.Id}/notes/{created.Id}/update", updatePayload);
        formerResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        SetAuthorizationHeader(ordinaryUser.Id);
        using var ordinaryResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{linked.Id}/notes/{created.Id}/update", updatePayload);
        ordinaryResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        ClearAuthorizationHeader();
        using var anonymousResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{linked.Id}/notes/{created.Id}/update", updatePayload);
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    [Authorization.AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/notes/{noteId}/history", "trainer-shared", "active-relationship-allow")]
    [Authorization.AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/notes/{noteId}/history", "trainer-shared", "unrelated-relationship-denial")]
    [Authorization.AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/notes/{noteId}/history", "trainer-shared", "former-relationship-denial")]
    [Authorization.AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/notes/{noteId}/history", "trainer-shared", "foreign-object-denial-no-mutation")]
    [Authorization.AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/notes/{noteId}/history", "trainer-shared", "anonymous-denial")]
    public async Task TrainerTraineeNoteHistoryRoute_IsRelationshipScopedAndNonDisclosing()
    {
        var trainer = await SeedUserAsync("http-history-trainer", "http-history-trainer@example.test");
        var linked = await SeedUserAsync("http-history-linked", "http-history-linked@example.test");
        var unrelated = await SeedUserAsync("http-history-unrelated", "http-history-unrelated@example.test");
        var ordinaryUser = await SeedUserAsync("http-history-ordinary", "http-history-ordinary@example.test");
        var note = VisibleNote(trainer.Id, linked.Id, "HTTP history note", DateTimeOffset.UtcNow);
        var history = new TraineeNoteHistory
        {
            Id = Id<TraineeNoteHistory>.New(),
            TraineeNoteId = note.Id,
            ChangedByUserId = trainer.Id,
            ChangedAt = DateTimeOffset.UtcNow,
            PreviousContent = "Before HTTP history",
            NewContent = "After HTTP history",
            ChangeType = "Updated"
        };

        await SeedRelationshipAsync(trainer.Id, linked.Id);
        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TraineeNotes.Add(note);
            database.TraineeNoteHistories.Add(history);
            await database.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        using var ownerResponse = await Client.GetAsync($"/api/trainer/trainees/{linked.Id}/notes/{note.Id}/history");
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerHistory = await ownerResponse.Content.ReadFromJsonAsync<List<TraineeNoteHistoryDto>>();
        ownerHistory.Should().NotBeNull();
        ownerHistory!.Should().ContainSingle(item => item.NewContent == history.NewContent && item.ChangeType == history.ChangeType);

        using var unrelatedResponse = await Client.GetAsync($"/api/trainer/trainees/{unrelated.Id}/notes/{note.Id}/history");
        unrelatedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var unrelatedText = await unrelatedResponse.Content.ReadAsStringAsync();
        unrelatedText.Should().NotContain(note.Id.ToString());
        unrelatedText.Should().NotContain(history.NewContent);

        using (var unlinkScope = Factory.Services.CreateScope())
        {
            var database = unlinkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await database.TrainerTraineeLinks
                .SingleAsync(candidate => candidate.TrainerId == trainer.Id && candidate.TraineeId == linked.Id);
            database.TrainerTraineeLinks.Remove(link);
            await database.SaveChangesAsync();
        }

        using var formerResponse = await Client.GetAsync($"/api/trainer/trainees/{linked.Id}/notes/{note.Id}/history");
        formerResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var formerText = await formerResponse.Content.ReadAsStringAsync();
        formerText.Should().NotContain(note.Id.ToString());
        formerText.Should().NotContain(history.NewContent);

        SetAuthorizationHeader(ordinaryUser.Id);
        using var ordinaryResponse = await Client.GetAsync($"/api/trainer/trainees/{linked.Id}/notes/{note.Id}/history");
        ordinaryResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var ordinaryText = await ordinaryResponse.Content.ReadAsStringAsync();
        ordinaryText.Should().NotContain(note.Id.ToString());
        ordinaryText.Should().NotContain(history.NewContent);

        ClearAuthorizationHeader();
        using var anonymousResponse = await Client.GetAsync($"/api/trainer/trainees/{linked.Id}/notes/{note.Id}/history");
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var anonymousText = await anonymousResponse.Content.ReadAsStringAsync();
        anonymousText.Should().NotContain(note.Id.ToString());
        anonymousText.Should().NotContain(history.NewContent);
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
