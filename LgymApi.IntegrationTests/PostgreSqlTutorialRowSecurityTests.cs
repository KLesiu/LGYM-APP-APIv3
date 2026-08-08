using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Api.Features.Tutorial.Contracts;
using LgymApi.Application.Features.Tutorial;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LgymApi.IntegrationTests;

[TestFixture]
[NonParallelizable]
[Category("PostgreSql")]
public sealed class PostgreSqlTutorialRowSecurityTests
{
    [Test]
    public async Task RuntimeRole_EnforcesParentChildIsolationAndClearsPooledActorContext()
    {
        await using var environment = await PostgreSqlTutorialRowSecurityTestEnvironment.CreateAsync();
        var actorA = await environment.SeedUserAsync("tutorial-actor-a", "tutorial-actor-a@example.test");
        var actorB = await environment.SeedUserAsync("tutorial-actor-b", "tutorial-actor-b@example.test");
        await SeedTutorialAsync(environment.Factory, actorA);
        await SeedTutorialAsync(environment.Factory, actorB);
        var (progressA, stepA) = await environment.ReadTutorialIdsAsync(actorA);
        var (progressB, stepB) = await environment.ReadTutorialIdsAsync(actorB);

        (await ReadCountsAsync(environment.RuntimeConnectionString, actorA.ToString())).Should().Be((1, 1));
        (await ReadCountsAsync(environment.RuntimeConnectionString, actorB.ToString())).Should().Be((1, 1));
        (await ReadCountsAsync(environment.RuntimeConnectionString, null)).Should().Be((0, 0));
        (await ReadCountsAsync(environment.RuntimeConnectionString, "malformed-actor")).Should().Be((0, 0));

        (await UpdateAsync(environment.RuntimeConnectionString, actorA.ToString(), progressA.ToString(), stepA.ToString())).Should().Be((1, 1));
        (await UpdateAsync(environment.RuntimeConnectionString, actorB.ToString(), progressB.ToString(), stepB.ToString())).Should().Be((1, 1));
        var victimBeforeForeignWrite = await environment.ReadTutorialSnapshotAsync(progressB, stepB);
        (await UpdateAsync(environment.RuntimeConnectionString, actorA.ToString(), progressB.ToString(), stepB.ToString())).Should().Be((0, 0));
        (await environment.ReadTutorialSnapshotAsync(progressB, stepB)).Should().Be(victimBeforeForeignWrite);
        var actorBeforeDeniedWrites = await environment.ReadTutorialSnapshotAsync(progressA, stepA);
        (await UpdateAsync(environment.RuntimeConnectionString, null, progressA.ToString(), stepA.ToString())).Should().Be((0, 0));
        (await environment.ReadTutorialSnapshotAsync(progressA, stepA)).Should().Be(actorBeforeDeniedWrites);
        (await UpdateAsync(environment.RuntimeConnectionString, "malformed-actor", progressA.ToString(), stepA.ToString())).Should().Be((0, 0));
        (await environment.ReadTutorialSnapshotAsync(progressA, stepA)).Should().Be(actorBeforeDeniedWrites);

        (await ReadCountsAsync(environment.RuntimeConnectionString, actorA.ToString())).Should().Be((1, 1));
        (await ReadCountsAsync(environment.RuntimeConnectionString, actorB.ToString())).Should().Be((1, 1));

        await AssertPooledScopeDoesNotLeakAsync(environment.RuntimeConnectionString, actorA.ToString(), actorB.ToString());
    }

    [Test]
    public async Task RuntimeRole_CannotEscalateOrPrepareMaintenanceSchema()
    {
        await using var environment = await PostgreSqlTutorialRowSecurityTestEnvironment.CreateAsync();

        await AssertPermissionDeniedAsync(environment.RuntimeConnectionString, $"SET ROLE {environment.MaintenanceRole};");
        await AssertPermissionDeniedAsync(environment.RuntimeConnectionString, "ALTER TABLE public.\"UserTutorialProgresses\" DISABLE ROW LEVEL SECURITY;");
        await AssertPermissionDeniedAsync(environment.RuntimeConnectionString, "ALTER TABLE public.\"UserTutorialProgresses\" ADD COLUMN \"runtime_schema_attempt\" integer;");
        await AssertPermissionDeniedAsync(environment.RuntimeConnectionString, "CREATE TABLE hangfire.\"runtime_schema_attempt\" (\"Id\" integer);");
    }

    [Test]
    public async Task TutorialIntegrationTests_RuntimeRole_PreservesOrdinaryTutorialBehavior()
    {
        await using var environment = await PostgreSqlTutorialRowSecurityTestEnvironment.CreateAsync();
        using var client = environment.Factory.CreateClient();
        var suffix = $"{Id<PostgreSqlTutorialRowSecurityTests>.New():N}";
        var userName = $"tutorial-runtime-{suffix}";
        var userId = await environment.SeedUserAsync(userName, $"{userName}@example.test");
        await SeedTutorialAsync(environment.Factory, userId);

        var loginResponse = await client.PostAsJsonAsync("/api/login", new { name = userName, password = "UserSecret123!" });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        login.Should().NotBeNull();
        login!.Token.Should().NotBeNullOrWhiteSpace();
        login.User!.HasActiveTutorials.Should().BeTrue();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

        var activeResponse = await client.GetAsync("/api/tutorials/active");
        activeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var activeTutorials = await activeResponse.Content.ReadFromJsonAsync<List<TutorialProgressDto>>(SharedSerializationOptions.Current);
        activeTutorials.Should().ContainSingle(tutorial => tutorial.TutorialType == TutorialType.OnboardingDemo);

        var stepResponse = await PostTutorialAsync(client, "/api/tutorials/completeStep", new CompleteStepRequest
        {
            TutorialType = TutorialType.OnboardingDemo,
            Step = TutorialStep.CreateArea
        }, suffix);
        stepResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var progressResponse = await client.GetAsync("/api/tutorials/OnboardingDemo");
        var progress = await progressResponse.Content.ReadFromJsonAsync<TutorialProgressDto>(SharedSerializationOptions.Current);
        progressResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        progress!.CompletedSteps.Should().ContainSingle().Which.Should().Be(TutorialStep.CreateArea);

        var completeResponse = await PostTutorialAsync(client, "/api/tutorials/complete", new CompleteTutorialRequest
        {
            TutorialType = TutorialType.OnboardingDemo
        }, suffix);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/tutorials/active")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task SeedTutorialAsync(PostgreSqlWebApplicationFactory factory, Id<User> userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITutorialService>();
        (await service.InitializeOnboardingTutorialAsync(userId)).IsSuccess.Should().BeTrue();
        (await service.CompleteStepAsync(userId, TutorialType.OnboardingDemo, TutorialStep.CreateArea)).IsSuccess.Should().BeTrue();
    }

    private static async Task<(int Progresses, int Steps)> ReadCountsAsync(string connectionString, string? actorId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await SetActorAsync(connection, transaction, actorId);
        await using var command = new NpgsqlCommand(
            "SELECT (SELECT count(*) FROM public.\"UserTutorialProgresses\"), (SELECT count(*) FROM public.\"UserTutorialStepProgresses\");",
            connection,
            transaction);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        var result = (Convert.ToInt32(reader.GetInt64(0)), Convert.ToInt32(reader.GetInt64(1)));
        await reader.CloseAsync();
        await transaction.CommitAsync();
        return result;
    }

    private static async Task<(int Progresses, int Steps)> UpdateAsync(string connectionString, string? actorId, string progressId, string stepId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await SetActorAsync(connection, transaction, actorId);
        await using var progressCommand = new NpgsqlCommand(
            "UPDATE public.\"UserTutorialProgresses\" SET \"IsCompleted\" = NOT \"IsCompleted\" WHERE \"Id\" = CAST(@id AS uuid);",
            connection,
            transaction);
        progressCommand.Parameters.AddWithValue("id", progressId);
        var progresses = await progressCommand.ExecuteNonQueryAsync();
        await using var stepCommand = new NpgsqlCommand(
            "UPDATE public.\"UserTutorialStepProgresses\" SET \"CompletedAt\" = CURRENT_TIMESTAMP WHERE \"Id\" = CAST(@id AS uuid);",
            connection,
            transaction);
        stepCommand.Parameters.AddWithValue("id", stepId);
        var steps = await stepCommand.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return (progresses, steps);
    }

    private static async Task AssertPooledScopeDoesNotLeakAsync(string connectionString, string actorA, string actorB)
    {
        (await ReadCountsAsync(connectionString, actorA)).Should().Be((1, 1));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var settingCommand = new NpgsqlCommand("SELECT current_setting('lgym.account_id', true);", connection))
        {
            var value = (string?)await settingCommand.ExecuteScalarAsync();
            string.IsNullOrEmpty(value).Should().BeTrue();
        }

        await using var transaction = await connection.BeginTransactionAsync();
        await SetActorAsync(connection, transaction, actorB);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM public.\"UserTutorialProgresses\";", connection, transaction);
        (await command.ExecuteScalarAsync()).Should().Be(1L);
        await transaction.CommitAsync();
    }

    private static async Task SetActorAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string? actorId)
    {
        if (actorId is null)
        {
            return;
        }

        await using var command = new NpgsqlCommand("SELECT set_config('lgym.account_id', @actorId, true);", connection, transaction);
        command.Parameters.AddWithValue("actorId", actorId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertPermissionDeniedAsync(string connectionString, string sql)
    {
        var action = async () =>
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        };
        var exception = await action.Should().ThrowAsync<PostgresException>();
        exception.Which.SqlState.Should().Be("42501");
    }

    private static async Task<HttpResponseMessage> PostTutorialAsync<T>(HttpClient client, string path, T request, string suffix)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter());
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"tutorial-rls-{suffix}-{path}");
        var response = await client.PostAsync(path, new StringContent(JsonSerializer.Serialize(request, options), Encoding.UTF8, "application/json"));
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        return response;
    }
}
