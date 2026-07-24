using System.Globalization;
using System.Net;
using System.Net.Http.Json;
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
public sealed class SupplementationApiTests : IntegrationTestBase
{
    private CultureInfo? _originalCulture;
    private CultureInfo? _originalUiCulture;

    [SetUp]
    public void UseEnglishRequestCultureForGoldenAssertions()
    {
        _originalCulture = CultureInfo.CurrentCulture;
        _originalUiCulture = CultureInfo.CurrentUICulture;
        var englishCulture = CultureInfo.GetCultureInfo("en");
        CultureInfo.CurrentCulture = englishCulture;
        CultureInfo.CurrentUICulture = englishCulture;
        SetRequestCulture("en");
    }

    [TearDown]
    public void RestoreCultureAfterGoldenAssertions()
    {
        CultureInfo.CurrentCulture = _originalCulture!;
        CultureInfo.CurrentUICulture = _originalUiCulture!;
    }

    [Test]
    public async Task SupplementScheduleAndAdherenceFlow_Works()
    {
        var trainer = await SeedTrainerAsync("trainer-supp", "trainer-supp@example.com");
        var trainee = await SeedUserAsync(name: "trainee-supp", email: "trainee-supp@example.com", password: "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var dayMask = MaskForDate(date);

        SetAuthorizationHeader(trainer.Id);
        var createPlanResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans", new
        {
            name = "Cut Stack",
            notes = "Week 1",
            items = new object[]
            {
                new { supplementName = "Omega 3", dosage = "2 caps", timeOfDay = "08:00", daysOfWeekMask = dayMask, order = 0 }
            }
        });

        createPlanResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdPlan = await createPlanResponse.Content.ReadFromJsonAsync<SupplementPlanResponse>();
        createdPlan.Should().NotBeNull();

        var assignResponse = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans/{createdPlan!.Id}/assign", content: null);
        assignResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        SetAuthorizationHeader(trainee.Id);
        var scheduleResponse = await Client.GetAsync($"/api/trainee/supplements/schedule?date={date:yyyy-MM-dd}");
        scheduleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var schedule = await scheduleResponse.Content.ReadFromJsonAsync<List<SupplementScheduleEntryResponse>>();
        schedule.Should().NotBeNull();
        schedule!.Should().ContainSingle();
        schedule[0].Taken.Should().BeFalse();

        var checkOffResponse = await Client.PostAsJsonAsync("/api/trainee/supplements/intakes/check-off", new
        {
            planItemId = schedule[0].PlanItemId,
            intakeDate = date,
            takenAt = DateTimeOffset.UtcNow
        });

        checkOffResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var scheduleAfterResponse = await Client.GetAsync($"/api/trainee/supplements/schedule?date={date:yyyy-MM-dd}");
        scheduleAfterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var scheduleAfter = await scheduleAfterResponse.Content.ReadFromJsonAsync<List<SupplementScheduleEntryResponse>>();
        scheduleAfter.Should().NotBeNull();
        scheduleAfter![0].Taken.Should().BeTrue();

        SetAuthorizationHeader(trainer.Id);
        var complianceResponse = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/supplements/compliance?fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}");
        complianceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var compliance = await complianceResponse.Content.ReadFromJsonAsync<SupplementComplianceResponse>();
        compliance.Should().NotBeNull();
        compliance!.PlannedDoses.Should().Be(1);
        compliance.TakenDoses.Should().Be(1);
        compliance.AdherenceRate.Should().Be(100);
    }

    [Test]
    public async Task SupplementationEndpoints_EnforceAuthorization()
    {
        var trainerOwner = await SeedTrainerAsync("trainer-owner", "trainer-owner@example.com");
        var trainerOther = await SeedTrainerAsync("trainer-other", "trainer-other@example.com");
        var trainee = await SeedUserAsync(name: "trainee-auth", email: "trainee-auth@example.com", password: "password123");
        await LinkTrainerAndTraineeAsync(trainerOwner.Id, trainee.Id);

        SetAuthorizationHeader(trainerOther.Id);
        var summaryByOtherTrainer = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/supplements/compliance?fromDate=2026-01-01&toDate=2026-01-02");
        summaryByOtherTrainer.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var regularUser = await SeedUserAsync(name: "normal-user", email: "normal-user@example.com", password: "password123");
        SetAuthorizationHeader(regularUser.Id);
        var trainerOnlyEndpoint = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans");
        trainerOnlyEndpoint.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task SupplementationEndpoints_InvalidIds_ReturnBadRequest()
    {
        var trainer = await SeedTrainerAsync("trainer-invalid-supp", "trainer-invalid-supp@example.com");
        SetAuthorizationHeader(trainer.Id);

        var badTraineeIdResponse = await Client.GetAsync("/api/trainer/trainees/not-a-guid/supplement-plans");
        var badPlanIdResponse = await Client.PostAsJsonAsync("/api/trainer/trainees/00000000-0000-0000-0000-000000000001/supplement-plans/not-a-guid/update", new
        {
            name = "x",
            items = new object[] { new { supplementName = "A", dosage = "1", timeOfDay = "08:00", daysOfWeekMask = 127, order = 0 } }
        });

        badTraineeIdResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        badPlanIdResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task TrainerSupplementPlanCrudEndpoints_Work()
    {
        var trainer = await SeedTrainerAsync("trainer-supp-crud", "trainer-supp-crud@example.com");
        var trainee = await SeedUserAsync(name: "trainee-supp-crud", email: "trainee-supp-crud@example.com", password: "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        var createResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans", new
        {
            name = "Bulk",
            notes = "v1",
            items = new object[]
            {
                new { supplementName = "Magnesium", dosage = "1 tab", timeOfDay = "21:00", daysOfWeekMask = 127, order = 0 }
            }
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<SupplementPlanResponse>();

        var listResponse = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var plans = await listResponse.Content.ReadFromJsonAsync<List<SupplementPlanResponse>>();
        plans.Should().NotBeNull();
        plans!.Any(x => x.Id == created!.Id).Should().BeTrue();

        var updateResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans/{created!.Id}/update", new
        {
            name = "Bulk v2",
            notes = "v2",
            items = new object[]
            {
                new { supplementName = "Magnesium", dosage = "2 tabs", timeOfDay = "22:00", daysOfWeekMask = 127, order = 0 }
            }
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await updateResponse.Content.ReadFromJsonAsync<SupplementPlanResponse>();
        updated.Should().NotBeNull();
        updated!.Id.Should().NotBe(created.Id);

        var unassignResponse = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans/unassign", content: null);
        unassignResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var deleteResponse = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans/{updated.Id}/delete", content: null);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task TraineeCheckOff_WithInvalidPlanItemId_ReturnsBadRequest()
    {
        var trainee = await SeedUserAsync(name: "trainee-invalid-check", email: "trainee-invalid-check@example.com", password: "password123");
        SetAuthorizationHeader(trainee.Id);

        var response = await Client.PostAsJsonAsync("/api/trainee/supplements/intakes/check-off", new
        {
            planItemId = "not-a-guid",
            intakeDate = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Compliance_WithTooLargeDateRange_ReturnsBadRequest()
    {
         var trainer = await SeedTrainerAsync("trainer-supp-range", "trainer-supp-range@example.com");
         var trainee = await SeedUserAsync(name: "trainee-supp-range", email: "trainee-supp-range@example.com", password: "password123");
         await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        var response = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/supplements/compliance?fromDate=2025-01-01&toDate=2026-12-31");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Compliance_WithoutRequiredQueryDates_ReturnsBadRequest()
    {
         var trainer = await SeedTrainerAsync("trainer-supp-missing-dates", "trainer-supp-missing-dates@example.com");
         var trainee = await SeedUserAsync(name: "trainee-supp-missing-dates", email: "trainee-supp-missing-dates@example.com", password: "password123");
         await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        var response = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/supplements/compliance");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreatePlan_WithNullItems_ReturnsBadRequest()
    {
         var trainer = await SeedTrainerAsync("trainer-supp-null-items", "trainer-supp-null-items@example.com");
         var trainee = await SeedUserAsync(name: "trainee-supp-null-items", email: "trainee-supp-null-items@example.com", password: "password123");
         await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        var response = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans", new
        {
            name = "Plan",
            notes = "n",
            items = (object?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task TraineeCheckOff_WithoutIntakeDate_ReturnsBadRequest()
    {
        var trainee = await SeedUserAsync(name: "trainee-missing-intake-date", email: "trainee-missing-intake-date@example.com", password: "password123");
        SetAuthorizationHeader(trainee.Id);

        var response = await Client.PostAsJsonAsync("/api/trainee/supplements/intakes/check-off", new
        {
            planItemId = Domain.ValueObjects.Id<object>.New().ToString()
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SupplementUpdate_ReplacesIdentityAndRetainsActiveState()
    {
        var trainer = await SeedTrainerAsync("trainer-supp-update", "trainer-supp-update@example.com");
        var trainee = await SeedUserAsync("trainee-supp-update", "trainee-supp-update@example.com", "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        var created = await CreateSupplementPlanAsync(
            trainer.Id,
            trainee.Id,
            "Original stack",
            "Original notes",
            new SupplementPlanItemInput("Original item", "1 cap", "08:00", 127, 0));

        created.IsActive.Should().BeFalse();
        created.Items.Should().ContainSingle();

        SetAuthorizationHeader(trainer.Id);
        var listResponse = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var list = await ReadJsonAsync(listResponse))
        {
            list.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
            list.RootElement.GetArrayLength().Should().Be(1);
            AssertPlanContract(list.RootElement[0]);
            list.RootElement[0].GetProperty("_id").GetString().Should().Be(created.Id);
            list.RootElement[0].GetProperty("name").GetString().Should().Be("Original stack");
        }

        var assignResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{trainee.Id}/supplement-plans/{created.Id}/assign",
            content: null);
        await AssertMessageAsync(assignResponse, HttpStatusCode.OK, Messages.Updated);

        var updateResponse = await Client.PostAsJsonAsync(
            $"/api/trainer/trainees/{trainee.Id}/supplement-plans/{created.Id}/update",
            new
            {
                name = "Replacement stack",
                notes = "Replacement notes",
                items = new object[]
                {
                    new { supplementName = "Later", dosage = "2 caps", timeOfDay = "20:00", daysOfWeekMask = 127, order = 1 },
                    new { supplementName = "Earlier", dosage = "1 cap", timeOfDay = "08:00", daysOfWeekMask = 65, order = 0 }
                }
            });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        string updatedId;
        string[] updatedItemIds;
        using (var updated = await ReadJsonAsync(updateResponse))
        {
            AssertPlanContract(updated.RootElement);
            updatedId = updated.RootElement.GetProperty("_id").GetString()!;
            updatedId.Should().NotBe(created.Id);
            updated.RootElement.GetProperty("trainerId").GetString().Should().Be(trainer.Id.ToString());
            updated.RootElement.GetProperty("traineeId").GetString().Should().Be(trainee.Id.ToString());
            updated.RootElement.GetProperty("name").GetString().Should().Be("Replacement stack");
            updated.RootElement.GetProperty("notes").GetString().Should().Be("Replacement notes");
            updated.RootElement.GetProperty("isActive").GetBoolean().Should().BeTrue();
            updated.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("supplementName").GetString())
                .Should().Equal("Earlier", "Later");
            updatedItemIds = updated.RootElement.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("_id").GetString()!)
                .ToArray();
        }

        var persistedPlans = await GetSupplementPlansIgnoringFiltersAsync(trainee.Id);
        persistedPlans.Should().HaveCount(2);
        var replacedPlan = persistedPlans.Single(plan => plan.Id.ToString() == created.Id);
        var replacementPlan = persistedPlans.Single(plan => plan.Id.ToString() == updatedId);
        replacedPlan.IsDeleted.Should().BeTrue();
        replacedPlan.IsActive.Should().BeFalse();
        replacementPlan.IsDeleted.Should().BeFalse();
        replacementPlan.IsActive.Should().BeTrue();
        replacementPlan.Items.Select(item => item.Id.ToString()).Should().Equal(updatedItemIds);
        replacementPlan.Items.Select(item => item.Id.ToString())
            .Intersect(created.Items.Select(item => item.Id))
            .Should().BeEmpty();

        var deleteResponse = await Client.PostAsync(
            $"/api/trainer/trainees/{trainee.Id}/supplement-plans/{updatedId}/delete",
            content: null);
        await AssertMessageAsync(deleteResponse, HttpStatusCode.OK, Messages.Deleted);

        persistedPlans = await GetSupplementPlansIgnoringFiltersAsync(trainee.Id);
        var deletedReplacement = persistedPlans.Single(plan => plan.Id.ToString() == updatedId);
        deletedReplacement.IsDeleted.Should().BeTrue();
        deletedReplacement.IsActive.Should().BeFalse();
    }

    [Test]
    public async Task AssignAndUnassign_PreserveSingleActiveAndNoOpSemantics()
    {
        var trainer = await SeedTrainerAsync("trainer-supp-assignment", "trainer-supp-assignment@example.com");
        var otherTrainer = await SeedTrainerAsync("trainer-supp-assignment-other", "trainer-supp-assignment-other@example.com");
        var trainee = await SeedUserAsync("trainee-supp-assignment", "trainee-supp-assignment@example.com", "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        var first = await CreateSupplementPlanAsync(
            trainer.Id,
            trainee.Id,
            "First",
            "First notes",
            new SupplementPlanItemInput("First item", "1", "08:00", 127, 0));
        var second = await CreateSupplementPlanAsync(
            trainer.Id,
            trainee.Id,
            "Second",
            "Second notes",
            new SupplementPlanItemInput("Second item", "1", "09:00", 127, 0));
        var inactive = await CreateSupplementPlanAsync(
            trainer.Id,
            trainee.Id,
            "Inactive",
            "Inactive notes",
            new SupplementPlanItemInput("Inactive item", "1", "10:00", 127, 0));

        SetAuthorizationHeader(trainer.Id);
        await AssertMessageAsync(
            await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans/{first.Id}/assign", null),
            HttpStatusCode.OK,
            Messages.Updated);
        await AssertSingleActivePlanAsync(trainee.Id, first.Id);

        await AssertMessageAsync(
            await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans/{second.Id}/assign", null),
            HttpStatusCode.OK,
            Messages.Updated);
        await AssertSingleActivePlanAsync(trainee.Id, second.Id);

        var activePlans = await GetSupplementPlansIgnoringFiltersAsync(trainee.Id);
        activePlans.Single(plan => plan.Id.ToString() == first.Id).IsActive.Should().BeFalse();
        activePlans.Single(plan => plan.Id.ToString() == inactive.Id).IsActive.Should().BeFalse();

        await AssertMessageAsync(
            await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans/unassign", null),
            HttpStatusCode.OK,
            Messages.Updated);
        (await GetSupplementPlansIgnoringFiltersAsync(trainee.Id)).Should().NotContain(plan => plan.IsActive);

        var noActiveSnapshot = await GetSupplementPlanSnapshotsAsync(trainee.Id);
        await AssertMessageAsync(
            await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans/unassign", null),
            HttpStatusCode.OK,
            Messages.Updated);
        (await GetSupplementPlanSnapshotsAsync(trainee.Id)).Should().Equal(noActiveSnapshot);

        var foreignPlanId = await SeedSupplementPlanAsync(otherTrainer.Id, trainee.Id, "Foreign active", isActive: true);
        var foreignBefore = (await GetSupplementPlanSnapshotsAsync(trainee.Id))
            .Single(plan => plan.Id == foreignPlanId.ToString());

        await AssertMessageAsync(
            await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans/unassign", null),
            HttpStatusCode.OK,
            Messages.Updated);

        var foreignAfter = (await GetSupplementPlanSnapshotsAsync(trainee.Id))
            .Single(plan => plan.Id == foreignPlanId.ToString());
        foreignAfter.Should().Be(foreignBefore);
        foreignAfter.IsActive.Should().BeTrue();
    }

    [Test]
    public async Task Schedule_PreservesMaskOrderingAndEmptyResult()
    {
        var trainer = await SeedTrainerAsync("trainer-supp-schedule", "trainer-supp-schedule@example.com");
        var trainee = await SeedUserAsync("trainee-supp-schedule", "trainee-supp-schedule@example.com", "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);
        var monday = new DateOnly(2026, 7, 6);

        var plan = await CreateSupplementPlanAsync(
            trainer.Id,
            trainee.Id,
            "Schedule stack",
            "Schedule notes",
            new SupplementPlanItemInput("Later", "1", "20:00", 1, 2),
            new SupplementPlanItemInput("Tuesday only", "1", "07:00", 2, 0),
            new SupplementPlanItemInput("Zinc", "1", "09:00", 1, 1),
            new SupplementPlanItemInput("Iron", "1", "08:00", 1, 1),
            new SupplementPlanItemInput("Daily", "1", "07:00", 127, 0));

        SetAuthorizationHeader(trainer.Id);
        await AssertMessageAsync(
            await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans/{plan.Id}/assign", null),
            HttpStatusCode.OK,
            Messages.Updated);

        var iron = plan.Items.Single(item => item.SupplementName == "Iron");
        var takenAt = new DateTimeOffset(2026, 7, 6, 8, 5, 0, TimeSpan.Zero);
        SetAuthorizationHeader(trainee.Id);
        var checkOff = await Client.PostAsJsonAsync("/api/trainee/supplements/intakes/check-off", new
        {
            planItemId = iron.Id,
            intakeDate = monday,
            takenAt
        });
        checkOff.StatusCode.Should().Be(HttpStatusCode.OK);

        var scheduledResponse = await Client.GetAsync($"/api/trainee/supplements/schedule?date={monday:yyyy-MM-dd}");
        scheduledResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var schedule = await ReadJsonAsync(scheduledResponse))
        {
            schedule.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
            schedule.RootElement.EnumerateArray().Select(item => item.GetProperty("supplementName").GetString())
                .Should().Equal("Daily", "Iron", "Zinc", "Later");
            schedule.RootElement.EnumerateArray().Should().NotContain(item => item.GetProperty("supplementName").GetString() == "Tuesday only");

            foreach (var entry in schedule.RootElement.EnumerateArray())
            {
                var name = entry.GetProperty("supplementName").GetString();
                AssertScheduleContract(
                    entry,
                    monday,
                    name == "Iron",
                    name == "Iron" ? takenAt : null);
            }
        }

        var beforeDefaultDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var defaultScheduleResponse = await Client.GetAsync("/api/trainee/supplements/schedule");
        var afterDefaultDate = DateOnly.FromDateTime(DateTime.UtcNow);
        defaultScheduleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var defaultSchedule = await ReadJsonAsync(defaultScheduleResponse))
        {
            defaultSchedule.RootElement.EnumerateArray().Should().Contain(entry =>
                entry.GetProperty("supplementName").GetString() == "Daily");
            foreach (var entry in defaultSchedule.RootElement.EnumerateArray())
            {
                DateOnly.Parse(entry.GetProperty("intakeDate").GetString()!).Should().BeOneOf(beforeDefaultDate, afterDefaultDate);
            }
        }

        var emptyTrainee = await SeedUserAsync("trainee-supp-empty-schedule", "trainee-supp-empty-schedule@example.com", "password123");
        SetAuthorizationHeader(emptyTrainee.Id);
        var emptyResponse = await Client.GetAsync($"/api/trainee/supplements/schedule?date={monday:yyyy-MM-dd}");
        emptyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var empty = await ReadJsonAsync(emptyResponse);
        empty.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        empty.RootElement.GetArrayLength().Should().Be(0);
    }

    [Test]
    public async Task CheckOff_PreservesExistingAndNewLogSemantics()
    {
        var trainer = await SeedTrainerAsync("trainer-supp-check-off", "trainer-supp-check-off@example.com");
        var trainee = await SeedUserAsync("trainee-supp-check-off", "trainee-supp-check-off@example.com", "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);
        var intakeDate = new DateOnly(2026, 7, 7);
        var plan = await CreateSupplementPlanAsync(
            trainer.Id,
            trainee.Id,
            "Check-off stack",
            "Check-off notes",
            new SupplementPlanItemInput("Explicit", "1", "08:00", 127, 0),
            new SupplementPlanItemInput("Default", "1", "09:00", 127, 1));

        SetAuthorizationHeader(trainer.Id);
        await AssertMessageAsync(
            await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans/{plan.Id}/assign", null),
            HttpStatusCode.OK,
            Messages.Updated);

        var explicitItem = plan.Items.Single(item => item.SupplementName == "Explicit");
        var defaultItem = plan.Items.Single(item => item.SupplementName == "Default");
        var firstTakenAt = new DateTimeOffset(2026, 7, 7, 8, 5, 0, TimeSpan.Zero);
        var replacementTakenAt = new DateTimeOffset(2026, 7, 7, 8, 10, 0, TimeSpan.Zero);
        SetAuthorizationHeader(trainee.Id);

        var explicitResponse = await Client.PostAsJsonAsync("/api/trainee/supplements/intakes/check-off", new
        {
            planItemId = explicitItem.Id,
            intakeDate,
            takenAt = firstTakenAt
        });
        explicitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var explicitEntry = await ReadJsonAsync(explicitResponse))
        {
            AssertScheduleContract(explicitEntry.RootElement, intakeDate, taken: true, expectedTakenAt: firstTakenAt);
        }

        var retainedResponse = await Client.PostAsJsonAsync("/api/trainee/supplements/intakes/check-off", new
        {
            planItemId = explicitItem.Id,
            intakeDate
        });
        retainedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var retainedEntry = await ReadJsonAsync(retainedResponse))
        {
            AssertScheduleContract(retainedEntry.RootElement, intakeDate, taken: true, expectedTakenAt: firstTakenAt);
        }

        var replacedResponse = await Client.PostAsJsonAsync("/api/trainee/supplements/intakes/check-off", new
        {
            planItemId = explicitItem.Id,
            intakeDate,
            takenAt = replacementTakenAt
        });
        replacedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var replacedEntry = await ReadJsonAsync(replacedResponse))
        {
            AssertScheduleContract(replacedEntry.RootElement, intakeDate, taken: true, expectedTakenAt: replacementTakenAt);
        }

        var beforeDefaultTakenAt = DateTimeOffset.UtcNow;
        var defaultResponse = await Client.PostAsJsonAsync("/api/trainee/supplements/intakes/check-off", new
        {
            planItemId = defaultItem.Id,
            intakeDate
        });
        var afterDefaultTakenAt = DateTimeOffset.UtcNow;
        defaultResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var defaultEntry = await ReadJsonAsync(defaultResponse))
        {
            AssertScheduleContract(defaultEntry.RootElement, intakeDate, taken: true, expectedTakenAt: null);
            var generatedTakenAt = defaultEntry.RootElement.GetProperty("takenAt").GetDateTimeOffset();
            generatedTakenAt.Should().BeOnOrAfter(beforeDefaultTakenAt).And.BeOnOrBefore(afterDefaultTakenAt);
        }

        var logs = await GetSupplementIntakeLogsAsync(trainee.Id);
        logs.Should().HaveCount(2);
        logs.Single(log => log.PlanItemId.ToString() == explicitItem.Id).TakenAt.Should().Be(replacementTakenAt);
        logs.Single(log => log.PlanItemId.ToString() == defaultItem.Id).TakenAt.Should().BeOnOrAfter(beforeDefaultTakenAt).And.BeOnOrBefore(afterDefaultTakenAt);
    }

    [Test]
    public async Task Compliance_PreservesInclusiveRoundingAndZeroSummary()
    {
        var trainer = await SeedTrainerAsync("trainer-supp-compliance", "trainer-supp-compliance@example.com");
        var trainee = await SeedUserAsync("trainee-supp-compliance", "trainee-supp-compliance@example.com", "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);
        var fromDate = new DateOnly(2026, 7, 6);
        var toDate = new DateOnly(2026, 7, 8);
        var plan = await CreateSupplementPlanAsync(
            trainer.Id,
            trainee.Id,
            "Compliance stack",
            "Compliance notes",
            new SupplementPlanItemInput("Daily", "1", "08:00", 127, 0));

        SetAuthorizationHeader(trainer.Id);
        await AssertMessageAsync(
            await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans/{plan.Id}/assign", null),
            HttpStatusCode.OK,
            Messages.Updated);

        SetAuthorizationHeader(trainee.Id);
        var checkOffResponse = await Client.PostAsJsonAsync("/api/trainee/supplements/intakes/check-off", new
        {
            planItemId = plan.Items.Single().Id,
            intakeDate = fromDate,
            takenAt = new DateTimeOffset(2026, 7, 6, 8, 0, 0, TimeSpan.Zero)
        });
        checkOffResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        SetAuthorizationHeader(trainer.Id);
        var summaryResponse = await Client.GetAsync(
            $"/api/trainer/trainees/{trainee.Id}/supplements/compliance?fromDate={fromDate:yyyy-MM-dd}&toDate={toDate:yyyy-MM-dd}");
        summaryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var summary = await ReadJsonAsync(summaryResponse))
        {
            AssertComplianceContract(summary.RootElement, trainee.Id.ToString(), fromDate, toDate, 3, 1, 33.33);
        }

        await AssertMessageAsync(
            await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/supplements/compliance?fromDate={toDate:yyyy-MM-dd}&toDate={fromDate:yyyy-MM-dd}"),
            HttpStatusCode.BadRequest,
            Messages.InvalidDateRange);
        await AssertMessageAsync(
            await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/supplements/compliance?fromDate={fromDate:yyyy-MM-dd}&toDate={fromDate.AddDays(366):yyyy-MM-dd}"),
            HttpStatusCode.BadRequest,
            Messages.DateRangeTooLarge);
        await AssertMessageAsync(
            await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/supplements/compliance"),
            HttpStatusCode.BadRequest,
            Messages.DateRangeRequired);

        await AssertMessageAsync(
            await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans/unassign", null),
            HttpStatusCode.OK,
            Messages.Updated);
        var zeroSummaryResponse = await Client.GetAsync(
            $"/api/trainer/trainees/{trainee.Id}/supplements/compliance?fromDate={fromDate:yyyy-MM-dd}&toDate={toDate:yyyy-MM-dd}");
        zeroSummaryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var zeroSummary = await ReadJsonAsync(zeroSummaryResponse))
        {
            AssertComplianceContract(zeroSummary.RootElement, trainee.Id.ToString(), fromDate, toDate, 0, 0, 0);
        }

        SetRequestCulture("pl");
        await AssertMessageAsync(
            await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/supplements/compliance"),
            HttpStatusCode.BadRequest,
            GetMessageForCulture("pl", () => Messages.DateRangeRequired));
    }

    [Test]
    public async Task SupplementRoutes_PreserveMalformedIdAndMessageContracts()
    {
        var trainer = await SeedTrainerAsync("trainer-supp-routes", "trainer-supp-routes@example.com");
        var regularUser = await SeedUserAsync("regular-supp-routes", "regular-supp-routes@example.com", "password123");
        var existingTrainee = await SeedUserAsync("trainee-supp-routes", "trainee-supp-routes@example.com", "password123");

        SetAuthorizationHeader(trainer.Id);
        await AssertMessageAsync(
            await Client.GetAsync("/api/trainer/trainees/not-a-guid/supplement-plans"),
            HttpStatusCode.BadRequest,
            Messages.UserIdRequired);
        await AssertMessageAsync(
            await Client.PostAsJsonAsync("/api/trainer/trainees/not-a-guid/supplement-plans", ValidSupplementRequest()),
            HttpStatusCode.BadRequest,
            Messages.UserIdRequired);

        foreach (var action in new[] { "update", "delete", "assign" })
        {
            var route = $"/api/trainer/trainees/{existingTrainee.Id}/supplement-plans/not-a-guid/{action}";
            var response = action == "update"
                ? await Client.PostAsJsonAsync(route, ValidSupplementRequest())
                : await Client.PostAsync(route, null);
            await AssertMessageAsync(response, HttpStatusCode.BadRequest, Messages.FieldRequired);
        }

        await AssertMessageAsync(
            await Client.PostAsync("/api/trainer/trainees/not-a-guid/supplement-plans/unassign", null),
            HttpStatusCode.BadRequest,
            Messages.UserIdRequired);
        await AssertMessageAsync(
            await Client.GetAsync("/api/trainer/trainees/not-a-guid/supplements/compliance"),
            HttpStatusCode.BadRequest,
            Messages.UserIdRequired);

        SetAuthorizationHeader(existingTrainee.Id);
        await AssertMessageAsync(
            await Client.PostAsJsonAsync("/api/trainee/supplements/intakes/check-off", new
            {
                planItemId = "not-a-guid",
                intakeDate = new DateOnly(2026, 7, 6)
            }),
            HttpStatusCode.BadRequest,
            Messages.FieldRequired);

        SetAuthorizationHeader(regularUser.Id);
        var regularUserResponse = await Client.GetAsync($"/api/trainer/trainees/{existingTrainee.Id}/supplement-plans");
        regularUserResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        SetAuthorizationHeader(trainer.Id);
        await AssertMessageAsync(
            await Client.GetAsync($"/api/trainer/trainees/{existingTrainee.Id}/supplement-plans"),
            HttpStatusCode.NotFound,
            Messages.DidntFind);

        SetRequestCulture("pl");
        await AssertMessageAsync(
            await Client.PostAsync($"/api/trainer/trainees/{existingTrainee.Id}/supplement-plans/not-a-guid/delete", null),
            HttpStatusCode.BadRequest,
            GetMessageForCulture("pl", () => Messages.FieldRequired));
    }

    [Test]
    public async Task SupplementFailures_DoNotPersist()
    {
        var trainer = await SeedTrainerAsync("trainer-supp-failures", "trainer-supp-failures@example.com");
        var trainee = await SeedUserAsync("trainee-supp-failures", "trainee-supp-failures@example.com", "password123");
        await LinkTrainerAndTraineeAsync(trainer.Id, trainee.Id);

        SetAuthorizationHeader(trainer.Id);
        await AssertMessageAsync(
            await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans", new
            {
                name = "Invalid stack",
                notes = "Invalid notes",
                items = Array.Empty<object>()
            }),
            HttpStatusCode.BadRequest,
            Messages.FieldRequired);
        (await GetSupplementPlansIgnoringFiltersAsync(trainee.Id)).Should().BeEmpty();

        var plan = await CreateSupplementPlanAsync(
            trainer.Id,
            trainee.Id,
            "Stable stack",
            "Stable notes",
            new SupplementPlanItemInput("Stable item", "1", "08:00", 127, 0));
        await AssertMessageAsync(
            await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans/{plan.Id}/assign", null),
            HttpStatusCode.OK,
            Messages.Updated);
        var plansBeforeFailures = await GetSupplementPlanSnapshotsAsync(trainee.Id);

        await AssertMessageAsync(
            await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/supplement-plans/{plan.Id}/update", new
            {
                name = "Rejected replacement",
                notes = "Rejected notes",
                items = new object[]
                {
                    new { supplementName = "Rejected", dosage = "1", timeOfDay = "08:00", daysOfWeekMask = 0, order = 0 }
                }
            }),
            HttpStatusCode.BadRequest,
            Messages.FieldRequired);
        (await GetSupplementPlanSnapshotsAsync(trainee.Id)).Should().Equal(plansBeforeFailures);

        SetAuthorizationHeader(trainee.Id);
        await AssertMessageAsync(
            await Client.PostAsJsonAsync("/api/trainee/supplements/intakes/check-off", new
            {
                planItemId = Id<SupplementPlanItem>.New().ToString(),
                intakeDate = new DateOnly(2026, 7, 6)
            }),
            HttpStatusCode.NotFound,
            Messages.DidntFind);
        (await GetSupplementIntakeLogsAsync(trainee.Id)).Should().BeEmpty();

        SetAuthorizationHeader(trainer.Id);
        await AssertMessageAsync(
            await Client.PostAsync(
                $"/api/trainer/trainees/{trainee.Id}/supplement-plans/{Id<SupplementPlan>.New()}/delete",
                null),
            HttpStatusCode.NotFound,
            Messages.DidntFind);
        (await GetSupplementPlanSnapshotsAsync(trainee.Id)).Should().Equal(plansBeforeFailures);
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

    private static int MaskForDate(DateOnly date)
    {
        var normalizedDay = ((int)date.DayOfWeek + 6) % 7;
        return 1 << normalizedDay;
    }

    private async Task<SupplementPlanResponse> CreateSupplementPlanAsync(
        Id<User> trainerId,
        Id<User> traineeId,
        string name,
        string notes,
        params SupplementPlanItemInput[] items)
    {
        SetAuthorizationHeader(trainerId);
        var response = await Client.PostAsJsonAsync(
            $"/api/trainer/trainees/{traineeId}/supplement-plans",
            new
            {
                name,
                notes,
                items = items.Select(item => new
                {
                    supplementName = item.SupplementName,
                    dosage = item.Dosage,
                    timeOfDay = item.TimeOfDay,
                    daysOfWeekMask = item.DaysOfWeekMask,
                    order = item.Order
                })
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var document = await ReadJsonAsync(response);
        AssertPlanContract(document.RootElement);
        document.RootElement.GetProperty("trainerId").GetString().Should().Be(trainerId.ToString());
        document.RootElement.GetProperty("traineeId").GetString().Should().Be(traineeId.ToString());
        document.RootElement.GetProperty("name").GetString().Should().Be(name);
        document.RootElement.GetProperty("notes").GetString().Should().Be(notes);
        document.RootElement.GetProperty("isActive").GetBoolean().Should().BeFalse();

        return JsonSerializer.Deserialize<SupplementPlanResponse>(document.RootElement.GetRawText())!;
    }

    private async Task<List<SupplementPlan>> GetSupplementPlansIgnoringFiltersAsync(Id<User> traineeId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.SupplementPlans
            .IgnoreQueryFilters()
            .Where(plan => plan.TraineeId == traineeId)
            .Include(plan => plan.Items.OrderBy(item => item.Order).ThenBy(item => item.TimeOfDay).ThenBy(item => item.CreatedAt))
            .OrderBy(plan => plan.Name)
            .ToListAsync();
    }

    private async Task<List<SupplementIntakeLog>> GetSupplementIntakeLogsAsync(Id<User> traineeId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.SupplementIntakeLogs
            .Where(log => log.TraineeId == traineeId)
            .OrderBy(log => log.IntakeDate)
            .ThenBy(log => log.TakenAt)
            .ToListAsync();
    }

    private async Task<List<SupplementPlanSnapshot>> GetSupplementPlanSnapshotsAsync(Id<User> traineeId)
    {
        return (await GetSupplementPlansIgnoringFiltersAsync(traineeId))
            .Select(plan => new SupplementPlanSnapshot(
                plan.Id.ToString(),
                plan.IsActive,
                plan.IsDeleted,
                plan.UpdatedAt))
            .OrderBy(plan => plan.Id, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<Id<SupplementPlan>> SeedSupplementPlanAsync(
        Id<User> trainerId,
        Id<User> traineeId,
        string name,
        bool isActive)
    {
        var planId = Id<SupplementPlan>.New();
        var itemId = Id<SupplementPlanItem>.New();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SupplementPlans.Add(new SupplementPlan
        {
            Id = planId,
            TrainerId = trainerId,
            TraineeId = traineeId,
            Name = name,
            Notes = "Seeded",
            IsActive = isActive,
            IsDeleted = false,
            Items =
            [
                new SupplementPlanItem
                {
                    Id = itemId,
                    PlanId = planId,
                    SupplementName = "Seeded item",
                    Dosage = "1",
                    DaysOfWeekMask = DaysOfWeekSet.EveryDay,
                    TimeOfDay = new TimeSpan(8, 0, 0),
                    Order = 0
                }
            ]
        });
        await db.SaveChangesAsync();

        return planId;
    }

    private async Task AssertSingleActivePlanAsync(Id<User> traineeId, string expectedPlanId)
    {
        var plans = await GetSupplementPlansIgnoringFiltersAsync(traineeId);
        plans.Where(plan => plan.IsActive && !plan.IsDeleted).Select(plan => plan.Id.ToString())
            .Should().Equal(expectedPlanId);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task AssertMessageAsync(HttpResponseMessage response, HttpStatusCode statusCode, string expectedMessage)
    {
        response.StatusCode.Should().Be(statusCode);
        using var document = await ReadJsonAsync(response);
        document.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal("msg");
        document.RootElement.GetProperty("msg").GetString().Should().Be(expectedMessage);
        document.RootElement.TryGetProperty("message", out _).Should().BeFalse();
    }

    private static void AssertPlanContract(JsonElement plan)
    {
        plan.ValueKind.Should().Be(JsonValueKind.Object);
        plan.EnumerateObject().Select(property => property.Name).Should().Equal(
            "_id",
            "trainerId",
            "traineeId",
            "name",
            "notes",
            "isActive",
            "createdAt",
            "items");
        plan.TryGetProperty("id", out _).Should().BeFalse();
        plan.GetProperty("_id").GetString().Should().NotBeNullOrWhiteSpace();
        plan.GetProperty("trainerId").GetString().Should().NotBeNullOrWhiteSpace();
        plan.GetProperty("traineeId").GetString().Should().NotBeNullOrWhiteSpace();
        plan.GetProperty("createdAt").GetDateTimeOffset().Should().NotBe(default);

        foreach (var item in plan.GetProperty("items").EnumerateArray())
        {
            item.EnumerateObject().Select(property => property.Name).Should().Equal(
                "_id",
                "supplementName",
                "dosage",
                "timeOfDay",
                "daysOfWeekMask",
                "order");
            item.TryGetProperty("id", out _).Should().BeFalse();
            item.GetProperty("_id").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    private static void AssertScheduleContract(
        JsonElement entry,
        DateOnly expectedDate,
        bool taken,
        DateTimeOffset? expectedTakenAt)
    {
        entry.ValueKind.Should().Be(JsonValueKind.Object);
        var expectedProperties = taken
            ? new[] { "planItemId", "supplementName", "dosage", "timeOfDay", "intakeDate", "taken", "takenAt" }
            : new[] { "planItemId", "supplementName", "dosage", "timeOfDay", "intakeDate", "taken" };
        entry.EnumerateObject().Select(property => property.Name).Should().Equal(expectedProperties);
        entry.GetProperty("planItemId").GetString().Should().NotBeNullOrWhiteSpace();
        entry.GetProperty("intakeDate").GetString().Should().Be(expectedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        entry.GetProperty("taken").GetBoolean().Should().Be(taken);

        if (!taken)
        {
            entry.TryGetProperty("takenAt", out _).Should().BeFalse();
            return;
        }

        entry.GetProperty("takenAt").ValueKind.Should().Be(JsonValueKind.String);
        if (expectedTakenAt is not null)
        {
            entry.GetProperty("takenAt").GetString().Should().Be(
                expectedTakenAt.Value.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture));
        }
    }

    private static void AssertComplianceContract(
        JsonElement summary,
        string traineeId,
        DateOnly fromDate,
        DateOnly toDate,
        int plannedDoses,
        int takenDoses,
        double adherenceRate)
    {
        summary.ValueKind.Should().Be(JsonValueKind.Object);
        summary.EnumerateObject().Select(property => property.Name).Should().Equal(
            "traineeId",
            "fromDate",
            "toDate",
            "plannedDoses",
            "takenDoses",
            "adherenceRate");
        summary.GetProperty("traineeId").GetString().Should().Be(traineeId);
        summary.GetProperty("fromDate").GetString().Should().Be(fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        summary.GetProperty("toDate").GetString().Should().Be(toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        summary.GetProperty("plannedDoses").GetInt32().Should().Be(plannedDoses);
        summary.GetProperty("takenDoses").GetInt32().Should().Be(takenDoses);
        summary.GetProperty("adherenceRate").GetDouble().Should().Be(adherenceRate);
    }

    private void SetRequestCulture(string culture)
    {
        Client.DefaultRequestHeaders.Remove("Accept-Language");
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", culture).Should().BeTrue();
    }

    private static string GetMessageForCulture(string culture, Func<string> getMessage)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var requestedCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = requestedCulture;
            CultureInfo.CurrentUICulture = requestedCulture;
            return getMessage();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static object ValidSupplementRequest()
    {
        return new
        {
            name = "Valid stack",
            notes = "Valid notes",
            items = new object[]
            {
                new { supplementName = "Valid item", dosage = "1", timeOfDay = "08:00", daysOfWeekMask = 127, order = 0 }
            }
        };
    }

    private sealed record SupplementPlanItemInput(
        string SupplementName,
        string Dosage,
        string TimeOfDay,
        int DaysOfWeekMask,
        int Order);

    private sealed record SupplementPlanSnapshot(string Id, bool IsActive, bool IsDeleted, DateTimeOffset UpdatedAt);

    private sealed class SupplementPlanResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("trainerId")]
        public string TrainerId { get; set; } = string.Empty;

        [JsonPropertyName("traineeId")]
        public string TraineeId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("items")]
        public List<SupplementPlanItemResponse> Items { get; set; } = [];
    }

    private sealed class SupplementPlanItemResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("supplementName")]
        public string SupplementName { get; set; } = string.Empty;

        [JsonPropertyName("dosage")]
        public string Dosage { get; set; } = string.Empty;

        [JsonPropertyName("timeOfDay")]
        public string TimeOfDay { get; set; } = string.Empty;

        [JsonPropertyName("daysOfWeekMask")]
        public int DaysOfWeekMask { get; set; }

        [JsonPropertyName("order")]
        public int Order { get; set; }
    }

    private sealed class SupplementScheduleEntryResponse
    {
        [JsonPropertyName("planItemId")]
        public string PlanItemId { get; set; } = string.Empty;

        [JsonPropertyName("taken")]
        public bool Taken { get; set; }
    }

    private sealed class SupplementComplianceResponse
    {
        [JsonPropertyName("plannedDoses")]
        public int PlannedDoses { get; set; }

        [JsonPropertyName("takenDoses")]
        public int TakenDoses { get; set; }

        [JsonPropertyName("adherenceRate")]
        public double AdherenceRate { get; set; }
    }
}
