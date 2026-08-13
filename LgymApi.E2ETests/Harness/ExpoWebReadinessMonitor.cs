using System.Net;

namespace LgymApi.E2ETests.Harness;

internal sealed class ExpoWebReadinessMonitor : IExpoWebReadinessMonitor
{
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<ExpoWebReadinessOutcome> WaitUntilReadyAsync(Uri endpoint,
        Task<ExpoWebProcessExit> processExit, ExpoWebReadinessBounds bounds, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(bounds.HttpRequestTimeout);
            var probe = ProbeAsync(endpoint, requestTimeout.Token);
            if (await Task.WhenAny(probe, processExit).WaitAsync(cancellationToken) == processExit)
            {
                requestTimeout.Cancel();
                await ObserveAsync(probe);
                return ExpoWebReadinessOutcome.ProcessExited;
            }

            if (await probe == ExpoWebReadinessOutcome.Ready) return ExpoWebReadinessOutcome.Ready;
            await Task.Delay(bounds.PollInterval, cancellationToken);
        }
    }

    private static async Task<ExpoWebReadinessOutcome> ProbeAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices
                ? ExpoWebReadinessOutcome.Ready : ExpoWebReadinessOutcome.HttpFailure;
        }
        catch (HttpRequestException) { return ExpoWebReadinessOutcome.HttpFailure; }
        catch (OperationCanceledException) { return ExpoWebReadinessOutcome.HttpTimeout; }
    }

    private static async Task ObserveAsync(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
    }
}
