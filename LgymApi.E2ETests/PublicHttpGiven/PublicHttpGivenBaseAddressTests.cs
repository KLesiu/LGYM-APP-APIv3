using System.Net;
using LgymApi.E2ETests.Given;

namespace LgymApi.E2ETests.PublicHttpGiven;

[TestFixture]
[Category("WebHarness")]
public sealed class PublicHttpGivenBaseAddressTests
{
    private const string UriCanary = "uri-userinfo-canary-434";

    [Test]
    public void Missing_base_address_is_rejected_before_the_handler_receives_a_request()
    {
        // Given
        using var handler = CreateHandler();
        using var httpClient = new HttpClient(handler);

        // When
        var exception = Assert.Throws<PublicHttpGivenException>(() =>
            _ = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(2)));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Public HTTP client base address is invalid."));
            Assert.That(handler.Requests, Is.Empty);
        });
    }

    [Test]
    public void Unsupported_base_scheme_is_rejected_before_the_handler_receives_a_request()
    {
        // Given
        using var handler = CreateHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("ftp://example.invalid/prefix/") };

        // When
        var exception = Assert.Throws<PublicHttpGivenException>(() =>
            _ = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(2)));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Public HTTP client base address is invalid."));
            Assert.That(handler.Requests, Is.Empty);
        });
    }

    [Test]
    public void Userinfo_base_is_rejected_without_leaking_userinfo_or_sending()
    {
        // Given
        using var handler = CreateHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://{UriCanary}:password@example.invalid/prefix/")
        };

        // When
        var exception = Assert.Throws<PublicHttpGivenException>(() =>
            _ = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(2)));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Public HTTP client base address is invalid."));
            Assert.That(exception.ToString(), Does.Not.Contain(UriCanary));
            Assert.That(handler.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task Prefixed_base_sends_every_operation_to_its_exact_root_relative_route()
    {
        // Given
        using var handler = new PublicHttpGivenRecordingHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath switch
            {
                "/api/login" => PublicHttpGivenRecordingHandler.JsonResponse(
                    HttpStatusCode.OK,
                    "{\"token\":\"prefixed-base-token\"}"),
                "/api/tutorials/active" => PublicHttpGivenRecordingHandler.JsonResponse(
                    HttpStatusCode.OK,
                    "[]"),
                _ => PublicHttpGivenRecordingHandler.JsonResponse(HttpStatusCode.OK, "{\"msg\":\"ok\"}")
            }));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.invalid/prefix/")
        };
        var client = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(2));
        var credentials = SyntheticCredentials.Create();

        // When
        await client.RegisterAsync(credentials, CancellationToken.None);
        using var token = await client.LoginAsync(credentials, CancellationToken.None);
        await client.GetActiveTutorialsAsync(token, CancellationToken.None);
        await client.CompleteStepAsync(
            token,
            PublicTutorialType.OnboardingDemo,
            PublicTutorialStep.CreateArea,
            CancellationToken.None);

        // Then
        Assert.That(
            handler.Requests.Select(request => request.Path),
            Is.EqualTo(new[]
            {
                "/api/register",
                "/api/login",
                "/api/tutorials/active",
                "/api/tutorials/completeStep"
            }));
    }

    [Test]
    public async Task Captured_base_is_not_replaced_by_later_HttpClient_mutation()
    {
        // Given
        using var handler = CreateHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://original.invalid/prefix/")
        };
        var client = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(2));
        httpClient.BaseAddress = new Uri("https://changed.invalid/other/");

        // When
        await client.RegisterAsync(SyntheticCredentials.Create(), CancellationToken.None);

        // Then
        var uri = handler.Requests.Single().RequestUri;
        Assert.Multiple(() =>
        {
            Assert.That(uri.Host, Is.EqualTo("original.invalid"));
            Assert.That(uri.AbsolutePath, Is.EqualTo("/api/register"));
        });
    }

    private static PublicHttpGivenRecordingHandler CreateHandler() =>
        new((_, _) => Task.FromResult(
            PublicHttpGivenRecordingHandler.JsonResponse(HttpStatusCode.OK, "{\"msg\":\"ok\"}")));
}
