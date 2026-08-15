using System.Net;
using System.Net.Http.Json;
using LgymApi.E2ETests.Harness;

namespace LgymApi.E2ETests.Lifecycle;

internal sealed class DatabaseBackedApiReadinessProbe : IDatabaseBackedApiReadinessProbe
{
    private static readonly HttpClient Client = CreateLocalClient();
    private readonly HttpClient _client;

    internal DatabaseBackedApiReadinessProbe() : this(Client)
    {
    }

    internal DatabaseBackedApiReadinessProbe(HttpClient client)
    {
        _client = client;
    }

    public async Task<DatabaseBackedApiReadinessOutcome> WaitUntilReadyAsync(
        Uri baseAddress,
        ApiHostReadinessBounds bounds,
        CancellationToken cancellationToken)
    {
        if (!baseAddress.IsAbsoluteUri || !baseAddress.IsLoopback ||
            !string.Equals(baseAddress.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(baseAddress.UserInfo))
        {
            return DatabaseBackedApiReadinessOutcome.HttpFailure;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(bounds.HttpRequestTimeout);
        try
        {
            using var response = await PostInvalidLoginAsync(_client, baseAddress, timeout.Token);
            return response.StatusCode == HttpStatusCode.Unauthorized
                ? DatabaseBackedApiReadinessOutcome.Ready
                : DatabaseBackedApiReadinessOutcome.UnexpectedStatus;
        }
        catch (HttpRequestException)
        {
            return DatabaseBackedApiReadinessOutcome.HttpFailure;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(null, null, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return DatabaseBackedApiReadinessOutcome.HttpTimeout;
        }
    }

    internal static Task<HttpResponseMessage> PostInvalidLoginAsync(
        HttpClient client,
        Uri baseAddress,
        CancellationToken cancellationToken)
    {
        return client.SendAsync(
            CreateInvalidLoginRequest(new Uri(baseAddress, "api/login"), null),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    internal static Task<HttpResponseMessage> PostInvalidLoginAsync(
        HttpClient client,
        string? origin,
        CancellationToken cancellationToken) =>
        client.SendAsync(
            CreateInvalidLoginRequest(null, origin),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

    private static HttpClient CreateLocalClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private static HttpRequestMessage CreateInvalidLoginRequest(Uri? requestUri, string? origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri ?? new Uri("api/login", UriKind.Relative))
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

        return request;
    }
}
