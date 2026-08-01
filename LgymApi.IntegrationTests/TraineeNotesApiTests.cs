using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Data.SeedData;
using LgymApi.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class TraineeNotesApiTests : IntegrationTestBase
{
    [Test]
    public async Task TrainerCanCreateUpdateHistoryAndNotifyVisibleNote()
    {
        var trainer = await SeedTrainerAsync("trainer-note", "trainer-note@example.com");
        var trainee = await SeedUserAsync(name: "trainee-note", email: "trainee-note@example.com", password: "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        var createResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/notes", new
        {
            title = "Weekly focus",
            content = "Watch recovery and keep carbs stable.",
            visibleToTrainee = true,
            isPinned = true,
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<TraineeNoteResponse>();
        created.Should().NotBeNull();
        created!.VisibleToTrainee.Should().BeTrue();

        var updateResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/notes/{created.Id}/update", new
        {
            title = "Weekly focus",
            content = "Watch recovery and reduce carbs in the evening.",
            visibleToTrainee = true,
            isPinned = true,
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var historyResponse = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/notes/{created.Id}/history");
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await historyResponse.Content.ReadFromJsonAsync<List<TraineeNoteHistoryResponse>>();
        history.Should().NotBeNull();
        history!.Select(x => x.ChangeType).Should().Contain(new[] { "Created", "Updated" });

        SetAuthorizationHeader(trainee.Id);
        var visibleNotesResponse = await Client.GetAsync("/api/trainee/notes");
        visibleNotesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var visibleNotes = await visibleNotesResponse.Content.ReadFromJsonAsync<List<TraineeNoteResponse>>();
        visibleNotes.Should().ContainSingle(x => x.Id == created.Id);

        SetAuthorizationHeader(trainer.Id);
        var deleteResponse = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/notes/{created.Id}/delete", null);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var deleted = JsonDocument.Parse(await deleteResponse.Content.ReadAsStringAsync());
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            deleted.RootElement.GetProperty("msg").GetString().Should().Be(Messages.Deleted);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUiCulture;
        }

        SetAuthorizationHeader(trainee.Id);
        var deletedNoteResponse = await Client.GetAsync($"/api/trainee/notes/{created.Id}");
        deletedNoteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notificationCommand = await db.CommandEnvelopes
            .Where(x => x.CommandTypeFullName.Contains("TraineeNoteUpdatedInAppNotificationCommand"))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        notificationCommand.Should().NotBeNull();
        notificationCommand!.PayloadJson.Should().Contain(trainee.Id.ToString());
    }

    [Test]
    public async Task TrainerCannotCreateNoteForForeignTrainee()
    {
        var ownerTrainer = await SeedTrainerAsync("trainer-owner-note", "trainer-owner-note@example.com");
        var otherTrainer = await SeedTrainerAsync("trainer-other-note", "trainer-other-note@example.com");
        var trainee = await SeedUserAsync(name: "trainee-foreign-note", email: "trainee-foreign-note@example.com", password: "password123");
        await LinkTrainerAndTraineeAsync(ownerTrainer.Id, trainee.Id);

        SetAuthorizationHeader(otherTrainer.Id);
        var response = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/notes", new
        {
            content = "Forbidden note",
            visibleToTrainee = true,
            isPinned = false,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task InvisibleNoteIsNotVisibleForTrainee()
    {
        var trainer = await SeedTrainerAsync("trainer-hidden-note", "trainer-hidden-note@example.com");
        var trainee = await SeedUserAsync(name: "trainee-hidden-note", email: "trainee-hidden-note@example.com", password: "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        var createResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/notes", new
        {
            title = "Private coach note",
            content = "Internal only",
            visibleToTrainee = false,
            isPinned = false,
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        SetAuthorizationHeader(trainee.Id);
        var notesResponse = await Client.GetAsync("/api/trainee/notes");
        notesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var notes = await notesResponse.Content.ReadFromJsonAsync<List<TraineeNoteResponse>>();
        notes.Should().NotBeNull();
        notes.Should().BeEmpty();
    }

    [Test]
    public async Task MalformedNoteIds_PreserveLegacyErrorPrecedenceAndMessages()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var trainer = await SeedTrainerAsync("trainer-malformed-note", "trainer-malformed-note@example.com");
            SetAuthorizationHeader(trainer.Id);

            var malformedTrainee = await Client.GetAsync("/api/trainer/trainees/not-a-guid/notes");
            malformedTrainee.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using var traineeError = JsonDocument.Parse(await malformedTrainee.Content.ReadAsStringAsync());
            traineeError.RootElement.GetProperty("msg").GetString().Should().Be(Messages.UserIdRequired);

            var malformedNote = await Client.PostAsJsonAsync(
                $"/api/trainer/trainees/{Id<User>.New()}/notes/not-a-guid/update",
                new { content = "Ignored" });
            malformedNote.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using var noteError = JsonDocument.Parse(await malformedNote.Content.ReadAsStringAsync());
            noteError.RootElement.GetProperty("msg").GetString().Should().Be(Messages.FieldRequired);

            var trainee = await SeedUserAsync("trainee-malformed-visible-note", "trainee-malformed-visible-note@example.com", "password123");
            SetAuthorizationHeader(trainee.Id);
            var malformedVisibleNote = await Client.GetAsync("/api/trainee/notes/not-a-guid");
            malformedVisibleNote.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using var visibleError = JsonDocument.Parse(await malformedVisibleNote.Content.ReadAsStringAsync());
            visibleError.RootElement.GetProperty("msg").GetString().Should().Be(Messages.FieldRequired);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private async Task<User> SeedTrainerAsync(string name, string email)
    {
        var trainer = await SeedUserAsync(name: name, email: email, password: "password123");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var alreadyLinked = await db.UserRoles.AnyAsync(ur => ur.UserId == trainer.Id && ur.RoleId == RoleSeedDataConfiguration.TrainerRoleSeedId);
        if (!alreadyLinked)
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = trainer.Id,
                RoleId = RoleSeedDataConfiguration.TrainerRoleSeedId
            });
            await db.SaveChangesAsync();
        }

        return trainer;
    }

    private async Task LinkTrainerAndTraineeAsync(Id<User> trainerId, Id<User> traineeId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TrainerTraineeLinks.Add(new TrainerTraineeLink
        {
            Id = Id<TrainerTraineeLink>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId
        });
        await db.SaveChangesAsync();
    }

    private sealed class TraineeNoteResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("visibleToTrainee")]
        public bool VisibleToTrainee { get; set; }
    }

    private sealed class TraineeNoteHistoryResponse
    {
        [JsonPropertyName("changeType")]
        public string ChangeType { get; set; } = string.Empty;
    }
}
