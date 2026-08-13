using System.Net;
using System.Text.Json;
using LgymApi.E2ETests.Given;
using LgymApi.E2ETests.Harness;

namespace LgymApi.E2ETests.PublicHttpGiven;

[TestFixture]
[Category("WebHarness")]
public sealed class PublicHttpGivenRequestTests
{
    [Test]
    public async Task Existing_invalid_login_characterization_uses_public_post_json_convention()
    {
        // Given
        using var handler = CreateSuccessHandler();
        using var httpClient = CreateHttpClient(handler);

        // When
        using var response = await RealApiHostProofTests.PostInvalidLoginAsync(
            httpClient,
            origin: null,
            CancellationToken.None);

        // Then
        var request = handler.Requests.Single();
        using var body = JsonDocument.Parse(request.Body);
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.Path, Is.EqualTo("/api/login"));
            Assert.That(body.RootElement.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(new[] { "name", "password" }));
        });
    }

    [Test]
    public async Task Register_sends_exact_legacy_body_language_and_stable_idempotency_key()
    {
        // Given
        using var handler = CreateSuccessHandler();
        using var httpClient = CreateHttpClient(handler);
        var client = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(2));
        var credentials = SyntheticCredentials.Create();

        // When
        await client.RegisterAsync(credentials, CancellationToken.None);
        await client.RegisterAsync(credentials, CancellationToken.None);

        // Then
        var first = handler.Requests[0];
        using var body = JsonDocument.Parse(first.Body);
        Assert.Multiple(() =>
        {
            Assert.That(first.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(first.Path, Is.EqualTo("/api/register"));
            Assert.That(first.Languages, Is.EqualTo(new[] { "en" }));
            Assert.That(first.Authorization, Is.Null);
            Assert.That(first.IdempotencyKey, Is.Not.Empty);
            Assert.That(handler.Requests[1].IdempotencyKey, Is.EqualTo(first.IdempotencyKey));
            Assert.That(body.RootElement.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(new[] { "name", "email", "password", "cpassword", "isVisibleInRanking" }));
            Assert.That(body.RootElement.GetProperty("name").GetString(), Is.EqualTo(credentials.Name));
            Assert.That(body.RootElement.GetProperty("email").GetString(), Is.EqualTo(credentials.Email));
            Assert.That(body.RootElement.GetProperty("password").GetString(), Is.EqualTo(credentials.Password));
            Assert.That(body.RootElement.GetProperty("cpassword").GetString(), Is.EqualTo(credentials.Password));
            Assert.That(body.RootElement.GetProperty("isVisibleInRanking").GetBoolean(),
                Is.EqualTo(credentials.IsVisibleInRanking));
        });
    }

    [Test]
    public async Task Login_sends_anonymous_legacy_body_and_returns_redacted_token_holder()
    {
        // Given
        const string tokenCanary = "token-canary-login-434";
        using var handler = new PublicHttpGivenRecordingHandler((_, _) => Task.FromResult(
            PublicHttpGivenRecordingHandler.JsonResponse(HttpStatusCode.OK, $"{{\"token\":\"{tokenCanary}\",\"req\":{{\"name\":\"ignored\"}}}}")));
        using var httpClient = CreateHttpClient(handler);
        var client = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(2));
        var credentials = SyntheticCredentials.Create();

        // When
        using var token = await client.LoginAsync(credentials, CancellationToken.None);

        // Then
        var request = handler.Requests.Single();
        using var body = JsonDocument.Parse(request.Body);
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.Path, Is.EqualTo("/api/login"));
            Assert.That(request.Languages, Is.EqualTo(new[] { "en" }));
            Assert.That(request.Authorization, Is.Null);
            Assert.That(request.IdempotencyKey, Is.Null);
            Assert.That(body.RootElement.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(new[] { "name", "password" }));
            Assert.That(token.ToString(), Is.EqualTo("<redacted-bearer-token>"));
            Assert.That(token.ToString(), Does.Not.Contain(tokenCanary));
        });
    }

    [Test]
    public async Task Active_tutorials_uses_bearer_and_parses_only_setup_fields()
    {
        // Given
        const string tokenCanary = "token-canary-active-434";
        using var handler = new PublicHttpGivenRecordingHandler((_, _) => Task.FromResult(
            PublicHttpGivenRecordingHandler.JsonResponse(
                HttpStatusCode.OK,
                "[{\"tutorialType\":\"OnboardingDemo\",\"remainingSteps\":[\"CreateArea\"],\"tutorialDescription\":\"ignored\"}]")));
        using var httpClient = CreateHttpClient(handler);
        var client = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(2));
        using var token = InMemoryBearerToken.Create(tokenCanary);

        // When
        var tutorials = await client.GetActiveTutorialsAsync(token, CancellationToken.None);

        // Then
        var request = handler.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(request.Path, Is.EqualTo("/api/tutorials/active"));
            Assert.That(request.Body, Is.Empty);
            Assert.That(request.Languages, Is.EqualTo(new[] { "en" }));
            Assert.That(request.Authorization?.Scheme, Is.EqualTo("Bearer"));
            Assert.That(request.Authorization?.Parameter, Is.EqualTo(tokenCanary));
            Assert.That(tutorials.Single().TutorialType, Is.EqualTo(PublicTutorialType.OnboardingDemo));
            Assert.That(tutorials.Single().RemainingSteps, Is.EqualTo(new[] { PublicTutorialStep.CreateArea }));
        });
    }

    [Test]
    public async Task Complete_step_sends_exact_legacy_body_with_bearer()
    {
        // Given
        const string tokenCanary = "token-canary-step-434";
        using var handler = CreateSuccessHandler();
        using var httpClient = CreateHttpClient(handler);
        var client = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(2));
        using var token = InMemoryBearerToken.Create(tokenCanary);

        // When
        await client.CompleteStepAsync(
            token,
            PublicTutorialType.OnboardingDemo,
            PublicTutorialStep.CreateArea,
            CancellationToken.None);

        // Then
        var request = handler.Requests.Single();
        using var body = JsonDocument.Parse(request.Body);
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.Path, Is.EqualTo("/api/tutorials/completeStep"));
            Assert.That(request.Languages, Is.EqualTo(new[] { "en" }));
            Assert.That(request.Authorization?.Scheme, Is.EqualTo("Bearer"));
            Assert.That(request.Authorization?.Parameter, Is.EqualTo(tokenCanary));
            Assert.That(request.IdempotencyKey, Is.Null);
            Assert.That(body.RootElement.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(new[] { "tutorialType", "step" }));
            Assert.That(body.RootElement.GetProperty("tutorialType").GetString(), Is.EqualTo("OnboardingDemo"));
            Assert.That(body.RootElement.GetProperty("step").GetString(), Is.EqualTo("CreateArea"));
        });
    }

    [Test]
    public void Synthetic_credentials_are_distinct_and_redacted()
    {
        // Given / When
        var first = SyntheticCredentials.Create();
        var second = SyntheticCredentials.Create();

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(second.Name, Is.Not.EqualTo(first.Name));
            Assert.That(second.Email, Is.Not.EqualTo(first.Email));
            Assert.That(second.Password, Is.Not.EqualTo(first.Password));
            Assert.That(second.RegistrationIdempotencyKey, Is.Not.EqualTo(first.RegistrationIdempotencyKey));
            Assert.That(first.ToString(), Is.EqualTo("<synthetic-credentials>"));
            Assert.That(first.ToString(), Does.Not.Contain(first.Password));
        });
    }

    private static PublicHttpGivenRecordingHandler CreateSuccessHandler() =>
        new((_, _) => Task.FromResult(
            PublicHttpGivenRecordingHandler.JsonResponse(HttpStatusCode.OK, "{\"msg\":\"ok\"}")));

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://127.0.0.1:54321/") };
}
