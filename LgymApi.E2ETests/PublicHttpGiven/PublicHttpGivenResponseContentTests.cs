using System.Net;
using System.Net.Http.Headers;
using LgymApi.E2ETests.Given;

namespace LgymApi.E2ETests.PublicHttpGiven;

[TestFixture]
[Category("WebHarness")]
public sealed class PublicHttpGivenResponseContentTests
{
    private const string StreamCanary = "stream-fault-canary-434";

    [Test]
    public void Slow_response_content_uses_the_configured_timeout_and_sanitized_diagnostic()
    {
        // Given
        using var handler = CreateStreamHandler(() => new BlockingResponseStream());
        using var httpClient = CreateHttpClient(handler);
        var client = new PublicHttpGivenClient(httpClient, TimeSpan.FromMilliseconds(30));

        // When
        var exception = Assert.ThrowsAsync<PublicHttpGivenException>(() =>
            client.LoginAsync(SyntheticCredentials.Create(), CancellationToken.None));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Public HTTP login exceeded the configured timeout."));
            Assert.That(exception.InnerException, Is.Null);
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Caller_cancellation_during_response_content_remains_operation_canceled()
    {
        // Given
        using var handler = CreateStreamHandler(() => new BlockingResponseStream());
        using var httpClient = CreateHttpClient(handler);
        var client = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        // When
        var exception = Assert.CatchAsync<OperationCanceledException>(() =>
            client.LoginAsync(SyntheticCredentials.Create(), cancellation.Token));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.Not.TypeOf<PublicHttpGivenException>());
            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Response_content_stream_fault_is_sanitized_without_canary_or_inner_exception()
    {
        // Given
        using var handler = CreateStreamHandler(() => new FaultingResponseStream(StreamCanary));
        using var httpClient = CreateHttpClient(handler);
        var client = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(2));

        // When
        var exception = Assert.ThrowsAsync<PublicHttpGivenException>(() =>
            client.LoginAsync(SyntheticCredentials.Create(), CancellationToken.None));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Public HTTP login failed while reading the response."));
            Assert.That(exception.ToString(), Does.Not.Contain(StreamCanary));
            Assert.That(exception.InnerException, Is.Null);
        });
    }

    [TestCase("register")]
    [TestCase("login")]
    [TestCase("complete-step")]
    public async Task JSON_requests_use_application_json_content_type(string operation)
    {
        // Given
        using var handler = new PublicHttpGivenRecordingHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/api/login"
                ? PublicHttpGivenRecordingHandler.JsonResponse(HttpStatusCode.OK, "{\"token\":\"json-token\"}")
                : PublicHttpGivenRecordingHandler.JsonResponse(HttpStatusCode.OK, "{\"msg\":\"ok\"}")));
        using var httpClient = CreateHttpClient(handler);
        var client = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(2));
        var credentials = SyntheticCredentials.Create();

        // When
        switch (operation)
        {
            case "register":
                await client.RegisterAsync(credentials, CancellationToken.None);
                break;
            case "login":
                using (await client.LoginAsync(credentials, CancellationToken.None))
                {
                }
                break;
            case "complete-step":
                using (var token = InMemoryBearerToken.Create("json-token"))
                {
                    await client.CompleteStepAsync(
                        token,
                        PublicTutorialType.OnboardingDemo,
                        PublicTutorialStep.CreateArea,
                        CancellationToken.None);
                }
                break;
            default:
                throw new InvalidOperationException("Unknown JSON operation fixture.");
        }

        // Then
        Assert.That(handler.Requests.Single().ContentType?.MediaType, Is.EqualTo("application/json"));
    }

    private static PublicHttpGivenRecordingHandler CreateStreamHandler(Func<Stream> streamFactory) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(streamFactory())
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") }
            }
        }));

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://127.0.0.1:54321/") };

    private sealed class BlockingResponseStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private sealed class FaultingResponseStream(string message) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException(message);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException(message));
    }
}
