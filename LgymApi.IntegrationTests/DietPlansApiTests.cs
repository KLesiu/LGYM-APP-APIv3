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
using LgymApi.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class DietPlansApiTests : IntegrationTestBase
{
    [Test]
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
    public async Task TrainerCannotCreateDietForForeignTrainee()
    {
        var ownerTrainer = await SeedTrainerAsync("trainer-owner-diet", "trainer-owner-diet@example.com");
        var otherTrainer = await SeedTrainerAsync("trainer-other-diet", "trainer-other-diet@example.com");
        var trainee = await SeedUserAsync(name: "trainee-foreign-diet", email: "trainee-foreign-diet@example.com", password: "password123");
        await LinkTrainerAndTraineeAsync(ownerTrainer.Id, trainee.Id);

        SetAuthorizationHeader(otherTrainer.Id);
        var response = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans", new
        {
            name = "Forbidden",
            startDate = new DateOnly(2026, 6, 1),
            isActive = false,
            meals = new object[] { new { name = "Meal", order = 0 } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ActivateDietPlan_PreservesExistingActivePlanCharacterization()
    {
        var trainer = await SeedTrainerAsync("trainer-activate-diet", "trainer-activate-diet@example.com");
        var trainee = await SeedUserAsync(name: "trainee-activate-diet", email: "trainee-activate-diet@example.com", password: "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        var first = await CreateDietAsync(trainee.Id, "Diet A", true);
        var second = await CreateDietAsync(trainee.Id, "Diet B", false);

        var activateResponse = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{second.Id}/activate", null);
        activateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listResponse = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans");
        var plans = await listResponse.Content.ReadFromJsonAsync<List<DietPlanResponse>>();
        plans.Should().Contain(x => x.IsActive && x.Id == second.Id);
        plans.Should().Contain(x => x.IsActive && x.Id == first.Id);
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
    public async Task DietDeleteAndSingleRead_PreserveContracts()
    {
        var owner = await SeedTrainerAsync("trainer-diet-owner", "trainer-diet-owner@example.com");
        var otherTrainer = await SeedTrainerAsync("trainer-diet-other", "trainer-diet-other@example.com");
        var trainee = await SeedUserAsync("trainee-diet-owned", "trainee-diet-owned@example.com", "password123");
        await LinkTrainerAndTraineeAsync(owner.Id, trainee.Id);
        await LinkTrainerAndTraineeAsync(otherTrainer.Id, trainee.Id);

        SetAuthorizationHeader(owner.Id);
        var plan = await CreateDietAsync(trainee.Id, "Owner plan", true);

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
            await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/diet-plans/{plan.Id}/delete", null),
            HttpStatusCode.NotFound,
            EnglishMessage(() => Messages.DidntFind));

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

    private static object ValidDietRequest()
        => new
        {
            name = "Valid diet",
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
}
