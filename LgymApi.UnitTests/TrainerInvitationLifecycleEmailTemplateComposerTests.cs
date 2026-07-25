using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Options;
using LgymApi.Infrastructure.Services;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TrainerInvitationLifecycleEmailTemplateComposerTests
{
    private string _templateRootPath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _templateRootPath = Path.Combine(Path.GetTempPath(), $"lgym-invitation-lifecycle-templates-{Id<TrainerInvitationLifecycleEmailTemplateComposerTests>.New():N}");
        Directory.CreateDirectory(Path.Combine(_templateRootPath, "TrainerInvitationAccepted"));
        Directory.CreateDirectory(Path.Combine(_templateRootPath, "TrainerInvitationRevoked"));
        File.WriteAllText(
            Path.Combine(_templateRootPath, "TrainerInvitationAccepted", "en.email"),
            "Subject: {{TraineeName}} accepted {{TrainerName}}\n---\nAccepted by {{TraineeName}}");
        File.WriteAllText(
            Path.Combine(_templateRootPath, "TrainerInvitationRevoked", "en.email"),
            "Subject: {{TrainerName}} revoked invitation\n---\nRevoked by {{TrainerName}}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_templateRootPath))
        {
            Directory.Delete(_templateRootPath, recursive: true);
        }
    }

    [Test]
    public void Compose_InvitationAcceptedPayload_RendersTheAcceptedTemplate()
    {
        var composer = new TrainerInvitationAcceptedEmailTemplateComposer(CreateEmailOptions());
        var payload = new InvitationAcceptedEmailPayload
        {
            InvitationId = Id<TrainerInvitation>.New(),
            TrainerName = "Coach",
            TraineeName = "Trainee",
            RecipientEmail = "coach@example.com",
            CultureName = "en-US",
            PreferredTimeZone = "Europe/Warsaw"
        };

        var message = composer.Compose(JsonSerializer.Serialize(payload, SharedSerializationOptions.Current));

        message.To.Should().Be("coach@example.com");
        message.Subject.Should().Be("Trainee accepted Coach");
        message.Body.Should().Be("Accepted by Trainee");
    }

    [Test]
    public void Compose_InvitationRevokedPayload_RendersTheRevokedTemplate()
    {
        var composer = new TrainerInvitationRevokedEmailTemplateComposer(CreateEmailOptions());
        var payload = new InvitationRevokedEmailPayload
        {
            InvitationId = Id<TrainerInvitation>.New(),
            TrainerName = "Coach",
            RecipientEmail = "trainee@example.com",
            CultureName = "en-US",
            PreferredTimeZone = "Europe/Warsaw"
        };

        var message = composer.Compose(JsonSerializer.Serialize(payload, SharedSerializationOptions.Current));

        message.To.Should().Be("trainee@example.com");
        message.Subject.Should().Be("Coach revoked invitation");
        message.Body.Should().Be("Revoked by Coach");
    }

    private EmailOptions CreateEmailOptions() => new()
    {
        TemplateRootPath = _templateRootPath,
        DefaultCulture = CultureInfo.GetCultureInfo("en-US")
    };
}
