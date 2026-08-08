using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Data.SeedData;
using LgymApi.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class PlanDayTests : IntegrationTestBase
{
    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/planDay/{id}/createPlanDay", "own", "owner-allow")]
    public async Task CreatePlanDay_WithValidData_CreatesPlanDay()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "plandayuser",
            email: "planday@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "PlanDay Exercise", BodyParts.Chest);
        var planId = await CreatePlanViaEndpointAsync(userId, "PlanDay Plan");

        var request = new
        {
            name = "Chest Day",
            exercises = new[]
            {
                new { exercise = exerciseId.ToString(), series = 4, reps = "10" }
            }
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/planDay/{planId}/createPlanDay", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Created");
    }

    [Test]
    public async Task CreatePlanDay_WithMissingName_ReturnsBadRequest()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "plandayuser2",
            email: "planday2@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "PlanDay Exercise 2", BodyParts.Back);
        var planId = await CreatePlanViaEndpointAsync(userId, "PlanDay Plan 2");

        var request = new
        {
            name = "",
            exercises = new[]
            {
                new { exercise = exerciseId.ToString(), series = 3, reps = "12" }
            }
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/planDay/{planId}/createPlanDay", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreatePlanDay_WithNoExercises_ReturnsBadRequest()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "plandayuser3",
            email: "planday3@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var planId = await CreatePlanViaEndpointAsync(userId, "PlanDay Plan 3");

        var request = new
        {
            name = "Empty Day",
            exercises = Array.Empty<object>()
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/planDay/{planId}/createPlanDay", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreatePlanDay_WithInvalidPlanId_ReturnsNotFound()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "plandayuser4",
            email: "planday4@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "PlanDay Exercise 4", BodyParts.Shoulders);

        var nonExistentPlanId = Domain.ValueObjects.Id<Plan>.New();
        var request = new
        {
            name = "Test Day",
            exercises = new[]
            {
                new { exercise = exerciseId.ToString(), series = 3, reps = "10" }
            }
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/planDay/{nonExistentPlanId}/createPlanDay", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetPlanDay_WithMalformedId_ReturnsLegacyNotFoundPayload()
    {
        var (_, token) = await RegisterUserViaEndpointAsync(
            name: "plandaymalformedid",
            email: "plandaymalformedid@example.com",
            password: "password123");
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/planDay/not-a-guid/getPlanDay");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/planDay/{id}/getPlanDay", "own", "owner-allow")]
    public async Task GetPlanDay_WithValidId_ReturnsPlanDay()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "getdayuser",
            email: "getday@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "GetDay Exercise", BodyParts.Quads);
        var planId = await CreatePlanViaEndpointAsync(userId, "GetDay Plan");
        var planDayId = await CreatePlanDayViaEndpointAsync(userId, planId, "Leg Day", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 5, Reps = "5" }
        });

        var response = await Client.GetAsync($"/api/planDay/{planDayId}/getPlanDay");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PlanDayVmResponse>();
        body.Should().NotBeNull();
        body!.Name.Should().Be("Leg Day");
        body.Exercises.Should().HaveCount(1);
        body.Exercises[0].Series.Should().Be(5);
        body.Exercises[0].Reps.Should().Be("5");
    }

    [Test]
    public async Task GetPlanDay_WithAcceptLanguage_ReturnsTranslatedGlobalExerciseName()
    {
        var admin = await SeedAdminAsync();
        SetAuthorizationHeader(admin.Id);

        var exerciseId = await CreateGlobalExerciseViaEndpointAsync(admin.Id, "Squat", BodyParts.Quads);
        var translationRequest = new
        {
            exerciseId = exerciseId.ToString(),
            culture = "pl",
            name = "Przysiad"
        };
        var translationResponse = await PostAsJsonWithApiOptionsAsync($"/api/exercise/{admin.Id}/addGlobalTranslation", translationRequest);
        translationResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var planId = await CreatePlanViaEndpointAsync(admin.Id, "Translation Test Plan");
        var planDayId = await CreatePlanDayViaEndpointAsync(admin.Id, planId, "Translated Day", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 4, Reps = "8" }
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/planDay/{planDayId}/getPlanDay");
        request.Headers.AcceptLanguage.ParseAdd("pl-PL");
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PlanDayVmResponse>();
        body.Should().NotBeNull();
        body!.Exercises.Should().HaveCount(1);
        body.Exercises[0].Exercise.Should().NotBeNull();
        body.Exercises[0].Exercise!.Name.Should().Be("Przysiad");
    }

    [Test]
    public async Task GetPlanDay_PreservesExerciseOrderFromCreateRequest()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "ordercreateuser",
            email: "ordercreate@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseA = await CreateExerciseViaEndpointAsync(userId, "Order Exercise A", BodyParts.Quads);
        var exerciseB = await CreateExerciseViaEndpointAsync(userId, "Order Exercise B", BodyParts.Back);
        var exerciseC = await CreateExerciseViaEndpointAsync(userId, "Order Exercise C", BodyParts.Chest);
        var planId = await CreatePlanViaEndpointAsync(userId, "Order Plan");

        var planDayId = await CreatePlanDayViaEndpointAsync(userId, planId, "Order Day", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseB.ToString(), Series = 3, Reps = "10" },
            new() { ExerciseId = exerciseC.ToString(), Series = 4, Reps = "8" },
            new() { ExerciseId = exerciseA.ToString(), Series = 2, Reps = "15" }
        });

        var response = await Client.GetAsync($"/api/planDay/{planDayId}/getPlanDay");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PlanDayVmResponse>();
        body.Should().NotBeNull();
        body!.Exercises.Select(e => e.Exercise?.Id)
            .Should()
            .ContainInOrder(exerciseB.ToString(), exerciseC.ToString(), exerciseA.ToString());
    }

    [Test]
    public async Task GetPlanDay_WithInvalidId_ReturnsNotFound()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "getdayuser2",
            email: "getday2@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var nonExistentId = Domain.ValueObjects.Id<PlanDay>.New();
        var response = await Client.GetAsync($"/api/planDay/{nonExistentId}/getPlanDay");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/planDay/{id}/getPlanDays", "own", "owner-allow")]
    public async Task GetPlanDays_WithValidPlanId_ReturnsPlanDays()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "getdaysuser",
            email: "getdays@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "GetDays Exercise", BodyParts.Back);
        var planId = await CreatePlanViaEndpointAsync(userId, "GetDays Plan");

        await CreatePlanDayViaEndpointAsync(userId, planId, "Day A", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 3, Reps = "10" }
        });

        await CreatePlanDayViaEndpointAsync(userId, planId, "Day B", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 4, Reps = "8" }
        });

        var response = await Client.GetAsync($"/api/planDay/{planId}/getPlanDays");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<PlanDayVmResponse>>();
        body.Should().NotBeNull();
        body.Should().HaveCount(2);
        body!.Select(pd => pd.Name).Should().Contain(new[] { "Day A", "Day B" });
    }

    [Test]
    public async Task GetPlanDays_WithNoPlanDays_ReturnsNotFound()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "emptydaysuser",
            email: "emptydays@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var planId = await CreatePlanViaEndpointAsync(userId, "Empty Plan");

        var response = await Client.GetAsync($"/api/planDay/{planId}/getPlanDays");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/planDay/updatePlanDay", "own", "owner-allow")]
    public async Task UpdatePlanDay_WithValidData_UpdatesPlanDay()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "updatedayuser",
            email: "updateday@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "UpdateDay Exercise", BodyParts.Chest);
        var exerciseId2 = await CreateExerciseViaEndpointAsync(userId, "UpdateDay Exercise 2", BodyParts.Triceps);
        var planId = await CreatePlanViaEndpointAsync(userId, "UpdateDay Plan");
        var planDayId = await CreatePlanDayViaEndpointAsync(userId, planId, "Original Name", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 3, Reps = "10" }
        });

        var updateRequest = new
        {
            _id = planDayId.ToString(),
            name = "Updated Name",
            exercises = new[]
            {
                new { exercise = exerciseId.ToString(), series = 4, reps = "12" },
                new { exercise = exerciseId2.ToString(), series = 3, reps = "15" }
            }
        };

        var response = await PostAsJsonWithApiOptionsAsync("/api/planDay/updatePlanDay", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var verifyResponse = await Client.GetAsync($"/api/planDay/{planDayId}/getPlanDay");
        var updatedPlanDay = await verifyResponse.Content.ReadFromJsonAsync<PlanDayVmResponse>();
        updatedPlanDay!.Name.Should().Be("Updated Name");
        updatedPlanDay.Exercises.Should().HaveCount(2);
    }

    [Test]
    public async Task UpdatePlanDay_WithValidData_ReplacesExercisesAndKeepsOnlyUpdatedValues()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "updatedayuser3",
            email: "updateday3@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var removedExerciseId = await CreateExerciseViaEndpointAsync(userId, "Removed Exercise", BodyParts.Chest);
        var updatedExerciseId = await CreateExerciseViaEndpointAsync(userId, "Updated Exercise", BodyParts.Back);
        var addedExerciseId = await CreateExerciseViaEndpointAsync(userId, "Added Exercise", BodyParts.Quads);

        var planId = await CreatePlanViaEndpointAsync(userId, "Comprehensive Update Plan");
        var planDayId = await CreatePlanDayViaEndpointAsync(userId, planId, "Initial Day", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = removedExerciseId.ToString(), Series = 3, Reps = "10" },
            new() { ExerciseId = updatedExerciseId.ToString(), Series = 2, Reps = "8" }
        });

        var updateRequest = new
        {
            _id = planDayId.ToString(),
            name = "Updated Day Name",
            exercises = new[]
            {
                new { exercise = updatedExerciseId.ToString(), series = 5, reps = "6" },
                new { exercise = addedExerciseId.ToString(), series = 4, reps = "12" }
            }
        };

        var updateResponse = await PostAsJsonWithApiOptionsAsync("/api/planDay/updatePlanDay", updateRequest);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await Client.GetAsync($"/api/planDay/{planDayId}/getPlanDay");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedPlanDay = await getResponse.Content.ReadFromJsonAsync<PlanDayVmResponse>();
        updatedPlanDay.Should().NotBeNull();
        updatedPlanDay!.Name.Should().Be("Updated Day Name");
        updatedPlanDay.Exercises.Should().HaveCount(2);

        updatedPlanDay.Exercises
            .Should()
            .OnlyContain(e => e.Exercise != null && e.Exercise.Id != removedExerciseId.ToString());

        var updatedExercise = updatedPlanDay.Exercises
            .Single(e => e.Exercise != null && e.Exercise.Id == updatedExerciseId.ToString());
        updatedExercise.Series.Should().Be(5);
        updatedExercise.Reps.Should().Be("6");

        var addedExercise = updatedPlanDay.Exercises
            .Single(e => e.Exercise != null && e.Exercise.Id == addedExerciseId.ToString());
        addedExercise.Series.Should().Be(4);
        addedExercise.Reps.Should().Be("12");
    }

    [Test]
    public async Task UpdatePlanDay_PreservesExerciseOrderFromUpdateRequest()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "orderupdateuser",
            email: "orderupdate@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseA = await CreateExerciseViaEndpointAsync(userId, "Update Order Exercise A", BodyParts.Back);
        var exerciseB = await CreateExerciseViaEndpointAsync(userId, "Update Order Exercise B", BodyParts.Chest);
        var exerciseC = await CreateExerciseViaEndpointAsync(userId, "Update Order Exercise C", BodyParts.Quads);

        var planId = await CreatePlanViaEndpointAsync(userId, "Update Order Plan");
        var planDayId = await CreatePlanDayViaEndpointAsync(userId, planId, "Update Order Day", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseA.ToString(), Series = 3, Reps = "10" },
            new() { ExerciseId = exerciseB.ToString(), Series = 4, Reps = "8" }
        });

        var updateRequest = new
        {
            _id = planDayId.ToString(),
            name = "Update Order Day",
            exercises = new[]
            {
                new { exercise = exerciseC.ToString(), series = 5, reps = "6" },
                new { exercise = exerciseA.ToString(), series = 4, reps = "10" },
                new { exercise = exerciseB.ToString(), series = 3, reps = "12" }
            }
        };

        var updateResponse = await PostAsJsonWithApiOptionsAsync("/api/planDay/updatePlanDay", updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await Client.GetAsync($"/api/planDay/{planDayId}/getPlanDay");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedPlanDay = await getResponse.Content.ReadFromJsonAsync<PlanDayVmResponse>();
        updatedPlanDay.Should().NotBeNull();
        updatedPlanDay!.Exercises.Select(e => e.Exercise?.Id)
            .Should()
            .ContainInOrder(exerciseC.ToString(), exerciseA.ToString(), exerciseB.ToString());

        var dbContext = GetDbContext();
        var persistedOrders = await dbContext.PlanDayExercises
            .Where(x => x.PlanDayId == (Domain.ValueObjects.Id<PlanDay>)planDayId)
            .OrderBy(x => x.Order)
            .Select(x => x.Order)
            .ToListAsync();

        persistedOrders.Should().Equal(0, 1, 2);
    }

    [Test]
    public async Task UpdatePlanDay_WithMissingName_ReturnsBadRequest()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "updatedayuser2",
            email: "updateday2@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "UpdateDay Exercise 3", BodyParts.Back);
        var planId = await CreatePlanViaEndpointAsync(userId, "UpdateDay Plan 2");
        var planDayId = await CreatePlanDayViaEndpointAsync(userId, planId, "Test Day", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 3, Reps = "10" }
        });

        var updateRequest = new
        {
            _id = planDayId.ToString(),
            name = "",
            exercises = new[]
            {
                new { exercise = exerciseId.ToString(), series = 4, reps = "12" }
            }
        };

        var response = await PostAsJsonWithApiOptionsAsync("/api/planDay/updatePlanDay", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/planDay/{id}/deletePlanDay", "own", "owner-allow")]
    public async Task DeletePlanDay_WithValidId_DeletesPlanDay()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "deletedayuser",
            email: "deleteday@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "DeleteDay Exercise", BodyParts.Shoulders);
        var planId = await CreatePlanViaEndpointAsync(userId, "DeleteDay Plan");
        var planDayId = await CreatePlanDayViaEndpointAsync(userId, planId, "To Delete", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 3, Reps = "10" }
        });

        var response = await Client.GetAsync($"/api/planDay/{planDayId}/deletePlanDay");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var verifyResponse = await Client.GetAsync($"/api/planDay/{planId}/getPlanDays");
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DeletePlanDay_WithInvalidId_ReturnsNotFound()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "deletedayuser2",
            email: "deleteday2@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var nonExistentId = Domain.ValueObjects.Id<PlanDay>.New();
        var response = await Client.GetAsync($"/api/planDay/{nonExistentId}/deletePlanDay");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/planDay/{id}/deletePlanDay", "own", "foreign-object-denial-no-mutation")]
    public async Task DeletePlanDay_WithOtherUsersPlanDay_ReturnsForbidden()
    {
        var (_, user1Token) = await RegisterUserViaEndpointAsync(
            name: "deletedayuser3",
            email: "deleteday3@example.com",
            password: "password123");

        var (user2Id, user2Token) = await RegisterUserViaEndpointAsync(
            name: "deletedayuser4",
            email: "deleteday4@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user2Token);

        var exerciseId = await CreateExerciseViaEndpointAsync(user2Id, "DeleteDay Exercise 2", BodyParts.Back);
        var planId = await CreatePlanViaEndpointAsync(user2Id, "DeleteDay Plan 2");
        var planDayId = await CreatePlanDayViaEndpointAsync(user2Id, planId, "Protected Day", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 4, Reps = "8" }
        });

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user1Token);

        var response = await Client.GetAsync($"/api/planDay/{planDayId}/deletePlanDay");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be(CompatibilityResourceMessage.InCulture("en", () => Messages.Forbidden));
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/planDay/{id}/createPlanDay", "own", "foreign-object-denial-no-mutation")]
    public async Task CreatePlanDay_WithOtherUsersPlan_ReturnsForbidden()
    {
        var (ownerId, ownerToken) = await RegisterUserViaEndpointAsync(
            name: "plandayowner1",
            email: "plandayowner1@example.com",
            password: "password123");

        var (_, attackerToken) = await RegisterUserViaEndpointAsync(
            name: "plandayattacker1",
            email: "plandayattacker1@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ownerToken);

        var exerciseId = await CreateExerciseViaEndpointAsync(ownerId, "Create Forbidden Exercise", BodyParts.Back);
        var planId = await CreatePlanViaEndpointAsync(ownerId, "Create Forbidden Plan");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", attackerToken);

        var request = new
        {
            name = "Should Not Be Created",
            exercises = new[]
            {
                new { exercise = exerciseId.ToString(), series = 3, reps = "10" }
            }
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/planDay/{planId}/createPlanDay", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/planDay/updatePlanDay", "own", "foreign-object-denial-no-mutation")]
    public async Task UpdatePlanDay_WithOtherUsersPlanDay_ReturnsForbidden()
    {
        var (ownerId, ownerToken) = await RegisterUserViaEndpointAsync(
            name: "plandayowner2",
            email: "plandayowner2@example.com",
            password: "password123");

        var (_, attackerToken) = await RegisterUserViaEndpointAsync(
            name: "plandayattacker2",
            email: "plandayattacker2@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ownerToken);

        var exerciseId = await CreateExerciseViaEndpointAsync(ownerId, "Update Forbidden Exercise", BodyParts.Chest);
        var planId = await CreatePlanViaEndpointAsync(ownerId, "Update Forbidden Plan");
        var planDayId = await CreatePlanDayViaEndpointAsync(ownerId, planId, "Owner Day", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 3, Reps = "10" }
        });

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", attackerToken);

        var request = new
        {
            _id = planDayId.ToString(),
            name = "Hacked Name",
            exercises = new[]
            {
                new { exercise = exerciseId.ToString(), series = 4, reps = "8" }
            }
        };

        var response = await PostAsJsonWithApiOptionsAsync("/api/planDay/updatePlanDay", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/planDay/{id}/getPlanDay", "own", "foreign-object-denial-no-mutation")]
    public async Task GetPlanDay_WithOtherUsersPlanDay_ReturnsForbidden()
    {
        var (ownerId, ownerToken) = await RegisterUserViaEndpointAsync(
            name: "plandayowner3",
            email: "plandayowner3@example.com",
            password: "password123");

        var (_, attackerToken) = await RegisterUserViaEndpointAsync(
            name: "plandayattacker3",
            email: "plandayattacker3@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ownerToken);

        var exerciseId = await CreateExerciseViaEndpointAsync(ownerId, "Get Forbidden Exercise", BodyParts.Quads);
        var planId = await CreatePlanViaEndpointAsync(ownerId, "Get Forbidden Plan");
        var planDayId = await CreatePlanDayViaEndpointAsync(ownerId, planId, "Owner Day", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 3, Reps = "10" }
        });

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", attackerToken);

        var response = await Client.GetAsync($"/api/planDay/{planDayId}/getPlanDay");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/planDay/{id}/getPlanDays", "own", "foreign-object-denial-no-mutation")]
    public async Task GetPlanDays_WithOtherUsersPlan_ReturnsForbidden()
    {
        var (ownerId, ownerToken) = await RegisterUserViaEndpointAsync(
            name: "plandayowner4",
            email: "plandayowner4@example.com",
            password: "password123");

        var (_, attackerToken) = await RegisterUserViaEndpointAsync(
            name: "plandayattacker4",
            email: "plandayattacker4@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ownerToken);

        var exerciseId = await CreateExerciseViaEndpointAsync(ownerId, "GetDays Forbidden Exercise", BodyParts.Back);
        var planId = await CreatePlanViaEndpointAsync(ownerId, "GetDays Forbidden Plan");
        await CreatePlanDayViaEndpointAsync(ownerId, planId, "Owner Day", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 3, Reps = "10" }
        });

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", attackerToken);

        var response = await Client.GetAsync($"/api/planDay/{planId}/getPlanDays");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task GetPlanDays_AsLinkedTrainer_ReturnsOwnerPlanDays()
    {
        var (trainerId, trainerToken) = await RegisterUserViaEndpointAsync(
            name: "plandaylinkedtrainer",
            email: "plandaylinkedtrainer@example.com",
            password: "password123");
        var (ownerId, ownerToken) = await RegisterUserViaEndpointAsync(
            name: "plandaylinkedowner",
            email: "plandaylinkedowner@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ownerToken);
        var exerciseId = await CreateExerciseViaEndpointAsync(ownerId, "Linked Trainer Exercise", BodyParts.Back);
        var planId = await CreatePlanViaEndpointAsync(ownerId, "Linked Trainer Plan");
        await CreatePlanDayViaEndpointAsync(ownerId, planId, "Linked Trainer Day",
        [
            new PlanDayExerciseInput { ExerciseId = exerciseId.ToString(), Series = 3, Reps = "10" }
        ]);
        await SeedTrainerRelationshipAsync(trainerId, ownerId);

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", trainerToken);
        var response = await Client.GetAsync($"/api/planDay/{planId}/getPlanDays");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<PlanDayResult>>();
        body.Should().ContainSingle(day => day.Name == "Linked Trainer Day");
    }

    [Test]
    public async Task GetPlanDays_AsTrainerLinkedToAnotherOwner_ReturnsForbiddenForForeignPlan()
    {
        var (trainerId, trainerToken) = await RegisterUserViaEndpointAsync(
            name: "plandayforeigntrainer",
            email: "plandayforeigntrainer@example.com",
            password: "password123");
        var (linkedOwnerId, _) = await RegisterUserViaEndpointAsync(
            name: "plandaylinkedtrainee",
            email: "plandaylinkedtrainee@example.com",
            password: "password123");
        var (foreignOwnerId, foreignOwnerToken) = await RegisterUserViaEndpointAsync(
            name: "plandayforeignowner",
            email: "plandayforeignowner@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", foreignOwnerToken);
        var planId = await CreatePlanViaEndpointAsync(foreignOwnerId, "Foreign Owner Plan");
        await SeedTrainerRelationshipAsync(trainerId, linkedOwnerId);

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", trainerToken);
        var response = await Client.GetAsync($"/api/planDay/{planId}/getPlanDays");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/planDay/{id}/getPlanDaysTypes", "own", "foreign-object-denial-no-mutation")]
    public async Task GetPlanDaysTypes_WithOtherUserId_ReturnsForbidden()
    {
        var (ownerId, ownerToken) = await RegisterUserViaEndpointAsync(
            name: "plandayowner5",
            email: "plandayowner5@example.com",
            password: "password123");

        var (_, attackerToken) = await RegisterUserViaEndpointAsync(
            name: "plandayattacker5",
            email: "plandayattacker5@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ownerToken);

        var exerciseId = await CreateExerciseViaEndpointAsync(ownerId, "Types Forbidden Exercise", BodyParts.Biceps);
        var planId = await CreatePlanViaEndpointAsync(ownerId, "Types Forbidden Plan");
        await CreatePlanDayViaEndpointAsync(ownerId, planId, "Owner Day", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 4, Reps = "12" }
        });

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", attackerToken);

        var response = await Client.GetAsync($"/api/planDay/{ownerId}/getPlanDaysTypes");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/planDay/{id}/getPlanDaysInfo", "own", "foreign-object-denial-no-mutation")]
    public async Task GetPlanDaysInfo_WithOtherUsersPlan_ReturnsForbidden()
    {
        var (ownerId, ownerToken) = await RegisterUserViaEndpointAsync(
            name: "plandayowner6",
            email: "plandayowner6@example.com",
            password: "password123");

        var (_, attackerToken) = await RegisterUserViaEndpointAsync(
            name: "plandayattacker6",
            email: "plandayattacker6@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ownerToken);

        var exerciseId = await CreateExerciseViaEndpointAsync(ownerId, "Info Forbidden Exercise", BodyParts.Quads);
        var planId = await CreatePlanViaEndpointAsync(ownerId, "Info Forbidden Plan");
        await CreatePlanDayViaEndpointAsync(ownerId, planId, "Owner Day", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 3, Reps = "10" }
        });

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", attackerToken);

        var response = await Client.GetAsync($"/api/planDay/{planId}/getPlanDaysInfo");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/planDay/{id}/getPlanDaysTypes", "own", "owner-allow")]
    public async Task GetPlanDaysTypes_WithValidUserId_ReturnsTypes()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "typesuser",
            email: "types@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "Types Exercise", BodyParts.Biceps);
        var planId = await CreatePlanViaEndpointAsync(userId, "Types Plan");
        await CreatePlanDayViaEndpointAsync(userId, planId, "Arms Day", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 4, Reps = "12" }
        });

        var response = await Client.GetAsync($"/api/planDay/{userId}/getPlanDaysTypes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<PlanDayChooseResponse>>();
        body.Should().NotBeNull();
        body.Should().HaveCountGreaterThanOrEqualTo(1);
        body!.Should().Contain(pd => pd.Name == "Arms Day");
    }

    private async Task SeedTrainerRelationshipAsync(Id<User> trainerId, Id<User> traineeId)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.UserRoles.Add(new UserRole
        {
            UserId = trainerId,
            RoleId = RoleSeedDataConfiguration.TrainerRoleSeedId
        });
        dbContext.TrainerTraineeLinks.Add(new TrainerTraineeLink
        {
            Id = Id<TrainerTraineeLink>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId
        });
        await dbContext.SaveChangesAsync();
    }

    [Test]
    public async Task GetPlanDaysTypes_WithNoActivePlan_ReturnsNotFound()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "typesuser2",
            email: "types2@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync($"/api/planDay/{userId}/getPlanDaysTypes");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/planDay/{id}/getPlanDaysInfo", "own", "owner-allow")]
    public async Task GetPlanDaysInfo_WithValidPlanId_ReturnsInfo()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "infouser",
            email: "info@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "Info Exercise", BodyParts.Quads);
        var planId = await CreatePlanViaEndpointAsync(userId, "Info Plan");
        await CreatePlanDayViaEndpointAsync(userId, planId, "Info Day", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 3, Reps = "10" },
            new() { ExerciseId = exerciseId.ToString(), Series = 4, Reps = "8" }
        });

        var response = await Client.GetAsync($"/api/planDay/{planId}/getPlanDaysInfo");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<PlanDayInfoResponse>>();
        body.Should().NotBeNull();
        body.Should().HaveCount(1);
        body![0].Name.Should().Be("Info Day");
        body[0].TotalNumberOfExercises.Should().Be(2);
        body[0].TotalNumberOfSeries.Should().Be(7);
    }

    [Test]
    public async Task GetPlanDaysInfo_WithNoPlanDays_ReturnsOkWithEmptyList()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "infouser2",
            email: "info2@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var planId = await CreatePlanViaEndpointAsync(userId, "Info Empty Plan");

        var response = await Client.GetAsync($"/api/planDay/{planId}/getPlanDaysInfo");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<PlanDayInfoResponse>>();
        body.Should().NotBeNull();
        body.Should().BeEmpty();
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("msg")]
        public string Message { get; set; } = string.Empty;
    }

    private sealed class PlanDayVmResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("exercises")]
        public List<PlanDayExerciseVmResponse> Exercises { get; set; } = new();
    }

    private sealed class PlanDayExerciseVmResponse
    {
        [JsonPropertyName("series")]
        public int Series { get; set; }

        [JsonPropertyName("reps")]
        public string Reps { get; set; } = string.Empty;

        [JsonPropertyName("exercise")]
        public ExerciseResponse? Exercise { get; set; }
    }

    private sealed class ExerciseResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class PlanDayChooseResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class PlanDayInfoResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("lastTrainingDate")]
        public DateTime? LastTrainingDate { get; set; }

        [JsonPropertyName("totalNumberOfSeries")]
        public int TotalNumberOfSeries { get; set; }

        [JsonPropertyName("totalNumberOfExercises")]
        public int TotalNumberOfExercises { get; set; }
    }
}
