using System.Net;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class RealApiHostCorsProofTests
{
    private const string AllowedOrigin = "http://localhost:8083";
    private const string RejectedOrigin = "http://localhost:8084";

    [Test]
    public async Task E2E_allows_only_configured_credentialed_browser_origin()
    {
        using var deadline = RealApiHostProofTests.CreateDeadline();
        var context = await RealApiHostProofContext.CreateAsync(deadline.Token);
        await using var host = await context.StartAsync("E2E", deadline.Token);

        using var allowedPreflight = await SendPreflightAsync(host.Client, AllowedOrigin, deadline.Token);
        using var allowedActual = await RealApiHostProofTests.PostInvalidLoginAsync(
            host.Client,
            AllowedOrigin,
            deadline.Token);
        using var rejectedPreflight = await SendPreflightAsync(host.Client, RejectedOrigin, deadline.Token);
        using var rejectedActual = await RealApiHostProofTests.PostInvalidLoginAsync(
            host.Client,
            RejectedOrigin,
            deadline.Token);

        Assert.Multiple(() =>
        {
            Assert.That(allowedPreflight.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            AssertAllowedCredentialedOrigin(allowedPreflight, AllowedOrigin);
            Assert.That(GetHeader(allowedPreflight, "Access-Control-Allow-Methods"), Does.Contain("POST"));
            Assert.That(GetHeader(allowedPreflight, "Access-Control-Allow-Headers"), Does.Contain("content-type").IgnoreCase);
            Assert.That(allowedActual.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            AssertAllowedCredentialedOrigin(allowedActual, AllowedOrigin);
            Assert.That(rejectedPreflight.Headers.Contains("Access-Control-Allow-Origin"), Is.False);
            Assert.That(rejectedPreflight.Headers.Contains("Access-Control-Allow-Credentials"), Is.False);
            Assert.That(rejectedActual.Headers.Contains("Access-Control-Allow-Origin"), Is.False);
            Assert.That(rejectedActual.Headers.Contains("Access-Control-Allow-Credentials"), Is.False);
        });
        TestContext.Out.WriteLine(
            "receipt category=cors preflightAllowed=true actualAllowed=true credentials=true rejectedOriginHeaders=false");
    }

    private static Task<HttpResponseMessage> SendPreflightAsync(
        HttpClient client,
        string origin,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "api/login");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        return client.SendAsync(request, cancellationToken);
    }

    private static void AssertAllowedCredentialedOrigin(HttpResponseMessage response, string origin)
    {
        Assert.That(GetHeader(response, "Access-Control-Allow-Origin"), Is.EqualTo(origin));
        Assert.That(GetHeader(response, "Access-Control-Allow-Credentials"), Is.EqualTo("true"));
    }

    private static string GetHeader(HttpResponseMessage response, string name) =>
        string.Join(",", response.Headers.GetValues(name));
}
