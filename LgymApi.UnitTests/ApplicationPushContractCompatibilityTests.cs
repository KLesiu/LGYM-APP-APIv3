using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using ApplicationPushBackgroundScheduler = LgymApi.Application.Notifications.Contracts.Push.IPushBackgroundScheduler;
using ApplicationPushDeliveryRetrySettings = LgymApi.Application.Notifications.Contracts.Push.IPushNotificationDeliveryRetrySettings;
using ApplicationPushEventPayload = LgymApi.Application.Notifications.Contracts.Push.PushEventPayload;
using ApplicationPushProviderSender = LgymApi.Application.Notifications.Contracts.Push.IPushProviderSender;
using ApplicationPushSendAttemptResult = LgymApi.Application.Notifications.Contracts.Push.PushSendAttemptResult;
using ApplicationPushSendOutcome = LgymApi.Application.Notifications.Contracts.Push.PushSendOutcome;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ApplicationPushContractCompatibilityTests
{
    private const string PushContractsNamespace = "LgymApi.Application.Notifications.Contracts.Push";
    private const string NotificationIdText = "01234567-89ab-cdef-0123-456789abcdef";
    private const string ExpectedPayloadJson = "{\"schemaVersion\":1,\"type\":\"trainer.note.updated\",\"eventId\":\"event-contract-1\",\"entityId\":\"entity-contract-1\",\"inAppNotificationId\":\"01234567-89ab-cdef-0123-456789abcdef\",\"deeplink\":\"/notifications/event-contract-1\"}";

    private static readonly string[] PayloadMemberNames =
    [
        nameof(ApplicationPushEventPayload.SchemaVersion),
        nameof(ApplicationPushEventPayload.Type),
        nameof(ApplicationPushEventPayload.EventId),
        nameof(ApplicationPushEventPayload.EntityId),
        nameof(ApplicationPushEventPayload.InAppNotificationId),
        nameof(ApplicationPushEventPayload.Deeplink)
    ];

    private static readonly string[] ResultMemberNames =
    [
        nameof(ApplicationPushSendAttemptResult.Outcome),
        nameof(ApplicationPushSendAttemptResult.ProviderStatus),
        nameof(ApplicationPushSendAttemptResult.ProviderMessageId),
        nameof(ApplicationPushSendAttemptResult.ProviderErrorCode),
        nameof(ApplicationPushSendAttemptResult.ProviderResponseSummary)
    ];

    [Test]
    public void ApplicationPushContracts_ExposeOnlyTheExactNotificationsOwnedTypes()
    {
        var notificationsAssembly = typeof(ApplicationPushEventPayload).Assembly;
        var contractTypes = notificationsAssembly
            .GetExportedTypes()
            .Where(type => type.Namespace == PushContractsNamespace)
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();

        contractTypes.Select(type => type.FullName).Should().Equal(
            $"{PushContractsNamespace}.IPushBackgroundScheduler",
            $"{PushContractsNamespace}.IPushNotificationDeliveryRetrySettings",
            $"{PushContractsNamespace}.IPushProviderSender",
            $"{PushContractsNamespace}.PushEventPayload",
            $"{PushContractsNamespace}.PushSendAttemptResult",
            $"{PushContractsNamespace}.PushSendOutcome");
        contractTypes.Should().OnlyContain(type => type.Assembly == notificationsAssembly && type.IsPublic);
    }

    [Test]
    public void ApplicationPushScheduler_KeepsExactPublicMethodSignatures()
    {
        AssertInterfaceShape(typeof(ApplicationPushBackgroundScheduler), expectedMethodCount: 2);
        var schedulerMethods = typeof(ApplicationPushBackgroundScheduler)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(method => method.MetadataToken)
            .ToArray();

        AssertMethod(
            schedulerMethods[0],
            "Enqueue",
            typeof(string),
            ("notificationId", typeof(string), false));
        AssertMethod(
            schedulerMethods[1],
            "ScheduleRetry",
            typeof(string),
            ("notificationId", typeof(string), false),
            ("delay", typeof(TimeSpan), false));

        var nullability = new NullabilityInfoContext();
        nullability.Create(schedulerMethods[0].ReturnParameter).ReadState.Should().Be(NullabilityState.Nullable);
        nullability.Create(schedulerMethods[1].ReturnParameter).ReadState.Should().Be(NullabilityState.Nullable);
    }

    [Test]
    public void ApplicationPushProviderPort_AndRetrySettings_RemainProviderNeutral()
    {
        AssertInterfaceShape(typeof(ApplicationPushProviderSender), expectedMethodCount: 1);
        var sendMethod = typeof(ApplicationPushProviderSender).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Single();
        AssertMethod(
            sendMethod,
            "SendAsync",
            typeof(Task<ApplicationPushSendAttemptResult>),
            ("installationId", typeof(Id<PushInstallation>), false),
            ("payload", typeof(ApplicationPushEventPayload), false),
            ("cancellationToken", typeof(CancellationToken), true));

        var retrySettingsType = typeof(ApplicationPushDeliveryRetrySettings);
        retrySettingsType.IsPublic.Should().BeTrue();
        retrySettingsType.IsInterface.Should().BeTrue();
        retrySettingsType.IsGenericType.Should().BeFalse();
        retrySettingsType.GetInterfaces().Should().BeEmpty();
        retrySettingsType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .Should().Equal("get_RetryDelaysSeconds");
        retrySettingsType.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Should().BeEmpty();
        var retryDelays = retrySettingsType
            .GetProperty(nameof(ApplicationPushDeliveryRetrySettings.RetryDelaysSeconds));
        retryDelays.Should().NotBeNull();
        retryDelays!.PropertyType.Should().Be(typeof(IReadOnlyList<int>));
        retrySettingsType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .Should().Equal(nameof(ApplicationPushDeliveryRetrySettings.RetryDelaysSeconds));
    }

    [Test]
    public void ApplicationPushEventPayload_PreservesMemberOrderTypedIdBoundaryAndGoldenJson()
    {
        Id<InAppNotification>.TryParse(NotificationIdText, out var notificationId).Should().BeTrue();
        var applicationPayload = new ApplicationPushEventPayload(
            1,
            "trainer.note.updated",
            "event-contract-1",
            "entity-contract-1",
            notificationId,
            "/notifications/event-contract-1");

        AssertRecordShape<ApplicationPushEventPayload>(
            PayloadMemberNames,
            typeof(int),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(Id<InAppNotification>?),
            typeof(string));
        AssertPropertyNullability<ApplicationPushEventPayload>(
            (nameof(ApplicationPushEventPayload.Type), NullabilityState.NotNull),
            (nameof(ApplicationPushEventPayload.EventId), NullabilityState.NotNull),
            (nameof(ApplicationPushEventPayload.EntityId), NullabilityState.Nullable),
            (nameof(ApplicationPushEventPayload.InAppNotificationId), NullabilityState.Nullable),
            (nameof(ApplicationPushEventPayload.Deeplink), NullabilityState.Nullable));

        var applicationJson = JsonSerializer.Serialize(applicationPayload, SharedSerializationOptions.Current);

        applicationJson.Should().Be(ExpectedPayloadJson);
        JsonSerializer.Deserialize<ApplicationPushEventPayload>(ExpectedPayloadJson, SharedSerializationOptions.Current)
            .Should().Be(applicationPayload);
    }

    [Test]
    public void ApplicationPushSendAttemptResult_PreservesMemberOrderAndOutcomeValues()
    {
        AssertRecordShape<ApplicationPushSendAttemptResult>(
            ResultMemberNames,
            typeof(ApplicationPushSendOutcome),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(string));
        AssertPropertyNullability<ApplicationPushSendAttemptResult>(
            (nameof(ApplicationPushSendAttemptResult.ProviderStatus), NullabilityState.NotNull),
            (nameof(ApplicationPushSendAttemptResult.ProviderMessageId), NullabilityState.Nullable),
            (nameof(ApplicationPushSendAttemptResult.ProviderErrorCode), NullabilityState.Nullable),
            (nameof(ApplicationPushSendAttemptResult.ProviderResponseSummary), NullabilityState.Nullable));

        Enum.GetNames<ApplicationPushSendOutcome>().Should().Equal(
            nameof(ApplicationPushSendOutcome.Sent),
            nameof(ApplicationPushSendOutcome.TransientFailure),
            nameof(ApplicationPushSendOutcome.InvalidToken),
            nameof(ApplicationPushSendOutcome.PermanentFailure),
            nameof(ApplicationPushSendOutcome.Skipped));
        Enum.GetValues<ApplicationPushSendOutcome>().Select(outcome => (int)outcome).Should().Equal(0, 1, 2, 3, 4);
    }

    private static void AssertInterfaceShape(Type type, int expectedMethodCount)
    {
        type.IsPublic.Should().BeTrue();
        type.IsInterface.Should().BeTrue();
        type.IsGenericType.Should().BeFalse();
        type.GetInterfaces().Should().BeEmpty();
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Should().BeEmpty();
        type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Should().BeEmpty();
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Should().HaveCount(expectedMethodCount);
    }

    private static void AssertMethod(
        MethodInfo method,
        string expectedName,
        Type expectedReturnType,
        params (string Name, Type Type, bool IsOptional)[] expectedParameters)
    {
        method.Name.Should().Be(expectedName);
        method.ReturnType.Should().Be(expectedReturnType);
        method.IsGenericMethod.Should().BeFalse();

        var parameters = method.GetParameters();
        parameters.Select(parameter => parameter.Name).Should().Equal(expectedParameters.Select(parameter => parameter.Name));
        parameters.Select(parameter => parameter.ParameterType).Should().Equal(expectedParameters.Select(parameter => parameter.Type));
        parameters.Select(parameter => parameter.IsOptional).Should().Equal(expectedParameters.Select(parameter => parameter.IsOptional));
    }

    private static void AssertRecordShape<T>(string[] expectedMemberNames, params Type[] expectedMemberTypes)
    {
        var type = typeof(T);
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(property => property.MetadataToken)
            .ToArray();
        var constructor = type.GetConstructors().Should().ContainSingle().Subject;

        type.IsPublic.Should().BeTrue();
        type.IsSealed.Should().BeTrue();
        properties.Select(property => property.Name).Should().Equal(expectedMemberNames);
        properties.Select(property => property.PropertyType).Should().Equal(expectedMemberTypes);
        constructor.GetParameters().Select(parameter => parameter.Name).Should().Equal(expectedMemberNames);
        constructor.GetParameters().Select(parameter => parameter.ParameterType).Should().Equal(expectedMemberTypes);
    }

    private static void AssertPropertyNullability<T>(params (string Name, NullabilityState State)[] expectedProperties)
    {
        var nullability = new NullabilityInfoContext();
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach (var expectedProperty in expectedProperties)
        {
            var property = properties.Single(candidate => candidate.Name == expectedProperty.Name);
            nullability.Create(property).ReadState.Should().Be(expectedProperty.State);
        }
    }
}
