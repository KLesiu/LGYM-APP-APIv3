using LgymApi.Domain.ValueObjects;
using System.Text.Json;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Domain.Enums;
using NUnit.Framework;
using FluentAssertions;

namespace LgymApi.UnitTests;

/// <summary>
/// Tests roundtrip serialization/deserialization compatibility for email payload contracts.
/// Verifies that:
/// 1. Email payloads serialize and deserialize symmetrically using SharedSerializationOptions
/// 2. Legacy JSON payloads (PascalCase, old field shapes) remain readable (backward compatibility)
/// 3. Nested data structures (TrainingExerciseSummary) survive roundtrip intact
/// 4. Infrastructure composers can consume serialized payload JSON without errors
/// </summary>
[TestFixture]
public sealed class EmailPayloadRoundtripCompatibilityTests
{
    private const string MalformedCultureName = "!invalid-culture-380";

    // Helper to parse fixed test IDs from UUID string literals
    private static Id<T> ParseTestId<T>(string uuid)
    {
        if (!Id<T>.TryParse(uuid, out var id))
        {
            throw new ArgumentException($"Invalid UUID: {uuid}", nameof(uuid));
        }
        return id;
    }

    #region WelcomeEmailPayload Tests

    [Test]
    public void WelcomeEmailPayload_RoundtripsSuccessfully()
    {
        // Arrange
        var userId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User>.New();
        var originalPayload = new WelcomeEmailPayload
        {
            UserId = userId,
            UserName = "John Doe",
            RecipientEmail = "john@example.com",
            CultureName = "en-US"
        };
        var json = JsonSerializer.Serialize(originalPayload, SharedSerializationOptions.Current);

        // Act
        var deserialized = JsonSerializer.Deserialize<WelcomeEmailPayload>(json, SharedSerializationOptions.Current);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.UserId.Should().Be(userId);
        deserialized.UserName.Should().Be("John Doe");
        deserialized.RecipientEmail.Should().Be("john@example.com");
        deserialized.CultureName.Should().Be("en-US");
    }

    [Test]
    public void WelcomeEmailPayload_SerializedJsonUsesCamelCase()
    {
        // Arrange
        var originalPayload = new WelcomeEmailPayload
        {
            UserId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User>.New(),
            UserName = "Jane Doe",
            RecipientEmail = "jane@example.com",
            CultureName = "pl-PL"
        };

        // Act
        var json = JsonSerializer.Serialize(originalPayload, SharedSerializationOptions.Current);

        // Assert
        json.Should().Contain("userId");
        json.Should().Contain("userName");
        json.Should().Contain("recipientEmail");
        json.Should().Contain("cultureName");
        json.Should().NotContain("UserId");
        json.Should().NotContain("UserName");
    }

    [Test]
    public void WelcomeEmailPayload_DeserializesFromLegacyPascalCaseJson()
    {
        // Arrange - simulate legacy payload with PascalCase property names
        const string legacyUserId = "a50e8400-e29b-41d4-a716-446655440001";
        var legacyJson = "{\"UserId\":\"" + legacyUserId + "\"," +
                         "\"UserName\":\"Legacy User\"," +
                         "\"RecipientEmail\":\"legacy@example.com\"," +
                         "\"CultureName\":\"de-DE\"}";

        // Act
        var deserialized = JsonSerializer.Deserialize<WelcomeEmailPayload>(legacyJson, SharedSerializationOptions.Current);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.UserId.Should().Be(ParseTestId<LgymApi.Domain.Entities.User>(legacyUserId));
        deserialized.UserName.Should().Be("Legacy User");
        deserialized.RecipientEmail.Should().Be("legacy@example.com");
        deserialized.CultureName.Should().Be("de-DE");
    }

    [Test]
    public void WelcomeEmailPayload_RoundtripsPreservesAllFields()
    {
        // Arrange
        var userId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User>.New();
        var originalPayload = new WelcomeEmailPayload
        {
            UserId = userId,
            UserName = "Test User",
            RecipientEmail = "test@example.com",
            CultureName = "es-ES"
        };
        var json = JsonSerializer.Serialize(originalPayload, SharedSerializationOptions.Current);

        // Act
        var deserialized = JsonSerializer.Deserialize<WelcomeEmailPayload>(json, SharedSerializationOptions.Current);

        // Assert - each field must match exactly
        deserialized.Should().NotBeNull();
        deserialized!.UserId.Should().Be(userId, "UserId not preserved");
        deserialized.UserName.Should().Be("Test User", "UserName not preserved");
        deserialized.RecipientEmail.Should().Be("test@example.com", "RecipientEmail not preserved");
        deserialized.CultureName.Should().Be("es-ES", "CultureName not preserved");
    }

    [Test]
    public void WelcomeEmailPayload_CorrelationIdDerivesFromUserId()
    {
        // Arrange
        var userId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User>.New();
        var payload = new WelcomeEmailPayload
        {
            UserId = userId,
            UserName = "Corr User",
            RecipientEmail = "corr@example.com",
            CultureName = "fr-FR"
        };

        // Act
        var json = JsonSerializer.Serialize(payload, SharedSerializationOptions.Current);
        var deserialized = JsonSerializer.Deserialize<WelcomeEmailPayload>(json, SharedSerializationOptions.Current);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.CorrelationId.GetValue().Should().Be(userId.GetValue());
    }

    #endregion

    #region InvitationEmailPayload Tests

    [Test]
    public void InvitationEmailPayload_RoundtripsSuccessfully()
    {
        // Arrange
        var invitationId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.TrainerInvitation>.New();
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);
        var originalPayload = new InvitationEmailPayload
        {
            InvitationId = invitationId,
            InvitationCode = "INV12345",
            ExpiresAt = expiresAt,
            TrainerName = "Coach Mike",
            RecipientEmail = "client@example.com",
            CultureName = "en-US",
            PreferredTimeZone = "America/New_York"
        };
        var json = JsonSerializer.Serialize(originalPayload, SharedSerializationOptions.Current);

        // Act
        var deserialized = JsonSerializer.Deserialize<InvitationEmailPayload>(json, SharedSerializationOptions.Current);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.InvitationId.Should().Be(invitationId);
        deserialized.InvitationCode.Should().Be("INV12345");
        deserialized.ExpiresAt.Should().Be(expiresAt);
        deserialized.TrainerName.Should().Be("Coach Mike");
        deserialized.RecipientEmail.Should().Be("client@example.com");
        deserialized.CultureName.Should().Be("en-US");
        deserialized.PreferredTimeZone.Should().Be("America/New_York");
    }

    [Test]
    public void InvitationEmailPayload_SerializedJsonUsesCamelCase()
    {
        // Arrange
        var originalPayload = new InvitationEmailPayload
        {
            InvitationId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.TrainerInvitation>.New(),
            InvitationCode = "TEST123",
            ExpiresAt = DateTimeOffset.UtcNow,
            TrainerName = "Trainer",
            RecipientEmail = "recipient@example.com",
            CultureName = "pl-PL",
            PreferredTimeZone = "Europe/Warsaw"
        };

        // Act
        var json = JsonSerializer.Serialize(originalPayload, SharedSerializationOptions.Current);

        // Assert
        json.Should().Contain("invitationId");
        json.Should().Contain("invitationCode");
        json.Should().Contain("expiresAt");
        json.Should().Contain("trainerName");
        json.Should().Contain("recipientEmail");
        json.Should().Contain("cultureName");
        json.Should().Contain("preferredTimeZone");
        json.Should().NotContain("InvitationId");
        json.Should().NotContain("ExpiresAt");
    }

    [Test]
    public void InvitationEmailPayload_DeserializesFromLegacyPascalCaseJson()
    {
        // Arrange - simulate legacy payload with PascalCase property names
        const string legacyInvitationId = "b50e8400-e29b-41d4-a716-446655440002";
        var expiresAt = DateTimeOffset.UtcNow.AddDays(14);
        var legacyJson = "{\"InvitationId\":\"" + legacyInvitationId + "\"," +
                         "\"InvitationCode\":\"LEGACY123\"," +
                         "\"ExpiresAt\":\"" + expiresAt.ToString("O") + "\"," +
                         "\"TrainerName\":\"Old Trainer\"," +
                         "\"RecipientEmail\":\"oldclient@example.com\"," +
                         "\"CultureName\":\"de-DE\"," +
                         "\"PreferredTimeZone\":\"Europe/Berlin\"}";

        // Act
        var deserialized = JsonSerializer.Deserialize<InvitationEmailPayload>(legacyJson, SharedSerializationOptions.Current);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.InvitationId.Should().Be(ParseTestId<LgymApi.Domain.Entities.TrainerInvitation>(legacyInvitationId));
        deserialized.InvitationCode.Should().Be("LEGACY123");
        deserialized.TrainerName.Should().Be("Old Trainer");
        deserialized.RecipientEmail.Should().Be("oldclient@example.com");
    }

    [Test]
    public void InvitationEmailPayload_RoundtripsPreservesAllFields()
    {
        // Arrange
        var invitationId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.TrainerInvitation>.New();
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);
        var originalPayload = new InvitationEmailPayload
        {
            InvitationId = invitationId,
            InvitationCode = "FULL123",
            ExpiresAt = expiresAt,
            TrainerName = "Full Trainer",
            RecipientEmail = "fullclient@example.com",
            CultureName = "fr-FR",
            PreferredTimeZone = "Europe/Paris"
        };
        var json = JsonSerializer.Serialize(originalPayload, SharedSerializationOptions.Current);

        // Act
        var deserialized = JsonSerializer.Deserialize<InvitationEmailPayload>(json, SharedSerializationOptions.Current);

        // Assert - each field must match exactly
        deserialized.Should().NotBeNull();
        deserialized!.InvitationId.Should().Be(invitationId, "InvitationId not preserved");
        deserialized.InvitationCode.Should().Be("FULL123", "InvitationCode not preserved");
        deserialized.ExpiresAt.Should().Be(expiresAt, "ExpiresAt not preserved");
        deserialized.TrainerName.Should().Be("Full Trainer", "TrainerName not preserved");
        deserialized.RecipientEmail.Should().Be("fullclient@example.com", "RecipientEmail not preserved");
        deserialized.CultureName.Should().Be("fr-FR", "CultureName not preserved");
        deserialized.PreferredTimeZone.Should().Be("Europe/Paris", "PreferredTimeZone not preserved");
    }

    [Test]
    public void InvitationEmailPayload_CorrelationIdDerivesFromInvitationId()
    {
        // Arrange
        var invitationId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.TrainerInvitation>.New();
        var payload = new InvitationEmailPayload
        {
            InvitationId = invitationId,
            InvitationCode = "CORR123",
            ExpiresAt = DateTimeOffset.UtcNow,
            TrainerName = "Corr Trainer",
            RecipientEmail = "corrclient@example.com",
            CultureName = "en-GB",
            PreferredTimeZone = "Europe/London"
        };

        // Act
        var json = JsonSerializer.Serialize(payload, SharedSerializationOptions.Current);
        var deserialized = JsonSerializer.Deserialize<InvitationEmailPayload>(json, SharedSerializationOptions.Current);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.CorrelationId.GetValue().Should().Be(invitationId.GetValue());
    }

    #endregion

    #region TrainingCompletedEmailPayload Tests

    [Test]
    public void TrainingCompletedEmailPayload_RoundtripsSuccessfully()
    {
        // Arrange
        var userId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User>.New();
        var trainingId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.Training>.New();
        var trainingDate = DateTimeOffset.UtcNow;
        var originalPayload = new TrainingCompletedEmailPayload
        {
            UserId = userId,
            TrainingId = trainingId,
            RecipientEmail = "athlete@example.com",
            CultureName = "en-US",
            PreferredTimeZone = "America/Los_Angeles",
            PlanDayName = "Upper Body A",
            TrainingDate = trainingDate,
            Exercises = new List<TrainingExerciseSummary>
            {
                new()
                {
                    ExerciseId = ParseTestId<LgymApi.Domain.Entities.Exercise>("d50e8400-e29b-41d4-a716-446655440001"),
                    ExerciseName = "Bench Press",
                    Series = 1,
                    Reps = 8,
                    Weight = 80,
                    Unit = WeightUnits.Kilograms
                }
            }
        };
        var json = JsonSerializer.Serialize(originalPayload, SharedSerializationOptions.Current);

        // Act
        var deserialized = JsonSerializer.Deserialize<TrainingCompletedEmailPayload>(json, SharedSerializationOptions.Current);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.UserId.Should().Be(userId);
        deserialized.TrainingId.Should().Be(trainingId);
        deserialized.RecipientEmail.Should().Be("athlete@example.com");
        deserialized.CultureName.Should().Be("en-US");
        deserialized.PreferredTimeZone.Should().Be("America/Los_Angeles");
        deserialized.PlanDayName.Should().Be("Upper Body A");
        deserialized.TrainingDate.Should().Be(trainingDate);
    }

    [Test]
    public void TrainingCompletedEmailPayload_NestedExercisesSurviveRoundtrip()
    {
        // Arrange
        var userId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User>.New();
        var trainingId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.Training>.New();
        var originalPayload = new TrainingCompletedEmailPayload
        {
            UserId = userId,
            TrainingId = trainingId,
            RecipientEmail = "athlete@example.com",
            CultureName = "en-US",
            PreferredTimeZone = "America/New_York",
            PlanDayName = "Full Body",
            TrainingDate = DateTimeOffset.UtcNow,
            Exercises = new List<TrainingExerciseSummary>
            {
                new()
                {
                    ExerciseId = ParseTestId<LgymApi.Domain.Entities.Exercise>("d50e8400-e29b-41d4-a716-446655440002"),
                    ExerciseName = "Bench Press",
                    Series = 1,
                    Reps = 8,
                    Weight = 80,
                    Unit = WeightUnits.Kilograms
                },
                new()
                {
                    ExerciseId = ParseTestId<LgymApi.Domain.Entities.Exercise>("d50e8400-e29b-41d4-a716-446655440003"),
                    ExerciseName = "Squat",
                    Series = 2,
                    Reps = 6,
                    Weight = 120,
                    Unit = WeightUnits.Kilograms
                }
            }
        };
        var json = JsonSerializer.Serialize(originalPayload, SharedSerializationOptions.Current);

        // Act
        var deserialized = JsonSerializer.Deserialize<TrainingCompletedEmailPayload>(json, SharedSerializationOptions.Current);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Exercises.Should().NotBeNull();
        deserialized.Exercises.Count.Should().Be(2);

        // Verify first exercise
        var firstExercise = deserialized.Exercises.First();
        firstExercise.ExerciseId.Should().Be(ParseTestId<LgymApi.Domain.Entities.Exercise>("d50e8400-e29b-41d4-a716-446655440002"));
        firstExercise.ExerciseName.Should().Be("Bench Press");
        firstExercise.Series.Should().Be(1);
        firstExercise.Reps.Should().Be(8);
        firstExercise.Weight.Should().Be(80);
        firstExercise.Unit.Should().Be(WeightUnits.Kilograms);

        // Verify second exercise
        var secondExercise = deserialized.Exercises.Skip(1).First();
        secondExercise.ExerciseId.Should().Be(ParseTestId<LgymApi.Domain.Entities.Exercise>("d50e8400-e29b-41d4-a716-446655440003"));
        secondExercise.ExerciseName.Should().Be("Squat");
        secondExercise.Series.Should().Be(2);
        secondExercise.Reps.Should().Be(6);
        secondExercise.Weight.Should().Be(120);
        secondExercise.Unit.Should().Be(WeightUnits.Kilograms);
    }

    [Test]
    public void TrainingCompletedEmailPayload_SerializedJsonUsesCamelCase()
    {
        // Arrange
        var originalPayload = new TrainingCompletedEmailPayload
        {
            UserId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User>.New(),
            TrainingId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.Training>.New(),
            RecipientEmail = "test@example.com",
            CultureName = "pl-PL",
            PreferredTimeZone = "Europe/Warsaw",
            PlanDayName = "Test Day",
            TrainingDate = DateTimeOffset.UtcNow,
            Exercises = new List<TrainingExerciseSummary>()
        };

        // Act
        var json = JsonSerializer.Serialize(originalPayload, SharedSerializationOptions.Current);

        // Assert
        json.Should().Contain("userId");
        json.Should().Contain("trainingId");
        json.Should().Contain("recipientEmail");
        json.Should().Contain("cultureName");
        json.Should().Contain("preferredTimeZone");
        json.Should().Contain("planDayName");
        json.Should().Contain("trainingDate");
        json.Should().Contain("exercises");
        json.Should().NotContain("UserId");
        json.Should().NotContain("Exercises");
    }

    [Test]
    public void TrainingCompletedEmailPayload_NestedExercisesUseCamelCase()
    {
        // Arrange
        var payload = new TrainingCompletedEmailPayload
        {
            UserId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User>.New(),
            TrainingId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.Training>.New(),
            RecipientEmail = "test@example.com",
            CultureName = "en-US",
            PreferredTimeZone = "UTC",
            PlanDayName = "Test",
            TrainingDate = DateTimeOffset.UtcNow,
            Exercises = new List<TrainingExerciseSummary>
            {
                new()
                {
                    ExerciseId = ParseTestId<LgymApi.Domain.Entities.Exercise>("d50e8400-e29b-41d4-a716-446655440004"),
                    ExerciseName = "Test Exercise",
                    Series = 1,
                    Reps = 10,
                    Weight = 50,
                    Unit = WeightUnits.Pounds
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(payload, SharedSerializationOptions.Current);

        // Assert - verify nested properties use camelCase
        json.Should().Contain("exerciseId");
        json.Should().Contain("exerciseName");
        json.Should().Contain("series");
        json.Should().Contain("reps");
        json.Should().Contain("weight");
        json.Should().Contain("unit");
        json.Should().NotContain("ExerciseId");
        json.Should().NotContain("ExerciseName");
    }

    [Test]
    public void TrainingCompletedEmailPayload_DeserializesFromLegacyPascalCaseJson()
    {
        // Arrange - simulate legacy payload with PascalCase property names
        const string legacyUserId = "c50e8400-e29b-41d4-a716-446655440003";
        const string legacyTrainingId = "c50e8400-e29b-41d4-a716-446655440004";
        const string legacyExerciseId = "d50e8400-e29b-41d4-a716-446655440005";
        var trainingDate = DateTimeOffset.UtcNow;
        var legacyJson = "{\"UserId\":\"" + legacyUserId + "\"," +
                         "\"TrainingId\":\"" + legacyTrainingId + "\"," +
                         "\"RecipientEmail\":\"legacy@example.com\"," +
                         "\"CultureName\":\"de-DE\"," +
                         "\"PreferredTimeZone\":\"Europe/Berlin\"," +
                         "\"PlanDayName\":\"Legacy Day\"," +
                         "\"TrainingDate\":\"" + trainingDate.ToString("O") + "\"," +
                         "\"Exercises\":[{\"ExerciseId\":\"" + legacyExerciseId + "\"," +
                         "\"ExerciseName\":\"Legacy Exercise\"," +
                         "\"Series\":1," +
                         "\"Reps\":5," +
                         "\"Weight\":45," +
                         "\"Unit\":\"Kilograms\"}]}";

        // Act
        var deserialized = JsonSerializer.Deserialize<TrainingCompletedEmailPayload>(legacyJson, SharedSerializationOptions.Current);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.UserId.Should().Be(ParseTestId<LgymApi.Domain.Entities.User>(legacyUserId));
        deserialized.TrainingId.Should().Be(ParseTestId<LgymApi.Domain.Entities.Training>(legacyTrainingId));
        deserialized.Exercises.First().ExerciseId.Should().Be(ParseTestId<LgymApi.Domain.Entities.Exercise>(legacyExerciseId));
        deserialized.RecipientEmail.Should().Be("legacy@example.com");
        deserialized.Exercises.Count.Should().Be(1);
        deserialized.Exercises.First().ExerciseName.Should().Be("Legacy Exercise");
    }

    [Test]
    public void TrainingCompletedEmailPayload_DeserializesFromLegacyIntegerEnumValues()
    {
        // Arrange - simulate very old persisted payload with integer enum values (numeric 1 for Kilograms)
        // This represents payloads serialized before string enum enforcement
        const string legacyUserId = "c50e8400-e29b-41d4-a716-446655440005";
        const string legacyTrainingId = "c50e8400-e29b-41d4-a716-446655440006";
        const string legacyExerciseId = "d50e8400-e29b-41d4-a716-446655440006";
        var trainingDate = DateTimeOffset.UtcNow;
        var legacyJson = "{\"UserId\":\"" + legacyUserId + "\"," +
                         "\"TrainingId\":\"" + legacyTrainingId + "\"," +
                         "\"RecipientEmail\":\"ancient@example.com\"," +
                         "\"CultureName\":\"en-US\"," +
                         "\"PreferredTimeZone\":\"UTC\"," +
                         "\"PlanDayName\":\"Ancient Day\"," +
                         "\"TrainingDate\":\"" + trainingDate.ToString("O") + "\"," +
                         "\"Exercises\":[{\"ExerciseId\":\"" + legacyExerciseId + "\"," +
                         "\"ExerciseName\":\"Ancient Exercise\"," +
                         "\"Series\":1," +
                         "\"Reps\":8," +
                         "\"Weight\":50," +
                         "\"Unit\":1}]}";  // 1 = Kilograms enum value

        // Act
        var deserialized = JsonSerializer.Deserialize<TrainingCompletedEmailPayload>(legacyJson, SharedSerializationOptions.Current);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.UserId.Should().Be(ParseTestId<LgymApi.Domain.Entities.User>(legacyUserId));
        deserialized.TrainingId.Should().Be(ParseTestId<LgymApi.Domain.Entities.Training>(legacyTrainingId));
        deserialized.Exercises.First().ExerciseId.Should().Be(ParseTestId<LgymApi.Domain.Entities.Exercise>(legacyExerciseId));
        deserialized.RecipientEmail.Should().Be("ancient@example.com");
        deserialized.Exercises.Count.Should().Be(1);
        deserialized.Exercises.First().ExerciseName.Should().Be("Ancient Exercise");
        // Verify the enum was properly deserialized from integer value
        deserialized.Exercises.First().Unit.Should().Be(WeightUnits.Kilograms);
    }

    [Test]
    public void TrainingCompletedEmailPayload_RoundtripsPreservesAllFields()
    {
        // Arrange
        var userId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User>.New();
        var trainingId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.Training>.New();
        var trainingDate = DateTimeOffset.UtcNow;
        var originalPayload = new TrainingCompletedEmailPayload
        {
            UserId = userId,
            TrainingId = trainingId,
            RecipientEmail = "full@example.com",
            CultureName = "es-ES",
            PreferredTimeZone = "Europe/Madrid",
            PlanDayName = "Full Upper",
            TrainingDate = trainingDate,
            Exercises = new List<TrainingExerciseSummary>
            {
                new()
                {
                    ExerciseId = ParseTestId<LgymApi.Domain.Entities.Exercise>("d50e8400-e29b-41d4-a716-446655440007"),
                    ExerciseName = "Full Bench",
                    Series = 3,
                    Reps = 8,
                    Weight = 100,
                    Unit = WeightUnits.Kilograms
                }
            }
        };
        var json = JsonSerializer.Serialize(originalPayload, SharedSerializationOptions.Current);

        // Act
        var deserialized = JsonSerializer.Deserialize<TrainingCompletedEmailPayload>(json, SharedSerializationOptions.Current);

        // Assert - each field must match exactly
        deserialized.Should().NotBeNull();
        deserialized!.UserId.Should().Be(userId, "UserId not preserved");
        deserialized.TrainingId.Should().Be(trainingId, "TrainingId not preserved");
        deserialized.RecipientEmail.Should().Be("full@example.com", "RecipientEmail not preserved");
        deserialized.CultureName.Should().Be("es-ES", "CultureName not preserved");
        deserialized.PreferredTimeZone.Should().Be("Europe/Madrid", "PreferredTimeZone not preserved");
        deserialized.PlanDayName.Should().Be("Full Upper", "PlanDayName not preserved");
        deserialized.TrainingDate.Should().Be(trainingDate, "TrainingDate not preserved");
        deserialized.Exercises.Count.Should().Be(1, "Exercises count not preserved");
    }

    [Test]
    public void TrainingCompletedEmailPayload_CorrelationIdDerivesFromTrainingId()
    {
        // Arrange
        var trainingId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.Training>.New();
        var payload = new TrainingCompletedEmailPayload
        {
            UserId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User>.New(),
            TrainingId = trainingId,
            RecipientEmail = "corr@example.com",
            CultureName = "en-US",
            PreferredTimeZone = "UTC",
            PlanDayName = "Corr Day",
            TrainingDate = DateTimeOffset.UtcNow,
            Exercises = new List<TrainingExerciseSummary>()
        };

        // Act
        var json = JsonSerializer.Serialize(payload, SharedSerializationOptions.Current);
        var deserialized = JsonSerializer.Deserialize<TrainingCompletedEmailPayload>(json, SharedSerializationOptions.Current);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.CorrelationId.GetValue().Should().Be(trainingId.GetValue());
    }

    #endregion

    #region Composer Compatibility Tests

    [Test]
    public void WelcomeEmailPayload_CanBeDeserializedByComposerConsumer()
    {
        // Arrange - simulate payload stored by EmailSchedulerService and read back by composer
        var userId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User>.New();
        var storedPayload = new WelcomeEmailPayload
        {
            UserId = userId,
            UserName = "Composer Test",
            RecipientEmail = "composer@example.com",
            CultureName = "en-US"
        };
        // Simulate what EmailSchedulerService would persist
        var storedJson = JsonSerializer.Serialize(storedPayload, SharedSerializationOptions.Current);

        // Act - simulate what a composer would do
        var deserializedPayload = JsonSerializer.Deserialize<WelcomeEmailPayload>(storedJson, SharedSerializationOptions.Current);

        // Assert - composer can consume the payload without error
        deserializedPayload.Should().NotBeNull();
        deserializedPayload!.UserName.Should().Be("Composer Test");
        deserializedPayload.RecipientEmail.Should().Be("composer@example.com");
    }

    [Test]
    public void InvitationEmailPayload_CanBeDeserializedByComposerConsumer()
    {
        // Arrange - simulate payload stored by EmailSchedulerService and read back by composer
        var invitationId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.TrainerInvitation>.New();
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);
        var storedPayload = new InvitationEmailPayload
        {
            InvitationId = invitationId,
            InvitationCode = "COMP123",
            ExpiresAt = expiresAt,
            TrainerName = "Composer Trainer",
            RecipientEmail = "compinv@example.com",
            CultureName = "pl-PL",
            PreferredTimeZone = "Europe/Warsaw"
        };
        // Simulate what EmailSchedulerService would persist
        var storedJson = JsonSerializer.Serialize(storedPayload, SharedSerializationOptions.Current);

        // Act - simulate what a composer would do
        var deserializedPayload = JsonSerializer.Deserialize<InvitationEmailPayload>(storedJson, SharedSerializationOptions.Current);

        // Assert - composer can consume the payload without error
        deserializedPayload.Should().NotBeNull();
        deserializedPayload!.InvitationCode.Should().Be("COMP123");
        deserializedPayload.TrainerName.Should().Be("Composer Trainer");
        deserializedPayload.ExpiresAt.Should().Be(expiresAt);
    }

    [Test]
    public void TrainingCompletedEmailPayload_CanBeDeserializedByComposerConsumer()
    {
        // Arrange - simulate payload stored by EmailSchedulerService and read back by composer
        var userId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User>.New();
        var trainingId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.Training>.New();
        var trainingDate = DateTimeOffset.UtcNow;
        var storedPayload = new TrainingCompletedEmailPayload
        {
            UserId = userId,
            TrainingId = trainingId,
            RecipientEmail = "comptraining@example.com",
            CultureName = "en-US",
            PreferredTimeZone = "America/New_York",
            PlanDayName = "Composer Training",
            TrainingDate = trainingDate,
            Exercises = new List<TrainingExerciseSummary>
            {
                new()
                {
                    ExerciseId = ParseTestId<LgymApi.Domain.Entities.Exercise>("d50e8400-e29b-41d4-a716-446655440008"),
                    ExerciseName = "Composer Exercise",
                    Series = 1,
                    Reps = 8,
                    Weight = 75,
                    Unit = WeightUnits.Kilograms
                }
            }
        };
        // Simulate what EmailSchedulerService would persist
        var storedJson = JsonSerializer.Serialize(storedPayload, SharedSerializationOptions.Current);

        // Act - simulate what a composer would do
        var deserializedPayload = JsonSerializer.Deserialize<TrainingCompletedEmailPayload>(storedJson, SharedSerializationOptions.Current);

        // Assert - composer can consume the payload without error, including nested exercises
        deserializedPayload.Should().NotBeNull();
        deserializedPayload!.PlanDayName.Should().Be("Composer Training");
        deserializedPayload.Exercises.Count.Should().Be(1);
        deserializedPayload.Exercises.First().ExerciseName.Should().Be("Composer Exercise");
    }

    [Test]
    public void TrainingCompletedEmailPayload_ExerciseId_RemainsUuidStringInJson()
    {
        const string exerciseIdValue = "d50e8400-e29b-41d4-a716-446655440009";
        var payload = new TrainingCompletedEmailPayload
        {
            UserId = Id<LgymApi.Domain.Entities.User>.New(),
            TrainingId = Id<LgymApi.Domain.Entities.Training>.New(),
            Exercises =
            [
                new TrainingExerciseSummary
                {
                    ExerciseId = ParseTestId<LgymApi.Domain.Entities.Exercise>(exerciseIdValue),
                    ExerciseName = "Bench Press"
                }
            ]
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload, SharedSerializationOptions.Current));

        document.RootElement.GetProperty("exercises")[0].GetProperty("exerciseId").GetString().Should().Be(exerciseIdValue);
    }

    [Test]
    public void TrainingCompletedEmailPayload_InvalidLegacyExerciseId_ThrowsJsonException()
    {
        const string legacyJson = "{\"exercises\":[{\"exerciseId\":\"not-a-uuid\"}]}";

        FluentActions.Invoking(() => JsonSerializer.Deserialize<TrainingCompletedEmailPayload>(legacyJson, SharedSerializationOptions.Current))
            .Should().Throw<JsonException>();
    }

    #endregion

    #region Frozen Wave-1 Email Contract Fixtures

    [Test]
    public void WelcomeEmailPayload_FixedFixture_PreservesWireContract()
    {
        var userId = ParseTestId<LgymApi.Domain.Entities.User>("11111111-1111-1111-1111-111111111111");
        var fixture = new WelcomeEmailPayload
        {
            UserId = userId,
            UserName = "Wave One Welcome",
            RecipientEmail = "welcome.contract@example.test",
            CultureName = MalformedCultureName
        };

        var roundtrip = SerializeAndDeserialize(fixture);

        roundtrip.Json.Should().Contain($"\"userId\":\"{userId.GetValue()}\"");
        roundtrip.Json.Should().Contain("\"userName\":\"Wave One Welcome\"");
        roundtrip.Json.Should().Contain("\"recipientEmail\":\"welcome.contract@example.test\"");
        roundtrip.Json.Should().NotContain("\"UserId\"");
        roundtrip.Json.Should().NotContain("\"culture\"");
        roundtrip.Payload.UserId.Should().Be(userId);
        roundtrip.Payload.UserName.Should().Be("Wave One Welcome");
        roundtrip.Payload.RecipientEmail.Should().Be("welcome.contract@example.test");
        roundtrip.Payload.CultureName.Should().Be(MalformedCultureName);
        AssertPayloadMetadata(roundtrip.Payload, userId.Rebind<LgymApi.Domain.ValueObjects.CorrelationScope>(), "user.registration.welcome");
    }

    [Test]
    public void InvitationEmailPayload_FixedFixture_PreservesWireContract()
    {
        var invitationId = ParseTestId<LgymApi.Domain.Entities.TrainerInvitation>("22222222-2222-2222-2222-222222222222");
        var expiresAt = new DateTimeOffset(2026, 8, 1, 12, 30, 0, TimeSpan.Zero);
        var fixture = new InvitationEmailPayload
        {
            InvitationId = invitationId,
            InvitationCode = "INV-380-FIXTURE",
            ExpiresAt = expiresAt,
            TrainerName = "Coach Contract",
            RecipientEmail = "invitation.contract@example.test",
            CultureName = MalformedCultureName,
            PreferredTimeZone = "Europe/Warsaw"
        };

        var roundtrip = SerializeAndDeserialize(fixture);

        roundtrip.Json.Should().Contain($"\"invitationId\":\"{invitationId.GetValue()}\"");
        roundtrip.Json.Should().Contain("\"invitationCode\":\"INV-380-FIXTURE\"");
        roundtrip.Json.Should().Contain("\"preferredTimeZone\":\"Europe/Warsaw\"");
        roundtrip.Json.Should().NotContain("\"InvitationId\"");
        roundtrip.Json.Should().NotContain("\"culture\"");
        roundtrip.Payload.InvitationId.Should().Be(invitationId);
        roundtrip.Payload.InvitationCode.Should().Be("INV-380-FIXTURE");
        roundtrip.Payload.ExpiresAt.Should().Be(expiresAt);
        roundtrip.Payload.TrainerName.Should().Be("Coach Contract");
        roundtrip.Payload.RecipientEmail.Should().Be("invitation.contract@example.test");
        roundtrip.Payload.CultureName.Should().Be(MalformedCultureName);
        roundtrip.Payload.PreferredTimeZone.Should().Be("Europe/Warsaw");
        AssertPayloadMetadata(roundtrip.Payload, invitationId.Rebind<LgymApi.Domain.ValueObjects.CorrelationScope>(), "trainer.invitation.created");
    }

    [Test]
    public void InvitationAcceptedEmailPayload_FixedFixture_PreservesWireContract()
    {
        var invitationId = ParseTestId<LgymApi.Domain.Entities.TrainerInvitation>("33333333-3333-3333-3333-333333333333");
        var fixture = new InvitationAcceptedEmailPayload
        {
            InvitationId = invitationId,
            TrainerName = "Coach Accepted",
            TraineeName = "Trainee Accepted",
            RecipientEmail = "accepted.contract@example.test",
            CultureName = MalformedCultureName,
            PreferredTimeZone = "America/Toronto"
        };

        var roundtrip = SerializeAndDeserialize(fixture);

        roundtrip.Json.Should().Contain($"\"invitationId\":\"{invitationId.GetValue()}\"");
        roundtrip.Json.Should().Contain("\"trainerName\":\"Coach Accepted\"");
        roundtrip.Json.Should().Contain("\"traineeName\":\"Trainee Accepted\"");
        roundtrip.Json.Should().NotContain("\"InvitationId\"");
        roundtrip.Json.Should().NotContain("\"culture\"");
        roundtrip.Payload.InvitationId.Should().Be(invitationId);
        roundtrip.Payload.TrainerName.Should().Be("Coach Accepted");
        roundtrip.Payload.TraineeName.Should().Be("Trainee Accepted");
        roundtrip.Payload.RecipientEmail.Should().Be("accepted.contract@example.test");
        roundtrip.Payload.CultureName.Should().Be(MalformedCultureName);
        roundtrip.Payload.PreferredTimeZone.Should().Be("America/Toronto");
        AssertPayloadMetadata(roundtrip.Payload, invitationId.Rebind<LgymApi.Domain.ValueObjects.CorrelationScope>(), "trainer.invitation.accepted");
    }

    [Test]
    public void InvitationRevokedEmailPayload_FixedFixture_PreservesWireContract()
    {
        var invitationId = ParseTestId<LgymApi.Domain.Entities.TrainerInvitation>("44444444-4444-4444-4444-444444444444");
        var fixture = new InvitationRevokedEmailPayload
        {
            InvitationId = invitationId,
            TrainerName = "Coach Revoked",
            RecipientEmail = "revoked.contract@example.test",
            CultureName = MalformedCultureName,
            PreferredTimeZone = "Australia/Sydney"
        };

        var roundtrip = SerializeAndDeserialize(fixture);

        roundtrip.Json.Should().Contain($"\"invitationId\":\"{invitationId.GetValue()}\"");
        roundtrip.Json.Should().Contain("\"trainerName\":\"Coach Revoked\"");
        roundtrip.Json.Should().Contain("\"preferredTimeZone\":\"Australia/Sydney\"");
        roundtrip.Json.Should().NotContain("\"InvitationId\"");
        roundtrip.Json.Should().NotContain("\"culture\"");
        roundtrip.Payload.InvitationId.Should().Be(invitationId);
        roundtrip.Payload.TrainerName.Should().Be("Coach Revoked");
        roundtrip.Payload.RecipientEmail.Should().Be("revoked.contract@example.test");
        roundtrip.Payload.CultureName.Should().Be(MalformedCultureName);
        roundtrip.Payload.PreferredTimeZone.Should().Be("Australia/Sydney");
        AssertPayloadMetadata(roundtrip.Payload, invitationId.Rebind<LgymApi.Domain.ValueObjects.CorrelationScope>(), "trainer.invitation.revoked");
    }

    [Test]
    public void TrainingCompletedEmailPayload_FixedFixture_PreservesNestedExerciseAndEnumContract()
    {
        var userId = ParseTestId<LgymApi.Domain.Entities.User>("55555555-5555-5555-5555-555555555555");
        var trainingId = ParseTestId<LgymApi.Domain.Entities.Training>("66666666-6666-6666-6666-666666666666");
        var exerciseId = ParseTestId<LgymApi.Domain.Entities.Exercise>("77777777-7777-7777-7777-777777777777");
        var trainingDate = new DateTimeOffset(2026, 8, 2, 6, 45, 0, TimeSpan.Zero);
        var fixture = new TrainingCompletedEmailPayload
        {
            UserId = userId,
            TrainingId = trainingId,
            RecipientEmail = "training.contract@example.test",
            CultureName = MalformedCultureName,
            PreferredTimeZone = "America/Los_Angeles",
            PlanDayName = "Wave One Strength",
            TrainingDate = trainingDate,
            Exercises =
            [
                new TrainingExerciseSummary
                {
                    ExerciseId = exerciseId,
                    ExerciseName = "Contract Deadlift",
                    Series = 4,
                    Reps = 5.5,
                    Weight = 140.25,
                    Unit = WeightUnits.Pounds
                }
            ]
        };

        var roundtrip = SerializeAndDeserialize(fixture);

        roundtrip.Json.Should().Contain($"\"userId\":\"{userId.GetValue()}\"");
        roundtrip.Json.Should().Contain($"\"trainingId\":\"{trainingId.GetValue()}\"");
        roundtrip.Json.Should().Contain($"\"exerciseId\":\"{exerciseId.GetValue()}\"");
        roundtrip.Json.Should().Contain("\"exerciseName\":\"Contract Deadlift\"");
        roundtrip.Json.Should().Contain("\"unit\":\"Pounds\"");
        roundtrip.Json.Should().NotContain("\"TrainingId\"");
        roundtrip.Json.Should().NotContain("\"culture\"");
        roundtrip.Payload.UserId.Should().Be(userId);
        roundtrip.Payload.TrainingId.Should().Be(trainingId);
        roundtrip.Payload.RecipientEmail.Should().Be("training.contract@example.test");
        roundtrip.Payload.CultureName.Should().Be(MalformedCultureName);
        roundtrip.Payload.PreferredTimeZone.Should().Be("America/Los_Angeles");
        roundtrip.Payload.PlanDayName.Should().Be("Wave One Strength");
        roundtrip.Payload.TrainingDate.Should().Be(trainingDate);
        roundtrip.Payload.Exercises.Should().ContainSingle();
        roundtrip.Payload.Exercises[0].ExerciseId.Should().Be(exerciseId);
        roundtrip.Payload.Exercises[0].ExerciseName.Should().Be("Contract Deadlift");
        roundtrip.Payload.Exercises[0].Series.Should().Be(4);
        roundtrip.Payload.Exercises[0].Reps.Should().Be(5.5);
        roundtrip.Payload.Exercises[0].Weight.Should().Be(140.25);
        roundtrip.Payload.Exercises[0].Unit.Should().Be(WeightUnits.Pounds);
        AssertPayloadMetadata(roundtrip.Payload, trainingId.Rebind<LgymApi.Domain.ValueObjects.CorrelationScope>(), "training.completed");
    }

    [Test]
    public void PasswordRecoveryEmailPayload_FixedFixture_PreservesWireContract()
    {
        var userId = ParseTestId<LgymApi.Domain.Entities.User>("88888888-8888-8888-8888-888888888888");
        var tokenId = ParseTestId<LgymApi.Domain.Entities.PasswordResetToken>("99999999-9999-9999-9999-999999999999");
        var fixture = new PasswordRecoveryEmailPayload
        {
            UserId = userId,
            TokenId = tokenId,
            UserName = "Recovery Contract",
            RecipientEmail = "recovery.contract@example.test",
            ResetToken = "reset-token-380",
            ResetUrl = "https://request-sentinel.example.test/reset",
            CultureName = MalformedCultureName
        };

        var roundtrip = SerializeAndDeserialize(fixture);

        roundtrip.Json.Should().Contain($"\"userId\":\"{userId.GetValue()}\"");
        roundtrip.Json.Should().Contain($"\"tokenId\":\"{tokenId.GetValue()}\"");
        roundtrip.Json.Should().Contain("\"resetToken\":\"reset-token-380\"");
        roundtrip.Json.Should().Contain("\"resetUrl\":\"https://request-sentinel.example.test/reset\"");
        roundtrip.Json.Should().NotContain("\"TokenId\"");
        roundtrip.Json.Should().NotContain("\"culture\"");
        roundtrip.Payload.UserId.Should().Be(userId);
        roundtrip.Payload.TokenId.Should().Be(tokenId);
        roundtrip.Payload.UserName.Should().Be("Recovery Contract");
        roundtrip.Payload.RecipientEmail.Should().Be("recovery.contract@example.test");
        roundtrip.Payload.ResetToken.Should().Be("reset-token-380");
        roundtrip.Payload.ResetUrl.Should().Be("https://request-sentinel.example.test/reset");
        roundtrip.Payload.CultureName.Should().Be(MalformedCultureName);
        AssertPayloadMetadata(roundtrip.Payload, tokenId.Rebind<LgymApi.Domain.ValueObjects.CorrelationScope>(), "user.password.recovery");
    }

    [Test]
    public void PasswordRecoveryEmailPayload_NullResetUrl_IsOmittedFromWireJson()
    {
        var fixture = new PasswordRecoveryEmailPayload
        {
            UserId = ParseTestId<LgymApi.Domain.Entities.User>("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            TokenId = ParseTestId<LgymApi.Domain.Entities.PasswordResetToken>("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            UserName = "Null Omission",
            RecipientEmail = "null.contract@example.test",
            ResetToken = "null-reset-token",
            ResetUrl = null!,
            CultureName = "en-US"
        };

        var json = JsonSerializer.Serialize(fixture, SharedSerializationOptions.Current);

        json.Should().NotContain("\"resetUrl\"");
        json.Should().Contain("\"resetToken\":\"null-reset-token\"");
    }

    [Test]
    public void InvitationAcceptedEmailPayload_DeserializesLegacyPascalCaseJson()
    {
        const string invitationId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
        const string payloadJson = """{"InvitationId":"cccccccc-cccc-cccc-cccc-cccccccccccc","TrainerName":"Legacy Coach","TraineeName":"Legacy Trainee","RecipientEmail":"accepted.legacy@example.test","CultureName":"en-US","PreferredTimeZone":"UTC"}""";

        var payload = JsonSerializer.Deserialize<InvitationAcceptedEmailPayload>(payloadJson, SharedSerializationOptions.Current);

        payload.Should().NotBeNull();
        payload!.InvitationId.Should().Be(ParseTestId<LgymApi.Domain.Entities.TrainerInvitation>(invitationId));
        payload.TrainerName.Should().Be("Legacy Coach");
        payload.TraineeName.Should().Be("Legacy Trainee");
        payload.RecipientEmail.Should().Be("accepted.legacy@example.test");
    }

    [Test]
    public void InvitationRevokedEmailPayload_DeserializesMixedCaseJson()
    {
        const string invitationId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
        const string payloadJson = """{"iNvItAtIoNiD":"dddddddd-dddd-dddd-dddd-dddddddddddd","tRaInErNaMe":"Mixed Coach","rEcIpIeNtEmAiL":"revoked.mixed@example.test","cUlTuReNaMe":"en-US","pReFeRrEdTiMeZoNe":"UTC"}""";

        var payload = JsonSerializer.Deserialize<InvitationRevokedEmailPayload>(payloadJson, SharedSerializationOptions.Current);

        payload.Should().NotBeNull();
        payload!.InvitationId.Should().Be(ParseTestId<LgymApi.Domain.Entities.TrainerInvitation>(invitationId));
        payload.TrainerName.Should().Be("Mixed Coach");
        payload.RecipientEmail.Should().Be("revoked.mixed@example.test");
        payload.PreferredTimeZone.Should().Be("UTC");
    }

    [Test]
    public void PasswordRecoveryEmailPayload_DeserializesMixedCaseJson()
    {
        const string userId = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";
        const string tokenId = "ffffffff-ffff-ffff-ffff-ffffffffffff";
        const string payloadJson = """{"uSeRiD":"eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee","tOkEnId":"ffffffff-ffff-ffff-ffff-ffffffffffff","uSeRnAmE":"Mixed Recovery","rEcIpIeNtEmAiL":"recovery.mixed@example.test","rEsEtToKeN":"mixed-token","rEsEtUrL":"https://mixed.example.test/reset","cUlTuReNaMe":"en-US"}""";

        var payload = JsonSerializer.Deserialize<PasswordRecoveryEmailPayload>(payloadJson, SharedSerializationOptions.Current);

        payload.Should().NotBeNull();
        payload!.UserId.Should().Be(ParseTestId<LgymApi.Domain.Entities.User>(userId));
        payload.TokenId.Should().Be(ParseTestId<LgymApi.Domain.Entities.PasswordResetToken>(tokenId));
        payload.UserName.Should().Be("Mixed Recovery");
        payload.RecipientEmail.Should().Be("recovery.mixed@example.test");
        payload.ResetToken.Should().Be("mixed-token");
        payload.ResetUrl.Should().Be("https://mixed.example.test/reset");
    }

    [TestCaseSource(nameof(CreateGoldenPayloadCases))]
    public void EmailPayload_GoldenWireMatrix_PreservesEveryPayloadContract(EmailPayloadGoldenCase goldenCase)
    {
        var evaluation = EvaluateGoldenCase(goldenCase);

        goldenCase.AssertWire(evaluation.Wire.RootElement);
        goldenCase.AssertPayload(evaluation.RoundtrippedPayload);
        goldenCase.AssertPayload(evaluation.LegacyPayload);
        evaluation.Wire.RootElement.TryGetProperty("culture", out _).Should().BeFalse();
        evaluation.NullWire.RootElement.TryGetProperty(goldenCase.NullPropertyName, out _).Should().BeFalse();
    }

    private static IEnumerable<TestCaseData> CreateGoldenPayloadCases()
    {
        yield return new TestCaseData(new EmailPayloadGoldenCase(
            "welcome",
            new WelcomeEmailPayload
            {
                UserId = ParseTestId<LgymApi.Domain.Entities.User>("10101010-1010-1010-1010-101010101010"),
                UserName = "Golden Welcome",
                RecipientEmail = "golden.welcome@example.test",
                CultureName = MalformedCultureName
            },
            new WelcomeEmailPayload
            {
                UserId = ParseTestId<LgymApi.Domain.Entities.User>("10101010-1010-1010-1010-101010101010"),
                UserName = null!,
                RecipientEmail = "golden.welcome@example.test",
                CultureName = MalformedCultureName
            },
            "userName",
            """{"UsErId":"10101010-1010-1010-1010-101010101010","uSeRnAmE":"Golden Welcome","ReCiPiEnTeMaIl":"golden.welcome@example.test","CuLtUrEnAmE":"!invalid-culture-380"}""",
            root =>
            {
                AssertCamelCaseProperties(root, "userId", "userName", "recipientEmail", "cultureName");
                root.GetProperty("userId").GetString().Should().Be("10101010-1010-1010-1010-101010101010");
            },
            payload =>
            {
                var actual = payload.Should().BeOfType<WelcomeEmailPayload>().Subject;
                actual.UserId.Should().Be(ParseTestId<LgymApi.Domain.Entities.User>("10101010-1010-1010-1010-101010101010"));
                actual.UserName.Should().Be("Golden Welcome");
                actual.RecipientEmail.Should().Be("golden.welcome@example.test");
                actual.CultureName.Should().Be(MalformedCultureName);
                AssertPayloadMetadata(actual, ParseTestId<LgymApi.Domain.ValueObjects.CorrelationScope>("10101010-1010-1010-1010-101010101010"), "user.registration.welcome");
            })).SetName("Email payload golden matrix: Welcome");

        yield return new TestCaseData(new EmailPayloadGoldenCase(
            "invitation",
            new InvitationEmailPayload
            {
                InvitationId = ParseTestId<LgymApi.Domain.Entities.TrainerInvitation>("20202020-2020-2020-2020-202020202020"),
                InvitationCode = "GOLDEN-INVITATION",
                ExpiresAt = new DateTimeOffset(2026, 8, 1, 12, 30, 0, TimeSpan.Zero),
                TrainerName = "Golden Coach",
                RecipientEmail = "golden.invitation@example.test",
                CultureName = MalformedCultureName,
                PreferredTimeZone = "Europe/Warsaw"
            },
            new InvitationEmailPayload
            {
                InvitationId = ParseTestId<LgymApi.Domain.Entities.TrainerInvitation>("20202020-2020-2020-2020-202020202020"),
                InvitationCode = "GOLDEN-INVITATION",
                ExpiresAt = new DateTimeOffset(2026, 8, 1, 12, 30, 0, TimeSpan.Zero),
                TrainerName = null!,
                RecipientEmail = "golden.invitation@example.test",
                CultureName = MalformedCultureName,
                PreferredTimeZone = "Europe/Warsaw"
            },
            "trainerName",
            """{"InvitationId":"20202020-2020-2020-2020-202020202020","iNvItAtIoNcOdE":"GOLDEN-INVITATION","ExpiresAt":"2026-08-01T12:30:00+00:00","TrAiNeRnAmE":"Golden Coach","RecipientEmail":"golden.invitation@example.test","CultureName":"!invalid-culture-380","PreferredTimeZone":"Europe/Warsaw"}""",
            root =>
            {
                AssertCamelCaseProperties(root, "invitationId", "invitationCode", "expiresAt", "trainerName", "recipientEmail", "cultureName", "preferredTimeZone");
                root.GetProperty("invitationId").GetString().Should().Be("20202020-2020-2020-2020-202020202020");
            },
            payload =>
            {
                var actual = payload.Should().BeOfType<InvitationEmailPayload>().Subject;
                actual.InvitationId.Should().Be(ParseTestId<LgymApi.Domain.Entities.TrainerInvitation>("20202020-2020-2020-2020-202020202020"));
                actual.InvitationCode.Should().Be("GOLDEN-INVITATION");
                actual.ExpiresAt.Should().Be(new DateTimeOffset(2026, 8, 1, 12, 30, 0, TimeSpan.Zero));
                actual.TrainerName.Should().Be("Golden Coach");
                actual.RecipientEmail.Should().Be("golden.invitation@example.test");
                actual.CultureName.Should().Be(MalformedCultureName);
                actual.PreferredTimeZone.Should().Be("Europe/Warsaw");
                AssertPayloadMetadata(actual, ParseTestId<LgymApi.Domain.ValueObjects.CorrelationScope>("20202020-2020-2020-2020-202020202020"), "trainer.invitation.created");
            })).SetName("Email payload golden matrix: Invitation");

        yield return new TestCaseData(new EmailPayloadGoldenCase(
            "invitation accepted",
            new InvitationAcceptedEmailPayload
            {
                InvitationId = ParseTestId<LgymApi.Domain.Entities.TrainerInvitation>("30303030-3030-3030-3030-303030303030"),
                TrainerName = "Golden Accepted Coach",
                TraineeName = "Golden Accepted Trainee",
                RecipientEmail = "golden.accepted@example.test",
                CultureName = MalformedCultureName,
                PreferredTimeZone = "America/Toronto"
            },
            new InvitationAcceptedEmailPayload
            {
                InvitationId = ParseTestId<LgymApi.Domain.Entities.TrainerInvitation>("30303030-3030-3030-3030-303030303030"),
                TrainerName = "Golden Accepted Coach",
                TraineeName = null!,
                RecipientEmail = "golden.accepted@example.test",
                CultureName = MalformedCultureName,
                PreferredTimeZone = "America/Toronto"
            },
            "traineeName",
            """{"iNvItAtIoNiD":"30303030-3030-3030-3030-303030303030","TrainerName":"Golden Accepted Coach","tRaInEeNaMe":"Golden Accepted Trainee","RecipientEmail":"golden.accepted@example.test","CultureName":"!invalid-culture-380","PreferredTimeZone":"America/Toronto"}""",
            root =>
            {
                AssertCamelCaseProperties(root, "invitationId", "trainerName", "traineeName", "recipientEmail", "cultureName", "preferredTimeZone");
                root.GetProperty("invitationId").GetString().Should().Be("30303030-3030-3030-3030-303030303030");
            },
            payload =>
            {
                var actual = payload.Should().BeOfType<InvitationAcceptedEmailPayload>().Subject;
                actual.InvitationId.Should().Be(ParseTestId<LgymApi.Domain.Entities.TrainerInvitation>("30303030-3030-3030-3030-303030303030"));
                actual.TrainerName.Should().Be("Golden Accepted Coach");
                actual.TraineeName.Should().Be("Golden Accepted Trainee");
                actual.RecipientEmail.Should().Be("golden.accepted@example.test");
                actual.CultureName.Should().Be(MalformedCultureName);
                actual.PreferredTimeZone.Should().Be("America/Toronto");
                AssertPayloadMetadata(actual, ParseTestId<LgymApi.Domain.ValueObjects.CorrelationScope>("30303030-3030-3030-3030-303030303030"), "trainer.invitation.accepted");
            })).SetName("Email payload golden matrix: Invitation accepted");

        yield return new TestCaseData(new EmailPayloadGoldenCase(
            "invitation revoked",
            new InvitationRevokedEmailPayload
            {
                InvitationId = ParseTestId<LgymApi.Domain.Entities.TrainerInvitation>("40404040-4040-4040-4040-404040404040"),
                TrainerName = "Golden Revoked Coach",
                RecipientEmail = "golden.revoked@example.test",
                CultureName = MalformedCultureName,
                PreferredTimeZone = "Australia/Sydney"
            },
            new InvitationRevokedEmailPayload
            {
                InvitationId = ParseTestId<LgymApi.Domain.Entities.TrainerInvitation>("40404040-4040-4040-4040-404040404040"),
                TrainerName = null!,
                RecipientEmail = "golden.revoked@example.test",
                CultureName = MalformedCultureName,
                PreferredTimeZone = "Australia/Sydney"
            },
            "trainerName",
            """{"InvitationId":"40404040-4040-4040-4040-404040404040","tRaInErNaMe":"Golden Revoked Coach","rEcIpIeNtEmAiL":"golden.revoked@example.test","cUlTuReNaMe":"!invalid-culture-380","pReFeRrEdTiMeZoNe":"Australia/Sydney"}""",
            root =>
            {
                AssertCamelCaseProperties(root, "invitationId", "trainerName", "recipientEmail", "cultureName", "preferredTimeZone");
                root.GetProperty("invitationId").GetString().Should().Be("40404040-4040-4040-4040-404040404040");
            },
            payload =>
            {
                var actual = payload.Should().BeOfType<InvitationRevokedEmailPayload>().Subject;
                actual.InvitationId.Should().Be(ParseTestId<LgymApi.Domain.Entities.TrainerInvitation>("40404040-4040-4040-4040-404040404040"));
                actual.TrainerName.Should().Be("Golden Revoked Coach");
                actual.RecipientEmail.Should().Be("golden.revoked@example.test");
                actual.CultureName.Should().Be(MalformedCultureName);
                actual.PreferredTimeZone.Should().Be("Australia/Sydney");
                AssertPayloadMetadata(actual, ParseTestId<LgymApi.Domain.ValueObjects.CorrelationScope>("40404040-4040-4040-4040-404040404040"), "trainer.invitation.revoked");
            })).SetName("Email payload golden matrix: Invitation revoked");

        yield return new TestCaseData(new EmailPayloadGoldenCase(
            "training completed",
            new TrainingCompletedEmailPayload
            {
                UserId = ParseTestId<LgymApi.Domain.Entities.User>("50505050-5050-5050-5050-505050505050"),
                TrainingId = ParseTestId<LgymApi.Domain.Entities.Training>("60606060-6060-6060-6060-606060606060"),
                RecipientEmail = "golden.training@example.test",
                CultureName = MalformedCultureName,
                PreferredTimeZone = "America/Los_Angeles",
                PlanDayName = "Golden Strength",
                TrainingDate = new DateTimeOffset(2026, 8, 2, 6, 45, 0, TimeSpan.Zero),
                Exercises =
                [
                    new TrainingExerciseSummary
                    {
                        ExerciseId = ParseTestId<LgymApi.Domain.Entities.Exercise>("70707070-7070-7070-7070-707070707070"),
                        ExerciseName = "Golden Deadlift",
                        Series = 4,
                        Reps = 5.5,
                        Weight = 140.25,
                        Unit = WeightUnits.Pounds
                    }
                ]
            },
            new TrainingCompletedEmailPayload
            {
                UserId = ParseTestId<LgymApi.Domain.Entities.User>("50505050-5050-5050-5050-505050505050"),
                TrainingId = ParseTestId<LgymApi.Domain.Entities.Training>("60606060-6060-6060-6060-606060606060"),
                RecipientEmail = "golden.training@example.test",
                CultureName = MalformedCultureName,
                PreferredTimeZone = "America/Los_Angeles",
                PlanDayName = null!,
                TrainingDate = new DateTimeOffset(2026, 8, 2, 6, 45, 0, TimeSpan.Zero),
                Exercises = Array.Empty<TrainingExerciseSummary>()
            },
            "planDayName",
            """{"UsErId":"50505050-5050-5050-5050-505050505050","tRaInInGiD":"60606060-6060-6060-6060-606060606060","RecipientEmail":"golden.training@example.test","CultureName":"!invalid-culture-380","PreferredTimeZone":"America/Los_Angeles","PlanDayName":"Golden Strength","TrainingDate":"2026-08-02T06:45:00+00:00","Exercises":[{"ExerciseId":"70707070-7070-7070-7070-707070707070","ExerciseName":"Golden Deadlift","Series":4,"Reps":5.5,"Weight":140.25,"Unit":2}]}""",
            root =>
            {
                AssertCamelCaseProperties(root, "userId", "trainingId", "recipientEmail", "cultureName", "preferredTimeZone", "planDayName", "trainingDate", "exercises");
                root.GetProperty("userId").GetString().Should().Be("50505050-5050-5050-5050-505050505050");
                root.GetProperty("trainingId").GetString().Should().Be("60606060-6060-6060-6060-606060606060");
                var exercise = root.GetProperty("exercises")[0];
                AssertCamelCaseProperties(exercise, "exerciseId", "exerciseName", "series", "reps", "weight", "unit");
                exercise.GetProperty("exerciseId").GetString().Should().Be("70707070-7070-7070-7070-707070707070");
                exercise.GetProperty("unit").GetString().Should().Be("Pounds");
            },
            payload =>
            {
                var actual = payload.Should().BeOfType<TrainingCompletedEmailPayload>().Subject;
                actual.UserId.Should().Be(ParseTestId<LgymApi.Domain.Entities.User>("50505050-5050-5050-5050-505050505050"));
                actual.TrainingId.Should().Be(ParseTestId<LgymApi.Domain.Entities.Training>("60606060-6060-6060-6060-606060606060"));
                actual.RecipientEmail.Should().Be("golden.training@example.test");
                actual.CultureName.Should().Be(MalformedCultureName);
                actual.PreferredTimeZone.Should().Be("America/Los_Angeles");
                actual.PlanDayName.Should().Be("Golden Strength");
                actual.TrainingDate.Should().Be(new DateTimeOffset(2026, 8, 2, 6, 45, 0, TimeSpan.Zero));
                actual.Exercises.Should().ContainSingle();
                actual.Exercises[0].ExerciseId.Should().Be(ParseTestId<LgymApi.Domain.Entities.Exercise>("70707070-7070-7070-7070-707070707070"));
                actual.Exercises[0].ExerciseName.Should().Be("Golden Deadlift");
                actual.Exercises[0].Series.Should().Be(4);
                actual.Exercises[0].Reps.Should().Be(5.5);
                actual.Exercises[0].Weight.Should().Be(140.25);
                actual.Exercises[0].Unit.Should().Be(WeightUnits.Pounds);
                AssertPayloadMetadata(actual, ParseTestId<LgymApi.Domain.ValueObjects.CorrelationScope>("60606060-6060-6060-6060-606060606060"), "training.completed");
            })).SetName("Email payload golden matrix: Training completed");

        yield return new TestCaseData(new EmailPayloadGoldenCase(
            "password recovery",
            new PasswordRecoveryEmailPayload
            {
                UserId = ParseTestId<LgymApi.Domain.Entities.User>("80808080-8080-8080-8080-808080808080"),
                TokenId = ParseTestId<LgymApi.Domain.Entities.PasswordResetToken>("90909090-9090-9090-9090-909090909090"),
                UserName = "Golden Recovery",
                RecipientEmail = "golden.recovery@example.test",
                ResetToken = "golden-reset-token",
                ResetUrl = "https://golden-sentinel.example.test/reset",
                CultureName = MalformedCultureName
            },
            new PasswordRecoveryEmailPayload
            {
                UserId = ParseTestId<LgymApi.Domain.Entities.User>("80808080-8080-8080-8080-808080808080"),
                TokenId = ParseTestId<LgymApi.Domain.Entities.PasswordResetToken>("90909090-9090-9090-9090-909090909090"),
                UserName = "Golden Recovery",
                RecipientEmail = "golden.recovery@example.test",
                ResetToken = "golden-reset-token",
                ResetUrl = null!,
                CultureName = MalformedCultureName
            },
            "resetUrl",
            """{"uSeRiD":"80808080-8080-8080-8080-808080808080","ToKeNiD":"90909090-9090-9090-9090-909090909090","UserName":"Golden Recovery","rEcIpIeNtEmAiL":"golden.recovery@example.test","ResetToken":"golden-reset-token","ResetUrl":"https://golden-sentinel.example.test/reset","CultureName":"!invalid-culture-380"}""",
            root =>
            {
                AssertCamelCaseProperties(root, "userId", "tokenId", "userName", "recipientEmail", "resetToken", "resetUrl", "cultureName");
                root.GetProperty("userId").GetString().Should().Be("80808080-8080-8080-8080-808080808080");
                root.GetProperty("tokenId").GetString().Should().Be("90909090-9090-9090-9090-909090909090");
            },
            payload =>
            {
                var actual = payload.Should().BeOfType<PasswordRecoveryEmailPayload>().Subject;
                actual.UserId.Should().Be(ParseTestId<LgymApi.Domain.Entities.User>("80808080-8080-8080-8080-808080808080"));
                actual.TokenId.Should().Be(ParseTestId<LgymApi.Domain.Entities.PasswordResetToken>("90909090-9090-9090-9090-909090909090"));
                actual.UserName.Should().Be("Golden Recovery");
                actual.RecipientEmail.Should().Be("golden.recovery@example.test");
                actual.ResetToken.Should().Be("golden-reset-token");
                actual.ResetUrl.Should().Be("https://golden-sentinel.example.test/reset");
                actual.CultureName.Should().Be(MalformedCultureName);
                AssertPayloadMetadata(actual, ParseTestId<LgymApi.Domain.ValueObjects.CorrelationScope>("90909090-9090-9090-9090-909090909090"), "user.password.recovery");
            })).SetName("Email payload golden matrix: Password recovery");
    }

    private static GoldenPayloadEvaluation EvaluateGoldenCase(EmailPayloadGoldenCase goldenCase)
    {
        var wire = JsonDocument.Parse(JsonSerializer.Serialize(goldenCase.Payload, goldenCase.Payload.GetType(), SharedSerializationOptions.Current));
        var roundtrippedPayload = goldenCase.Deserialize(wire.RootElement.GetRawText());
        var legacyPayload = goldenCase.Deserialize(goldenCase.LegacyJson);
        var nullWire = JsonDocument.Parse(JsonSerializer.Serialize(goldenCase.NullPayload, goldenCase.NullPayload.GetType(), SharedSerializationOptions.Current));

        return new GoldenPayloadEvaluation(wire, nullWire, roundtrippedPayload, legacyPayload);
    }

    private static void AssertCamelCaseProperties(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            element.TryGetProperty(propertyName, out _).Should().BeTrue();
            element.TryGetProperty(char.ToUpperInvariant(propertyName[0]) + propertyName[1..], out _).Should().BeFalse();
        }
    }

    public sealed record EmailPayloadGoldenCase(
        string Name,
        IEmailPayload Payload,
        IEmailPayload NullPayload,
        string NullPropertyName,
        string LegacyJson,
        Action<JsonElement> AssertWire,
        Action<IEmailPayload> AssertPayload)
    {
        public IEmailPayload Deserialize(string json)
        {
            return Payload switch
            {
                WelcomeEmailPayload => JsonSerializer.Deserialize<WelcomeEmailPayload>(json, SharedSerializationOptions.Current)!,
                InvitationEmailPayload => JsonSerializer.Deserialize<InvitationEmailPayload>(json, SharedSerializationOptions.Current)!,
                InvitationAcceptedEmailPayload => JsonSerializer.Deserialize<InvitationAcceptedEmailPayload>(json, SharedSerializationOptions.Current)!,
                InvitationRevokedEmailPayload => JsonSerializer.Deserialize<InvitationRevokedEmailPayload>(json, SharedSerializationOptions.Current)!,
                TrainingCompletedEmailPayload => JsonSerializer.Deserialize<TrainingCompletedEmailPayload>(json, SharedSerializationOptions.Current)!,
                PasswordRecoveryEmailPayload => JsonSerializer.Deserialize<PasswordRecoveryEmailPayload>(json, SharedSerializationOptions.Current)!,
                _ => throw new InvalidOperationException($"Unsupported golden payload type: {Payload.GetType().FullName}")
            };
        }
    }

    private sealed record GoldenPayloadEvaluation(
        JsonDocument Wire,
        JsonDocument NullWire,
        IEmailPayload RoundtrippedPayload,
        IEmailPayload LegacyPayload);

    private static (string Json, TPayload Payload) SerializeAndDeserialize<TPayload>(TPayload fixture)
    {
        var json = JsonSerializer.Serialize(fixture, SharedSerializationOptions.Current);
        var payload = JsonSerializer.Deserialize<TPayload>(json, SharedSerializationOptions.Current);

        payload.Should().NotBeNull();
        return (json, payload!);
    }

    private static void AssertPayloadMetadata(
        IEmailPayload payload,
        Id<LgymApi.Domain.ValueObjects.CorrelationScope> expectedCorrelationId,
        string expectedNotificationType)
    {
        payload.CorrelationId.Should().Be(expectedCorrelationId);
        payload.NotificationType.Value.Should().Be(expectedNotificationType);
        payload.Culture.Name.Should().Be("en-US");
    }

    #endregion
}



