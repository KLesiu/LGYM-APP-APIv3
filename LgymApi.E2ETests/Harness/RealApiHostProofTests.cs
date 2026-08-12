using System.Net;
using System.Net.Http.Json;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class RealApiHostProofTests
{
    private static readonly string[] ExpectedCleanupOrder =
        ["api-process", "runtime-configuration", "postgresql"];

    [Test]
    public async Task Published_API_starts_as_exact_dotnet_DLL_process()
    {
        using var deadline = CreateDeadline();
        var context = await RealApiHostProofContext.CreateAsync(deadline.Token);
        await using var host = await context.StartAsync("E2E", deadline.Token);

        using var response = await host.Client.GetAsync("health/live", deadline.Token);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(context.Publication.Receipt.CommandName, Is.EqualTo("publish"));
            Assert.That(context.Publication.Receipt.ApiRepositoryHeadSha, Has.Length.EqualTo(40));
            Assert.That(context.Publication.Receipt.DllSha256, Has.Length.EqualTo(64));
        });
        TestContext.Out.WriteLine("receipt category=publication process=dotnet-dll health=200");
    }

    [Test]
    public async Task E2E_fresh_PostgreSQL_is_migrated_before_database_backed_readiness()
    {
        using var deadline = CreateDeadline();
        var context = await RealApiHostProofContext.CreateAsync(deadline.Token);
        await using var host = await context.StartAsync("E2E", deadline.Token);

        using var response = await PostInvalidLoginAsync(host.Client, origin: null, deadline.Token);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        TestContext.Out.WriteLine("receipt category=migration-readiness ready=true login=401");
    }

    [Test]
    public async Task E2E_enables_password_recovery_rate_limit()
    {
        using var deadline = CreateDeadline();
        var context = await RealApiHostProofContext.CreateAsync(deadline.Token);
        await using var host = await context.StartAsync("E2E", deadline.Token);
        var path = $"proof/{Guid.NewGuid():N}/forgot-password";
        var statuses = new List<HttpStatusCode>(6);

        for (var requestNumber = 0; requestNumber < 6; requestNumber++)
        {
            using var response = await host.Client.GetAsync(path, deadline.Token);
            statuses.Add(response.StatusCode);
        }

        Assert.Multiple(() =>
        {
            Assert.That(statuses.Take(5), Has.None.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(statuses[5], Is.EqualTo(HttpStatusCode.TooManyRequests));
        });
        TestContext.Out.WriteLine("receipt category=rate-limit firstFiveNon429=true sixth=429");
    }

    [Test]
    public async Task E2E_suppresses_Hangfire_dashboard_and_recurring_runtime()
    {
        using var deadline = CreateDeadline();
        var context = await RealApiHostProofContext.CreateAsync(deadline.Token);
        await using var host = await context.StartAsync("E2E", deadline.Token);

        using var response = await host.Client.GetAsync("hangfire", deadline.Token);
        await host.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(host.HangfireServerStartObserved, Is.False);
            Assert.That(host.CleanupReceipt.AttemptedCategories, Is.EqualTo(ExpectedCleanupOrder));
            Assert.That(host.CleanupReceipt.FailureCount, Is.Zero);
        });
        TestContext.Out.WriteLine(
            "receipt category=hangfire dashboard=404 serverStartObserved=false cleanupFailures=0");
    }

    [Test]
    public async Task Testing_behavior_remains_test_safe_without_migration_or_rate_limit()
    {
        using var deadline = CreateDeadline();
        var context = await RealApiHostProofContext.CreateAsync(deadline.Token);
        await using var host = await context.StartAsync("Testing", deadline.Token);
        var path = $"proof/{Guid.NewGuid():N}/forgot-password";
        var statuses = new List<HttpStatusCode>(6);

        using var health = await host.Client.GetAsync("health/live", deadline.Token);
        for (var requestNumber = 0; requestNumber < 6; requestNumber++)
        {
            using var response = await host.Client.GetAsync(path, deadline.Token);
            statuses.Add(response.StatusCode);
        }

        using var hangfire = await host.Client.GetAsync("hangfire", deadline.Token);
        await host.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(statuses, Has.None.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(hangfire.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(host.HangfireServerStartObserved, Is.False);
            Assert.That(host.CleanupReceipt.AttemptedCategories, Is.EqualTo(ExpectedCleanupOrder));
            Assert.That(host.CleanupReceipt.FailureCount, Is.Zero);
        });
        TestContext.Out.WriteLine(
            "receipt category=testing health=200 rateLimited=false dashboard=404 serverStartObserved=false cleanupFailures=0");
    }

    [Test]
    public async Task E2E_rejects_broadened_CORS_configuration_before_readiness()
    {
        using var deadline = CreateDeadline();
        var context = await RealApiHostProofContext.CreateAsync(deadline.Token);

        var receipt = await context.StartWithInvalidCorsAsync(deadline.Token);

        Assert.Multiple(() =>
        {
            Assert.That(receipt.Category, Is.EqualTo(ExternalApiHostLease.CorsPolicyFailureMessage));
            Assert.That(receipt.Ready, Is.False);
            Assert.That(receipt.ProcessTreeAbsent, Is.True);
            Assert.That(receipt.PrivateRunAbsent, Is.True);
            Assert.That(receipt.ContainerAbsent, Is.True);
        });
        TestContext.Out.WriteLine(
            "receipt category=cors-policy ready=false containerAbsent=true configuredValuesPersisted=false");
    }

    internal static CancellationTokenSource CreateDeadline()
    {
        var options = Configuration.E2EConfiguration.Load(
            TestContext.CurrentContext.TestDirectory,
            RepositoryRoot.Find());
        return new CancellationTokenSource(TimeSpan.FromSeconds(options.Timeouts.TestSessionSeconds));
    }

    internal static Task<HttpResponseMessage> PostInvalidLoginAsync(
        HttpClient client,
        string? origin,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/login")
        {
            Content = JsonContent.Create(new
            {
                name = "e2e-missing-account",
                password = "e2e-invalid-password"
            })
        };
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return client.SendAsync(request, cancellationToken);
    }
}
