using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LgymApi.E2ETests.Given;

internal sealed class PublicHttpGivenClient
{
    private const string Language = "en";
    private const string IdempotencyHeader = "Idempotency-Key";
    private const int MaximumResponseBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly HttpClient _httpClient;
    private readonly Uri _baseAddress;
    private readonly TimeSpan _requestTimeout;

    internal PublicHttpGivenClient(HttpClient httpClient, TimeSpan requestTimeout)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        var baseAddress = httpClient.BaseAddress;
        var isHttp = baseAddress?.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) == true;
        var isHttps = baseAddress?.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) == true;
        if (baseAddress is null ||
            !baseAddress.IsAbsoluteUri ||
            (!isHttp && !isHttps) ||
            baseAddress.UserInfo.Length != 0)
        {
            throw new PublicHttpGivenException("Public HTTP client base address is invalid.");
        }

        _httpClient = httpClient;
        _baseAddress = baseAddress;
        _requestTimeout = requestTimeout;
    }

    internal async Task RegisterAsync(
        SyntheticCredentials credentials,
        CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            "/api/register",
            new RegisterWireRequest(
                credentials.Name,
                credentials.Email,
                credentials.Password,
                credentials.Password,
                credentials.IsVisibleInRanking));
        request.Headers.Add(IdempotencyHeader, credentials.RegistrationIdempotencyKey);

        using var timeout = CreateTimeout(cancellationToken);
        using var response = await SendAsync(request, "register", timeout.Token, cancellationToken);
        EnsureSuccess(response, "register");
    }

    internal async Task<InMemoryBearerToken> LoginAsync(
        SyntheticCredentials credentials,
        CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            "/api/login",
            new LoginWireRequest(credentials.Name, credentials.Password));
        using var timeout = CreateTimeout(cancellationToken);
        using var response = await SendAsync(request, "login", timeout.Token, cancellationToken);
        EnsureSuccess(response, "login");

        var payload = await ReadJsonAsync<LoginWireResponse>(
            response,
            "login",
            timeout.Token,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(payload.Token))
        {
            throw new PublicHttpGivenException("Public HTTP login returned malformed JSON.");
        }

        return InMemoryBearerToken.Create(payload.Token);
    }

    internal async Task<IReadOnlyList<TutorialProgressWireResponse>> GetActiveTutorialsAsync(
        InMemoryBearerToken token,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "/api/tutorials/active");
        Authorize(request, token);
        using var timeout = CreateTimeout(cancellationToken);
        using var response = await SendAsync(request, "active tutorials", timeout.Token, cancellationToken);
        EnsureSuccess(response, "active tutorials");
        return await ReadJsonAsync<List<TutorialProgressWireResponse>>(
            response,
            "active tutorials",
            timeout.Token,
            cancellationToken);
    }

    internal async Task CompleteStepAsync(
        InMemoryBearerToken token,
        PublicTutorialType tutorialType,
        PublicTutorialStep step,
        CancellationToken cancellationToken)
    {
        if (tutorialType == PublicTutorialType.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(tutorialType));
        }

        if (step == PublicTutorialStep.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }

        using var request = CreateJsonRequest(
            HttpMethod.Post,
            "/api/tutorials/completeStep",
            new CompleteStepWireRequest(tutorialType, step));
        Authorize(request, token);
        using var timeout = CreateTimeout(cancellationToken);
        using var response = await SendAsync(
            request,
            "complete tutorial step",
            timeout.Token,
            cancellationToken);
        EnsureSuccess(response, "complete tutorial step");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string route)
    {
        var request = new HttpRequestMessage(method, new Uri(_baseAddress, route));
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(Language));
        return request;
    }

    private HttpRequestMessage CreateJsonRequest<T>(HttpMethod method, string route, T body)
    {
        var request = CreateRequest(method, route);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        return request;
    }

    private static void Authorize(HttpRequestMessage request, InMemoryBearerToken token) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.GetValue());

    private CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        return timeout;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        string operation,
        CancellationToken timeoutToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PublicHttpGivenException($"Public HTTP {operation} exceeded the configured timeout.");
        }
        catch (HttpRequestException)
        {
            throw new PublicHttpGivenException($"Public HTTP {operation} failed during transport.");
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new PublicHttpGivenException(
                $"Public HTTP {operation} failed with status {(int)response.StatusCode}.");
        }
    }

    private static async Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response,
        string operation,
        CancellationToken timeoutToken,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(timeoutToken);
            await using var bounded = new MemoryStream(MaximumResponseBytes);
            var buffer = new byte[4096];
            var total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, timeoutToken)) != 0)
            {
                total += read;
                if (total > MaximumResponseBytes)
                {
                    throw new PublicHttpGivenException($"Public HTTP {operation} returned malformed JSON.");
                }

                await bounded.WriteAsync(buffer.AsMemory(0, read), timeoutToken);
            }

            bounded.Position = 0;
            return await JsonSerializer.DeserializeAsync<T>(bounded, JsonOptions, timeoutToken)
                ?? throw new PublicHttpGivenException($"Public HTTP {operation} returned malformed JSON.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PublicHttpGivenException($"Public HTTP {operation} exceeded the configured timeout.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw new PublicHttpGivenException($"Public HTTP {operation} returned malformed JSON.");
        }
        catch (PublicHttpGivenException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new PublicHttpGivenException($"Public HTTP {operation} failed while reading the response.");
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        return options;
    }
}
