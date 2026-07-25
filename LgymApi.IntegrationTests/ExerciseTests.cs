using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class ExerciseTests : IntegrationTestBase
{
    [Test]
    public async Task AddExercise_WithValidData_CreatesGlobalExercise()
    {
        var user = await SeedUserAsync(name: "exerciseuser", email: "exercise@example.com");
        SetAuthorizationHeader(user.Id);

        var request = new
        {
            name = "Bench Press",
            bodyPart = BodyParts.Chest.ToString(),
            description = "Classic chest exercise"
        };

        var response = await Client.PostAsJsonAsync("/api/exercise/addExercise", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Created");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var exercise = await db.Exercises.FirstOrDefaultAsync(e => e.Name == "Bench Press" && e.UserId == null);
        exercise.Should().NotBeNull();
        exercise!.BodyPart.ToString().Should().Be("Chest");
        exercise.EloFormula.Should().Be(ExerciseEloFormula.Standard);
        exercise.IsDeleted.Should().BeFalse();
    }

    [Test]
    public async Task AddExerciseWithFormula_AsAuthorizedUser_PersistsFormula()
    {
        var user = await SeedAdminAsync();
        SetAuthorizationHeader(user.Id);

        var request = new
        {
            name = "Weighted Pullup",
            bodyPart = BodyParts.Back.ToString(),
            eloFormula = ExerciseEloFormula.StrengthWeighted.ToString(),
            description = "Weighted pullup"
        };

        var response = await PostAsJsonWithApiOptionsAsync("/api/exercise/addExerciseWithFormula", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var exercise = await db.Exercises.FirstOrDefaultAsync(e => e.Name == "Weighted Pullup" && e.UserId == null);
        exercise.Should().NotBeNull();
        exercise!.EloFormula.Should().Be(ExerciseEloFormula.StrengthWeighted);
    }

    [Test]
    public async Task AddUserExerciseWithFormula_AsAuthorizedUser_PersistsFormula()
    {
        var user = await SeedAdminAsync();
        SetAuthorizationHeader(user.Id);

        var request = new
        {
            name = "Weighted Pullup User",
            bodyPart = BodyParts.Back.ToString(),
            eloFormula = ExerciseEloFormula.VolumeWeighted.ToString(),
            description = "Weighted pullup user"
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/exercise/{user.Id}/addUserExerciseWithFormula", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var exercise = await db.Exercises.FirstOrDefaultAsync(e => e.Name == "Weighted Pullup User" && e.UserId == user.Id);
        exercise.Should().NotBeNull();
        exercise!.EloFormula.Should().Be(ExerciseEloFormula.VolumeWeighted);
    }

    [Test]
    public async Task AddUserExercise_WithValidData_CreatesUserExercise()
    {
        var user = await SeedUserAsync(name: "exerciseuser", email: "exercise@example.com");
        SetAuthorizationHeader(user.Id);

        var request = new
        {
            name = "Custom Squat",
            bodyPart = BodyParts.Quads.ToString(),
            description = "Custom leg exercise"
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/exercise/{user.Id}/addUserExercise", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var exercise = await db.Exercises.FirstOrDefaultAsync(e => e.Name == "Custom Squat" && e.UserId == user.Id);
        exercise.Should().NotBeNull();
        exercise!.BodyPart.ToString().Should().Be("Quads");
    }

    [Test]
    public async Task AddExercise_WithoutName_ReturnsBadRequest()
    {
        var user = await SeedUserAsync(name: "exerciseuser", email: "exercise@example.com");
        SetAuthorizationHeader(user.Id);

        var request = new
        {
            name = "",
            bodyPart = BodyParts.Chest.ToString()
        };

        var response = await Client.PostAsJsonAsync("/api/exercise/addExercise", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AddExercise_WithInvalidBodyPart_ReturnsBadRequest()
    {
        var user = await SeedUserAsync(name: "exerciseuser", email: "exercise@example.com");
        SetAuthorizationHeader(user.Id);

        var request = new
        {
            name = "Test Exercise",
            bodyPart = "InvalidBodyPart"
        };

        var response = await Client.PostAsJsonAsync("/api/exercise/addExercise", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task DeleteExercise_AsOwner_MarksAsDeleted()
    {
        var user = await SeedUserAsync(name: "exerciseuser", email: "exercise@example.com");
        var exercise = await SeedExerciseAsync(user.Id, "Test Exercise", "Chest");
        SetAuthorizationHeader(user.Id);

        var request = new Dictionary<string, string>
        {
            { "id", exercise.Id.ToString() }
        };

        var response = await Client.PostAsJsonAsync($"/api/exercise/{user.Id}/deleteExercise", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var deletedExercise = await db.Exercises
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == exercise.Id);
        deletedExercise.Should().NotBeNull();
        deletedExercise!.IsDeleted.Should().BeTrue();
    }

    [Test]
    public async Task DeleteExercise_AsAdmin_CanDeleteAnyExercise()
    {
        var admin = await SeedUserAsync(name: "admin", email: "admin@example.com", isAdmin: true);
        var exercise = await SeedExerciseAsync(null, "Global Exercise", "Chest");
        SetAuthorizationHeader(admin.Id);

        var request = new Dictionary<string, string>
        {
            { "id", exercise.Id.ToString() }
        };

        var response = await Client.PostAsJsonAsync($"/api/exercise/{admin.Id}/deleteExercise", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var deletedExercise = await db.Exercises
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == exercise.Id);
        deletedExercise!.IsDeleted.Should().BeTrue();
    }

    [Test]
    public async Task DeleteExercise_NonOwnerNonAdmin_ReturnsForbidden()
    {
        var user1 = await SeedUserAsync(name: "user1", email: "user1@example.com");
        var user2 = await SeedUserAsync(name: "user2", email: "user2@example.com");
        var exercise = await SeedExerciseAsync(user2.Id, "Test Exercise", "Back");
        SetAuthorizationHeader(user1.Id);

        var request = new Dictionary<string, string>
        {
            { "id", exercise.Id.ToString() }
        };

        var response = await Client.PostAsJsonAsync($"/api/exercise/{user1.Id}/deleteExercise", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task UpdateExercise_WithValidData_UpdatesExercise()
    {
        var user = await SeedUserAsync(name: "exerciseuser", email: "exercise@example.com");
        var exercise = await SeedExerciseAsync(user.Id, "Test Exercise", "Chest");
        SetAuthorizationHeader(user.Id);

        var request = new
        {
            _id = exercise.Id.ToString(),
            name = "New Name",
            bodyPart = BodyParts.Back.ToString(),
            description = "Updated description"
        };

        var response = await PostAsJsonWithApiOptionsAsync("/api/exercise/updateExercise", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var updatedExercise = await db.Exercises.FirstOrDefaultAsync(e => e.Id == exercise.Id);
        updatedExercise.Should().NotBeNull();
        updatedExercise!.Name.Should().Be("New Name");
        updatedExercise.BodyPart.ToString().Should().Be("Back");
        updatedExercise.Description.Should().Be("Updated description");
        updatedExercise.EloFormula.Should().Be(ExerciseEloFormula.Standard);
    }

    [Test]
    public async Task UpdateExerciseWithFormula_AsAuthorizedUser_UpdatesFormula()
    {
        var user = await SeedAdminAsync();
        var exercise = await SeedExerciseAsync(null, "Update Formula", "Chest");
        SetAuthorizationHeader(user.Id);

        var request = new
        {
            _id = exercise.Id.ToString(),
            name = "Update Formula",
            bodyPart = BodyParts.Back.ToString(),
            eloFormula = ExerciseEloFormula.StrengthWeighted.ToString(),
            description = "Updated with formula"
        };

        var response = await PostAsJsonWithApiOptionsAsync("/api/exercise/updateExerciseWithFormula", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var updatedExercise = await db.Exercises.FirstOrDefaultAsync(e => e.Id == exercise.Id);
        updatedExercise.Should().NotBeNull();
        updatedExercise!.EloFormula.Should().Be(ExerciseEloFormula.StrengthWeighted);
        updatedExercise.BodyPart.ToString().Should().Be("Back");
        updatedExercise.Description.Should().Be("Updated with formula");
    }

    [Test]
    public async Task UpdateExercise_WhenUserIsNotOwnerAndHasNoPermission_ReturnsForbidden()
    {
        var owner = await SeedUserAsync(name: "owner", email: "owner@example.com");
        var otherUser = await SeedUserAsync(name: "other", email: "other@example.com");
        var exercise = await SeedExerciseAsync(owner.Id, "Owner Exercise", "Chest");
        SetAuthorizationHeader(otherUser.Id);

        var request = new
        {
            _id = exercise.Id.ToString(),
            name = "Not Allowed",
            bodyPart = BodyParts.Back.ToString(),
            description = "Should fail"
        };

        var response = await PostAsJsonWithApiOptionsAsync("/api/exercise/updateExercise", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task GetAllGlobalExercises_ReturnsOnlyGlobalExercises()
    {
        var user = await SeedUserAsync(name: "exerciseuser", email: "exercise@example.com");
        await SeedExerciseAsync(null, "Global Exercise 1", "Chest");
        await SeedExerciseAsync(null, "Global Exercise 2", "Back");
        await SeedExerciseAsync(user.Id, "User Exercise", "Chest");
        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync("/api/exercise/getAllGlobalExercises");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<ExerciseResponse>>();
        body.Should().NotBeNull();
        body.Should().HaveCount(2);
        body!.Select(e => e.Name).Should().Contain(new[] { "Global Exercise 1", "Global Exercise 2" });
    }

    [Test]
    public async Task GetAllUserExercises_ReturnsOnlyUserExercises()
    {
        var user = await SeedUserAsync(name: "exerciseuser", email: "exercise@example.com");
        await SeedExerciseAsync(null, "Global Exercise", "Chest");
        await SeedExerciseAsync(user.Id, "User Exercise 1", "Chest");
        await SeedExerciseAsync(user.Id, "User Exercise 2", "Back");
        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync($"/api/exercise/{user.Id}/getAllUserExercises");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<ExerciseResponse>>();
        body.Should().NotBeNull();
        body.Should().HaveCount(2);
        body!.Select(e => e.Name).Should().Contain(new[] { "User Exercise 1", "User Exercise 2" });
    }

    [Test]
    public async Task GetAllExercises_ReturnsGlobalAndUserExercises()
    {
        var user = await SeedUserAsync(name: "exerciseuser", email: "exercise@example.com");
        await SeedExerciseAsync(null, "Global Exercise", "Chest");
        await SeedExerciseAsync(user.Id, "User Exercise", "Back");
        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync($"/api/exercise/{user.Id}/getAllExercises");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<ExerciseResponse>>();
        body.Should().NotBeNull();
        body.Should().HaveCount(2);
        body!.Select(e => e.Name).Should().Contain(new[] { "Global Exercise", "User Exercise" });
    }

    [Test]
    public async Task GetExerciseByBodyPart_FiltersCorrectly()
    {
        var user = await SeedUserAsync(name: "exerciseuser", email: "exercise@example.com");
        await SeedExerciseAsync(null, "Chest Exercise", "Chest");
        await SeedExerciseAsync(null, "Back Exercise", "Back");
        SetAuthorizationHeader(user.Id);

        var request = new { bodyPart = BodyParts.Chest.ToString() };

        var response = await Client.PostAsJsonAsync($"/api/exercise/{user.Id}/getExerciseByBodyPart", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<ExerciseResponse>>();
        body.Should().NotBeNull();
        body.Should().HaveCount(1);
        body![0].Name.Should().Be("Chest Exercise");
    }

    [Test]
    public async Task GetExerciseByBodyPart_WithInvalidBodyPart_ReturnsBadRequest()
    {
        var user = await SeedUserAsync(name: "exerciseuseralias", email: "exercisealias@example.com");
        SetAuthorizationHeader(user.Id);

        var request = new { bodyPart = "ChestAlias" };
        var response = await Client.PostAsJsonAsync($"/api/exercise/{user.Id}/getExerciseByBodyPart", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task GetExerciseByBodyPart_WithNumericBodyPart_ReturnsBadRequest()
    {
        var user = await SeedUserAsync(name: "exerciseusernum", email: "exercisenum@example.com");
        SetAuthorizationHeader(user.Id);

        var request = new { bodyPart = 1 };
        var response = await Client.PostAsJsonAsync($"/api/exercise/{user.Id}/getExerciseByBodyPart", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task GetExercise_WithValidId_ReturnsExercise()
    {
        var user = await SeedUserAsync(name: "exerciseuser", email: "exercise@example.com");
        var exercise = await SeedExerciseAsync(user.Id, "Test Exercise", "Chest", eloFormula: ExerciseEloFormula.StrengthWeighted);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seededExercise = await db.Exercises.FirstAsync(e => e.Id == exercise.Id);
            seededExercise.Description = "Detailed description";
            seededExercise.Image = "https://cdn.example.com/exercise.png";
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync($"/api/exercise/{exercise.Id}/getExercise");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ExerciseResponse>();
        body.Should().NotBeNull();
        body!.Name.Should().Be("Test Exercise");
        body.BodyPart.Should().NotBeNull();
        body.BodyPart!.Name.Should().Be("Chest");
        body.Description.Should().Be("Detailed description");
        body.Image.Should().Be("https://cdn.example.com/exercise.png");
        body.EloFormula.Should().NotBeNull();
        body.EloFormula!.Id.Should().Be(ExerciseEloFormula.StrengthWeighted.ToString());
    }

    private async Task<Exercise> SeedExerciseAsync(
        Id<User>? userId,
        string name,
        string bodyPart,
        bool isDeleted = false,
        ExerciseEloFormula eloFormula = ExerciseEloFormula.Standard)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Enum.TryParse<BodyParts>(bodyPart, out var bodyPartEnum);

        var exercise = new Exercise
        {
            Id = Id<Exercise>.New(),
            UserId = userId,
            Name = name,
            BodyPart = bodyPartEnum,
            EloFormula = eloFormula,
            IsDeleted = isDeleted
        };

        db.Exercises.Add(exercise);
        await db.SaveChangesAsync();

        return exercise;
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("msg")]
        public string Message { get; set; } = string.Empty;
    }

    private sealed class ExerciseResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("bodyPart")]
        public BodyPartLookup? BodyPart { get; set; }

        [JsonPropertyName("eloFormula")]
        public LookupItemResponse? EloFormula { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("image")]
        public string? Image { get; set; }

        [JsonPropertyName("userId")]
        public string? UserId { get; set; }
    }

    private sealed class BodyPartLookup
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;
    }

    private sealed class LookupItemResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;
    }

    [Test]
    public async Task AddGlobalTranslation_AsAdmin_AddsTranslation()
    {
        var admin = await SeedAdminAsync();
        SetAuthorizationHeader(admin.Id);

        var exercise = await SeedExerciseAsync(null, "Global Push Ups", "Chest");

        var request = new
        {
            exerciseId = exercise.Id.ToString(),
            culture = "pl",
            name = "Pompki"
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/exercise/{admin.Id}/addGlobalTranslation", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Updated");
    }

    [Test]
    public async Task AddGlobalTranslation_AsNonAdmin_ReturnsForbidden()
    {
        var admin = await SeedAdminAsync();
        var user = await SeedUserAsync(name: "normaluser", email: "normal@example.com");

        SetAuthorizationHeader(admin.Id);
        var exercise = await SeedExerciseAsync(null, "Global Squats", "Quads");

        SetAuthorizationHeader(user.Id);

        var request = new
        {
            exerciseId = exercise.Id.ToString(),
            culture = "pl",
            name = "Przysiady"
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/exercise/{user.Id}/addGlobalTranslation", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AddGlobalTranslation_ForUserExercise_ReturnsForbidden()
    {
        var admin = await SeedAdminAsync();
        SetAuthorizationHeader(admin.Id);

        var exercise = await SeedExerciseAsync(admin.Id, "User Exercise for Translation", "Chest");

        var request = new
        {
            exerciseId = exercise.Id.ToString(),
            culture = "pl",
            name = "Cwiczenie uzytkownika"
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/exercise/{admin.Id}/addGlobalTranslation", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AddGlobalTranslation_WithInvalidCulture_ReturnsBadRequest()
    {
        var admin = await SeedAdminAsync();
        SetAuthorizationHeader(admin.Id);

        var exercise = await SeedExerciseAsync(null, "Global Deadlift", "Back");

        var request = new
        {
            exerciseId = exercise.Id.ToString(),
            culture = "invalid-culture-code",
            name = "Martwy ciag"
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/exercise/{admin.Id}/addGlobalTranslation", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AddGlobalTranslation_WithMissingFields_ReturnsBadRequest()
    {
        var admin = await SeedAdminAsync();
        SetAuthorizationHeader(admin.Id);

        var request = new
        {
            exerciseId = "",
            culture = "pl",
            name = "Test"
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/exercise/{admin.Id}/addGlobalTranslation", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AddGlobalTranslation_WithNonExistentExercise_ReturnsNotFound()
    {
        var admin = await SeedAdminAsync();
        SetAuthorizationHeader(admin.Id);

        var nonExistentId = Id<Exercise>.New();
        var request = new
        {
            exerciseId = nonExistentId.ToString(),
            culture = "pl",
            name = "Test"
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/exercise/{admin.Id}/addGlobalTranslation", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
