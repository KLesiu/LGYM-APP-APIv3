using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Data.SeedData;
using LgymApi.IntegrationTests.Authorization;
using LgymApi.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class DietPlansApiTests : IntegrationTestBase
{
    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans", "trainer-shared", "active-relationship-allow")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/diet-plans", "trainer-shared", "active-relationship-allow")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/history", "trainer-shared", "active-relationship-allow")]
    public async Task TrainerDietPlanCrudFlow_Works()
    {
        var trainer = await SeedTrainerAsync("trainer-diet", "trainer-diet@example.com");
        var trainee = await SeedUserAsync(name: "trainee-diet", email: "trainee-diet@example.com", password: "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        var createResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans", new
        {
            name = "Mass phase",
            startDate = new DateOnly(2026, 6, 1),
            estimatedCalories = 3100,
            proteinGrams = 180,
            carbsGrams = 360,
            fatGrams = 90,
            notes = "Initial version",
            isActive = true,
            meals = new object[]
            {
                new { name = "Breakfast", order = 0, description = "Eggs and oats", estimatedCalories = 750, proteinGrams = 40, carbsGrams = 60, fatGrams = 25 }
            }
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<DietPlanResponse>();
        created.Should().NotBeNull();
        created!.IsActive.Should().BeTrue();

        var listResponse = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var plans = await listResponse.Content.ReadFromJsonAsync<List<DietPlanResponse>>();
        plans.Should().ContainSingle(x => x.Id == created.Id && x.IsActive);

        var updateResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{created.Id}/update", new
        {
            name = "Mass phase v2",
            startDate = new DateOnly(2026, 6, 2),
            endDate = new DateOnly(2026, 8, 31),
            estimatedCalories = 3200,
            proteinGrams = 190,
            carbsGrams = 375,
            fatGrams = 92,
            notes = "Updated version",
            isActive = true,
            meals = new object[]
            {
                new { name = "Breakfast", order = 0, description = "Eggs, oats, banana", estimatedCalories = 800, proteinGrams = 45, carbsGrams = 70, fatGrams = 25 },
                new { name = "Dinner", order = 1, description = "Rice and chicken", estimatedCalories = 900, proteinGrams = 55, carbsGrams = 100, fatGrams = 20 }
            }
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var historyResponse = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{created.Id}/history");
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await historyResponse.Content.ReadFromJsonAsync<List<DietPlanHistoryResponse>>();
        history.Should().NotBeNull();
        history!.Count.Should().BeGreaterThanOrEqualTo(2);
        history.Select(x => x.ChangeType).Should().Contain(new[] { "Created", "Updated" });

        SetAuthorizationHeader(trainee.Id);
        var currentResponse = await Client.GetAsync("/api/trainee/diet-plan/current");
        currentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var current = await currentResponse.Content.ReadFromJsonAsync<DietPlanResponse>();
        current.Should().NotBeNull();
        current!.Name.Should().Be("Mass phase v2");
        current.Meals.Should().HaveCount(2);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans", "trainer-shared", "foreign-object-denial-no-mutation")]
    public async Task TrainerCannotCreateDietForForeignTrainee()
    {
        var ownerTrainer = await SeedTrainerAsync("trainer-owner-diet", "trainer-owner-diet@example.com");
        var otherTrainer = await SeedTrainerAsync("trainer-other-diet", "trainer-other-diet@example.com");
        var trainee = await SeedUserAsync(name: "trainee-foreign-diet", email: "trainee-foreign-diet@example.com", password: "password123");
        await LinkTrainerAndTraineeAsync(ownerTrainer.Id, trainee.Id);

        SetAuthorizationHeader(otherTrainer.Id);
        var beforeCreate = await GetDietPersistenceCountsAsync();
        await AssertExactMessageAsync(
            await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans", ValidDietRequest("Foreign trainee plan")),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));

        (await GetDietPersistenceCountsAsync()).Should().Be(beforeCreate);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans", "trainer-shared", "unrelated-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans", "trainer-shared", "anonymous-denial")]
    public async Task TrainerDietCreate_WithoutRelationshipOrAuthentication_DoesNotPersist()
    {
        var unrelatedTrainer = await SeedTrainerAsync("task10-unrelated-diet-create-trainer", "task10-unrelated-diet-create-trainer@example.com");
        var trainee = await SeedUserAsync("task10-unrelated-diet-create-trainee", "task10-unrelated-diet-create-trainee@example.com", "password123");
        var beforeCreate = await GetDietPersistenceCountsAsync();

        SetAuthorizationHeader(unrelatedTrainer.Id);
        await AssertExactMessageAsync(
            await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans", ValidDietRequest("Unrelated trainee plan")),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));
        (await GetDietPersistenceCountsAsync()).Should().Be(beforeCreate);

        ClearAuthorizationHeader();
        using (var anonymousResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans", ValidDietRequest("Anonymous trainee plan")))
        {
            anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await GetDietPersistenceCountsAsync()).Should().Be(beforeCreate);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans", "trainer-shared", "former-relationship-denial")]
    public async Task TrainerDietCreate_AfterUnlink_ReturnsNotFoundWithoutPersistence()
    {
        var trainer = await SeedTrainerAsync("task10-former-diet-create-trainer", "task10-former-diet-create-trainer@example.com");
        var trainee = await SeedUserAsync("task10-former-diet-create-trainee", "task10-former-diet-create-trainee@example.com", "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        var beforeCreate = await GetDietPersistenceCountsAsync();
        using (var unlinkResponse = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/unlink", null))
        {
            unlinkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await AssertExactMessageAsync(
            await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans", ValidDietRequest("Former trainee plan")),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));

        (await GetDietPersistenceCountsAsync()).Should().Be(beforeCreate);
    }

    [Test]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/diet-plans", "trainer-shared", "unrelated-relationship-denial")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/diet-plans", "trainer-shared", "anonymous-denial")]
    public async Task TrainerDietList_WithoutRelationshipOrAuthentication_DoesNotMutatePersistence()
    {
        var unrelatedTrainer = await SeedTrainerAsync("task10-unrelated-diet-list-trainer", "task10-unrelated-diet-list-trainer@example.com");
        var trainee = await SeedUserAsync("task10-unrelated-diet-list-trainee", "task10-unrelated-diet-list-trainee@example.com", "password123");
        var beforeRead = await GetDietPersistenceCountsAsync();

        SetAuthorizationHeader(unrelatedTrainer.Id);
        await AssertExactMessageAsync(
            await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans"),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));
        (await GetDietPersistenceCountsAsync()).Should().Be(beforeRead);

        ClearAuthorizationHeader();
        using (var anonymousResponse = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans"))
        {
            anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await GetDietPersistenceCountsAsync()).Should().Be(beforeRead);
    }

    [Test]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/diet-plans", "trainer-shared", "foreign-object-denial-no-mutation")]
    public async Task TrainerDietList_ForForeignTrainee_ReturnsNotFoundAndPreservesPlan()
    {
        var owner = await SeedTrainerAsync("task10-foreign-diet-list-owner", "task10-foreign-diet-list-owner@example.com");
        var otherTrainer = await SeedTrainerAsync("task10-foreign-diet-list-trainer", "task10-foreign-diet-list-trainer@example.com");
        var trainee = await SeedUserAsync("task10-foreign-diet-list-trainee", "task10-foreign-diet-list-trainee@example.com", "password123");
        await LinkTrainerAndTraineeAsync(owner.Id, trainee.Id);

        SetAuthorizationHeader(owner.Id);
        var plan = await CreateDietAsync(trainee.Id, "Foreign trainer list plan", true);
        var originalPlan = await GetDietPlanSnapshotAsync(plan.Id);
        var beforeRead = await GetDietPersistenceCountsAsync();

        SetAuthorizationHeader(otherTrainer.Id);
        await AssertExactMessageAsync(
            await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans"),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));

        (await GetDietPlanSnapshotAsync(plan.Id)).Should().BeEquivalentTo(originalPlan);
        (await GetDietPersistenceCountsAsync()).Should().Be(beforeRead);
    }

    [Test]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/diet-plans", "trainer-shared", "former-relationship-denial")]
    public async Task TrainerDietList_AfterUnlink_ReturnsNotFoundAndPreservesPlan()
    {
        var trainer = await SeedTrainerAsync("task10-former-diet-list-trainer", "task10-former-diet-list-trainer@example.com");
        var trainee = await SeedUserAsync("task10-former-diet-list-trainee", "task10-former-diet-list-trainee@example.com", "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        var plan = await CreateDietAsync(trainee.Id, "Former trainer list plan", true);
        var originalPlan = await GetDietPlanSnapshotAsync(plan.Id);
        var beforeRead = await GetDietPersistenceCountsAsync();
        using (var unlinkResponse = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/unlink", null))
        {
            unlinkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await AssertExactMessageAsync(
            await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans"),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));

        (await GetDietPlanSnapshotAsync(plan.Id)).Should().BeEquivalentTo(originalPlan);
        (await GetDietPersistenceCountsAsync()).Should().Be(beforeRead);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/activate", "trainer-shared", "active-relationship-allow")]
    public async Task ActivateDietPlan_PreservesExistingActivePlanCharacterization()
    {
        var trainer = await SeedTrainerAsync("trainer-activate-diet", "trainer-activate-diet@example.com");
        var trainee = await SeedUserAsync(name: "trainee-activate-diet", email: "trainee-activate-diet@example.com", password: "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        var first = await CreateDietAsync(trainee.Id, "Diet A", true);
        var second = await CreateDietAsync(trainee.Id, "Diet B", false);
        var beforeActivation = await GetDietPersistenceCountsAsync();

        using var activateResponse = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{second.Id}/activate", null);
        await AssertExactMessageAsync(activateResponse, HttpStatusCode.OK, EnglishMessage(() => Messages.Updated));

        var listResponse = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans");
        var plans = await listResponse.Content.ReadFromJsonAsync<List<DietPlanResponse>>();
        plans.Should().Contain(x => x.IsActive && x.Id == second.Id);
        plans.Should().Contain(x => x.IsActive && x.Id == first.Id);
        (await GetDietPersistenceCountsAsync()).Should().Be((beforeActivation.Plans, beforeActivation.Histories + 1, beforeActivation.Commands + 1));
    }

    [Test]
    public async Task DietRoutes_PreserveMalformedIdAndMessageContracts()
    {
        var trainer = await SeedTrainerAsync("trainer-malformed-diet", "trainer-malformed-diet@example.com");
        SetAuthorizationHeader(trainer.Id);
        var validTraineeId = Id<User>.New();

        await AssertExactMessageAsync(
            await Client.GetAsync("/api/trainer/trainees/not-a-guid/diet-plans"),
            HttpStatusCode.BadRequest,
            EnglishMessage(() => Messages.UserIdRequired));
        await AssertExactMessageAsync(
            await Client.PostAsJsonAsync("/api/trainer/trainees/not-a-guid/diet-plans", ValidDietRequest()),
            HttpStatusCode.BadRequest,
            EnglishMessage(() => Messages.UserIdRequired));

        foreach (var response in new[]
                 {
                     await Client.GetAsync($"/api/trainer/trainees/not-a-guid/diet-plans/{Id<DietPlan>.New()}"),
                     await Client.PostAsJsonAsync($"/api/trainer/trainees/not-a-guid/diet-plans/{Id<DietPlan>.New()}/update", ValidDietRequest()),
                     await Client.PostAsync($"/api/trainer/trainees/not-a-guid/diet-plans/{Id<DietPlan>.New()}/activate", null),
                     await Client.PostAsync($"/api/trainer/trainees/not-a-guid/diet-plans/{Id<DietPlan>.New()}/delete", null),
                     await Client.GetAsync($"/api/trainer/trainees/not-a-guid/diet-plans/{Id<DietPlan>.New()}/history")
                 })
        {
            await AssertExactMessageAsync(response, HttpStatusCode.BadRequest, EnglishMessage(() => Messages.UserIdRequired));
        }

        foreach (var response in new[]
                 {
                     await Client.GetAsync($"/api/trainer/trainees/{validTraineeId}/diet-plans/not-a-guid"),
                     await Client.PostAsJsonAsync($"/api/trainer/trainees/{validTraineeId}/diet-plans/not-a-guid/update", ValidDietRequest()),
                     await Client.PostAsync($"/api/trainer/trainees/{validTraineeId}/diet-plans/not-a-guid/activate", null),
                     await Client.PostAsync($"/api/trainer/trainees/{validTraineeId}/diet-plans/not-a-guid/delete", null),
                     await Client.GetAsync($"/api/trainer/trainees/{validTraineeId}/diet-plans/not-a-guid/history")
                 })
        {
            await AssertExactMessageAsync(response, HttpStatusCode.BadRequest, EnglishMessage(() => Messages.FieldRequired));
        }
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/delete", "trainer-shared", "active-relationship-allow")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/delete", "trainer-shared", "foreign-object-denial-no-mutation")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}", "trainer-shared", "active-relationship-allow")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}", "trainer-shared", "foreign-object-denial-no-mutation")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/history", "trainer-shared", "foreign-object-denial-no-mutation")]
    public async Task DietDeleteAndSingleRead_PreserveContracts()
    {
        var owner = await SeedTrainerAsync("trainer-diet-owner", "trainer-diet-owner@example.com");
        var otherTrainer = await SeedTrainerAsync("trainer-diet-other", "trainer-diet-other@example.com");
        var trainee = await SeedUserAsync("trainee-diet-owned", "trainee-diet-owned@example.com", "password123");
        await LinkTrainerAndTraineeAsync(owner.Id, trainee.Id);
        await LinkTrainerAndTraineeAsync(otherTrainer.Id, trainee.Id);

        SetAuthorizationHeader(owner.Id);
        var plan = await CreateDietAsync(trainee.Id, "Owner plan", true);
        var originalPlan = await GetDietPlanSnapshotAsync(plan.Id);
        var beforeForeignRead = await GetDietPersistenceCountsAsync();

        var singleRead = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}");
        singleRead.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var document = JsonDocument.Parse(await singleRead.Content.ReadAsStringAsync()))
        {
            document.RootElement.GetProperty("_id").GetString().Should().Be(plan.Id);
            document.RootElement.TryGetProperty("id", out _).Should().BeFalse();
            document.RootElement.GetProperty("name").GetString().Should().Be("Owner plan");
        }

        SetAuthorizationHeader(otherTrainer.Id);
        await AssertExactMessageAsync(
            await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}"),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));
        await AssertExactMessageAsync(
            await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/history"),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));
        await AssertExactMessageAsync(
            await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/delete", null),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));
        (await GetDietPlanSnapshotAsync(plan.Id)).Should().BeEquivalentTo(originalPlan);
        (await GetDietPersistenceCountsAsync()).Should().Be(beforeForeignRead);

        SetAuthorizationHeader(owner.Id);
        await AssertExactMessageAsync(
            await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/delete", null),
            HttpStatusCode.OK,
            EnglishMessage(() => Messages.Deleted));
        await AssertExactMessageAsync(
            await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}"),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));

        var listAfterDelete = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans");
        listAfterDelete.StatusCode.Should().Be(HttpStatusCode.OK);
        (await listAfterDelete.Content.ReadAsStringAsync()).Should().Be("[]");
    }

    [Test]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}", "trainer-shared", "unrelated-relationship-denial")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}", "trainer-shared", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/history", "trainer-shared", "unrelated-relationship-denial")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/history", "trainer-shared", "anonymous-denial")]
    public async Task TrainerDietSingleRead_WithoutRelationshipOrAuthentication_DoesNotMutatePlan()
    {
        var owner = await SeedTrainerAsync("task10-unrelated-diet-read-owner", "task10-unrelated-diet-read-owner@example.com");
        var unrelatedTrainer = await SeedTrainerAsync("task10-unrelated-diet-read-trainer", "task10-unrelated-diet-read-trainer@example.com");
        var trainee = await SeedUserAsync("task10-unrelated-diet-read-trainee", "task10-unrelated-diet-read-trainee@example.com", "password123");
        await LinkTrainerAndTraineeAsync(owner.Id, trainee.Id);

        SetAuthorizationHeader(owner.Id);
        var plan = await CreateDietAsync(trainee.Id, "Unrelated trainer read plan", true);
        var originalPlan = await GetDietPlanSnapshotAsync(plan.Id);
        var beforeRead = await GetDietPersistenceCountsAsync();

        SetAuthorizationHeader(unrelatedTrainer.Id);
        await AssertExactMessageAsync(
            await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}"),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));
        await AssertExactMessageAsync(
            await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/history"),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));
        (await GetDietPlanSnapshotAsync(plan.Id)).Should().BeEquivalentTo(originalPlan);
        (await GetDietPersistenceCountsAsync()).Should().Be(beforeRead);

        ClearAuthorizationHeader();
        using (var anonymousResponse = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}"))
        {
            anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        using (var anonymousHistoryResponse = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/history"))
        {
            anonymousHistoryResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await GetDietPlanSnapshotAsync(plan.Id)).Should().BeEquivalentTo(originalPlan);
        (await GetDietPersistenceCountsAsync()).Should().Be(beforeRead);
    }

    [Test]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}", "trainer-shared", "former-relationship-denial")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/history", "trainer-shared", "former-relationship-denial")]
    public async Task TrainerDietSingleRead_AfterUnlink_ReturnsNotFoundAndPreservesPlan()
    {
        var trainer = await SeedTrainerAsync("task10-former-diet-read-trainer", "task10-former-diet-read-trainer@example.com");
        var trainee = await SeedUserAsync("task10-former-diet-read-trainee", "task10-former-diet-read-trainee@example.com", "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        var plan = await CreateDietAsync(trainee.Id, "Former trainer read plan", true);
        var originalPlan = await GetDietPlanSnapshotAsync(plan.Id);
        var beforeRead = await GetDietPersistenceCountsAsync();
        using (var unlinkResponse = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/unlink", null))
        {
            unlinkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await AssertExactMessageAsync(
            await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}"),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));
        await AssertExactMessageAsync(
            await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/history"),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));

        (await GetDietPlanSnapshotAsync(plan.Id)).Should().BeEquivalentTo(originalPlan);
        (await GetDietPersistenceCountsAsync()).Should().Be(beforeRead);
    }

    [Test]
    [AuthorizationEvidence("GET", "/api/trainee/diet-plan/current", "own", "owner-allow")]
    [AuthorizationEvidence("GET", "/api/trainee/diet-plan/current", "own", "no-client-subject")]
    [AuthorizationEvidence("GET", "/api/trainee/diet-plans/current", "own", "owner-allow")]
    [AuthorizationEvidence("GET", "/api/trainee/diet-plans/current", "own", "no-client-subject")]
    public async Task TraineeCurrentDietRoutes_PreservePluralAndSingularSemantics()
    {
        var trainer = await SeedTrainerAsync("trainer-current-contract", "trainer-current-contract@example.com");
        var trainee = await SeedUserAsync("trainee-current-contract", "trainee-current-contract@example.com", "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainee.Id);
        var emptyPlural = await Client.GetAsync("/api/trainee/diet-plans/current");
        emptyPlural.StatusCode.Should().Be(HttpStatusCode.OK);
        (await emptyPlural.Content.ReadAsStringAsync()).Should().Be("[]");
        await AssertExactMessageAsync(
            await Client.GetAsync("/api/trainee/diet-plan/current"),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));

        SetAuthorizationHeader(trainer.Id);
        var first = await CreateDietAsync(trainee.Id, "Training day", true);
        var second = await CreateDietAsync(trainee.Id, "Rest day", true);

        SetAuthorizationHeader(trainee.Id);
        var plural = await Client.GetAsync("/api/trainee/diet-plans/current");
        plural.StatusCode.Should().Be(HttpStatusCode.OK);
        var plans = await plural.Content.ReadFromJsonAsync<List<DietPlanResponse>>();
        plans.Should().NotBeNull();
        plans!.Select(plan => plan.Id).Should().Contain(new[] { first.Id, second.Id });
        plans.Should().OnlyContain(plan => plan.IsActive);

        var singular = await Client.GetAsync("/api/trainee/diet-plan/current");
        singular.StatusCode.Should().Be(HttpStatusCode.OK);
        var current = await singular.Content.ReadFromJsonAsync<DietPlanResponse>();
        current.Should().NotBeNull();
        current!.Id.Should().BeOneOf(first.Id, second.Id);
    }

    [Test]
    [AuthorizationEvidence("GET", "/api/trainee/diet-plan/current", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/trainee/diet-plans/current", "own", "anonymous-denial")]
    public async Task TraineeCurrentDietRoutes_WithoutAuthentication_AreUnauthorized()
    {
        ClearAuthorizationHeader();

        using var singularResponse = await Client.GetAsync("/api/trainee/diet-plan/current");
        using var pluralResponse = await Client.GetAsync("/api/trainee/diet-plans/current");

        singularResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        pluralResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/update", "trainer-shared", "active-relationship-allow")]
    public async Task TrainerDietUpdate_WithActiveRelationship_UpdatesPlan()
    {
        var trainer = await SeedTrainerAsync("task10-active-diet-trainer", "task10-active-diet-trainer@example.com");
        var trainee = await SeedUserAsync("task10-active-diet-trainee", "task10-active-diet-trainee@example.com", "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        var plan = await CreateDietAsync(trainee.Id, "Active trainer plan", true);

        using var response = await Client.PostAsJsonAsync(
            $"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/update",
            ValidDietRequest("Active trainer updated plan"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedPlan = await response.Content.ReadFromJsonAsync<DietPlanResponse>();
        updatedPlan.Should().NotBeNull();
        updatedPlan!.Name.Should().Be("Active trainer updated plan");

        var persistedPlan = await GetDietPlanSnapshotAsync(plan.Id);
        persistedPlan.Name.Should().Be("Active trainer updated plan");
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/update", "trainer-shared", "unrelated-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/update", "trainer-shared", "anonymous-denial")]
    public async Task TrainerDietUpdate_WithoutRelationshipOrAuthentication_DoesNotMutatePlan()
    {
        var owner = await SeedTrainerAsync("task10-unrelated-diet-update-owner", "task10-unrelated-diet-update-owner@example.com");
        var unrelatedTrainer = await SeedTrainerAsync("task10-unrelated-diet-update-trainer", "task10-unrelated-diet-update-trainer@example.com");
        var trainee = await SeedUserAsync("task10-unrelated-diet-update-trainee", "task10-unrelated-diet-update-trainee@example.com", "password123");
        await LinkTrainerAndTraineeAsync(owner.Id, trainee.Id);

        SetAuthorizationHeader(owner.Id);
        var plan = await CreateDietAsync(trainee.Id, "Unrelated trainer update plan", true);
        var originalPlan = await GetDietPlanSnapshotAsync(plan.Id);
        var originalPersistenceCounts = await GetDietPersistenceCountsAsync();

        SetAuthorizationHeader(unrelatedTrainer.Id);
        await AssertExactMessageAsync(
            await Client.PostAsJsonAsync(
                $"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/update",
                ValidDietRequest("Unrelated trainer mutation")),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));
        (await GetDietPlanSnapshotAsync(plan.Id)).Should().BeEquivalentTo(originalPlan);
        (await GetDietPersistenceCountsAsync()).Should().Be(originalPersistenceCounts);

        ClearAuthorizationHeader();
        using (var anonymousResponse = await Client.PostAsJsonAsync(
                   $"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/update",
                   ValidDietRequest("Anonymous mutation")))
        {
            anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await GetDietPlanSnapshotAsync(plan.Id)).Should().BeEquivalentTo(originalPlan);
        (await GetDietPersistenceCountsAsync()).Should().Be(originalPersistenceCounts);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/update", "trainer-shared", "foreign-object-denial-no-mutation")]
    public async Task TrainerDietUpdate_ForPlanOwnedByAnotherTrainer_ReturnsNotFoundAndPreservesPlan()
    {
        var owner = await SeedTrainerAsync("task10-foreign-diet-update-owner", "task10-foreign-diet-update-owner@example.com");
        var otherTrainer = await SeedTrainerAsync("task10-foreign-diet-update-trainer", "task10-foreign-diet-update-trainer@example.com");
        var trainee = await SeedUserAsync("task10-foreign-diet-update-trainee", "task10-foreign-diet-update-trainee@example.com", "password123");
        await LinkTrainerAndTraineeAsync(owner.Id, trainee.Id);
        await LinkTrainerAndTraineeAsync(otherTrainer.Id, trainee.Id);

        SetAuthorizationHeader(owner.Id);
        var plan = await CreateDietAsync(trainee.Id, "Foreign trainer update plan", true);
        var originalPlan = await GetDietPlanSnapshotAsync(plan.Id);
        var originalPersistenceCounts = await GetDietPersistenceCountsAsync();

        SetAuthorizationHeader(otherTrainer.Id);
        await AssertExactMessageAsync(
            await Client.PostAsJsonAsync(
                $"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/update",
                ValidDietRequest("Foreign trainer mutation")),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));

        (await GetDietPlanSnapshotAsync(plan.Id)).Should().BeEquivalentTo(originalPlan);
        (await GetDietPersistenceCountsAsync()).Should().Be(originalPersistenceCounts);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/update", "trainer-shared", "former-relationship-denial")]
    public async Task TrainerDietUpdate_AfterUnlink_ReturnsNotFoundAndPreservesPlan()
    {
        var trainer = await SeedTrainerAsync("task10-former-diet-trainer", "task10-former-diet-trainer@example.com");
        var trainee = await SeedUserAsync("task10-former-diet-trainee", "task10-former-diet-trainee@example.com", "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        var plan = await CreateDietAsync(trainee.Id, "Former trainer plan", true);
        var originalPlan = await GetDietPlanSnapshotAsync(plan.Id);
        var originalPersistenceCounts = await GetDietPersistenceCountsAsync();
        using (var unlinkResponse = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/unlink", null))
        {
            unlinkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var response = await Client.PostAsJsonAsync(
            $"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/update",
            ValidDietRequest("Former trainer mutation"));
        await AssertExactMessageAsync(response, HttpStatusCode.NotFound, EnglishMessage(() => Messages.DidntFind));

        (await GetDietPlanSnapshotAsync(plan.Id)).Should().BeEquivalentTo(originalPlan);
        (await GetDietPersistenceCountsAsync()).Should().Be(originalPersistenceCounts);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/activate", "trainer-shared", "unrelated-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/activate", "trainer-shared", "anonymous-denial")]
    public async Task TrainerDietActivate_WithoutRelationshipOrAuthentication_DoesNotMutatePlan()
    {
        var owner = await SeedTrainerAsync("task10-unrelated-diet-activate-owner", "task10-unrelated-diet-activate-owner@example.com");
        var unrelatedTrainer = await SeedTrainerAsync("task10-unrelated-diet-activate-trainer", "task10-unrelated-diet-activate-trainer@example.com");
        var trainee = await SeedUserAsync("task10-unrelated-diet-activate-trainee", "task10-unrelated-diet-activate-trainee@example.com", "password123");
        await LinkTrainerAndTraineeAsync(owner.Id, trainee.Id);

        SetAuthorizationHeader(owner.Id);
        var plan = await CreateDietAsync(trainee.Id, "Unrelated trainer activate plan", false);
        var originalPlan = await GetDietPlanSnapshotAsync(plan.Id);
        var originalPersistenceCounts = await GetDietPersistenceCountsAsync();

        SetAuthorizationHeader(unrelatedTrainer.Id);
        await AssertExactMessageAsync(
            await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/activate", null),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));
        (await GetDietPlanSnapshotAsync(plan.Id)).Should().BeEquivalentTo(originalPlan);
        (await GetDietPersistenceCountsAsync()).Should().Be(originalPersistenceCounts);

        ClearAuthorizationHeader();
        using (var anonymousResponse = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/activate", null))
        {
            anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await GetDietPlanSnapshotAsync(plan.Id)).Should().BeEquivalentTo(originalPlan);
        (await GetDietPersistenceCountsAsync()).Should().Be(originalPersistenceCounts);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/activate", "trainer-shared", "foreign-object-denial-no-mutation")]
    public async Task TrainerDietActivate_ForPlanOwnedByAnotherTrainer_ReturnsNotFoundAndPreservesPlan()
    {
        var owner = await SeedTrainerAsync("task10-foreign-diet-activate-owner", "task10-foreign-diet-activate-owner@example.com");
        var otherTrainer = await SeedTrainerAsync("task10-foreign-diet-activate-trainer", "task10-foreign-diet-activate-trainer@example.com");
        var trainee = await SeedUserAsync("task10-foreign-diet-activate-trainee", "task10-foreign-diet-activate-trainee@example.com", "password123");
        await LinkTrainerAndTraineeAsync(owner.Id, trainee.Id);
        await LinkTrainerAndTraineeAsync(otherTrainer.Id, trainee.Id);

        SetAuthorizationHeader(owner.Id);
        var plan = await CreateDietAsync(trainee.Id, "Foreign trainer activate plan", false);
        var originalPlan = await GetDietPlanSnapshotAsync(plan.Id);
        var originalPersistenceCounts = await GetDietPersistenceCountsAsync();

        SetAuthorizationHeader(otherTrainer.Id);
        await AssertExactMessageAsync(
            await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/activate", null),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));

        (await GetDietPlanSnapshotAsync(plan.Id)).Should().BeEquivalentTo(originalPlan);
        (await GetDietPersistenceCountsAsync()).Should().Be(originalPersistenceCounts);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/activate", "trainer-shared", "former-relationship-denial")]
    public async Task TrainerDietActivate_AfterUnlink_ReturnsNotFoundAndPreservesPlan()
    {
        var trainer = await SeedTrainerAsync("task10-former-diet-activate-trainer", "task10-former-diet-activate-trainer@example.com");
        var trainee = await SeedUserAsync("task10-former-diet-activate-trainee", "task10-former-diet-activate-trainee@example.com", "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        var plan = await CreateDietAsync(trainee.Id, "Former trainer activate plan", false);
        var originalPlan = await GetDietPlanSnapshotAsync(plan.Id);
        var originalPersistenceCounts = await GetDietPersistenceCountsAsync();
        using (var unlinkResponse = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/unlink", null))
        {
            unlinkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await AssertExactMessageAsync(
            await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/activate", null),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));

        (await GetDietPlanSnapshotAsync(plan.Id)).Should().BeEquivalentTo(originalPlan);
        (await GetDietPersistenceCountsAsync()).Should().Be(originalPersistenceCounts);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/delete", "trainer-shared", "former-relationship-denial")]
    public async Task TrainerDietDelete_AfterUnlink_ReturnsNotFoundAndPreservesPlan()
    {
        var trainer = await SeedTrainerAsync("task10-former-diet-delete-trainer", "task10-former-diet-delete-trainer@example.com");
        var trainee = await SeedUserAsync("task10-former-diet-delete-trainee", "task10-former-diet-delete-trainee@example.com", "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        var plan = await CreateDietAsync(trainee.Id, "Former trainer delete plan", true);
        using (var unlinkResponse = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/unlink", null))
        {
            unlinkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var response = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/delete", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Former trainer delete plan");

        using var verifyScope = Factory.Services.CreateScope();
        var database = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedPlan = await database.DietPlans.IgnoreQueryFilters().AsNoTracking().SingleAsync(item => item.Id.ToString() == plan.Id);
        persistedPlan.Name.Should().Be("Former trainer delete plan");
        persistedPlan.IsDeleted.Should().BeFalse();
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/delete", "trainer-shared", "unrelated-relationship-denial")]
    [AuthorizationEvidence("POST", "/api/trainer/trainees/{traineeId}/diet-plans/{dietPlanId}/delete", "trainer-shared", "anonymous-denial")]
    public async Task TrainerDietDelete_WithoutRelationshipOrAuthentication_DoesNotMutatePlan()
    {
        var owner = await SeedTrainerAsync("task10-unrelated-diet-owner", "task10-unrelated-diet-owner@example.com");
        var unrelatedTrainer = await SeedTrainerAsync("task10-unrelated-diet-trainer", "task10-unrelated-diet-trainer@example.com");
        var trainee = await SeedUserAsync("task10-unrelated-diet-trainee", "task10-unrelated-diet-trainee@example.com", "password123");
        await LinkTrainerAndTraineeAsync(owner.Id, trainee.Id);

        SetAuthorizationHeader(owner.Id);
        var plan = await CreateDietAsync(trainee.Id, "Unrelated trainer delete plan", true);

        SetAuthorizationHeader(unrelatedTrainer.Id);
        using (var unrelatedResponse = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/delete", null))
        {
            unrelatedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        ClearAuthorizationHeader();
        using (var anonymousResponse = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/delete", null))
        {
            anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using var verifyScope = Factory.Services.CreateScope();
        var database = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedPlan = await database.DietPlans.IgnoreQueryFilters().AsNoTracking().SingleAsync(item => item.Id.ToString() == plan.Id);
        persistedPlan.Name.Should().Be("Unrelated trainer delete plan");
        persistedPlan.IsDeleted.Should().BeFalse();
    }

    [Test]
    public async Task DietFailures_DoNotWriteOrQueueCommands()
    {
        var trainer = await SeedTrainerAsync("trainer-diet-failure", "trainer-diet-failure@example.com");
        var trainee = await SeedUserAsync("trainee-diet-failure", "trainee-diet-failure@example.com", "password123");
        var foreignTrainee = await SeedUserAsync("trainee-diet-foreign", "trainee-diet-foreign@example.com", "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);
        SetAuthorizationHeader(trainer.Id);

        var beforeInvalidShape = await GetDietPersistenceCountsAsync();
        await AssertExactMessageAsync(
            await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans", new
            {
                name = " ",
                startDate = new DateOnly(2026, 7, 23),
                isActive = true,
                meals = new[] { new { name = "Meal", order = 0, estimatedCalories = 500 } }
            }),
            HttpStatusCode.BadRequest,
            EnglishMessage(() => Messages.FieldRequired));
        (await GetDietPersistenceCountsAsync()).Should().Be(beforeInvalidShape);

        await AssertExactMessageAsync(
            await Client.PostAsJsonAsync($"/api/trainer/trainees/{foreignTrainee.Id}/diet-plans", ValidDietRequest()),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));
        (await GetDietPersistenceCountsAsync()).Should().Be(beforeInvalidShape);

        var plan = await CreateDietAsync(trainee.Id, "Deleted failure plan", true);
        await AssertExactMessageAsync(
            await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/delete", null),
            HttpStatusCode.OK,
            EnglishMessage(() => Messages.Deleted));
        var afterDelete = await GetDietPersistenceCountsAsync();

        await AssertExactMessageAsync(
            await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/update", ValidDietRequest()),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));
        (await GetDietPersistenceCountsAsync()).Should().Be(afterDelete);
    }

    [Test]
    public async Task TrainerCanCreateGlobalDietWithoutMeals_WhenMacroTargetsAreProvided()
    {
        var trainer = await SeedTrainerAsync("trainer-global-diet", "trainer-global-diet@example.com");
        var trainee = await SeedUserAsync(name: "trainee-global-diet", email: "trainee-global-diet@example.com", password: "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        var createResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans", new
        {
            name = "Rest day",
            startDate = new DateOnly(2026, 6, 24),
            estimatedCalories = 450,
            proteinGrams = 50,
            carbsGrams = 40,
            fatGrams = 10,
            notes = "Global only",
            isActive = true,
            meals = Array.Empty<object>()
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<DietPlanResponse>();
        created.Should().NotBeNull();
        created!.Meals.Should().BeEmpty();
        created.IsActive.Should().BeTrue();
    }

    [Test]
    public async Task TraineeCurrentDiets_ReturnsAllActiveDiets()
    {
        var trainer = await SeedTrainerAsync("trainer-current-diets", "trainer-current-diets@example.com");
        var trainee = await SeedUserAsync(name: "trainee-current-diets", email: "trainee-current-diets@example.com", password: "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        var trainingDay = await CreateDietAsync(trainee.Id, "Training Day", true);
        var restDay = await CreateDietAsync(trainee.Id, "Rest Day", true);

        SetAuthorizationHeader(trainee.Id);
        var currentResponse = await Client.GetAsync("/api/trainee/diet-plans/current");
        currentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var currentPlans = await currentResponse.Content.ReadFromJsonAsync<List<DietPlanResponse>>();
        currentPlans.Should().NotBeNull();
        currentPlans!.Select(x => x.Id).Should().Contain(new[] { trainingDay.Id, restDay.Id });
        currentPlans.Should().OnlyContain(x => x.IsActive);
    }

    [Test]
    public async Task ActiveDietUpdate_QueuesDietNotificationCommand()
    {
        var trainer = await SeedTrainerAsync("trainer-diet-notif", "trainer-diet-notif@example.com");
        var trainee = await SeedUserAsync(name: "trainee-diet-notif", email: "trainee-diet-notif@example.com", password: "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        var plan = await CreateDietAsync(trainee.Id, "Diet Notification", true);

        var response = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/update", new
        {
            name = "Diet Notification v2",
            startDate = new DateOnly(2026, 6, 5),
            isActive = true,
            meals = new object[] { new { name = "Meal", order = 0, estimatedCalories = 500 } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notificationCommand = await db.CommandEnvelopes
            .Where(x => x.CommandTypeFullName.Contains("DietPlanUpdatedInAppNotificationCommand"))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        notificationCommand.Should().NotBeNull();
        notificationCommand!.PayloadJson.Should().Contain("Diet Notification v2");
        notificationCommand.PayloadJson.Should().Contain(trainee.Id.ToString());
    }

    private async Task<DietPlanResponse> CreateDietAsync(Id<User> traineeId, string name, bool isActive)
    {
        var response = await Client.PostAsJsonAsync($"/api/trainer/trainees/{traineeId}/diet-plans", new
        {
            name,
            startDate = new DateOnly(2026, 6, 1),
            isActive,
            meals = new object[] { new { name = "Meal", order = 0, estimatedCalories = 400 } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<DietPlanResponse>())!;
    }

    private async Task<(int Plans, int Histories, int Commands)> GetDietPersistenceCountsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (
            await db.DietPlans.CountAsync(),
            await db.DietPlanHistories.CountAsync(),
            await db.CommandEnvelopes.CountAsync());
    }

    private async Task<DietPlanSnapshot> GetDietPlanSnapshotAsync(string dietPlanId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var plan = await db.DietPlans
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(item => item.Meals)
            .SingleAsync(item => item.Id.ToString() == dietPlanId);

        return new DietPlanSnapshot(
            plan.Name,
            plan.StartDate,
            plan.EndDate,
            plan.EstimatedCalories,
            plan.ProteinGrams,
            plan.CarbsGrams,
            plan.FatGrams,
            plan.Notes,
            plan.IsActive,
            plan.IsDeleted,
            plan.Meals
                .OrderBy(meal => meal.Order)
                .Select(meal => new DietMealSnapshot(
                    meal.Name,
                    meal.Order,
                    meal.Description,
                    meal.EstimatedCalories,
                    meal.ProteinGrams,
                    meal.CarbsGrams,
                    meal.FatGrams))
                .ToArray());
    }

    private static object ValidDietRequest(string name = "Valid diet")
        => new
        {
            name,
            startDate = new DateOnly(2026, 7, 23),
            isActive = true,
            meals = new[] { new { name = "Meal", order = 0, estimatedCalories = 500 } }
        };

    private static async Task AssertExactMessageAsync(HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedMessage)
    {
        response.StatusCode.Should().Be(expectedStatus);
        (await response.Content.ReadAsStringAsync()).Should().Be(JsonSerializer.Serialize(new { msg = expectedMessage }));
    }

    private static string EnglishMessage(Func<string> getMessage)
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        try
        {
            return getMessage();
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
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

    private sealed class DietPlanResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }

        [JsonPropertyName("meals")]
        public List<DietMealResponse> Meals { get; set; } = [];
    }

    private sealed class DietMealResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class DietPlanHistoryResponse
    {
        [JsonPropertyName("changeType")]
        public string ChangeType { get; set; } = string.Empty;
    }

    private sealed record DietPlanSnapshot(
        string Name,
        DateOnly StartDate,
        DateOnly? EndDate,
        int? EstimatedCalories,
        decimal? ProteinGrams,
        decimal? CarbsGrams,
        decimal? FatGrams,
        string? Notes,
        bool IsActive,
        bool IsDeleted,
        IReadOnlyList<DietMealSnapshot> Meals);

    private sealed record DietMealSnapshot(
        string Name,
        int Order,
        string? Description,
        int? EstimatedCalories,
        decimal? ProteinGrams,
        decimal? CarbsGrams,
        decimal? FatGrams);
}
