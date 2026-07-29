using System.Net.Mail;
using MailKitSmtpClient = MailKit.Net.Smtp.SmtpClient;
using MailKit.Security;
using MimeKit;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Infrastructure.Options;

namespace LgymApi.Infrastructure.Services;

internal sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _emailOptions;

    public SmtpEmailSender(EmailOptions emailOptions)
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

        MailAddress recipientAddress;
        try
        {
            recipientAddress = new MailAddress(message.To);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Recipient email address is invalid.", ex);
        }

        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(_emailOptions.FromName, _emailOptions.FromAddress));
        mimeMessage.To.Add(new MailboxAddress(string.Empty, recipientAddress.Address));
        mimeMessage.Subject = message.Subject;
        var contentType = message.IsHtml ? "html" : "plain";
        mimeMessage.Body = new TextPart(contentType)
        {
            Text = message.Body
        };

        using var smtpClient = new MailKitSmtpClient();
        var secureSocketOption = _emailOptions.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;

        try
        {
            await smtpClient.ConnectAsync(_emailOptions.SmtpHost, _emailOptions.SmtpPort, secureSocketOption, cancellationToken);

            if (!string.IsNullOrWhiteSpace(_emailOptions.Username))
            {
                await smtpClient.AuthenticateAsync(_emailOptions.Username, _emailOptions.Password, cancellationToken);
            }

            await smtpClient.SendAsync(mimeMessage, cancellationToken);
        }
        finally
        {
            if (smtpClient.IsConnected)
            {
                using var disconnectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await smtpClient.DisconnectAsync(quit: true, disconnectTimeout.Token);
            }
        }

        return true;
    }
}
