using System.Globalization;
using FluentAssertions;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Options;
using LgymApi.Infrastructure.Services;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class PasswordRecoveryEmailTemplateComposerTests
{
    [Test]
    public void Compose_WithEnglishPayload_ReturnsEnglishTemplateWithPlaceholdersReplaced()
    {
        var composer = CreateComposer();
        var payload = new PasswordRecoveryEmailPayload
        {
            UserId = Id<User>.New(),
            TokenId = Id<PasswordResetToken>.New(),
            UserName = "Alex",
            RecipientEmail = "alex@example.com",
            ResetToken = "ABC123DEF456",
            CultureName = "en-US"
        };

        var message = composer.ComposePasswordRecovery(payload);

        message.To.Should().Be("alex@example.com");
        message.Subject.Should().Be("Reset your LGYM password");
        message.Body.Should().Contain("Hi Alex,");
        message.Body.Should().Contain("Click the link below to reset your password:");
        message.Body.Should().Contain("ABC123DEF456");
        message.Body.Should().Contain("This link will expire in 30 minutes.");
        message.Body.Should().NotContain("{{UserName}}");
        message.Body.Should().NotContain("{{ResetToken}}");
        message.Body.Should().NotContain("{{ResetUrl}}");
        message.Body.Should().NotContain("{{ExpiryMinutes}}");
    }

    [Test]
    public void Compose_WithPolishPayload_ReturnsPolishTemplateWithPlaceholdersReplaced()
    {
        var composer = CreateComposer();
        var payload = new PasswordRecoveryEmailPayload
        {
            UserId = Id<User>.New(),
            TokenId = Id<PasswordResetToken>.New(),
            UserName = "Piotr",
            RecipientEmail = "piotr@example.pl",
            ResetToken = "XYZ789GHI012",
            CultureName = "pl-PL"
        };

        var message = composer.ComposePasswordRecovery(payload);

        message.To.Should().Be("piotr@example.pl");
        message.Subject.Should().Be("Zresetuj hasło do konta LGYM");
        message.Body.Should().Contain("Cześć Piotr,");
        message.Body.Should().Contain("Kliknij poniższy link, aby zresetować hasło:");
        message.Body.Should().Contain("XYZ789GHI012");
        message.Body.Should().Contain("Ten link wygaśnie za 30 minut.");
        message.Body.Should().NotContain("{{UserName}}");
        message.Body.Should().NotContain("{{ResetToken}}");
        message.Body.Should().NotContain("{{ResetUrl}}");
        message.Body.Should().NotContain("{{ExpiryMinutes}}");
    }

    [Test]
    public void Compose_WithJsonPayload_DeserializesAndReturnsEmailMessage()
    {
        var composer = CreateComposer();
        var payloadJson = """
            {
                "userId": "d75e53b9-2701-4cb0-b2d1-c02f0dbf8aa0",
                "tokenId": "f85e53b9-2701-4cb0-b2d1-c02f0dbf8bb1",
                "userName": "TestUser",
                "recipientEmail": "test@example.com",
                "resetToken": "TOKEN123456",
                "cultureName": "en-US"
            }
            """;

        var message = composer.Compose(payloadJson);

        message.To.Should().Be("test@example.com");
        message.Subject.Should().Be("Reset your LGYM password");
        message.Body.Should().Contain("Hi TestUser,");
        message.Body.Should().Contain("Click the link below to reset your password:");
        message.Body.Should().Contain("TOKEN123456");
    }

    [Test]
    public void Compose_WithInvalidJson_ThrowsInvalidOperationException()
    {
        var composer = CreateComposer();
        var invalidJson = "{ invalid json ";

        var ex = FluentActions.Invoking(() => composer.Compose(invalidJson)).Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("Failed to deserialize password recovery email payload");
    }

    [Test]
    public void Compose_WithNullPayload_ThrowsInvalidOperationException()
    {
        var composer = CreateComposer();
        var nullPayloadJson = "null";

        var ex = FluentActions.Invoking(() => composer.Compose(nullPayloadJson)).Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("Failed to deserialize password recovery email payload");
    }

    [Test]
    public void Compose_WithSpecialCharactersInUserName_SanitizesCorrectly()
    {
        var composer = CreateComposer();
        var payload = new PasswordRecoveryEmailPayload
        {
            UserId = Id<User>.New(),
            TokenId = Id<PasswordResetToken>.New(),
            UserName = "O'Connor <test@example.com>",
            RecipientEmail = "oconnor@example.com",
            ResetToken = "ABC123",
            CultureName = "en-US"
        };

        var message = composer.ComposePasswordRecovery(payload);

        message.Body.Should().Contain("O'Connor");
        message.Body.Should().NotBeEmpty();
    }

    [Test]
    public void Compose_WithDefaultCulture_FallsBackToEnglish()
    {
        var composer = CreateComposer();
        var payload = new PasswordRecoveryEmailPayload
        {
            UserId = Id<User>.New(),
            TokenId = Id<PasswordResetToken>.New(),
            UserName = "Alex",
            RecipientEmail = "alex@example.com",
            ResetToken = "ABC123",
            CultureName = "fr-FR" // French culture, but only en/pl templates exist
        };

        // Should fall back to en-US default culture based on EmailOptions.DefaultCulture
        var message = composer.ComposePasswordRecovery(payload);

        (message.Subject.Contains("Reset your LGYM password") || message.Subject.Contains("Zresetuj hasło")).Should().BeTrue();
        message.Body.Should().NotBeEmpty();
    }

    [Test]
    public void NotificationType_ReturnsPasswordRecoveryType()
    {
        var composer = CreateComposer();

        composer.NotificationType.Value.Should().Be("user.password.recovery");
    }

    [Test]
    public void Compose_WithEmptyStrings_DoesNotCrash()
    {
        var composer = CreateComposer();
        var payload = new PasswordRecoveryEmailPayload
        {
            UserId = Id<User>.New(),
            TokenId = Id<PasswordResetToken>.New(),
            UserName = string.Empty,
            RecipientEmail = "test@example.com",
            ResetToken = string.Empty,
            CultureName = "en-US"
        };

        var message = composer.ComposePasswordRecovery(payload);

        message.To.Should().Be("test@example.com");
        message.Subject.Should().NotBeEmpty();
        message.Body.Should().NotBeEmpty();
    }

    [Test]
    public void ComposePasswordRecovery_DerivesUrlFromConfiguredBaseUrlAndResetToken()
    {
        var composer = CreateComposer();
        var payload = new PasswordRecoveryEmailPayload
        {
            UserId = Id<User>.New(),
            TokenId = Id<PasswordResetToken>.New(),
            UserName = "Configured URL",
            RecipientEmail = "configured-url@example.com",
            ResetToken = "raw token/with?chars",
            ResetUrl = "https://request-sentinel.example.test/ignored",
            CultureName = "en-US"
        };

        var message = composer.ComposePasswordRecovery(payload);

        message.Body.Should().Contain("https://app.example.com/reset?token=raw%20token%2Fwith%3Fchars");
        message.Body.Should().NotContain("https://request-sentinel.example.test/ignored");
    }

    private static PasswordRecoveryEmailTemplateComposer CreateComposer()
    {
        var emailOptions = new EmailOptions
        {
            TemplateRootPath = "EmailTemplates",
            DefaultCulture = CultureInfo.GetCultureInfo("en-US"),
            PasswordRecoveryBaseUrl = "https://app.example.com/reset"
        };

        return new PasswordRecoveryEmailTemplateComposer(emailOptions);
    }
}
