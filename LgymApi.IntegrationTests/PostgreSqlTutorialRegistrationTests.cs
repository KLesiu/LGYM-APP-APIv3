using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LgymApi.Api.Features.User.Contracts;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
}
