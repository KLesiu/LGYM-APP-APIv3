using FluentAssertions;
using LgymApi.Domain.ValueObjects;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Application.Options;
using LgymApi.Infrastructure.Options;
using LgymApi.Infrastructure.Services;
using System.Globalization;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class InvitationEmailTemplateComposerTests
{
    private string _templateRootPath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _templateRootPath = Path.Combine(Path.GetTempPath(), $"lgym-email-templates-{Id<InvitationEmailTemplateComposerTests>.New():N}");
        Directory.CreateDirectory(Path.Combine(_templateRootPath, "TrainerInvitation"));
        File.WriteAllText(
            Path.Combine(_templateRootPath, "TrainerInvitation", "en.email"),
            "Subject: Trainer invitation from {{TrainerName}}\n---\nInvitation {{InvitationCode}}\nAccept: {{AcceptUrl}}\nReject: {{RejectUrl}}\nExpires: {{ExpiresAt}}");
        File.WriteAllText(
            Path.Combine(_templateRootPath, "TrainerInvitation", "pl.email"),
            "Subject: Zaproszenie od {{TrainerName}}\n---\nKod: {{InvitationCode}}\nAkceptuj: {{AcceptUrl}}\nOdrzuc: {{RejectUrl}}\nWygasa: {{ExpiresAt}}");
    }

    [TearDown]
    public void TearDown()
    {
        if (!string.IsNullOrWhiteSpace(_templateRootPath) && Directory.Exists(_templateRootPath))
        {
            Directory.Delete(_templateRootPath, recursive: true);
        }
    }

    [Test]
    public void ComposeTrainerInvitation_ShouldUsePolishTemplate_WhenLanguageIsPl()
    {
        var composer = CreateComposer();

        var invitationId = Id<LgymApi.Domain.Entities.TrainerInvitation>.New();
        var payload = new InvitationEmailPayload
        {
            InvitationId = invitationId,
            InvitationCode = "ABC123XYZ789",
            ExpiresAt = DateTimeOffset.Parse("2026-03-01T10:00:00+00:00"),
            TrainerName = "Coach Mike",
            RecipientEmail = "trainee@example.com",
            CultureName = "pl-PL",
            PreferredTimeZone = "Europe/Warsaw"
        };

        var message = composer.ComposeTrainerInvitation(payload);

        message.To.Should().Be("trainee@example.com");
        message.Subject.Should().Be("Zaproszenie od Coach Mike");
        message.Body.Should().Contain("Kod: ABC123XYZ789");
        message.Body.Should().Contain("Akceptuj:");
        message.Body.Should().Contain("Wygasa:");
        message.Body.Should().Contain("2026-03-01 11:00");
        message.Body.Should().Contain($"/accept/{invitationId}");
        message.Body.Should().Contain($"/reject/{invitationId}");
    }

    [Test]
    public void ComposeTrainerInvitation_ShouldFallbackToDefaultLanguage_WhenTemplateMissing()
    {
        var composer = CreateComposer();

        var invitationId = Id<LgymApi.Domain.Entities.TrainerInvitation>.New();
        var payload = new InvitationEmailPayload
        {
            InvitationId = invitationId,
            InvitationCode = "ZZZ999YYY888",
            ExpiresAt = DateTimeOffset.Parse("2026-03-01T10:00:00+00:00"),
            TrainerName = "Coach Jane",
            RecipientEmail = "trainee@example.com",
            CultureName = "de-DE",
            PreferredTimeZone = "Europe/Warsaw"
        };

        var message = composer.ComposeTrainerInvitation(payload);

        message.Subject.Should().Be("Trainer invitation from Coach Jane");
        message.Body.Should().Contain("Invitation ZZZ999YYY888");
        message.Body.Should().Contain("Accept:");
    }

    [Test]
    public void ComposeTrainerInvitation_ShouldUseConfiguredFallbackTimeZone_WhenPreferredTimeZoneInvalid()
    {
        var composer = CreateComposer(new AppDefaultsOptions { PreferredLanguage = "en-US", PreferredTimeZone = "UTC" });

        var payload = new InvitationEmailPayload
        {
            InvitationId = Id<LgymApi.Domain.Entities.TrainerInvitation>.New(),
            InvitationCode = "UTC123",
            ExpiresAt = DateTimeOffset.Parse("2026-03-01T10:00:00+00:00"),
            TrainerName = "Coach UTC",
            RecipientEmail = "trainee@example.com",
            CultureName = "en-US",
            PreferredTimeZone = "Invalid/Zone"
        };

        var message = composer.ComposeTrainerInvitation(payload);

        message.Body.Should().Contain("Expires: 2026-03-01 10:00");
    }

    [Test]
    public void ComposeTrainerInvitation_ShouldUseConfiguredFallbackTimeZone_WhenPreferredTimeZoneEmpty()
    {
        var composer = CreateComposer(new AppDefaultsOptions { PreferredLanguage = "en-US", PreferredTimeZone = "UTC" });

        var payload = new InvitationEmailPayload
        {
            InvitationId = Id<LgymApi.Domain.Entities.TrainerInvitation>.New(),
            InvitationCode = "UTC456",
            ExpiresAt = DateTimeOffset.Parse("2026-03-01T10:00:00+00:00"),
            TrainerName = "Coach UTC",
            RecipientEmail = "trainee@example.com",
            CultureName = "en-US",
            PreferredTimeZone = string.Empty
        };

        var message = composer.ComposeTrainerInvitation(payload);

        message.Body.Should().Contain("Expires: 2026-03-01 10:00");
    }

    private TrainerInvitationEmailTemplateComposer CreateComposer(AppDefaultsOptions? appDefaultsOptions = null)
    {
        return new TrainerInvitationEmailTemplateComposer(new EmailOptions
        {
            InvitationBaseUrl = "https://app.example.com/invitations",
            TemplateRootPath = _templateRootPath,
            DefaultCulture = CultureInfo.GetCultureInfo("en-US")
        }, appDefaultsOptions ?? new AppDefaultsOptions());
    }
}
