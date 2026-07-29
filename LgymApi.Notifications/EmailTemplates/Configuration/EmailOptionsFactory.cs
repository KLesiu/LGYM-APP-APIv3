using System.Globalization;
using System.Net.Mail;
using LgymApi.Infrastructure.Options;
using Microsoft.Extensions.Configuration;

namespace LgymApi.Infrastructure.Configuration;

internal static class EmailOptionsFactory
{
    internal static EmailOptions Create(IConfiguration configuration, string? preferredLanguage)
    {
        return new EmailOptions
        {
            Enabled = bool.TryParse(configuration["Email:Enabled"], out var enabled) && enabled,
            DeliveryMode = ResolveEmailDeliveryMode(configuration["Email:DeliveryMode"]),
            DummyOutputDirectory = configuration["Email:DummyOutputDirectory"] ?? "EmailOutbox",
            FromAddress = configuration["Email:FromAddress"] ?? string.Empty,
            FromName = configuration["Email:FromName"] ?? "LGYM Trainer",
            SmtpHost = configuration["Email:SmtpHost"] ?? string.Empty,
            SmtpPort = int.TryParse(configuration["Email:SmtpPort"], out var smtpPort) ? smtpPort : 587,
            Username = configuration["Email:Username"] ?? string.Empty,
            Password = configuration["Email:Password"] ?? string.Empty,
            UseSsl = GetBooleanOrDefault(configuration["Email:UseSsl"], defaultValue: true),
            InvitationBaseUrl = configuration["Email:InvitationBaseUrl"] ?? string.Empty,
            PasswordRecoveryBaseUrl = configuration["Email:PasswordRecoveryBaseUrl"] ?? string.Empty,
            TemplateRootPath = configuration["Email:TemplateRootPath"] ?? "EmailTemplates",
            DefaultCulture = ResolveDefaultCulture(configuration["Email:DefaultCulture"], preferredLanguage)
        };
    }

    internal static void Validate(EmailOptions options)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.InvitationBaseUrl))
        {
            throw new InvalidOperationException("Email:InvitationBaseUrl is required.");
        }

        if (!Uri.TryCreate(options.InvitationBaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Email:InvitationBaseUrl must be a valid absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(options.PasswordRecoveryBaseUrl))
        {
            throw new InvalidOperationException("Email:PasswordRecoveryBaseUrl is required.");
        }

        if (!Uri.TryCreate(options.PasswordRecoveryBaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Email:PasswordRecoveryBaseUrl must be a valid absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(options.TemplateRootPath))
        {
            throw new InvalidOperationException("Email:TemplateRootPath is required when email is enabled.");
        }

        if (options.DefaultCulture == null)
        {
            throw new InvalidOperationException("Email:DefaultCulture is required when email is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.FromAddress))
        {
            throw new InvalidOperationException("Email:FromAddress is required when email is enabled.");
        }

        try
        {
            _ = new MailAddress(options.FromAddress);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Email:FromAddress must be a valid email address.");
        }

        if (options.DeliveryMode == EmailDeliveryMode.Dummy)
        {
            if (string.IsNullOrWhiteSpace(options.DummyOutputDirectory))
            {
                throw new InvalidOperationException("Email:DummyOutputDirectory is required when Email:DeliveryMode is Dummy.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(options.SmtpHost))
        {
            throw new InvalidOperationException("Email:SmtpHost is required when email is enabled.");
        }

        if (options.SmtpPort <= 0)
        {
            throw new InvalidOperationException("Email:SmtpPort must be greater than 0 when email is enabled.");
        }
    }

    private static CultureInfo ResolveDefaultCulture(string? value, string? preferredLanguageFallback)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            try
            {
                return CultureInfo.GetCultureInfo(value);
            }
            catch (CultureNotFoundException)
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(preferredLanguageFallback))
        {
            try
            {
                return CultureInfo.GetCultureInfo(preferredLanguageFallback);
            }
            catch (CultureNotFoundException)
            {
            }
        }

        return CultureInfo.GetCultureInfo("en-US");
    }

    private static bool GetBooleanOrDefault(string? value, bool defaultValue)
    {
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static EmailDeliveryMode ResolveEmailDeliveryMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return EmailDeliveryMode.Smtp;
        }

        if (Enum.TryParse<EmailDeliveryMode>(value, ignoreCase: true, out var mode))
        {
            return mode;
        }

        throw new InvalidOperationException("Email:DeliveryMode must be one of: Smtp, Dummy.");
    }
}
