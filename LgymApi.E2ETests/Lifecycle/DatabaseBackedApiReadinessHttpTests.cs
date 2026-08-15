using System.Net;
using System.Net.Sockets;
using System.Text;
using LgymApi.E2ETests.Harness;

namespace LgymApi.E2ETests.Lifecycle;

[TestFixture]
[Category("Lifecycle")]
public sealed class DatabaseBackedApiReadinessHttpTests
{
    [Test]
    public async Task DatabaseBacked_probe_reports_its_own_bounded_timeout()
    {
        using var client = new HttpClient(new CancellationBlockingHandler());
        var probe = new DatabaseBackedApiReadinessProbe(client);

        var outcome = await probe.WaitUntilReadyAsync(
            new Uri("http://127.0.0.1/"),
            new ApiHostReadinessBounds(TimeSpan.FromMilliseconds(75), TimeSpan.Zero),
            CancellationToken.None);

        Assert.That(outcome, Is.EqualTo(DatabaseBackedApiReadinessOutcome.HttpTimeout));
    }

    [Test]
    public void DatabaseBacked_probe_preserves_caller_cancellation()
    {
        using var client = new HttpClient(new CancellationBlockingHandler());
        var probe = new DatabaseBackedApiReadinessProbe(client);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = Assert.CatchAsync<OperationCanceledException>(() => probe.WaitUntilReadyAsync(
            new Uri("http://127.0.0.1/"),
            new ApiHostReadinessBounds(TimeSpan.FromSeconds(1), TimeSpan.Zero),
            cancellation.Token));

        Assert.That(exception!.CancellationToken, Is.EqualTo(cancellation.Token));
    }

    [TestCase(307, "Temporary Redirect")]
    [TestCase(308, "Permanent Redirect")]
    public async Task DatabaseBacked_probe_does_not_follow_redirects_to_a_401(
        int statusCode,
        string reasonPhrase)
    {
        await using var server = new RedirectingLoopbackServer(statusCode, reasonPhrase);
        var probe = new DatabaseBackedApiReadinessProbe();

        var outcome = await probe.WaitUntilReadyAsync(
            server.BaseAddress,
            new ApiHostReadinessBounds(TimeSpan.FromSeconds(1), TimeSpan.Zero),
            CancellationToken.None);
        await server.CompleteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(DatabaseBackedApiReadinessOutcome.UnexpectedStatus));
            Assert.That(server.RequestCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task DatabaseBacked_probe_classifies_headers_without_consuming_the_response_body()
    {
        var content = new NeverCompletingContent();
        using var client = new HttpClient(new ResponseHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = content }));
        var probe = new DatabaseBackedApiReadinessProbe(client);

        var outcome = await probe.WaitUntilReadyAsync(
            new Uri("http://127.0.0.1/"),
            new ApiHostReadinessBounds(TimeSpan.FromMilliseconds(100), TimeSpan.Zero),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(DatabaseBackedApiReadinessOutcome.UnexpectedStatus));
            Assert.That(content.SerializationCount, Is.Zero);
        });
    }

    [Test]
    public async Task DatabaseBacked_probe_rejects_non_loopback_without_starting_transport()
    {
        var handler = new ResponseHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = new HttpClient(handler);
        var probe = new DatabaseBackedApiReadinessProbe(client);

        var outcome = await probe.WaitUntilReadyAsync(
            new Uri("http://example.invalid/"),
            new ApiHostReadinessBounds(TimeSpan.FromSeconds(1), TimeSpan.Zero),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(DatabaseBackedApiReadinessOutcome.HttpFailure));
            Assert.That(handler.RequestCount, Is.Zero);
        });
    }

    private sealed class CancellationBlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The synthetic request was not canceled.");
        }
    }

    private sealed class ResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(response);
        }
    }

    private sealed class NeverCompletingContent : HttpContent
    {
        internal int SerializationCount { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            SerializationCount++;
            return Task.FromException(new IOException("Synthetic response-body consumption failure."));
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            SerializationCount++;
            return Task.FromException(new IOException("Synthetic response-body consumption failure."));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 1;
            return true;
        }
    }

    private sealed class RedirectingLoopbackServer : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(3));
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _server;
        private readonly int _statusCode;
        private readonly string _reasonPhrase;

        internal RedirectingLoopbackServer(int statusCode, string reasonPhrase)
        {
            _statusCode = statusCode;
            _reasonPhrase = reasonPhrase;
            _listener.Start();
            BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
            _server = ServeAsync();
        }

        internal Uri BaseAddress { get; }

        internal int RequestCount { get; private set; }

        internal async Task CompleteAsync()
        {
            await _server;
        }

        public async ValueTask DisposeAsync()
        {
            _lifetime.Cancel();
            _listener.Stop();
            try
            {
                await _server;
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException)
            {
            }

            _lifetime.Dispose();
        }

        private async Task ServeAsync()
        {
            using var first = await _listener.AcceptTcpClientAsync(_lifetime.Token);
            await ReadRequestAsync(first, _lifetime.Token);
            RequestCount++;
            await WriteResponseAsync(
                first,
                $"HTTP/1.1 {_statusCode} {_reasonPhrase}\r\nLocation: /redirected\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
                _lifetime.Token);

            using var redirectedWait = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            redirectedWait.CancelAfter(TimeSpan.FromMilliseconds(250));
            try
            {
                using var redirected = await _listener.AcceptTcpClientAsync(redirectedWait.Token);
                await ReadRequestAsync(redirected, redirectedWait.Token);
                RequestCount++;
                await WriteResponseAsync(
                    redirected,
                    "HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
                    redirectedWait.Token);
            }
            catch (OperationCanceledException) when (redirectedWait.IsCancellationRequested)
            {
            }
        }

        private static async Task ReadRequestAsync(TcpClient client, CancellationToken cancellationToken)
        {
            var buffer = new byte[1024];
            var stream = client.GetStream();
            while (true)
            {
                var count = await stream.ReadAsync(buffer, cancellationToken);
                if (count == 0 || Encoding.ASCII.GetString(buffer, 0, count).Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    return;
                }
            }
        }

        private static Task WriteResponseAsync(
            TcpClient client,
            string response,
            CancellationToken cancellationToken) =>
            client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken).AsTask();
    }
}
