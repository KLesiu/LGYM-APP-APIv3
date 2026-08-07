using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using FluentAssertions.Execution;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class PlanTests : IntegrationTestBase
{
    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/{id}/createPlan", "own", "owner-allow")]
    public async Task CreatePlan_WithValidData_CreatesPlanAndMakesItActive()
    {
        var user = await SeedUserAsync(name: "planuser", email: "plan@example.com");
        SetAuthorizationHeader(user.Id);

        var request = new
        {
            name = "My Workout Plan"
        };

        var response = await Client.PostAsJsonAsync($"/api/{user.Id}/createPlan", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Created");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Name == "My Workout Plan" && p.UserId == user.Id);
        plan.Should().NotBeNull();
        plan!.IsActive.Should().BeTrue();

        (await db.Plans.CountAsync(item => item.UserId == user.Id && item.IsActive && !item.IsDeleted)).Should().Be(1);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/{id}/createPlan", "own", "foreign-object-denial-no-mutation")]
    public async Task CreatePlan_WithMismatchedUserId_ReturnsForbidden()
    {
        var user1 = await SeedUserAsync(name: "user1", email: "user1@example.com");
        var user2 = await SeedUserAsync(name: "user2", email: "user2@example.com");
        SetAuthorizationHeader(user1.Id);

        var request = new
        {
            name = "Plan"
        };

        var response = await Client.PostAsJsonAsync($"/api/{user2.Id}/createPlan", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be(CompatibilityResourceMessage.InCulture("en", () => Messages.Forbidden));
    }

    [Test]
    public async Task CreatePlan_WithMalformedRouteUserId_ReturnsNotFound()
    {
        var user = await SeedUserAsync(name: "invalidcreateplanuser", email: "invalidcreateplan@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await Client.PostAsJsonAsync("/api/not-an-id/createPlan", new { name = "Plan" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/{id}/updatePlan", "own", "owner-allow")]
    public async Task UpdatePlan_ByOwner_ReturnsCurrentSuccessShapeAndUpdatesPlanName()
    {
        var owner = await SeedUserAsync(name: "planupdateowner", email: "planupdateowner@example.com");
        var ownerPlan = await SeedPlanAsync(owner.Id, "Owner Plan Before Update");
        SetAuthorizationHeader(owner.Id);

        var request = new
        {
            _id = ownerPlan.Id.ToString(),
            name = "Owner Plan After Update"
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/{owner.Id}/updatePlan", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be(CompatibilityResourceMessage.InCulture("en", () => Messages.Updated));

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var updatedPlan = await db.Plans.FirstOrDefaultAsync(p => p.Id == ownerPlan.Id);
        updatedPlan.Should().NotBeNull();
        updatedPlan!.Name.Should().Be("Owner Plan After Update");
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/{id}/updatePlan", "own", "foreign-object-denial-no-mutation")]
    public async Task UpdatePlan_WithAttackerRouteAndVictimPlanId_ReturnsNotFoundAndPreservesVictimPlan()
    {
        var attacker = await SeedUserAsync(name: "planupdateattacker", email: "planupdateattacker@example.com");
        var victim = await SeedUserAsync(name: "planupdatevictim", email: "planupdatevictim@example.com");
        var victimPlan = await SeedPlanAsync(victim.Id, "Victim Plan Before Attack");
        SetAuthorizationHeader(attacker.Id);

        var response = await PostAsJsonWithApiOptionsAsync($"/api/{attacker.Id}/updatePlan", new
        {
            _id = victimPlan.Id.ToString(),
            name = "Attacker Controlled Name"
        });

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedVictimPlan = await verifyDb.Plans
            .AsNoTracking()
            .SingleAsync(plan => plan.Id == victimPlan.Id);

        using var assertionScope = new AssertionScope();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        persistedVictimPlan.Name.Should().Be("Victim Plan Before Attack");
    }

    [Test]
    public async Task UpdatePlan_WithMalformedRouteUserId_ReturnsNotFound()
    {
        var user = await SeedUserAsync(name: "invalidupdateplanuser", email: "invalidupdateplan@example.com");
        var plan = await SeedPlanAsync(user.Id, "Plan");
        SetAuthorizationHeader(user.Id);

        var response = await PostAsJsonWithApiOptionsAsync("/api/not-an-id/updatePlan", new
        {
            _id = plan.Id.ToString(),
            name = "Updated"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UpdatePlan_WithMalformedBodyPlanId_ReturnsNotFound()
    {
        var user = await SeedUserAsync(name: "invalidupdatebodyuser", email: "invalidupdatebody@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await PostAsJsonWithApiOptionsAsync($"/api/{user.Id}/updatePlan", new
        {
            _id = "not-an-id",
            name = "Updated"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/{id}/getPlanConfig", "own", "owner-allow")]
    public async Task GetPlanConfig_WithActivePlan_ReturnsPlan()
    {
        var user = await SeedUserAsync(name: "planuser", email: "plan@example.com");
        var plan = await SeedPlanAsync(user.Id, "Active Plan", isActive: true);
        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync($"/api/{user.Id}/getPlanConfig");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PlanFormResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(plan.Id.ToString());
        body.Name.Should().Be("Active Plan");
        body.IsActive.Should().BeTrue();
    }

    [Test]
    public async Task GetPlanConfig_WithNoActivePlan_ReturnsNotFound()
    {
        var user = await SeedUserAsync(name: "planuser", email: "plan@example.com");
        await SeedPlanAsync(user.Id, "Inactive Plan", isActive: false);
        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync($"/api/{user.Id}/getPlanConfig");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/{id}/checkIsUserHavePlan", "own", "owner-allow")]
    public async Task CheckIsUserHavePlan_WithNoPlan_ReturnsFalse()
    {
        var user = await SeedUserAsync(name: "planuser", email: "plan@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync($"/api/{user.Id}/checkIsUserHavePlan");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<bool>();
        result.Should().BeFalse();
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/{id}/getPlansList", "own", "owner-allow")]
    public async Task GetPlansList_WithMultiplePlans_ReturnsAllPlans()
    {
        var user = await SeedUserAsync(name: "planuser", email: "plan@example.com");
        await SeedPlanAsync(user.Id, "Plan 1", isActive: true);
        await SeedPlanAsync(user.Id, "Plan 2", isActive: false);
        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync($"/api/{user.Id}/getPlansList");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<PlanFormResponse>>();
        body.Should().NotBeNull();
        body.Should().HaveCount(2);
        body!.Select(p => p.Name).Should().Contain(new[] { "Plan 1", "Plan 2" });
    }

    [Test]
    public async Task GetPlansList_WithNoPlans_ReturnsNotFound()
    {
        var user = await SeedUserAsync(name: "planuser", email: "plan@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync($"/api/{user.Id}/getPlansList");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/{id}/setNewActivePlan", "own", "owner-allow")]
    public async Task SetNewActivePlan_WithValidPlanId_SetsActivePlan()
    {
        var user = await SeedUserAsync(name: "planuser", email: "plan@example.com");
        var plan1 = await SeedPlanAsync(user.Id, "Plan 1", isActive: true);
        var plan2 = await SeedPlanAsync(user.Id, "Plan 2", isActive: false);
        SetAuthorizationHeader(user.Id);

        var request = new
        {
            _id = plan2.Id.ToString()
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/{user.Id}/setNewActivePlan", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var updatedPlan1 = await db.Plans.FirstOrDefaultAsync(p => p.Id == plan1.Id);
        var updatedPlan2 = await db.Plans.FirstOrDefaultAsync(p => p.Id == plan2.Id);

        updatedPlan1!.IsActive.Should().BeFalse();
        updatedPlan2!.IsActive.Should().BeTrue();
    }

    [Test]
    public async Task SetNewActivePlan_WithMalformedRouteUserId_ReturnsNotFound()
    {
        var user = await SeedUserAsync(name: "invalidactiveplanuser", email: "invalidactiveplan@example.com");
        var plan = await SeedPlanAsync(user.Id, "Plan");
        SetAuthorizationHeader(user.Id);

        var response = await PostAsJsonWithApiOptionsAsync("/api/not-an-id/setNewActivePlan", new
        {
            _id = plan.Id.ToString()
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SetNewActivePlan_WithMalformedBodyPlanId_ReturnsNotFound()
    {
        var user = await SeedUserAsync(name: "invalidactivebodyuser", email: "invalidactivebody@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await PostAsJsonWithApiOptionsAsync($"/api/{user.Id}/setNewActivePlan", new
        {
            _id = "not-an-id"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/{id}/getPlanConfig", "own", "foreign-object-denial-no-mutation")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/{id}/checkIsUserHavePlan", "own", "foreign-object-denial-no-mutation")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/{id}/getPlansList", "own", "foreign-object-denial-no-mutation")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/{id}/setNewActivePlan", "own", "foreign-object-denial-no-mutation")]
    public async Task PlanAccountRoutes_WithForeignRouteAreDeniedAndDoNotChangeActivePlan()
    {
        var attacker = await SeedUserAsync("plan-route-attacker", "plan-route-attacker@example.com");
        var victim = await SeedUserAsync("plan-route-victim", "plan-route-victim@example.com");
        var victimActivePlan = await SeedPlanAsync(victim.Id, "Victim Active", isActive: true);
        var victimInactivePlan = await SeedPlanAsync(victim.Id, "Victim Inactive", isActive: false);
        SetAuthorizationHeader(attacker.Id);

        using var beforeScope = Factory.Services.CreateScope();
        var beforeDb = beforeScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var beforeActive = await beforeDb.Plans.AsNoTracking().SingleAsync(plan => plan.Id == victimActivePlan.Id);
        var responses = new[]
        {
            await Client.GetAsync($"/api/{victim.Id}/getPlanConfig"),
            await Client.GetAsync($"/api/{victim.Id}/checkIsUserHavePlan"),
            await Client.GetAsync($"/api/{victim.Id}/getPlansList"),
            await PostAsJsonWithApiOptionsAsync($"/api/{victim.Id}/setNewActivePlan", new { _id = victimInactivePlan.Id.ToString() })
        };

        using var afterScope = Factory.Services.CreateScope();
        var afterDb = afterScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var afterActive = await afterDb.Plans.AsNoTracking().SingleAsync(plan => plan.Id == victimActivePlan.Id);
        var afterInactive = await afterDb.Plans.AsNoTracking().SingleAsync(plan => plan.Id == victimInactivePlan.Id);

        using (new AssertionScope())
        {
            responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.Forbidden);
            afterActive.IsActive.Should().Be(beforeActive.IsActive);
            afterInactive.IsActive.Should().BeFalse();
        }

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/{id}/deletePlan", "own", "owner-allow")]
    public async Task DeletePlan_WithValidId_SoftDeletesPlanAndAllPlanDays()
    {
        var user = await SeedUserAsync(name: "deleteplanuser", email: "deleteplan@example.com");
        SetAuthorizationHeader(user.Id);

        var exerciseId = await CreateExerciseViaEndpointAsync(user.Id, "Delete Plan Exercise", BodyParts.Chest);
        var planId = await CreatePlanViaEndpointAsync(user.Id, "Test Plan");
        await CreatePlanDayViaEndpointAsync(user.Id, planId, "Delete Day 1", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 3, Reps = "10" }
        });
        await CreatePlanDayViaEndpointAsync(user.Id, planId, "Delete Day 2", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 4, Reps = "8" }
        });
        await CreatePlanDayViaEndpointAsync(user.Id, planId, "Delete Day 3", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 3, Reps = "10" }
        });

        var response = await Client.PostAsync($"/api/{planId}/deletePlan", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var updatedPlan = await db.Plans
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == (Domain.ValueObjects.Id<Plan>)planId);
        updatedPlan.Should().NotBeNull();
        updatedPlan!.IsActive.Should().BeFalse();
        updatedPlan.IsDeleted.Should().BeTrue();

        var planDays = await db.PlanDays
            .IgnoreQueryFilters()
            .Where(pd => pd.PlanId == (Domain.ValueObjects.Id<Plan>)planId)
            .ToListAsync();
        planDays.Should().HaveCount(3);
        planDays.All(pd => pd.IsDeleted).Should().BeTrue();

        var updatedUser = await db.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
        updatedUser.Should().NotBeNull();
        updatedUser!.PlanId.Should().BeNull();
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/{id}/deletePlan", "own", "foreign-object-denial-no-mutation")]
    public async Task DeletePlan_WithOtherUsersPlan_ReturnsForbidden()
    {
        var user1 = await SeedUserAsync(name: "deleteplanuser2", email: "deleteplan2@example.com");
        var user2 = await SeedUserAsync(name: "deleteplanuser3", email: "deleteplan3@example.com");
        var plan = await SeedPlanAsync(user2.Id, "Other User Plan", isActive: true);
        var exerciseId = await CreateExerciseViaEndpointAsync(user2.Id, "Protected Plan Exercise", BodyParts.Back);
        await CreatePlanDayViaEndpointAsync(user2.Id, plan.Id, "Protected Day 1", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 3, Reps = "10" }
        });
        await CreatePlanDayViaEndpointAsync(user2.Id, plan.Id, "Protected Day 2", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 5, Reps = "5" }
        });

        SetAuthorizationHeader(user1.Id);

        var response = await Client.PostAsync($"/api/{plan.Id}/deletePlan", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unchangedPlan = await db.Plans.FirstOrDefaultAsync(p => p.Id == plan.Id);
        unchangedPlan.Should().NotBeNull();
        unchangedPlan!.IsActive.Should().BeTrue();
        unchangedPlan.IsDeleted.Should().BeFalse();

        var unchangedPlanDays = await db.PlanDays.Where(pd => pd.PlanId == plan.Id).ToListAsync();
        unchangedPlanDays.Should().HaveCount(2);
        unchangedPlanDays.All(pd => !pd.IsDeleted).Should().BeTrue();
    }

    [Test]
    public async Task DeletePlan_WithMalformedRoutePlanId_ReturnsNotFound()
    {
        var user = await SeedUserAsync(name: "invaliddeleteplanuser", email: "invaliddeleteplan@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await Client.PostAsync("/api/not-an-id/deletePlan", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DeletePlan_WithPlanDaysAndTrainings_ByOwner_SoftDeletesPlanAndPlanDaysAndKeepsTrainings()
    {
        var user = await SeedUserAsync(name: "deleteplanownertrain", email: "deleteplanownertrain@example.com");
        SetAuthorizationHeader(user.Id);

        var exerciseId = await CreateExerciseViaEndpointAsync(user.Id, "Owner Delete Exercise", BodyParts.Chest);
        var gymId = await CreateGymViaEndpointAsync(user.Id, "Test Gym");
        var planId = await CreatePlanViaEndpointAsync(user.Id, "Test Plan");
        var planDay1Id = await CreatePlanDayViaEndpointAsync(user.Id, planId, "Owner Delete Day 1", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 3, Reps = "10" }
        });
        var planDay2Id = await CreatePlanDayViaEndpointAsync(user.Id, planId, "Owner Delete Day 2", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 4, Reps = "8" }
        });

        await AddTrainingViaEndpointAsync(user.Id, gymId, planDay1Id, exerciseId);
        await AddTrainingViaEndpointAsync(user.Id, gymId, planDay2Id, exerciseId);

        var response = await Client.PostAsync($"/api/{planId}/deletePlan", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var updatedPlan = await db.Plans
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == (Domain.ValueObjects.Id<Plan>)planId);
        updatedPlan.Should().NotBeNull();
        updatedPlan!.IsActive.Should().BeFalse();
        updatedPlan.IsDeleted.Should().BeTrue();

        var planDays = await db.PlanDays
            .IgnoreQueryFilters()
            .Where(pd => pd.PlanId == (Domain.ValueObjects.Id<Plan>)planId)
            .ToListAsync();
        planDays.Should().HaveCount(2);
        planDays.All(pd => pd.IsDeleted).Should().BeTrue();

        var trainings = await db.Trainings
            .Where(t => t.UserId == (Domain.ValueObjects.Id<User>)user.Id && (t.TypePlanDayId == (Domain.ValueObjects.Id<PlanDay>)planDay1Id || t.TypePlanDayId == (Domain.ValueObjects.Id<PlanDay>)planDay2Id))
            .ToListAsync();
        trainings.Should().HaveCount(2);
    }

    [Test]
    public async Task DeletePlan_WithPlanDaysAndTrainings_ByNonOwner_ReturnsForbiddenAndKeepsData()
    {
        var owner = await SeedUserAsync(name: "deleteplannonowner1", email: "deleteplannonowner1@example.com");
        var attacker = await SeedUserAsync(name: "deleteplannonowner2", email: "deleteplannonowner2@example.com");

        SetAuthorizationHeader(owner.Id);
        var exerciseId = await CreateExerciseViaEndpointAsync(owner.Id, "NonOwner Delete Exercise", BodyParts.Back);
        var gymId = await CreateGymViaEndpointAsync(owner.Id, "Test Gym");
        var planId = await CreatePlanViaEndpointAsync(owner.Id, "Test Plan");
        var planDay1Id = await CreatePlanDayViaEndpointAsync(owner.Id, planId, "NonOwner Day 1", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 3, Reps = "10" }
        });
        var planDay2Id = await CreatePlanDayViaEndpointAsync(owner.Id, planId, "NonOwner Day 2", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 5, Reps = "5" }
        });

        await AddTrainingViaEndpointAsync(owner.Id, gymId, planDay1Id, exerciseId);
        await AddTrainingViaEndpointAsync(owner.Id, gymId, planDay2Id, exerciseId);

        SetAuthorizationHeader(attacker.Id);
        var response = await Client.PostAsync($"/api/{planId}/deletePlan", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unchangedPlan = await db.Plans.FirstOrDefaultAsync(p => p.Id == (Domain.ValueObjects.Id<Plan>)planId);
        unchangedPlan.Should().NotBeNull();
        unchangedPlan!.IsActive.Should().BeTrue();
        unchangedPlan.IsDeleted.Should().BeFalse();

        var unchangedPlanDays = await db.PlanDays.Where(pd => pd.PlanId == (Domain.ValueObjects.Id<Plan>)planId).ToListAsync();
        unchangedPlanDays.Should().HaveCount(2);
        unchangedPlanDays.All(pd => !pd.IsDeleted).Should().BeTrue();

        var trainings = await db.Trainings
            .Where(t => t.UserId == (Domain.ValueObjects.Id<User>)owner.Id && (t.TypePlanDayId == (Domain.ValueObjects.Id<PlanDay>)planDay1Id || t.TypePlanDayId == (Domain.ValueObjects.Id<PlanDay>)planDay2Id))
            .ToListAsync();
        trainings.Should().HaveCount(2);
    }

    [Test]
    public async Task DeletePlan_WhenDeletingNonActivePlan_PreservesActivePlan()
    {
        var user = await SeedUserAsync(name: "deleteinactiveplanuser", email: "deleteinactiveplan@example.com");
        var activePlan = await SeedPlanAsync(user.Id, "Active Plan", isActive: true);
        var inactivePlan = await SeedPlanAsync(user.Id, "Inactive Plan To Delete", isActive: false);

        SetAuthorizationHeader(user.Id);

        var response = await Client.PostAsync($"/api/{inactivePlan.Id}/deletePlan", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var deletedPlan = await verifyDb.Plans
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == inactivePlan.Id);
        deletedPlan.Should().NotBeNull();
        deletedPlan!.IsActive.Should().BeFalse();
        deletedPlan.IsDeleted.Should().BeTrue();

        var unchangedActivePlan = await verifyDb.Plans.FirstOrDefaultAsync(plan => plan.Id == activePlan.Id);
        unchangedActivePlan.Should().NotBeNull();
        unchangedActivePlan!.IsActive.Should().BeTrue();
    }

    [Test]
    public async Task DeletePlan_WhenDeletingActivePlan_WithInactivePlan_ActivatesLatestInactivePlan()
    {
        var user = await SeedUserAsync(name: "deleteactiveplanuser", email: "deleteactiveplan@example.com");
        var activePlan = await SeedPlanAsync(user.Id, "Active Plan", isActive: true);
        var fallbackPlan = await SeedPlanAsync(user.Id, "Fallback Plan", isActive: false);

        SetAuthorizationHeader(user.Id);

        var response = await Client.PostAsync($"/api/{activePlan.Id}/deletePlan", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var deletedPlan = await verifyDb.Plans
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == activePlan.Id);
        deletedPlan.Should().NotBeNull();
        deletedPlan!.IsDeleted.Should().BeTrue();
        deletedPlan.IsActive.Should().BeFalse();

        var activatedFallback = await verifyDb.Plans.FirstOrDefaultAsync(p => p.Id == fallbackPlan.Id);
        activatedFallback.Should().NotBeNull();
        activatedFallback!.IsDeleted.Should().BeFalse();
        activatedFallback.IsActive.Should().BeTrue();

    }

    [Test]
    public async Task DeletePlan_WhenDeletingActivePlan_WithOnlyDeletedInactivePlans_LeavesNoActivePlan()
    {
        var user = await SeedUserAsync(name: "deleteactiveplanuser2", email: "deleteactiveplan2@example.com");
        var activePlan = await SeedPlanAsync(user.Id, "Active Plan", isActive: true);
        var deletedFallbackPlan = await SeedPlanAsync(user.Id, "Deleted Fallback", isActive: false);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var fallback = await db.Plans.FirstOrDefaultAsync(p => p.Id == deletedFallbackPlan.Id);
            fallback.Should().NotBeNull();
            fallback!.IsDeleted = true;

            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(user.Id);

        var response = await Client.PostAsync($"/api/{activePlan.Id}/deletePlan", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unchangedFallback = await verifyDb.Plans
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == deletedFallbackPlan.Id);
        unchangedFallback.Should().NotBeNull();
        unchangedFallback!.IsDeleted.Should().BeTrue();
        unchangedFallback.IsActive.Should().BeFalse();

        (await verifyDb.Plans.AnyAsync(plan => plan.UserId == user.Id && plan.IsActive && !plan.IsDeleted)).Should().BeFalse();
    }

    private async Task AddTrainingViaEndpointAsync(Id<User> userId, Id<Gym> gymId, Id<PlanDay> planDayId, Id<Exercise> exerciseId)
    {
        var request = new
        {
            gym = gymId.ToString(),
            type = planDayId.ToString(),
            createdAt = DateTime.UtcNow,
            exercises = new[]
            {
                new { exercise = exerciseId.ToString(), series = 1, reps = 10, weight = 60.0, unit = WeightUnits.Kilograms.ToString() }
            }
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/{userId}/addTraining", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<Plan> SeedPlanAsync(Id<User> userId, string name, bool isActive = true)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var plan = new Plan
        {
            Id = Id<Plan>.New(),
            UserId = userId,
            Name = name,
            IsActive = isActive
        };

        db.Plans.Add(plan);
        await db.SaveChangesAsync();

        return plan;
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("msg")]
        public string Message { get; set; } = string.Empty;
    }

    private sealed class PlanFormResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }
    }

    private sealed class ShareCodeResponse
    {
        [JsonPropertyName("shareCode")]
        public string ShareCode { get; set; } = string.Empty;
    }

    private sealed class CopiedPlanResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }

        [JsonPropertyName("userId")]
        public string UserId { get; set; } = string.Empty;
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/{id}/share", "own", "owner-allow")]
    public async Task GenerateShareCode_WithValidPlan_ReturnsShareCode()
    {
        var user = await SeedUserAsync(name: "shareuser", email: "share@example.com");
        var plan = await SeedPlanAsync(user.Id, "Shareable Plan");
        SetAuthorizationHeader(user.Id);

        var response = await Client.PostAsync($"/api/{plan.Id}/share", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ShareCodeResponse>();
        body.Should().NotBeNull();
        body!.ShareCode.Should().NotBeNullOrWhiteSpace();
        body.ShareCode.Should().HaveLength(10);
    }

    [Test]
    public async Task GenerateShareCode_WithInvalidPlanId_ReturnsNotFound()
    {
        var user = await SeedUserAsync(name: "shareuser2", email: "share2@example.com");
        SetAuthorizationHeader(user.Id);

        var nonExistentPlanId = Id<Plan>.New();
        var response = await Client.PostAsync($"/api/{nonExistentPlanId}/share", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GenerateShareCode_WithMalformedRoutePlanId_ReturnsNotFound()
    {
        var user = await SeedUserAsync(name: "invalidshareplanuser", email: "invalidshareplan@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await Client.PostAsync("/api/not-an-id/share", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/{id}/share", "own", "foreign-object-denial-no-mutation")]
    public async Task GenerateShareCode_WithOtherUsersPlan_ReturnsForbidden()
    {
        var user1 = await SeedUserAsync(name: "shareuser3", email: "share3@example.com");
        var user2 = await SeedUserAsync(name: "shareuser4", email: "share4@example.com");
        var plan = await SeedPlanAsync(user2.Id, "Other User Plan");
        SetAuthorizationHeader(user1.Id);

        var response = await Client.PostAsync($"/api/{plan.Id}/share", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/copy", "own", "owner-allow")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/copy", "own", "no-client-subject")]
    public async Task CopyPlan_WithValidShareCode_CopiesPlan()
    {
        var user1 = await SeedUserAsync(name: "copyuser1", email: "copy1@example.com");
        var user2 = await SeedUserAsync(name: "copyuser2", email: "copy2@example.com");
        var plan = await SeedPlanAsync(user1.Id, "Plan To Copy");

        SetAuthorizationHeader(user1.Id);
        var shareResponse = await Client.PostAsync($"/api/{plan.Id}/share", null);
        var shareBody = await shareResponse.Content.ReadFromJsonAsync<ShareCodeResponse>();
        var shareCode = shareBody!.ShareCode;

        SetAuthorizationHeader(user2.Id);
        var copyRequest = new { shareCode };
        var copyResponse = await Client.PostAsJsonAsync("/api/copy", copyRequest);

        copyResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var copyBody = await copyResponse.Content.ReadFromJsonAsync<CopiedPlanResponse>();
        copyBody.Should().NotBeNull();
        copyBody!.Name.Should().Be("Plan To Copy");
        copyBody.UserId.Should().Be(user2.Id.ToString());
        copyBody.IsActive.Should().BeTrue();
    }

    [Test]
    public async Task CopyPlan_WithInvalidShareCode_ReturnsNotFound()
    {
        var user = await SeedUserAsync(name: "copyuser3", email: "copy3@example.com");
        SetAuthorizationHeader(user.Id);

        var copyRequest = new { shareCode = "INVALID1" };
        var copyResponse = await Client.PostAsJsonAsync("/api/copy", copyRequest);

        copyResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/copy", "own", "anonymous-denial")]
    public async Task CopyPlan_WithoutAuth_ReturnsUnauthorized()
    {
        ClearAuthorizationHeader();

        var copyRequest = new { shareCode = "TESTCODE" };
        var copyResponse = await Client.PostAsJsonAsync("/api/copy", copyRequest);

        copyResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GenerateShareCode_CalledTwice_ReturnsSameCode()
    {
        var user = await SeedUserAsync(name: "sharetwice", email: "sharetwice@example.com");
        var plan = await SeedPlanAsync(user.Id, "Double Share Plan");
        SetAuthorizationHeader(user.Id);

        var response1 = await Client.PostAsync($"/api/{plan.Id}/share", null);
        var body1 = await response1.Content.ReadFromJsonAsync<ShareCodeResponse>();

        var response2 = await Client.PostAsync($"/api/{plan.Id}/share", null);
        var body2 = await response2.Content.ReadFromJsonAsync<ShareCodeResponse>();

        body1!.ShareCode.Should().Be(body2!.ShareCode);
    }
}
