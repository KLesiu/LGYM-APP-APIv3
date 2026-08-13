using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace LgymApi.E2ETests.PublicHttpGiven;

internal sealed record ObservedPublicHttpRequest(
    HttpMethod Method,
    Uri RequestUri,
    string Body,
    IReadOnlyList<string> Languages,
    AuthenticationHeaderValue? Authorization,
    string? IdempotencyKey,
    MediaTypeHeaderValue? ContentType)
{
    internal string Path => RequestUri.AbsolutePath;
}

internal sealed class PublicHttpGivenRecordingHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private readonly List<ObservedPublicHttpRequest> _requests = [];

    internal IReadOnlyList<ObservedPublicHttpRequest> Requests => _requests;

    internal static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        _requests.Add(new ObservedPublicHttpRequest(
            request.Method,
            request.RequestUri ?? throw new InvalidOperationException("Recorded request URI was absent."),
            body,
            request.Headers.AcceptLanguage.Select(value => value.Value).ToArray(),
            request.Headers.Authorization,
            request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null,
            request.Content?.Headers.ContentType));

        return await respond(request, cancellationToken);
    }
}
