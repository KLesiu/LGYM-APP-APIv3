using System.Globalization;
using System.Text.Json.Serialization;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.BackgroundWorker.Common.Notifications.Models;

public sealed class TrainingCompletedEmailPayload : IEmailPayload
{
    public LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User> UserId { get; init; }
    public LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.Training> TrainingId { get; init; }
    public string RecipientEmail { get; init; } = string.Empty;
    public string CultureName { get; init; } = string.Empty;
    public string PreferredTimeZone { get; init; } = string.Empty;
    public string PlanDayName { get; init; } = string.Empty;
    public DateTimeOffset TrainingDate { get; init; }
    public IReadOnlyList<TrainingExerciseSummary> Exercises { get; init; } = Array.Empty<TrainingExerciseSummary>();

    public Id<CorrelationScope> CorrelationId => TrainingId.Rebind<CorrelationScope>();
    public EmailNotificationType NotificationType => Domain.Notifications.EmailNotificationTypes.TrainingCompleted;

    [JsonIgnore]
    public CultureInfo Culture
    {
        get
        {
            try
            {
                return CultureInfo.GetCultureInfo(CultureName);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.GetCultureInfo("en-US");
            }
        }
    }
}

public sealed class TrainingExerciseSummary
{
    public Id<Exercise> ExerciseId { get; init; }
    public string ExerciseName { get; init; } = string.Empty;
    public int Series { get; init; }
    public double Reps { get; init; }
    public double Weight { get; init; }
    public WeightUnits Unit { get; init; }
}
