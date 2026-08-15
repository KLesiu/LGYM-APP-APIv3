using System.Reflection;
using FluentAssertions;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Repositories;
using LgymApi.Application.Notifications.Providers.Fcm;
using LgymApi.Application.Repositories;
using LgymApi.BackgroundWorker.Common.Jobs;
using LgymApi.Domain.Entities;
using LgymApi.TestUtils;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class PushNotificationRetentionContractTests
{
    [Test]
    public void PushInstallationRepository_WhenSelectingStaleActiveCandidates_UsesCallerSuppliedCutoffAndLimit()
    {
        var method = typeof(IPushInstallationRepository).GetMethod(
            nameof(IPushInstallationRepository.GetStaleActiveAsync),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<List<PushInstallation>>));
        method.GetParameters().Select(parameter => parameter.ParameterType).Should().Equal(
            typeof(DateTimeOffset),
            typeof(int),
            typeof(CancellationToken));
    }

    [Test]
    public void RetentionRepositories_WhenSelectingAndDeletingCandidates_DeclareTimestampSpecificBoundedStageOnlyPorts()
    {
        AssertRetentionCandidateContract<IPushNotificationMessageRepository, PushNotificationMessage>(
            "GetRetentionCandidatesCreatedBeforeAsync");
        AssertRetentionCandidateContract<IPushInstallationRepository, PushInstallation>(
            "GetRetentionCandidatesDisabledBeforeAsync");
        AssertRetentionCandidateContract<IInAppNotificationRepository, InAppNotification>(
            "GetRetentionCandidatesCreatedBeforeAsync");
    }

    [Test]
    public void PushNotificationOptionsFactory_WhenExistingValuesAreMissingOrInvalid_UsesEstablishedDefaultsAndNormalization()
    {
        var configuration = TestConfigurationBuilder.BuildConfiguration(new Dictionary<string, string?>
        {
            ["PushNotifications:RetryDelaysSeconds:0"] = "0",
            ["PushNotifications:RetryDelaysSeconds:1"] = "-5",
            ["PushNotifications:StaleTokenInactivityDays"] = "0",
            ["PushNotifications:StaleTokenCleanupBatchSize"] = "-1",
            ["PushNotifications:Fcm:ProjectId"] = " project-id ",
            ["PushNotifications:Fcm:CredentialsPath"] = " path.json ",
            ["PushNotifications:Fcm:CredentialsJson"] = "  ",
            ["PushNotifications:Fcm:BaseUrl"] = " https://fcm.example.test/ "
        });

        var options = PushNotificationOptionsFactory.Create(configuration);

        options.IsSendEnabled.Should().BeFalse();
        options.RetryDelaysSeconds.Should().Equal(30, 120, 600);
        options.StaleTokenInactivityDays.Should().Be(45);
        options.StaleTokenCleanupBatchSize.Should().Be(500);
        options.Fcm.ProjectId.Should().Be("project-id");
        options.Fcm.CredentialsPath.Should().Be("path.json");
        options.Fcm.CredentialsJson.Should().BeNull();
        options.Fcm.BaseUrl.Should().Be("https://fcm.example.test");
    }

    [Test]
    public void PushNotificationOptionsFactory_WhenRetentionValuesAreMissing_UsesApprovedDefaults()
    {
        var options = PushNotificationOptionsFactory.Create(
            TestConfigurationBuilder.BuildConfiguration(new Dictionary<string, string?>()));

        options.MessageHistoryDays.Should().Be(30);
        options.DisabledInstallationDays.Should().Be(30);
        options.InAppNotificationDays.Should().Be(90);
        options.RetentionPurgeBatchSize.Should().Be(500);
    }

    [Test]
    public void PushNotificationOptionsFactory_WhenRetentionValuesAreInvalid_NormalizesPositiveValuesAndRejectsNonNumericValues()
    {
        var configuration = TestConfigurationBuilder.BuildConfiguration(new Dictionary<string, string?>
        {
            ["PushNotifications:MessageHistoryDays"] = "0",
            ["PushNotifications:DisabledInstallationDays"] = "-1",
            ["PushNotifications:InAppNotificationDays"] = "0",
            ["PushNotifications:RetentionPurgeBatchSize"] = "-1"
        });

        var options = PushNotificationOptionsFactory.Create(configuration);
        options.MessageHistoryDays.Should().Be(30);
        options.DisabledInstallationDays.Should().Be(30);
        options.InAppNotificationDays.Should().Be(90);
        options.RetentionPurgeBatchSize.Should().Be(500);

        var invalidConfiguration = TestConfigurationBuilder.BuildConfiguration(new Dictionary<string, string?>
        {
            ["PushNotifications:MessageHistoryDays"] = "not-an-integer"
        });

        var action = () => PushNotificationOptionsFactory.Create(invalidConfiguration);

        action.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void NotificationRetentionSettings_ProjectsConfiguredValuesAndRetentionJobsRemainNarrow()
    {
        var options = PushNotificationOptionsFactory.Create(
            TestConfigurationBuilder.BuildConfiguration(new Dictionary<string, string?>
            {
                ["PushNotifications:MessageHistoryDays"] = "31",
                ["PushNotifications:DisabledInstallationDays"] = "32",
                ["PushNotifications:InAppNotificationDays"] = "91",
                ["PushNotifications:RetentionPurgeBatchSize"] = "250"
            }));
        INotificationRetentionSettings settings = new NotificationRetentionSettings(options);

        settings.MessageHistoryDays.Should().Be(31);
        settings.DisabledInstallationDays.Should().Be(32);
        settings.InAppNotificationDays.Should().Be(91);
        settings.BatchSize.Should().Be(250);

        AssertCleanupContract<IPushNotificationMessageRetentionCleanupService>();
        AssertCleanupContract<IDisabledPushInstallationRetentionCleanupService>();
        AssertCleanupContract<IInAppNotificationRetentionCleanupService>();
        AssertJobContract<IPushNotificationMessageRetentionCleanupJob>();
        AssertJobContract<IDisabledPushInstallationRetentionCleanupJob>();
        AssertJobContract<IInAppNotificationRetentionCleanupJob>();
    }

    private static void AssertCleanupContract<TCleanupService>()
    {
        typeof(TCleanupService).GetMethod("CleanupAsync")!.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Should()
            .Equal(typeof(CancellationToken));
    }

    private static void AssertJobContract<TJob>()
    {
        typeof(TJob).GetMethod("ExecuteAsync")!.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Should()
            .Equal(typeof(CancellationToken));
    }

    private static void AssertRetentionCandidateContract<TRepository, TCandidate>(string selectionMethodName)
    {
        var repositoryType = typeof(TRepository);
        var selection = repositoryType.GetMethod(
            selectionMethodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var deletion = repositoryType.GetMethod(
            "RemoveRange",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        selection.Should().NotBeNull();
        selection!.ReturnType.Should().Be(typeof(Task<IReadOnlyList<TCandidate>>));
        selection.GetParameters().Select(parameter => (parameter.Name, parameter.ParameterType)).Should().Equal(
            ("cutoff", typeof(DateTimeOffset)),
            ("candidateLimit", typeof(int)),
            ("cancellationToken", typeof(CancellationToken)));

        deletion.Should().NotBeNull();
        deletion!.ReturnType.Should().Be(typeof(void));
        deletion.GetParameters().Select(parameter => parameter.ParameterType).Should().Equal(
            typeof(IEnumerable<TCandidate>));
    }
}
