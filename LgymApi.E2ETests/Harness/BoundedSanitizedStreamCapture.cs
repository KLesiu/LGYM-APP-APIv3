using System.Text;

namespace LgymApi.E2ETests.Harness;

internal sealed class BoundedSanitizedStreamCapture(IReadOnlyList<string> secrets)
{
    private const int ReadBufferCharacters = 4096;
    private readonly StreamingSecretRedactor _redactor = new(secrets);
    private readonly Queue<Rune> _tail = new();
    private int _retainedUtf8Bytes;

    public bool WasTruncated { get; private set; }

    public async Task DrainAsync(TextReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[ReadBufferCharacters];
        while (true)
        {
            var charactersRead = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (charactersRead == 0)
            {
                AppendSanitized(_redactor.Transform(ReadOnlySpan<char>.Empty, isFinal: true));
                return;
            }

            AppendSanitized(_redactor.Transform(buffer.AsSpan(0, charactersRead), isFinal: false));
        }
    }

    public ExternalProcessOutput Snapshot()
    {
        var builder = new StringBuilder(_tail.Count);
        foreach (var rune in _tail)
        {
            builder.Append(rune.ToString());
        }

        return new ExternalProcessOutput(builder.ToString(), WasTruncated);
    }

    private void AppendSanitized(string value)
    {
        foreach (var rune in value.EnumerateRunes())
        {
            _tail.Enqueue(rune);
            _retainedUtf8Bytes += rune.Utf8SequenceLength;
            while (_retainedUtf8Bytes > ExternalProcessOutput.MaximumTailBytes)
            {
                _retainedUtf8Bytes -= _tail.Dequeue().Utf8SequenceLength;
                WasTruncated = true;
            }
        }
    }
}

internal sealed class StreamingSecretRedactor
{
    private const string RedactionMarker = "[REDACTED]";
    private readonly string[] _secrets;
    private readonly int _carryLength;
    private string _carry = string.Empty;

    public StreamingSecretRedactor(IReadOnlyList<string> secrets)
    {
        _secrets = secrets
            .Where(secret => !string.IsNullOrEmpty(secret))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(secret => secret.Length)
            .ToArray();
        _carryLength = _secrets.Length == 0 ? 0 : _secrets[0].Length - 1;
    }

    public string Transform(ReadOnlySpan<char> chunk, bool isFinal)
    {
        var combined = string.Concat(_carry, chunk);
        var safeLength = isFinal
            ? combined.Length
            : Math.Max(0, combined.Length - _carryLength);
        var output = new StringBuilder(safeLength);
        var index = 0;

        while (index < safeLength)
        {
            var secret = FindSecretAt(combined, index);
            if (secret is not null)
            {
                output.Append(RedactionMarker);
                index += secret.Length;
                continue;
            }

            output.Append(combined[index]);
            index++;
        }

        _carry = isFinal ? string.Empty : combined[index..];
        return output.ToString();
    }

    private string? FindSecretAt(string value, int index)
    {
        foreach (var secret in _secrets)
        {
            if (value.AsSpan(index).StartsWith(secret, StringComparison.Ordinal))
            {
                return secret;
            }
        }

        return null;
    }
}
