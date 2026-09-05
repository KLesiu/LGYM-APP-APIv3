using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LgymApi.Api.Features.User.Contracts;
using LgymApi.Application.Features.Tutorial;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LgymApi.IntegrationTests;

[TestFixture]
[NonParallelizable]
[Category("PostgreSql")]
internal sealed class PostgreSqlTutorialRegistrationTests : PostgreSqlIntegrationTestBase
{
    [Test]
    public async Task RegisterAsync_InitializesActiveOnboardingTutorialBeforeFirstLogin()
    {
        var suffix = $"{Id<User>.New():N}";
        var name = $"tutorial-register-{suffix}";
        var email = $"{name}@example.test";
        const string password = "UserSecret123!";
        Client.DefaultRequestHeaders.Add("Idempotency-Key", $"tutorial-register-{suffix}");

        var registerResponse = await Client.PostAsJsonAsync("/api/register", new
        {
            name,
            email,
            password,
            cpassword = password,
            isVisibleInRanking = true
        });

        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var registeredUser = await database.Users
                .AsNoTracking()
                .SingleAsync(user => user.Email == email);
            var tutorial = await database.UserTutorialProgresses
                .AsNoTracking()
                .SingleAsync(progress =>
                    progress.UserId == registeredUser.Id
                    && progress.TutorialType == TutorialType.OnboardingDemo);

            tutorial.IsCompleted.Should().BeFalse();
        }

        Client.DefaultRequestHeaders.Remove("Idempotency-Key");
        var loginResponse = await Client.PostAsJsonAsync("/api/login", new { name, password });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>(SharedSerializationOptions.Current);
        login.Should().NotBeNull();
        login!.User.Should().NotBeNull();
        login.User!.HasActiveTutorials.Should().BeTrue();
    }

    [Test]
    public async Task GoogleSignInAsync_InitializesActiveOnboardingTutorialInFirstResponse()
    {
        var suffix = $"{Id<User>.New():N}";
        var email = $"tutorial-google-{suffix}@example.test";
        var tokenValidator = new GoogleTokenValidatorStub(new GoogleTokenPayload(
            $"tutorial-google-{suffix}",
            email,
            true,
            "Tutorial Google",
            null));
        using var factory = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGoogleTokenValidator>();
                services.AddSingleton<IGoogleTokenValidator>(tokenValidator);
            }));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/google", new { idToken = "valid-token" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var login = await response.Content.ReadFromJsonAsync<LoginResponseDto>(SharedSerializationOptions.Current);
        login.Should().NotBeNull();
        login!.User.Should().NotBeNull();
        login.User!.HasActiveTutorials.Should().BeTrue();

        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var registeredUserId = await database.Users
            .AsNoTracking()
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleAsync();
        var tutorial = await database.UserTutorialProgresses
            .AsNoTracking()
            .SingleAsync(progress => progress.UserId == registeredUserId);
        tutorial.TutorialType.Should().Be(TutorialType.OnboardingDemo);
        tutorial.IsCompleted.Should().BeFalse();
    }

    [Test]
    public async Task InitializeOnboardingTutorialAsync_WhenOuterTransactionRollsBack_DoesNotPersistTutorial()
    {
        var user = await SeedUserAsync(
            $"tutorial-rollback-{Id<User>.New():N}",
            $"tutorial-rollback-{Id<User>.New():N}@example.test");

        await using (var serviceScope = Factory.Services.CreateAsyncScope())
        {
            var database = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unitOfWork = serviceScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var tutorialService = serviceScope.ServiceProvider.GetRequiredService<ITutorialService>();
            await using var transaction = await unitOfWork.BeginTransactionAsync();

            var result = await tutorialService.InitializeOnboardingTutorialAsync(user.Id);

            result.IsSuccess.Should().BeTrue();
            database.Database.CurrentTransaction.Should().NotBeNull();
            await transaction.RollbackAsync();
        }

        await using var verificationScope = Factory.Services.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tutorialExists = await verificationDatabase.UserTutorialProgresses
            .AsNoTracking()
            .AnyAsync(progress => progress.UserId == user.Id);
        tutorialExists.Should().BeFalse();
    }

    private sealed class GoogleTokenValidatorStub(GoogleTokenPayload payload) : IGoogleTokenValidator
    {
        public Task<GoogleTokenPayload?> ValidateAsync(
            string idToken,
            string? accessToken,
            CancellationToken cancellationToken)
            => Task.FromResult<GoogleTokenPayload?>(idToken == "valid-token" ? payload : null);
    }
}
