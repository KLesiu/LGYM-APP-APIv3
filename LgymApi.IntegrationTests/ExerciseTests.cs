using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using FluentAssertions.Execution;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class ExerciseTests : IntegrationTestBase
{
    [TestCase("api/exercise/addExercise")]
    [TestCase("api/exercise/addExerciseWithFormula")]
    [TestCase("api/exercise/{id}/addGlobalTranslation")]
    public void GlobalExerciseWriteRoutes_RequireManageGlobalExercisesPolicy(string route)
    {
        var endpoint = Factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                string.Equals(candidate.RoutePattern.RawText, route, StringComparison.Ordinal)
                && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST", StringComparer.Ordinal) == true);

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(metadata => metadata.Policy)
            .Should()
            .Equal(AuthConstants.Policies.ManageGlobalExercises);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/exercise/addExercise", "admin", "current-permission-allow")]
    public async Task AddExercise_WithValidData_CreatesGlobalExercise()
    {
        var manager = await SeedAdminAsync();
        SetAuthorizationHeader(manager.Id);

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
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/exercise/addExerciseWithFormula", "admin", "current-permission-allow")]
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

    [TestCase("/api/exercise/addExercise", false)]
    [TestCase("/api/exercise/addExerciseWithFormula", true)]
    public async Task GlobalExerciseCreate_AsCurrentNonManager_ReturnsForbiddenWithoutCreatingExercise(
        string route,
        bool includeFormula)
    {
        var attacker = await SeedUserAsync(
            name: $"global-create-attacker-{includeFormula}",
            email: $"global-create-attacker-{includeFormula}@example.com");
        SetAuthorizationHeader(attacker.Id);
        var attemptedExerciseName = $"Denied Global Exercise {includeFormula}";
        object request = includeFormula
            ? new
            {
                name = attemptedExerciseName,
                bodyPart = BodyParts.Back.ToString(),
                eloFormula = ExerciseEloFormula.StrengthWeighted.ToString(),
                description = "Must not be persisted"
            }
            : new
            {
                name = attemptedExerciseName,
                bodyPart = BodyParts.Back.ToString(),
                description = "Must not be persisted"
            };

        var response = await PostAsJsonWithApiOptionsAsync(route, request);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var exerciseExists = await db.Exercises
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(exercise => exercise.Name == attemptedExerciseName);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            exerciseExists.Should().BeFalse();
        }
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/exercise/{id}/addUserExerciseWithFormula", "admin", "current-permission-allow")]
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
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/exercise/{id}/addUserExercise", "own", "owner-allow")]
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
        var user = await SeedAdminAsync();
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
        var user = await SeedAdminAsync();
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
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/exercise/{id}/deleteExercise", "own", "owner-allow")]
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
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/exercise/{id}/deleteExercise", "own", "foreign-object-denial-no-mutation")]
    public async Task DeleteExercise_NonOwnerNonAdmin_ReturnsNotFoundWithoutMutation()
    {
        var (attacker, victim, victimExercise) = await SeedForeignExerciseScenarioAsync(
            "foreign-delete-own-route",
            "Victim Private Delete By Object",
            BodyParts.Back);
        SetAuthorizationHeader(attacker.Id);

        var request = new Dictionary<string, string>
        {
            { "id", victimExercise.Id.ToString() }
        };

        var response = await Client.PostAsJsonAsync($"/api/exercise/{attacker.Id}/deleteExercise", request);
        var responseBody = await response.Content.ReadAsStringAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedExercise = await db.Exercises
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(item => item.Id == victimExercise.Id);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            responseBody.Should().NotContain(victimExercise.Name);
            responseBody.Should().NotContain(victimExercise.Id.ToString());
            persistedExercise.UserId.Should().Be(victim.Id);
            persistedExercise.Name.Should().Be(victimExercise.Name);
            persistedExercise.IsDeleted.Should().BeFalse();
        }
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/exercise/updateExercise", "own", "owner-allow")]
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
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/exercise/updateExerciseWithFormula", "admin", "current-permission-allow")]
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
    public async Task UpdateExercise_WhenUserIsNotOwnerAndHasNoPermission_ReturnsNotFoundWithoutMutation()
    {
        var (attacker, victim, victimExercise) = await SeedForeignExerciseScenarioAsync(
            "foreign-update",
            "Victim Private Update",
            BodyParts.Chest);
        SetAuthorizationHeader(attacker.Id);

        var request = new
        {
            _id = victimExercise.Id.ToString(),
            name = "Attacker Update Attempt",
            bodyPart = BodyParts.Back.ToString(),
            description = "Must not be persisted"
        };

        var response = await PostAsJsonWithApiOptionsAsync("/api/exercise/updateExercise", request);
        var responseBody = await response.Content.ReadAsStringAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedExercise = await db.Exercises
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(item => item.Id == victimExercise.Id);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            responseBody.Should().NotContain(victimExercise.Name);
            responseBody.Should().NotContain(victimExercise.Id.ToString());
            persistedExercise.UserId.Should().Be(victim.Id);
            persistedExercise.Name.Should().Be(victimExercise.Name);
            persistedExercise.BodyPart.Should().Be(BodyParts.Chest);
            persistedExercise.Description.Should().BeNull();
            persistedExercise.IsDeleted.Should().BeFalse();
        }
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/exercise/getAllGlobalExercises", "authenticated-global", "ordinary-authenticated-allow")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/exercise/getAllGlobalExercises", "authenticated-global", "global-resource-allow")]
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
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/exercise/{id}/getAllUserExercises", "own", "owner-allow")]
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
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/exercise/{id}/getAllExercises", "own", "owner-allow")]
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
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/exercise/{id}/getExerciseByBodyPart", "own", "owner-allow")]
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
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/exercise/{id}/getExercise", "authenticated-global", "ordinary-authenticated-allow")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/exercise/{id}/getExercise", "authenticated-global", "owner-custom-allow")]
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

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/exercise/{id}/getExercise", "authenticated-global", "foreign-custom-denial")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/exercise/{id}/getExercise", "authenticated-global", "ordinary-manager-denial")]
    public async Task GetExercise_AsDifferentUser_ReturnsNotFoundWithoutDisclosingCustomExercise()
    {
        var (attacker, _, victimExercise) = await SeedForeignExerciseScenarioAsync(
            "foreign-detail",
            "Victim Private Detail",
            BodyParts.Chest);
        SetAuthorizationHeader(attacker.Id);

        var response = await Client.GetAsync($"/api/exercise/{victimExercise.Id}/getExercise");
        var responseBody = await response.Content.ReadAsStringAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedExercise = await db.Exercises
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(exercise => exercise.Id == victimExercise.Id);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            responseBody.Should().NotContain(victimExercise.Name);
            responseBody.Should().NotContain(victimExercise.Id.ToString());
            persistedExercise.UserId.Should().Be(victimExercise.UserId);
            persistedExercise.Name.Should().Be(victimExercise.Name);
            persistedExercise.IsDeleted.Should().BeFalse();
        }
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/exercise/{id}/addUserExercise", "own", "foreign-object-denial-no-mutation")]
    public async Task AddUserExercise_WithVictimRoute_ReturnsForbiddenWithoutCreatingExercise()
    {
        var (attacker, victim, victimExercise) = await SeedForeignExerciseScenarioAsync(
            "foreign-create",
            "Victim Existing Create",
            BodyParts.Quads);
        SetAuthorizationHeader(attacker.Id);
        const string attemptedExerciseName = "Attacker Assigned To Victim";
        var request = new
        {
            name = attemptedExerciseName,
            bodyPart = BodyParts.Quads.ToString(),
            description = "Must not be persisted"
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/exercise/{victim.Id}/addUserExercise", request);
        var responseBody = await response.Content.ReadAsStringAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attemptedExercise = await db.Exercises
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(exercise => exercise.Name == attemptedExerciseName);
        var attemptedExerciseExists = attemptedExercise is not null;
        var persistedVictimExercise = await db.Exercises
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(exercise => exercise.Id == victimExercise.Id);

        if (attemptedExercise is not null)
        {
            attemptedExercise.UserId.Should().Be(victim.Id);
        }

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            responseBody.Should().NotContain(victimExercise.Name);
            attemptedExerciseExists.Should().BeFalse(
                "an attacker must not create exercise '{0}' for victim {1}; the persisted owner was {2}",
                attemptedExerciseName,
                victim.Id,
                attemptedExercise?.UserId);
            persistedVictimExercise.UserId.Should().Be(victim.Id);
            persistedVictimExercise.IsDeleted.Should().BeFalse();
        }
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/exercise/{id}/getAllUserExercises", "own", "foreign-object-denial-no-mutation")]
    public async Task GetAllUserExercises_WithVictimRoute_ReturnsForbiddenWithoutDisclosingExercises()
    {
        var (attacker, victim, victimExercise) = await SeedForeignExerciseScenarioAsync(
            "foreign-list",
            "Victim Private List",
            BodyParts.Back);
        SetAuthorizationHeader(attacker.Id);

        var response = await Client.GetAsync($"/api/exercise/{victim.Id}/getAllUserExercises");
        var responseBody = await response.Content.ReadAsStringAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedExercise = await db.Exercises
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(exercise => exercise.Id == victimExercise.Id);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            responseBody.Should().NotContain(victimExercise.Name);
            responseBody.Should().NotContain(victimExercise.Id.ToString());
            persistedExercise.UserId.Should().Be(victim.Id);
            persistedExercise.Name.Should().Be(victimExercise.Name);
            persistedExercise.IsDeleted.Should().BeFalse();
        }
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/exercise/{id}/getAllExercises", "own", "foreign-object-denial-no-mutation")]
    public async Task GetAllExercises_WithVictimRoute_ReturnsForbiddenWithoutDisclosingExercises()
    {
        var (attacker, victim, victimExercise) = await SeedForeignExerciseScenarioAsync(
            "foreign-combined-list",
            "Victim Private Combined List",
            BodyParts.Biceps);
        SetAuthorizationHeader(attacker.Id);

        var response = await Client.GetAsync($"/api/exercise/{victim.Id}/getAllExercises");
        var responseBody = await response.Content.ReadAsStringAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedExercise = await db.Exercises
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(exercise => exercise.Id == victimExercise.Id);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            responseBody.Should().NotContain(victimExercise.Name);
            responseBody.Should().NotContain(victimExercise.Id.ToString());
            persistedExercise.UserId.Should().Be(victim.Id);
            persistedExercise.Name.Should().Be(victimExercise.Name);
            persistedExercise.IsDeleted.Should().BeFalse();
        }
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/exercise/{id}/getExerciseByBodyPart", "own", "foreign-object-denial-no-mutation")]
    public async Task GetExerciseByBodyPart_WithVictimRoute_ReturnsForbiddenWithoutDisclosingExercises()
    {
        var (attacker, victim, victimExercise) = await SeedForeignExerciseScenarioAsync(
            "foreign-body-part",
            "Victim Private Body Part",
            BodyParts.Shoulders);
        SetAuthorizationHeader(attacker.Id);
        var request = new { bodyPart = BodyParts.Shoulders.ToString() };

        var response = await Client.PostAsJsonAsync($"/api/exercise/{victim.Id}/getExerciseByBodyPart", request);
        var responseBody = await response.Content.ReadAsStringAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedExercise = await db.Exercises
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(exercise => exercise.Id == victimExercise.Id);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            responseBody.Should().NotContain(victimExercise.Name);
            responseBody.Should().NotContain(victimExercise.Id.ToString());
            persistedExercise.UserId.Should().Be(victim.Id);
            persistedExercise.Name.Should().Be(victimExercise.Name);
            persistedExercise.IsDeleted.Should().BeFalse();
        }
    }

    [Test]
    public async Task DeleteExercise_WithVictimRouteAndExercise_ReturnsForbiddenWithoutDeletingExercise()
    {
        var (attacker, victim, victimExercise) = await SeedForeignExerciseScenarioAsync(
            "foreign-delete",
            "Victim Private Delete",
            BodyParts.Triceps);
        SetAuthorizationHeader(attacker.Id);
        var request = new Dictionary<string, string>
        {
            { "id", victimExercise.Id.ToString() }
        };

        var response = await Client.PostAsJsonAsync($"/api/exercise/{victim.Id}/deleteExercise", request);
        var responseBody = await response.Content.ReadAsStringAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedExercise = await db.Exercises
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(exercise => exercise.Id == victimExercise.Id);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            responseBody.Should().NotContain(victimExercise.Name);
            persistedExercise.UserId.Should().Be(victim.Id);
            persistedExercise.Name.Should().Be(victimExercise.Name);
            persistedExercise.IsDeleted.Should().BeFalse();
        }
    }

    private async Task<(User Attacker, User Victim, Exercise VictimExercise)> SeedForeignExerciseScenarioAsync(
        string scenario,
        string exerciseName,
        BodyParts bodyPart)
    {
        var attacker = await SeedUserAsync(
            name: $"exercise-attacker-{scenario}",
            email: $"exercise-attacker-{scenario}@example.com");
        var victim = await SeedUserAsync(
            name: $"exercise-victim-{scenario}",
            email: $"exercise-victim-{scenario}@example.com");
        var victimExercise = await SeedExerciseAsync(victim.Id, exerciseName, bodyPart.ToString());

        return (attacker, victim, victimExercise);
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
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/exercise/{id}/addGlobalTranslation", "admin", "current-permission-allow")]
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
        var user = await SeedUserAsync(name: "normaluser", email: "normal@example.com");
        var exercise = await SeedExerciseAsync(null, "Global Squats", "Quads");
        SetAuthorizationHeader(user.Id);

        var request = new
        {
            exerciseId = exercise.Id.ToString(),
            culture = "pl",
            name = "Przysiady"
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/exercise/{user.Id}/addGlobalTranslation", request);
        var responseBody = await response.Content.ReadAsStringAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var translationExists = await db.ExerciseTranslations
            .AsNoTracking()
            .AnyAsync(translation => translation.ExerciseId == exercise.Id && translation.Culture == "pl");
        var persistedExercise = await db.Exercises.AsNoTracking().SingleAsync(item => item.Id == exercise.Id);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            responseBody.Should().NotContain(exercise.Name);
            responseBody.Should().NotContain(exercise.Id.ToString());
            translationExists.Should().BeFalse();
            persistedExercise.Name.Should().Be(exercise.Name);
            persistedExercise.IsDeleted.Should().BeFalse();
        }
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
        var responseBody = await response.Content.ReadAsStringAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var translationExists = await db.ExerciseTranslations
            .AsNoTracking()
            .AnyAsync(translation => translation.ExerciseId == exercise.Id);
        var persistedExercise = await db.Exercises.AsNoTracking().SingleAsync(item => item.Id == exercise.Id);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            responseBody.Should().NotContain(exercise.Name);
            responseBody.Should().NotContain(exercise.Id.ToString());
            translationExists.Should().BeFalse();
            persistedExercise.UserId.Should().Be(admin.Id);
            persistedExercise.Name.Should().Be(exercise.Name);
            persistedExercise.IsDeleted.Should().BeFalse();
        }
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
