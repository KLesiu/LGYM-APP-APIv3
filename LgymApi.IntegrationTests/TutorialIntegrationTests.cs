using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Api.Features.Tutorial.Contracts;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Application.Features.Tutorial;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class TutorialIntegrationTests : IntegrationTestBase
{


    private async Task InitializeTutorialAsync(Id<User> userId)
    {
        using var scope = Factory.Services.CreateScope();
        var tutorialService = scope.ServiceProvider.GetRequiredService<ITutorialService>();
        await tutorialService.InitializeOnboardingTutorialAsync(userId);
    }
    [Test]
    public async Task RegisteredUser_HasActiveTutorials()
    {
        // Register a new user
        var registerRequest = new
        {
            name = "tutorial-user",
            email = "tutorial@example.com",
            password = "password123",
            cpassword = "password123",
            isVisibleInRanking = true,
            adultConfirmed = true
        };

        SetIdempotencyKey("test-register-tutorial-user");
        var registerResponse = await Client.PostAsJsonAsync("/api/register", registerRequest);
        ClearIdempotencyKey();

        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var registeredUser = await db.Users.FirstAsync(u => u.Email == "tutorial@example.com");
        var tutorial = await db.UserTutorialProgresses
            .AsNoTracking()
            .SingleAsync(progress => progress.UserId == registeredUser.Id && progress.TutorialType == TutorialType.OnboardingDemo);
        tutorial.IsCompleted.Should().BeFalse();

        // Login to get token and user info
        var loginRequest = new
        {
            name = "tutorial-user",
            password = "password123"
        };

        var loginResponse = await Client.PostAsJsonAsync("/api/login", loginRequest);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(SharedSerializationOptions.Current);
        loginBody.Should().NotBeNull();
        loginBody!.Token.Should().NotBeNullOrWhiteSpace();
        loginBody.User.Should().NotBeNull();
        loginBody.User!.HasActiveTutorials.Should().BeTrue();
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/tutorials/active", "own", "owner-allow")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/tutorials/active", "own", "no-client-subject")]
    public async Task GetActiveTutorials_ReturnsOnboardingTutorial()
    {
        // Seed user and initialize tutorial
        var user = await SeedUserAsync(name: "active-tutorial-user", email: "active@example.com");
        await InitializeTutorialAsync(user.Id);
        SetAuthorizationHeader(user.Id);

        // Get active tutorials
        var response = await Client.GetAsync("/api/tutorials/active");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<TutorialProgressDto>>(SharedSerializationOptions.Current);
        body.Should().NotBeNull();
        body.Should().HaveCount(1);
        body![0].TutorialType.Should().Be(TutorialType.OnboardingDemo);
        body[0].IsCompleted.Should().BeFalse();
        body[0].TotalSteps.Should().Be(6);
        body[0].CompletedSteps.Should().BeEmpty();
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/tutorials/{tutorialType}", "own", "owner-allow")]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/tutorials/{tutorialType}", "own", "no-client-subject")]
    public async Task GetTutorialProgress_ReturnsProgressWithZeroCompletedSteps()
    {
        // Seed user and initialize tutorial
        var user = await SeedUserAsync(name: "progress-user", email: "progress@example.com");
        await InitializeTutorialAsync(user.Id);
        SetAuthorizationHeader(user.Id);

        // Get tutorial progress
        var response = await Client.GetAsync("/api/tutorials/OnboardingDemo");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<TutorialProgressDto>(SharedSerializationOptions.Current);
        body.Should().NotBeNull();
        body!.TutorialType.Should().Be(TutorialType.OnboardingDemo);
        body.IsCompleted.Should().BeFalse();
        body.CompletedSteps.Should().BeEmpty();
        body.RemainingSteps.Should().HaveCount(6);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/tutorials/completeStep", "own", "owner-allow")]
    public async Task CompleteStep_UpdatesProgressAndMarksStepComplete()
    {
        // Seed user and initialize tutorial
        var user = await SeedUserAsync(name: "step-complete-user", email: "step@example.com");
        await InitializeTutorialAsync(user.Id);
        SetAuthorizationHeader(user.Id);

        // Complete first step
        var completeStepRequest = new CompleteStepRequest
        {
            TutorialType = TutorialType.OnboardingDemo,
            Step = TutorialStep.CreateArea
        };

        var completeResponse = await PostAsJsonWithApiOptionsAsync("/api/tutorials/completeStep", completeStepRequest);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Get updated progress
        var progressResponse = await Client.GetAsync("/api/tutorials/OnboardingDemo");
        progressResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var progress = await progressResponse.Content.ReadFromJsonAsync<TutorialProgressDto>(SharedSerializationOptions.Current);
        progress.Should().NotBeNull();
        progress!.CompletedSteps.Should().HaveCount(1);
        progress!.CompletedSteps.Should().Contain(TutorialStep.CreateArea);
        progress.RemainingSteps.Should().HaveCount(5);
        progress.IsCompleted.Should().BeFalse();
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/tutorials/complete", "own", "owner-allow")]
    public async Task CompleteTutorial_MarksInactiveAndClearsActiveTutorials()
    {
        // Seed user and initialize tutorial
        var user = await SeedUserAsync(name: "complete-tutorial-user", email: "complete@example.com");
        await InitializeTutorialAsync(user.Id);
        SetAuthorizationHeader(user.Id);

        // Complete tutorial
        var completeTutorialRequest = new CompleteTutorialRequest
        {
            TutorialType = TutorialType.OnboardingDemo
        };

        var completeResponse = await PostAsJsonWithApiOptionsAsync("/api/tutorials/complete", completeTutorialRequest);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Get active tutorials - should be empty
        var activeResponse = await Client.GetAsync("/api/tutorials/active");
        activeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var activeTutorials = await activeResponse.Content.ReadFromJsonAsync<List<TutorialProgressDto>>(SharedSerializationOptions.Current);
        activeTutorials.Should().NotBeNull();
        activeTutorials.Should().BeEmpty();

        // Check token - hasActiveTutorials should be false
        var checkTokenResponse = await Client.GetAsync("/api/checkToken");
        checkTokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokenUser = await checkTokenResponse.Content.ReadFromJsonAsync<UserDto>(SharedSerializationOptions.Current);
        tokenUser.Should().NotBeNull();
        tokenUser!.HasActiveTutorials.Should().BeFalse();
    }

    [Test]
    public async Task CompleteAllSteps_MarksCompletedAndHasActiveTutorialsFalse()
    {
        // Seed user and initialize tutorial
        var user = await SeedUserAsync(name: "complete-all-steps-user", email: "allsteps@example.com");
        await InitializeTutorialAsync(user.Id);
        SetAuthorizationHeader(user.Id);

        // Complete all 6 steps
        var steps = new[]
        {
            TutorialStep.CreateArea,
            TutorialStep.CreateGym,
            TutorialStep.CreatePlan,
            TutorialStep.CreatePlanDay,
            TutorialStep.CreateTraining,
            TutorialStep.LastTreningResult
        };

        foreach (var step in steps)
        {
            var completeStepRequest = new CompleteStepRequest
            {
                TutorialType = TutorialType.OnboardingDemo,
                Step = step
            };

            var stepResponse = await PostAsJsonWithApiOptionsAsync("/api/tutorials/completeStep", completeStepRequest);
            stepResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Get tutorial progress - should be completed
        var progressResponse = await Client.GetAsync("/api/tutorials/OnboardingDemo");
        progressResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var progress = await progressResponse.Content.ReadFromJsonAsync<TutorialProgressDto>(SharedSerializationOptions.Current);
        progress.Should().NotBeNull();
        progress!.CompletedSteps.Should().HaveCount(6);
        progress.IsCompleted.Should().BeTrue();

        // Get active tutorials - should be empty
        var activeResponse = await Client.GetAsync("/api/tutorials/active");
        activeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var activeTutorials = await activeResponse.Content.ReadFromJsonAsync<List<TutorialProgressDto>>(SharedSerializationOptions.Current);
        activeTutorials.Should().NotBeNull();
        activeTutorials.Should().BeEmpty();

        // Check token - hasActiveTutorials should be false
        var checkTokenResponse = await Client.GetAsync("/api/checkToken");
        checkTokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokenUser = await checkTokenResponse.Content.ReadFromJsonAsync<UserDto>(SharedSerializationOptions.Current);
        tokenUser.Should().NotBeNull();
        tokenUser!.HasActiveTutorials.Should().BeFalse();
    }

    [Test]
    public async Task CompleteTutorial_Idempotent_CanBeCalledMultipleTimes()
    {
        // Seed user and initialize tutorial
        var user = await SeedUserAsync(name: "idempotent-user", email: "idempotent@example.com");
        await InitializeTutorialAsync(user.Id);
        SetAuthorizationHeader(user.Id);

        var completeTutorialRequest = new CompleteTutorialRequest
        {
            TutorialType = TutorialType.OnboardingDemo
        };

        // Complete tutorial first time
        var firstResponse = await PostAsJsonWithApiOptionsAsync("/api/tutorials/complete", completeTutorialRequest);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Complete tutorial second time - should not fail
        var secondResponse = await PostAsJsonWithApiOptionsAsync("/api/tutorials/complete", completeTutorialRequest);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify still completed and active list is empty
        var activeResponse = await Client.GetAsync("/api/tutorials/active");
        activeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var activeTutorials = await activeResponse.Content.ReadFromJsonAsync<List<TutorialProgressDto>>(SharedSerializationOptions.Current);
        activeTutorials.Should().NotBeNull();
        activeTutorials.Should().BeEmpty();
    }
}

// Response DTOs for deserialization

public sealed class LoginResponse
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = null!;

    [JsonPropertyName("req")]
    public UserDto? User { get; set; }
}

public sealed class UserDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("_id")]
    public string? Id { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = null!;

    [JsonPropertyName("hasActiveTutorials")]
    public bool HasActiveTutorials { get; set; }
}
