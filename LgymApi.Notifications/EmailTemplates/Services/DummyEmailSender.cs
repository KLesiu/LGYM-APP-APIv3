using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Options;
using System.Text;

namespace LgymApi.Infrastructure.Services;

internal sealed class DummyEmailSender : IEmailSender
{
    private readonly EmailOptions _emailOptions;

    public DummyEmailSender(EmailOptions emailOptions)
    {
        _emailOptions = emailOptions;
    }

    public async Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_emailOptions.Enabled)
        {
            return false;
        }

        var outputDirectory = ResolveOutputDirectory(_emailOptions.DummyOutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Id<DummyEmailSender>.New():N}.email.txt";
        var filePath = Path.Combine(outputDirectory, fileName);

        var content = BuildContent(message);
        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);
        return true;
    }

    private static string ResolveOutputDirectory(string configuredPath)
    {
        var trimmed = configuredPath.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return trimmed;
        }

        return Path.Combine(AppContext.BaseDirectory, trimmed);
    }

    private string BuildContent(EmailMessage message)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"SavedAtUtc: {DateTimeOffset.UtcNow:O}");
        builder.AppendLine($"DeliveryMode: {_emailOptions.DeliveryMode}");
        builder.AppendLine($"From: {_emailOptions.FromName} <{_emailOptions.FromAddress}>");
        builder.AppendLine($"To: {message.To}");
        builder.AppendLine($"Subject: {message.Subject}");
        builder.AppendLine($"ContentType: {(message.IsHtml ? "text/html" : "text/plain")}");
        builder.AppendLine("---");
        builder.Append(message.Body);
        return builder.ToString();
    }
}
