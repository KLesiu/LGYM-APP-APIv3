using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;

namespace LgymApi.TestUtils;

/// <summary>
/// Provides a test double for IEmailSender that captures sent messages and simulates failure scenarios.
/// </summary>
public sealed class TestEmailSender : IEmailSender
{
    public List<EmailMessage> SentMessages { get; } = new();

    /// <summary>
    /// Controls how many consecutive SendAsync calls should throw exceptions before succeeding.
    /// </summary>
    public int FailuresRemaining { get; set; }

    /// <summary>
    /// Forces SendAsync to return false instead of true after capturing the message.
    /// </summary>
    public bool ReturnFalse { get; set; }

    /// <summary>
    /// Clears all captured messages and resets failure simulation flags.
    /// </summary>
    public void Reset()
    {
        SentMessages.Clear();
        FailuresRemaining = 0;
        ReturnFalse = false;
    }

    /// <summary>
    /// Captures the email message and returns success or simulated failure based on FailuresRemaining and ReturnFalse flags.
    /// </summary>
    public Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (FailuresRemaining > 0)
        {
            FailuresRemaining -= 1;
            throw new InvalidOperationException("Simulated SMTP failure");
        }

        SentMessages.Add(message);
        return Task.FromResult(!ReturnFalse);
    }
}
