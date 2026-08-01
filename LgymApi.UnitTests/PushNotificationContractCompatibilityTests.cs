using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.Application.Notifications.Models;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class PushNotificationContractCompatibilityTests
{
    private static readonly string[] PayloadFieldNames =
    [
        nameof(PushEventPayload.SchemaVersion),
        nameof(PushEventPayload.Type),
        nameof(PushEventPayload.EventId),
        nameof(PushEventPayload.EntityId),
        nameof(PushEventPayload.InAppNotificationId),
        nameof(PushEventPayload.Deeplink)
    ];

    private static readonly string[] PayloadJsonFieldNames =
    [
        "schemaVersion",
        "type",
        "eventId",
        "entityId",
        "inAppNotificationId",
        "deeplink"
    ];

    private static readonly string[] SendResultFieldNames =
    [
        nameof(PushSendAttemptResult.Outcome),
        nameof(PushSendAttemptResult.ProviderStatus),
        nameof(PushSendAttemptResult.ProviderMessageId),
        nameof(PushSendAttemptResult.ProviderErrorCode),
        nameof(PushSendAttemptResult.ProviderResponseSummary)
    ];

    private const int WireSchemaVersion = 1;
    private const string WireType = "trainer.note.updated";
    private const string WireEventId = "event-contract-1";
    private const string WireEntityId = "entity-contract-1";
    private const string WireInAppNotificationId = "01234567-89ab-cdef-0123-456789abcdef";
    private const string WireDeeplink = "/notifications/event-contract-1";
    private const string ExpectedWirePayloadJson = "{\"schemaVersion\":1,\"type\":\"trainer.note.updated\",\"eventId\":\"event-contract-1\",\"entityId\":\"entity-contract-1\",\"inAppNotificationId\":\"01234567-89ab-cdef-0123-456789abcdef\",\"deeplink\":\"/notifications/event-contract-1\"}";
    private const string ExpectedMetadataOnlyPayloadJson = "{\"schemaVersion\":1,\"type\":\"trainer.note.updated\",\"eventId\":\"event-contract-1\"}";

    private const int PersistedSchemaVersion = 7;
    private const string PersistedType = "trainer.note.updated";
    private const string PersistedEventId = "event-contract-2";
    private const string PersistedEntityId = "entity-contract-2";
    private const string PersistedInAppNotificationId = "fedcba98-7654-3210-fedc-ba9876543210";
    private const string PersistedDeeplink = "/notifications/event-contract-2";
    private const string ExpectedPersistedPayloadJson = "{\"schemaVersion\":7,\"type\":\"trainer.note.updated\",\"eventId\":\"event-contract-2\",\"entityId\":\"entity-contract-2\",\"inAppNotificationId\":\"fedcba98-7654-3210-fedc-ba9876543210\",\"deeplink\":\"/notifications/event-contract-2\"}";

    [Test]
    public void PushEventPayload_FreezesFieldNamesOrderAndLegacyTypedIdJson()
    {
        var notificationId = new Id<InAppNotification>(new Guid(WireInAppNotificationId));
        var payload = new PushEventPayload(
            WireSchemaVersion,
            WireType,
            WireEventId,
            WireEntityId,
            notificationId,
            WireDeeplink);

        GetOrderedPropertyNames<PushEventPayload>().Should().Equal(PayloadFieldNames);
        GetPrimaryConstructorParameterNames<PushEventPayload>().Should().Equal(PayloadFieldNames);

        var json = JsonSerializer.Serialize(payload, SharedSerializationOptions.Current);
        json.Should().Be(ExpectedWirePayloadJson);
        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal(PayloadJsonFieldNames);

        var deserializedLegacyPayload = JsonSerializer.Deserialize<PushEventPayload>(ExpectedWirePayloadJson, SharedSerializationOptions.Current);
        deserializedLegacyPayload.Should().Be(payload);
    }

    [Test]
    public void PushEventPayload_OmitsNullFieldsAndRetainsOnlyPrivacySafeMetadata()
    {
        var payload = new PushEventPayload(
            WireSchemaVersion,
            WireType,
            WireEventId,
            null,
            null,
            null);

        var json = JsonSerializer.Serialize(payload, SharedSerializationOptions.Current);

        json.Should().Be(ExpectedMetadataOnlyPayloadJson);
        JsonSerializer.Deserialize<PushEventPayload>(json, SharedSerializationOptions.Current).Should().Be(payload);

        using var document = JsonDocument.Parse(json);
        var serializedFieldNames = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        serializedFieldNames.Should().Equal("schemaVersion", "type", "eventId");
        new[] { "message", "title", "body", "fcmToken", "deviceToken", "providerMessageId" }
            .Any(forbiddenField => PayloadJsonFieldNames.Contains(forbiddenField, StringComparer.Ordinal))
            .Should().BeFalse();
    }

    [Test]
    public void PushSendAttemptResult_FreezesFieldsAndOutcomeNumericValues()
    {
        var result = new PushSendAttemptResult(
            PushSendOutcome.InvalidToken,
            "BadRequest",
            "provider-message-1",
            "UNREGISTERED",
            "registration-token-not-registered");

        GetOrderedPropertyNames<PushSendAttemptResult>().Should().Equal(SendResultFieldNames);
        GetPrimaryConstructorParameterNames<PushSendAttemptResult>().Should().Equal(SendResultFieldNames);
        Enum.GetNames<PushSendOutcome>().Should().Equal(
            nameof(PushSendOutcome.Sent),
            nameof(PushSendOutcome.TransientFailure),
            nameof(PushSendOutcome.InvalidToken),
            nameof(PushSendOutcome.PermanentFailure),
            nameof(PushSendOutcome.Skipped));
        Enum.GetValues<PushSendOutcome>().Select(outcome => (int)outcome).Should().Equal(0, 1, 2, 3, 4);

        result.Outcome.Should().Be(PushSendOutcome.InvalidToken);
        result.ProviderStatus.Should().Be("BadRequest");
        result.ProviderMessageId.Should().Be("provider-message-1");
        result.ProviderErrorCode.Should().Be("UNREGISTERED");
        result.ProviderResponseSummary.Should().Be("registration-token-not-registered");
    }

    [Test]
    public async Task EnqueueAsync_PersistsSerializedPayloadEqualToDenormalizedMessageColumns()
    {
        await using var db = CreateDbContext();
        var userId = Id<User>.New();
        var notificationId = new Id<InAppNotification>(new Guid(PersistedInAppNotificationId));
        var installation = new PushInstallation
        {
            Id = Id<PushInstallation>.New(),
            UserId = userId,
            InstallationId = "push-contract-installation",
            Platform = "android",
            FcmToken = "push-contract-token",
            Environment = "development",
            PermissionStatus = "authorized",
            LastSeenAt = DateTimeOffset.UtcNow
        };
        db.PushInstallations.Add(installation);
        await db.SaveChangesAsync();

        var scheduler = new RecordingPushBackgroundScheduler();
        var service = new PushNotificationService(
            new PushInstallationRepository(db),
            new PushNotificationMessageRepository(db),
            scheduler,
            new EfUnitOfWork(db),
            NullLogger<PushNotificationService>.Instance);

        await service.EnqueueAsync(new EnqueuePushNotificationInput(
            userId,
            PersistedSchemaVersion,
            $" {PersistedType} ",
            $" {PersistedEventId} ",
            $" {PersistedEntityId} ",
            notificationId,
            $" {PersistedDeeplink} "));

        var row = db.PushNotificationMessages.Single();

        row.PayloadJson.Should().Be(ExpectedPersistedPayloadJson);
        row.SchemaVersion.Should().Be(PersistedSchemaVersion);
        row.Type.Should().Be(PersistedType);
        row.EventId.Should().Be(PersistedEventId);
        row.EntityId.Should().Be(PersistedEntityId);
        row.InAppNotificationId.Should().Be(new Id<InAppNotification>(new Guid(PersistedInAppNotificationId)));
        row.Deeplink.Should().Be(PersistedDeeplink);
        scheduler.EnqueuedNotificationIds.Should().ContainSingle().Which.Should().Be(row.Id);
    }

    private static AppDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"push-contract-compatibility-{Id<PushNotificationContractCompatibilityTests>.New():N}")
            .Options);

    private static string[] GetOrderedPropertyNames<T>()
        => typeof(T)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .OrderBy(property => property.MetadataToken)
            .Select(property => property.Name)
            .ToArray();

    private static string[] GetPrimaryConstructorParameterNames<T>()
    {
        var parameterNames = typeof(T)
            .GetConstructors()
            .Should()
            .ContainSingle()
            .Which
            .GetParameters()
            .Select(parameter => parameter.Name)
            .ToArray();

        parameterNames.Should().NotContainNulls();
        return parameterNames.Select(parameterName => parameterName!).ToArray();
    }

    private sealed class RecordingPushBackgroundScheduler : IPushBackgroundScheduler
    {
        public List<Id<PushNotificationMessage>> EnqueuedNotificationIds { get; } = [];

        public string? Enqueue(string notificationId)
        {
            if (!Id<PushNotificationMessage>.TryParse(notificationId, out var parsedNotificationId))
            {
                throw new FormatException("Notification ID must be a valid ID.");
            }

            EnqueuedNotificationIds.Add(parsedNotificationId);
            return "push-contract-job";
        }

        public string? ScheduleRetry(string notificationId, TimeSpan delay) => "push-contract-retry-job";
    }
}
