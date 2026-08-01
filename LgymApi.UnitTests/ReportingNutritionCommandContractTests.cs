using FluentAssertions;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using NUnit.Framework;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DietPlanUpdatedInAppNotificationCommand = LgymApi.Application.Nutrition.Contracts.BackgroundCommands.DietPlanUpdatedInAppNotificationCommand;
using ReportFeedbackAddedInAppNotificationCommand = LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportFeedbackAddedInAppNotificationCommand;
using ReportRequestCreatedInAppNotificationCommand = LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportRequestCreatedInAppNotificationCommand;
using ReportSubmissionCreatedInAppNotificationCommand = LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportSubmissionCreatedInAppNotificationCommand;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ReportingNutritionCommandContractTests
{
    [Test]
    public void Commands_HaveExactApplicationOwnedPublicSurfaceAndDefaults()
    {
        AssertCommandContract<ReportRequestCreatedInAppNotificationCommand>(
            "LgymApi.Application.Reporting.Contracts.BackgroundCommands",
            ("RequestId", typeof(Id<ReportRequest>)),
            ("TraineeId", typeof(Id<AccountReference>)),
            ("TrainerId", typeof(Id<AccountReference>)),
            ("TemplateName", typeof(string)));
        AssertCommandContract<ReportSubmissionCreatedInAppNotificationCommand>(
            "LgymApi.Application.Reporting.Contracts.BackgroundCommands",
            ("SubmissionId", typeof(Id<ReportSubmission>)),
            ("TrainerId", typeof(Id<AccountReference>)),
            ("TraineeId", typeof(Id<AccountReference>)),
            ("TemplateName", typeof(string)));
        AssertCommandContract<ReportFeedbackAddedInAppNotificationCommand>(
            "LgymApi.Application.Reporting.Contracts.BackgroundCommands",
            ("SubmissionId", typeof(Id<ReportSubmission>)),
            ("TraineeId", typeof(Id<AccountReference>)),
            ("TrainerId", typeof(Id<AccountReference>)),
            ("TemplateName", typeof(string)),
            ("TriggeredAt", typeof(DateTimeOffset)));
        AssertCommandContract<DietPlanUpdatedInAppNotificationCommand>(
            "LgymApi.Application.Nutrition.Contracts.BackgroundCommands",
            ("DietPlanId", typeof(Id<DietPlan>)),
            ("TraineeId", typeof(Id<User>)),
            ("TrainerId", typeof(Id<User>)),
            ("DietPlanName", typeof(string)),
            ("TriggeredAt", typeof(DateTimeOffset)));

        Assert.Multiple(() =>
        {
            new ReportRequestCreatedInAppNotificationCommand().TemplateName.Should().BeEmpty();
            new ReportSubmissionCreatedInAppNotificationCommand().TemplateName.Should().BeEmpty();
            new ReportFeedbackAddedInAppNotificationCommand().TemplateName.Should().BeEmpty();
            new ReportFeedbackAddedInAppNotificationCommand().TriggeredAt.Should().Be(default);
            new DietPlanUpdatedInAppNotificationCommand().DietPlanName.Should().BeEmpty();
            new DietPlanUpdatedInAppNotificationCommand().TriggeredAt.Should().Be(default);
        });
    }

    [Test]
    public void Commands_SerializeToExactLegacyCompatibleGoldenJson()
    {
        var reportRequest = new ReportRequestCreatedInAppNotificationCommand
        {
            RequestId = ParseId<ReportRequest>("00000000-0000-0000-0000-000000000101"),
            TraineeId = ParseId<AccountReference>("00000000-0000-0000-0000-000000000102"),
            TrainerId = ParseId<AccountReference>("00000000-0000-0000-0000-000000000103"),
            TemplateName = "Weekly request"
        };
        var reportSubmission = new ReportSubmissionCreatedInAppNotificationCommand
        {
            SubmissionId = ParseId<ReportSubmission>("00000000-0000-0000-0000-000000000201"),
            TrainerId = ParseId<AccountReference>("00000000-0000-0000-0000-000000000202"),
            TraineeId = ParseId<AccountReference>("00000000-0000-0000-0000-000000000203"),
            TemplateName = "Weekly submission"
        };
        var feedbackTriggeredAt = new DateTimeOffset(2026, 7, 18, 14, 15, 16, TimeSpan.FromHours(2))
            .AddTicks(1_234_567);
        var reportFeedback = new ReportFeedbackAddedInAppNotificationCommand
        {
            SubmissionId = ParseId<ReportSubmission>("00000000-0000-0000-0000-000000000301"),
            TraineeId = ParseId<AccountReference>("00000000-0000-0000-0000-000000000302"),
            TrainerId = ParseId<AccountReference>("00000000-0000-0000-0000-000000000303"),
            TemplateName = "Coach feedback",
            TriggeredAt = feedbackTriggeredAt
        };
        var dietPlan = new DietPlanUpdatedInAppNotificationCommand
        {
            DietPlanId = ParseId<DietPlan>("00000000-0000-0000-0000-000000000401"),
            TraineeId = ParseId<User>("00000000-0000-0000-0000-000000000402"),
            TrainerId = ParseId<User>("00000000-0000-0000-0000-000000000403"),
            DietPlanName = "Competition plan",
            TriggeredAt = new DateTimeOffset(2026, 7, 18, 8, 9, 10, TimeSpan.FromHours(-4))
        };

        Assert.Multiple(() =>
        {
            JsonSerializer.Serialize(reportRequest, SharedSerializationOptions.Current).Should().Be(
                "{\"requestId\":\"00000000-0000-0000-0000-000000000101\",\"traineeId\":\"00000000-0000-0000-0000-000000000102\",\"trainerId\":\"00000000-0000-0000-0000-000000000103\",\"templateName\":\"Weekly request\"}");
            JsonSerializer.Serialize(reportSubmission, SharedSerializationOptions.Current).Should().Be(
                "{\"submissionId\":\"00000000-0000-0000-0000-000000000201\",\"trainerId\":\"00000000-0000-0000-0000-000000000202\",\"traineeId\":\"00000000-0000-0000-0000-000000000203\",\"templateName\":\"Weekly submission\"}");
            JsonSerializer.Serialize(reportFeedback, SharedSerializationOptions.Current).Should().Be(
                "{\"submissionId\":\"00000000-0000-0000-0000-000000000301\",\"traineeId\":\"00000000-0000-0000-0000-000000000302\",\"trainerId\":\"00000000-0000-0000-0000-000000000303\",\"templateName\":\"Coach feedback\",\"triggeredAt\":\"2026-07-18T14:15:16.1234567+02:00\"}");
            JsonSerializer.Serialize(dietPlan, SharedSerializationOptions.Current).Should().Be(
                "{\"dietPlanId\":\"00000000-0000-0000-0000-000000000401\",\"traineeId\":\"00000000-0000-0000-0000-000000000402\",\"trainerId\":\"00000000-0000-0000-0000-000000000403\",\"dietPlanName\":\"Competition plan\",\"triggeredAt\":\"2026-07-18T08:09:10-04:00\"}");
        });
    }

    [Test]
    public void Commands_ExposeTypedIdsWithoutForeignEntitiesRepositoriesOrWorkerTypes()
    {
        var commandTypes = new[]
        {
            typeof(ReportRequestCreatedInAppNotificationCommand),
            typeof(ReportSubmissionCreatedInAppNotificationCommand),
            typeof(ReportFeedbackAddedInAppNotificationCommand),
            typeof(DietPlanUpdatedInAppNotificationCommand)
        };

        var exposedTypes = commandTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.Multiple(() =>
        {
            exposedTypes.Where(type => type.IsGenericType)
                .All(type => type.GetGenericTypeDefinition() == typeof(Id<>))
                .Should().BeTrue();
            exposedTypes.Any(type =>
            {
                var typeNamespace = type.Namespace;
                return typeNamespace == "LgymApi.Domain.Entities"
                    || typeNamespace?.StartsWith("LgymApi.Application.Repositories", StringComparison.Ordinal) == true
                    || typeNamespace?.StartsWith("LgymApi.BackgroundWorker", StringComparison.Ordinal) == true;
            }).Should().BeFalse();
        });
    }

    private static void AssertCommandContract<TCommand>(
        string expectedNamespace,
        params (string Name, Type Type)[] expectedProperties)
    {
        var commandType = typeof(TCommand);
        var properties = commandType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(property => property.MetadataToken)
            .ToArray();
        var nullabilityContext = new NullabilityInfoContext();

        Assert.Multiple(() =>
        {
            commandType.Assembly.GetName().Name.Should().Be("LgymApi.Application");
            commandType.Namespace.Should().Be(expectedNamespace);
            commandType.IsPublic.Should().BeTrue();
            commandType.IsSealed.Should().BeTrue();
            commandType.IsClass.Should().BeTrue();
            commandType.IsGenericType.Should().BeFalse();
            commandType.GetInterfaces().Should().Equal(typeof(IActionCommand));
            properties.Select(property => (property.Name, property.PropertyType))
                .Should().Equal(expectedProperties);
            properties.All(property =>
                    property.GetMethod is { IsPublic: true }
                    && property.SetMethod is { IsPublic: true }
                    && property.SetMethod.ReturnParameter
                        .GetRequiredCustomModifiers()
                        .Contains(typeof(IsExternalInit)))
                .Should().BeTrue();
            properties.All(property =>
                    nullabilityContext.Create(property).ReadState == NullabilityState.NotNull)
                .Should().BeTrue();
        });
    }

    private static Id<TEntity> ParseId<TEntity>(string value)
        where TEntity : class
    {
        Id<TEntity>.TryParse(value, out var id).Should().BeTrue();
        return id;
    }
}
