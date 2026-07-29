using System.Globalization;

namespace LgymApi.Infrastructure.Options;

internal sealed class EmailOptions
{
    public bool Enabled { get; set; } = true;
    public EmailDeliveryMode DeliveryMode { get; set; } = EmailDeliveryMode.Smtp;
    public string DummyOutputDirectory { get; set; } = "EmailOutbox";
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "LGYM Trainer";
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseSsl { get; set; } = true;
    public string InvitationBaseUrl { get; set; } = string.Empty;
    public string PasswordRecoveryBaseUrl { get; set; } = string.Empty;
    public string TemplateRootPath { get; set; } = "EmailTemplates";
    public CultureInfo DefaultCulture { get; set; } = CultureInfo.GetCultureInfo("en-US");
}
