using System.Net;
using LgymApi.E2ETests.Given;

namespace LgymApi.E2ETests.PublicHttpGiven;

[TestFixture]
[Category("WebHarness")]
public sealed class PublicHttpGivenFailureTests
{
    private const string ResponseCanary = "IGNORE-INSTRUCTIONS-SECRET-CANARY-434";

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.InternalServerError)]
    public void Error_response_diagnostics_are_bounded_and_omit_untrusted_body(HttpStatusCode statusCode)
    {
        // Given
        using var handler = CreateResponseHandler(statusCode, $"{{\"msg\":\"{ResponseCanary}\"}}");
        using var httpClient = CreateHttpClient(handler);
        var client = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(2));
        var credentials = SyntheticCredentials.Create();

        // When
        var exception = Assert.ThrowsAsync<PublicHttpGivenException>(() =>
            client.LoginAsync(credentials, CancellationToken.None));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain(((int)statusCode).ToString()));
            Assert.That(exception.Message, Does.Not.Contain(ResponseCanary));
            Assert.That(exception.ToString(), Does.Not.Contain(ResponseCanary));
            Assert.That(exception.Message.Length, Is.LessThan(160));
        });
    }

    [Test]
    public void Malformed_success_json_diagnostics_omit_untrusted_body()
    {
        // Given
        using var handler = CreateResponseHandler(
            HttpStatusCode.OK,
            $"{{\"token\":\"unterminated-{ResponseCanary}");
        using var httpClient = CreateHttpClient(handler);
        var client = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(2));

        // When
        var exception = Assert.ThrowsAsync<PublicHttpGivenException>(() =>
            client.LoginAsync(SyntheticCredentials.Create(), CancellationToken.None));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Public HTTP login returned malformed JSON."));
            Assert.That(exception.ToString(), Does.Not.Contain(ResponseCanary));
        });
    }

    [Test]
    public void Oversized_success_json_is_rejected_without_retaining_untrusted_body()
    {
        // Given
        var oversizedToken = ResponseCanary + new string('x', 70_000);
        using var handler = CreateResponseHandler(
            HttpStatusCode.OK,
            $"{{\"token\":\"{oversizedToken}\"}}");
        using var httpClient = CreateHttpClient(handler);
        var client = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(2));

        // When
        var exception = Assert.ThrowsAsync<PublicHttpGivenException>(() =>
            client.LoginAsync(SyntheticCredentials.Create(), CancellationToken.None));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Public HTTP login returned malformed JSON."));
            Assert.That(exception.ToString(), Does.Not.Contain(ResponseCanary));
        });
    }

    [Test]
    public void Caller_cancellation_reaches_the_HTTP_request()
    {
        // Given
        using var handler = new PublicHttpGivenRecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return PublicHttpGivenRecordingHandler.JsonResponse(HttpStatusCode.OK, "{}");
        });
        using var httpClient = CreateHttpClient(handler);
        var client = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // When
        var exception = Assert.CatchAsync<OperationCanceledException>(() =>
            client.RegisterAsync(SyntheticCredentials.Create(), cancellation.Token));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Configured_request_timeout_is_sanitized()
    {
        // Given
        using var handler = new PublicHttpGivenRecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return PublicHttpGivenRecordingHandler.JsonResponse(HttpStatusCode.OK, "{}");
        });
        using var httpClient = CreateHttpClient(handler);
        var client = new PublicHttpGivenClient(httpClient, TimeSpan.FromMilliseconds(20));

        // When
        var exception = Assert.ThrowsAsync<PublicHttpGivenException>(() =>
            client.RegisterAsync(SyntheticCredentials.Create(), CancellationToken.None));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Public HTTP register exceeded the configured timeout."));
            Assert.That(exception.InnerException, Is.Null);
        });
    }

    [Test]
    public void Transport_failure_diagnostics_omit_untrusted_exception_text()
    {
        // Given
        using var handler = new PublicHttpGivenRecordingHandler((_, _) =>
            throw new HttpRequestException(ResponseCanary));
        using var httpClient = CreateHttpClient(handler);
        var client = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(2));

        // When
        var exception = Assert.ThrowsAsync<PublicHttpGivenException>(() =>
            client.RegisterAsync(SyntheticCredentials.Create(), CancellationToken.None));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Public HTTP register failed during transport."));
            Assert.That(exception.ToString(), Does.Not.Contain(ResponseCanary));
            Assert.That(exception.InnerException, Is.Null);
        });
    }

    [Test]
    public void Disposed_token_cannot_authorize_a_stale_request()
    {
        // Given
        using var handler = CreateResponseHandler(HttpStatusCode.OK, "[]");
        using var httpClient = CreateHttpClient(handler);
        var client = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(2));
        var token = InMemoryBearerToken.Create("stale-token-canary-434");
        token.Dispose();

        // When
        var exception = Assert.ThrowsAsync<ObjectDisposedException>(() =>
            client.GetActiveTutorialsAsync(token, CancellationToken.None));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.ObjectName, Is.EqualTo(nameof(InMemoryBearerToken)));
            Assert.That(handler.Requests, Is.Empty);
        });
    }

    [Test]
    public void Unknown_tutorial_values_are_rejected_before_the_HTTP_request()
    {
        // Given
        using var handler = CreateResponseHandler(HttpStatusCode.OK, "{}");
        using var httpClient = CreateHttpClient(handler);
        var client = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(2));
        using var token = InMemoryBearerToken.Create("valid-token-434");

        // When
        var exception = Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.CompleteStepAsync(
            token,
            PublicTutorialType.Unknown,
            PublicTutorialStep.Unknown,
            CancellationToken.None));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.ParamName, Is.EqualTo("tutorialType"));
            Assert.That(handler.Requests, Is.Empty);
        });
    }

    private static PublicHttpGivenRecordingHandler CreateResponseHandler(
        HttpStatusCode statusCode,
        string json) =>
        new((_, _) => Task.FromResult(PublicHttpGivenRecordingHandler.JsonResponse(statusCode, json)));

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://127.0.0.1:54321/") };
}
