using System.Net;

namespace LgymApi.E2ETests.Harness;

internal enum ApiHostReadinessOutcome
{
    Ready,
    AddressInUse,
    CorsPolicyRejected,
    PendingMigrations,
    ProcessExited,
    HttpFailure,
    HttpTimeout,
    StartupTimeout
}

internal sealed record ApiHostReadinessBounds(TimeSpan HttpRequestTimeout, TimeSpan PollInterval);

internal interface IApiHostReadinessMonitor
{
    Task<ApiHostReadinessOutcome> WaitUntilReadyAsync(
        Uri healthEndpoint,
        Task<ExternalApiProcessExit> processExit,
        ApiHostReadinessBounds bounds,
        CancellationToken cancellationToken);
}

internal sealed class ApiHostReadinessMonitor : IApiHostReadinessMonitor
{
    private static readonly HttpClient Client = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public async Task<ApiHostReadinessOutcome> WaitUntilReadyAsync(
        Uri healthEndpoint,
        Task<ExternalApiProcessExit> processExit,
        ApiHostReadinessBounds bounds,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(bounds.HttpRequestTimeout);
            var request = ProbeAsync(healthEndpoint, requestTimeout.Token);
            var completed = await Task.WhenAny(request, processExit).WaitAsync(cancellationToken);
            if (completed == processExit)
            {
                requestTimeout.Cancel();
                await ObserveCanceledProbeAsync(request);
                return MapProcessExit(await processExit);
            }

            var outcome = await request;
            if (outcome == ApiHostReadinessOutcome.Ready)
            {
                return outcome;
            }

            await Task.Delay(bounds.PollInterval, cancellationToken);
        }
    }

    private static async Task<ApiHostReadinessOutcome> ProbeAsync(
        Uri healthEndpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client.GetAsync(
                healthEndpoint,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            return response.StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices
                ? ApiHostReadinessOutcome.Ready
                : ApiHostReadinessOutcome.HttpFailure;
        }
        catch (HttpRequestException)
        {
            return ApiHostReadinessOutcome.HttpFailure;
        }
        catch (OperationCanceledException)
        {
            return ApiHostReadinessOutcome.HttpTimeout;
        }
    }

    private static async Task ObserveCanceledProbeAsync(Task<ApiHostReadinessOutcome> request)
    {
        try
        {
            await request;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static ApiHostReadinessOutcome MapProcessExit(ExternalApiProcessExit processExit) =>
        processExit.Kind == ExternalApiProcessExitKind.AddressInUse
            ? ApiHostReadinessOutcome.AddressInUse
            : processExit.Kind == ExternalApiProcessExitKind.CorsPolicyRejected
                ? ApiHostReadinessOutcome.CorsPolicyRejected
                : processExit.Kind == ExternalApiProcessExitKind.PendingMigrations
                    ? ApiHostReadinessOutcome.PendingMigrations
                : ApiHostReadinessOutcome.ProcessExited;
}
