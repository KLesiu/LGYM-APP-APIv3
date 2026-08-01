using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using LgymApi.Application.Options;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Options;
using LgymApi.Infrastructure.Services;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class EmailTemplateRenderingCompatibilityTests
{
    [TestCaseSource(nameof(RenderCases))]
    public void PublishedTemplate_RendersThroughProviderComposerInBothCultures(RenderCase renderCase)
    {
        var factory = CreateFactory();
        var payloadJson = JsonSerializer.Serialize(
            renderCase.Payload,
            renderCase.Payload.GetType(),
            SharedSerializationOptions.Current);

        var message = factory.ComposeMessage(renderCase.Payload.NotificationType, payloadJson);

        message.To.Should().Be(renderCase.Payload.RecipientEmail);
        message.Subject.Should().Be(renderCase.ExpectedSubject);
        message.Body.Should().Contain(renderCase.ExpectedBodyFragment);
        message.Subject.Should().NotContain("{{");
        message.Body.Should().NotContain("{{");
    }

    private static IEnumerable<TestCaseData> RenderCases()
    {
        foreach (var culture in new[] { "en-US", "pl-PL" })
        {
            var polish = culture == "pl-PL";
            yield return Case(
                "Welcome",
                culture,
                new WelcomeEmailPayload
                {
                    UserId = ParseId<User>("11111111-1111-1111-1111-111111111111"),
                    UserName = "Alex",
                    RecipientEmail = "welcome@example.test",
                    CultureName = culture
                },
                polish ? "Witaj w LGYM, Alex!" : "Welcome to LGYM, Alex!",
                polish ? "Twoje konto zostało pomyślnie utworzone." : "Your account has been created successfully.");
            yield return Case(
                "TrainerInvitation",
                culture,
                new InvitationEmailPayload
                {
                    InvitationId = ParseId<TrainerInvitation>("22222222-2222-2222-2222-222222222222"),
                    InvitationCode = "CODE-35",
                    ExpiresAt = new DateTimeOffset(2026, 8, 1, 12, 30, 0, TimeSpan.Zero),
                    TrainerName = "Coach",
                    RecipientEmail = "invitation@example.test",
                    CultureName = culture,
                    PreferredTimeZone = "UTC"
                },
                polish ? "Zaproszenie trenerskie od Coach" : "Trainer invitation from Coach",
                polish ? "Kod zaproszenia: CODE-35" : "Invitation code: CODE-35");
            yield return Case(
                "TrainerInvitationAccepted",
                culture,
                new InvitationAcceptedEmailPayload
                {
                    InvitationId = ParseId<TrainerInvitation>("33333333-3333-3333-3333-333333333333"),
                    TrainerName = "Coach",
                    TraineeName = "Trainee",
                    RecipientEmail = "accepted@example.test",
                    CultureName = culture,
                    PreferredTimeZone = "UTC"
                },
                polish ? "Twoje zaproszenie zostało zaakceptowane" : "Your invitation was accepted",
                polish ? "Trainee zaakceptował(a)" : "Trainee has accepted");
            yield return Case(
                "TrainerInvitationRevoked",
                culture,
                new InvitationRevokedEmailPayload
                {
                    InvitationId = ParseId<TrainerInvitation>("44444444-4444-4444-4444-444444444444"),
                    TrainerName = "Coach",
                    RecipientEmail = "revoked@example.test",
                    CultureName = culture,
                    PreferredTimeZone = "UTC"
                },
                polish ? "Zaproszenie trenerskie anulowane" : "Training invitation cancelled",
                polish ? "od Coach zostało anulowane" : "from Coach has been cancelled");
            yield return Case(
                "TrainingCompleted",
                culture,
                new TrainingCompletedEmailPayload
                {
                    UserId = ParseId<User>("55555555-5555-5555-5555-555555555555"),
                    TrainingId = ParseId<Training>("66666666-6666-6666-6666-666666666666"),
                    RecipientEmail = "training@example.test",
                    CultureName = culture,
                    PreferredTimeZone = "UTC",
                    PlanDayName = "Strength",
                    TrainingDate = new DateTimeOffset(2026, 8, 2, 6, 45, 0, TimeSpan.Zero),
                    Exercises = []
                },
                polish ? "Trening zakonczony - Strength" : "Training completed - Strength",
                polish ? "Twoj trening zostal zapisany." : "Your training has been recorded.");
            yield return Case(
                "PasswordRecovery",
                culture,
                new PasswordRecoveryEmailPayload
                {
                    UserId = ParseId<User>("77777777-7777-7777-7777-777777777777"),
                    TokenId = ParseId<PasswordResetToken>("88888888-8888-8888-8888-888888888888"),
                    UserName = "Alex",
                    RecipientEmail = "recovery@example.test",
                    ResetToken = "reset-token-35",
                    ResetUrl = "https://request-sentinel.example.test/ignored",
                    CultureName = culture
                },
                polish ? "Zresetuj hasło do konta LGYM" : "Reset your LGYM password",
                polish ? "Kliknij poniższy link, aby zresetować hasło:" : "Click the link below to reset your password:");
        }
    }

    private static TestCaseData Case(
        string template,
        string culture,
        IEmailPayload payload,
        string expectedSubject,
        string expectedBodyFragment) =>
        new(new RenderCase(payload, expectedSubject, expectedBodyFragment))
        {
            TestName = $"Published {template}/{culture[..2]}.email renders"
        };

    private static EmailTemplateComposerFactory CreateFactory()
    {
        var options = new EmailOptions
        {
            TemplateRootPath = Path.Combine(AppContext.BaseDirectory, "EmailTemplates"),
            DefaultCulture = CultureInfo.GetCultureInfo("en-US"),
            InvitationBaseUrl = "https://app.example.test/invitations",
            PasswordRecoveryBaseUrl = "https://app.example.test/reset"
        };
        var defaults = new AppDefaultsOptions
        {
            PreferredLanguage = "en-US",
            PreferredTimeZone = "UTC"
        };

        return new EmailTemplateComposerFactory(
        [
            new TrainerInvitationEmailTemplateComposer(options, defaults),
            new TrainerInvitationAcceptedEmailTemplateComposer(options),
            new TrainerInvitationRevokedEmailTemplateComposer(options),
            new TrainingCompletedEmailTemplateComposer(options, defaults),
            new WelcomeEmailTemplateComposer(options),
            new PasswordRecoveryEmailTemplateComposer(options)
        ]);
    }

    private static Id<TEntity> ParseId<TEntity>(string value)
    {
        Id<TEntity>.TryParse(value, out var id).Should().BeTrue();
        return id;
    }

    public sealed record RenderCase(
        IEmailPayload Payload,
        string ExpectedSubject,
        string ExpectedBodyFragment);
}
