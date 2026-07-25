using FluentAssertions;
using LgymApi.Domain.ValueObjects;
using System.Globalization;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Application.Options;
using LgymApi.Domain.Enums;
using LgymApi.Infrastructure.Options;
using LgymApi.Infrastructure.Services;
using LgymApi.Resources;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TrainingCompletedEmailTemplateComposerTests
{
    private string _templateRootPath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _templateRootPath = Path.Combine(Path.GetTempPath(), $"lgym-training-email-templates-{Id<TrainingCompletedEmailTemplateComposerTests>.New():N}");
        Directory.CreateDirectory(Path.Combine(_templateRootPath, "TrainingCompleted"));
        File.WriteAllText(
            Path.Combine(_templateRootPath, "TrainingCompleted", "en.email"),
            "Subject: Training completed - {{PlanDayName}}\n---\nPlan: {{PlanDayName}}\nDate: {{TrainingDate}}\n\n{{TrainingTable}}");
        File.WriteAllText(
            Path.Combine(_templateRootPath, "TrainingCompleted", "pl.email"),
            "Subject: Trening zakonczony - {{PlanDayName}}\n---\nPlan: {{PlanDayName}}\nData: {{TrainingDate}}\n\n{{TrainingTable}}");
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
    public void ComposeTrainingCompleted_ShouldRenderTableWithNames()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        var composer = CreateComposer();
        var payload = new TrainingCompletedEmailPayload
        {
            UserId = Id<LgymApi.Domain.Entities.User>.New(),
            TrainingId = Id<LgymApi.Domain.Entities.Training>.New(),
            RecipientEmail = "user@example.com",
            CultureName = "en-US",
            PreferredTimeZone = "Europe/Warsaw",
            PlanDayName = "Upper Body A",
            TrainingDate = DateTimeOffset.Parse("2026-03-01T10:00:00+00:00"),
            Exercises = new List<TrainingExerciseSummary>
             {
                 new()
                 {
                     ExerciseName = "Bench Press",
                     Series = 1,
                     Reps = 8,
                     Weight = 80,
                     Unit = WeightUnits.Kilograms
                 },
                 new()
                 {
                     ExerciseName = "Bench Press",
                     Series = 2,
                     Reps = 6,
                     Weight = 85,
                     Unit = WeightUnits.Kilograms
                 }
             }
        };

        var message = composer.ComposeTrainingCompleted(payload);

        message.To.Should().Be("user@example.com");
        message.Subject.Should().Be("Training completed - Upper Body A");
        message.IsHtml.Should().BeTrue();
        message.Body.Should().Contain("<table");
        message.Body.Should().Contain("Bench Press");
        message.Body.Should().Contain("Date: 2026-03-01 11:00");
        message.Body.Should().Contain($"{Emails.TrainingSeriesLabel} #1");
        message.Body.Should().Contain($"{Emails.TrainingSeriesLabel} #2");
        message.Body.Should().Contain(">8<");
        message.Body.Should().Contain(">80<");
    }

    [Test]
    public void ComposeTrainingCompleted_ShouldFallbackToDefaultLanguage_WhenTemplateMissing()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        var composer = CreateComposer();
        var payload = new TrainingCompletedEmailPayload
        {
            UserId = Id<LgymApi.Domain.Entities.User>.New(),
            TrainingId = Id<LgymApi.Domain.Entities.Training>.New(),
            RecipientEmail = "user@example.com",
            CultureName = "de-DE",
            PreferredTimeZone = "Europe/Warsaw",
            PlanDayName = "Lower Body B",
            TrainingDate = DateTimeOffset.Parse("2026-03-01T10:00:00+00:00"),
            Exercises = new List<TrainingExerciseSummary>()
        };

        var message = composer.ComposeTrainingCompleted(payload);

        message.Subject.Should().Be("Training completed - Lower Body B");
        message.IsHtml.Should().BeTrue();
        message.Body.Should().Contain(Emails.TrainingNoExercises);
    }

    [Test]
    public void ComposeTrainingCompleted_ShouldPreserveExerciseGroupOrder_NotAlphabetical()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        var composer = CreateComposer();
        var payload = new TrainingCompletedEmailPayload
        {
            UserId = Id<LgymApi.Domain.Entities.User>.New(),
            TrainingId = Id<LgymApi.Domain.Entities.Training>.New(),
            RecipientEmail = "user@example.com",
            CultureName = "en-US",
            PreferredTimeZone = "Europe/Warsaw",
            PlanDayName = "Full Body",
            TrainingDate = DateTimeOffset.Parse("2026-03-01T10:00:00+00:00"),
            Exercises = new List<TrainingExerciseSummary>
             {
                 // Order in list: Squats, Bench Press, Deadlifts
                 // Alphabetical would be: Bench Press, Deadlifts, Squats
                 new()
                 {
                     ExerciseName = "Squats",
                     Series = 1,
                     Reps = 10,
                     Weight = 100,
                     Unit = WeightUnits.Kilograms
                 },
                 new()
                 {
                     ExerciseName = "Bench Press",
                     Series = 1,
                     Reps = 8,
                     Weight = 80,
                     Unit = WeightUnits.Kilograms
                 },
                 new()
                 {
                     ExerciseName = "Deadlifts",
                     Series = 1,
                     Reps = 5,
                     Weight = 120,
                     Unit = WeightUnits.Kilograms
                 }
             }
        };

        var message = composer.ComposeTrainingCompleted(payload);

        // Find the positions of exercise names in the HTML body
        var squatsPos = message.Body.IndexOf(">Squats<", StringComparison.Ordinal);
        var benchPos = message.Body.IndexOf(">Bench Press<", StringComparison.Ordinal);
        var deadliftsPos = message.Body.IndexOf(">Deadlifts<", StringComparison.Ordinal);

        squatsPos.Should().BeGreaterThan(-1, "Squats not found in body");
        benchPos.Should().BeGreaterThan(-1, "Bench Press not found in body");
        deadliftsPos.Should().BeGreaterThan(-1, "Deadlifts not found in body");
        // Verify the order: Squats should appear before Bench Press, which should appear before Deadlifts
        squatsPos.Should().BeLessThan(benchPos, "Squats should appear before Bench Press (payload order, not alphabetical)");
        benchPos.Should().BeLessThan(deadliftsPos, "Bench Press should appear before Deadlifts (payload order, not alphabetical)");
    }

    [Test]
    public void ComposeTrainingCompleted_ShouldRenderLocalizedUnitDisplayName()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        var composer = CreateComposer();
        var payload = new TrainingCompletedEmailPayload
        {
            UserId = Id<LgymApi.Domain.Entities.User>.New(),
            TrainingId = Id<LgymApi.Domain.Entities.Training>.New(),
            RecipientEmail = "user@example.com",
            CultureName = "en-US",
            PreferredTimeZone = "Europe/Warsaw",
            PlanDayName = "Testing Units",
            TrainingDate = DateTimeOffset.Parse("2026-03-01T10:00:00+00:00"),
            Exercises = new List<TrainingExerciseSummary>
             {
                 new()
                 {
                     ExerciseName = "Deadlifts",
                     Series = 1,
                     Reps = 5,
                     Weight = 100,
                     Unit = WeightUnits.Kilograms
                 },
                 new()
                 {
                     ExerciseName = "Bench Press",
                     Series = 1,
                     Reps = 8,
                     Weight = 185,
                     Unit = WeightUnits.Pounds
                 }
             }
        };

        var message = composer.ComposeTrainingCompleted(payload);

        message.Body.Should().Contain(">kg<", "Should render localized name for Kilograms");
        message.Body.Should().Contain(">lbs<", "Should render localized name for Pounds");
        message.Body.Should().NotContain(">Kilograms<", "Should not render enum name for Kilograms");
        message.Body.Should().NotContain(">Pounds<", "Should not render enum name for Pounds");
    }

    [Test]
    public void ComposeTrainingCompleted_ShouldUseConfiguredFallbackTimeZone_WhenPreferredTimeZoneInvalid()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        var composer = CreateComposer(new AppDefaultsOptions { PreferredLanguage = "en-US", PreferredTimeZone = "UTC" });
        var payload = new TrainingCompletedEmailPayload
        {
            UserId = Id<LgymApi.Domain.Entities.User>.New(),
            TrainingId = Id<LgymApi.Domain.Entities.Training>.New(),
            RecipientEmail = "user@example.com",
            CultureName = "en-US",
            PreferredTimeZone = "Invalid/Zone",
            PlanDayName = "Fallback TZ",
            TrainingDate = DateTimeOffset.Parse("2026-03-01T10:00:00+00:00"),
            Exercises = new List<TrainingExerciseSummary>()
        };

        var message = composer.ComposeTrainingCompleted(payload);

        message.Body.Should().Contain("Date: 2026-03-01 10:00");
    }

    [Test]
    public void ComposeTrainingCompleted_ShouldUseConfiguredFallbackTimeZone_WhenPreferredTimeZoneEmpty()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        var composer = CreateComposer(new AppDefaultsOptions { PreferredLanguage = "en-US", PreferredTimeZone = "UTC" });
        var payload = new TrainingCompletedEmailPayload
        {
            UserId = Id<LgymApi.Domain.Entities.User>.New(),
            TrainingId = Id<LgymApi.Domain.Entities.Training>.New(),
            RecipientEmail = "user@example.com",
            CultureName = "en-US",
            PreferredTimeZone = string.Empty,
            PlanDayName = "Fallback TZ",
            TrainingDate = DateTimeOffset.Parse("2026-03-01T10:00:00+00:00"),
            Exercises = new List<TrainingExerciseSummary>()
        };

        var message = composer.ComposeTrainingCompleted(payload);

        message.Body.Should().Contain("Date: 2026-03-01 10:00");
    }

    private TrainingCompletedEmailTemplateComposer CreateComposer(AppDefaultsOptions? appDefaultsOptions = null)
    {
        return new TrainingCompletedEmailTemplateComposer(new EmailOptions
        {
            InvitationBaseUrl = "https://app.example.com/invitations",
            TemplateRootPath = _templateRootPath,
            DefaultCulture = CultureInfo.GetCultureInfo("en-US")
        }, appDefaultsOptions ?? new AppDefaultsOptions());
    }
}
