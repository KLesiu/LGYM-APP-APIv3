using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LgymApi.E2ETests.PublicHttpGiven;

internal sealed record LoopbackWireReceipt(
    string Method,
    string Path,
    string Language,
    bool AuthorizationPresent,
    bool BodyRetained)
{
    public override string ToString() =>
        $"method={Method} path={Path} language={Language} authorizationPresent={AuthorizationPresent} bodyRetained={BodyRetained}";
}

internal sealed class PublicHttpGivenLoopbackServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Task<LoopbackWireReceipt> _receiptTask;
    private int _disposed;

    private PublicHttpGivenLoopbackServer(TcpListener listener)
    {
        _listener = listener;
        BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
        _receiptTask = AcceptOnceAsync();
    }

    internal Uri BaseAddress { get; }

    internal bool IsStopped => Volatile.Read(ref _disposed) != 0;

    internal static PublicHttpGivenLoopbackServer Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new PublicHttpGivenLoopbackServer(listener);
    }

    internal Task<LoopbackWireReceipt> GetReceiptAsync() => _receiptTask;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _listener.Stop();
        try
        {
            await _receiptTask;
        }
        catch (SocketException)
        {
        }
    }

    private async Task<LoopbackWireReceipt> AcceptOnceAsync()
    {
        using var client = await _listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync() ?? string.Empty;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
        {
            var separator = line.IndexOf(':');
            if (separator > 0)
            {
                headers[line[..separator]] = line[(separator + 1)..].Trim();
            }
        }

        if (headers.TryGetValue("Content-Length", out var lengthText) && int.TryParse(lengthText, out var length))
        {
            var buffer = new char[length];
            await reader.ReadBlockAsync(buffer);
        }

        var response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 12\r\nConnection: close\r\n\r\n{\"msg\":\"ok\"}");
        await stream.WriteAsync(response);
        await stream.FlushAsync();

        var requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new LoopbackWireReceipt(
            requestParts.ElementAtOrDefault(0) ?? string.Empty,
            requestParts.ElementAtOrDefault(1) ?? string.Empty,
            headers.GetValueOrDefault("Accept-Language", string.Empty),
            headers.ContainsKey("Authorization"),
            BodyRetained: false);
    }
}
