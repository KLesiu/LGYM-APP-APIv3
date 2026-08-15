using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class NotificationRetentionSchemaTests
{
    [Test]
    public void NotificationRetention_Model_ProvidesCutoffIndexesWithoutChangingExistingIndexesOrForeignKeys()
    {
        using var database = CreateDatabase();
        var inAppNotification = GetEntity(database, typeof(InAppNotification));
        var pushInstallation = GetEntity(database, typeof(PushInstallation));
        var pushMessage = GetEntity(database, typeof(PushNotificationMessage));

        AssertIndex(inAppNotification, [nameof(InAppNotification.CreatedAt)], false, "IX_in_app_notifications_CreatedAt");
        AssertIndex(inAppNotification, [nameof(InAppNotification.RecipientId), nameof(InAppNotification.CreatedAt), nameof(InAppNotification.Id)], false, "IX_in_app_notifications_RecipientId_CreatedAt_Id");
        AssertIndex(inAppNotification, [nameof(InAppNotification.RecipientId), nameof(InAppNotification.Type), nameof(InAppNotification.DeliveryKey)], true, "IX_in_app_notifications_RecipientId_Type_DeliveryKey", "\"IsDeleted\" = FALSE AND \"DeliveryKey\" IS NOT NULL");

        AssertIndex(pushInstallation, [nameof(PushInstallation.DisabledAt)], false, "IX_PushInstallations_DisabledAt");
        AssertIndex(pushInstallation, [nameof(PushInstallation.InstallationId)], true, "IX_PushInstallations_InstallationId", "\"IsDeleted\" = FALSE");
        AssertForeignKey(pushInstallation, nameof(PushInstallation.UserId), DeleteBehavior.SetNull);
        AssertForeignKey(pushInstallation, nameof(PushInstallation.SessionId), DeleteBehavior.SetNull);

        AssertIndex(pushMessage, [nameof(PushNotificationMessage.CreatedAt)], false, "IX_PushNotificationMessages_CreatedAt");
        AssertIndex(pushMessage, [nameof(PushNotificationMessage.PushInstallationId), nameof(PushNotificationMessage.Type), nameof(PushNotificationMessage.EventId)], true, "IX_PushNotificationMessages_PushInstallationId_Type_EventId", "\"IsDeleted\" = FALSE");
        AssertIndex(pushMessage, [nameof(PushNotificationMessage.Status), nameof(PushNotificationMessage.NextAttemptAt), nameof(PushNotificationMessage.CreatedAt)], false, "IX_PushNotificationMessages_Status_NextAttemptAt_CreatedAt", "\"IsDeleted\" = FALSE");
        AssertForeignKey(pushMessage, nameof(PushNotificationMessage.UserId), DeleteBehavior.Cascade);
        AssertForeignKey(pushMessage, nameof(PushNotificationMessage.PushInstallationId), DeleteBehavior.Cascade);
        AssertForeignKey(pushMessage, nameof(PushNotificationMessage.InAppNotificationId), DeleteBehavior.SetNull);
    }

    private static void AssertIndex(IEntityType entity, string[] properties, bool unique, string name, string? filter = null)
    {
        var index = entity.GetIndexes().Single(candidate => candidate.Properties.Select(property => property.Name).SequenceEqual(properties));
        index.IsUnique.Should().Be(unique);
        index.GetDatabaseName().Should().Be(name);
        index.GetFilter().Should().Be(filter);
    }

    private static void AssertForeignKey(IEntityType entity, string property, DeleteBehavior deleteBehavior)
    {
        var foreignKey = entity.GetForeignKeys().Single(candidate => candidate.Properties.Select(key => key.Name).SequenceEqual([property]));
        foreignKey.DeleteBehavior.Should().Be(deleteBehavior);
    }

    private static AppDbContext CreateDatabase() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase($"notification-retention-schema-{Id<NotificationRetentionSchemaTests>.New():N}")
        .Options);

    private static IEntityType GetEntity(AppDbContext database, Type type) => database.Model.FindEntityType(type)!;
}
