using System.Globalization;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Application.Notifications.Models;
using LgymApi.Application.Options;
using LgymApi.Domain.Notifications;
using LgymApi.Resources;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications.InApp;

internal sealed class DietPlanUpdatedInAppNotificationDeliveryService(
    IInAppNotificationService notifications,
    IAccountReadService accounts,
    AppDefaultsOptions defaults,
    ILogger<DietPlanUpdatedInAppNotificationDeliveryService> logger) : IDietPlanUpdatedInAppNotificationDeliveryPort
{
    public async Task DeliverAsync(DietPlanUpdatedInAppNotificationDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        if (!LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.DietPlan>.TryParse(request.DietPlanId, out var dietPlanId)
            || !LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User>.TryParse(request.TraineeId, out var traineeId)
            || !LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User>.TryParse(request.TrainerId, out var trainerId)) return;
        var trainer = await accounts.GetByIdAsync(trainerId, cancellationToken);
        var trainee = await accounts.GetByIdAsync(traineeId, cancellationToken);
        var priorCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = ResolveCulture(trainee?.PreferredLanguage);
            var trainerName = string.IsNullOrWhiteSpace(trainer?.Name) ? Messages.GenericTrainerDisplayName : trainer.Name;
            var planName = string.IsNullOrWhiteSpace(request.DietPlanName) ? Messages.GenericDietPlanDisplayName : request.DietPlanName.Trim();
            var result = await notifications.CreateAsync(new CreateInAppNotificationInput(
                traineeId, trainerId, $"diet-plan:{dietPlanId}:{request.TriggeredAt:O}", false,
                string.Format(Messages.TrainerDietPlanUpdated, trainerName, planName), $"/trainer/diet-plans/{dietPlanId}", InAppNotificationTypes.DietPlanUpdated), cancellationToken);
            if (result.IsFailure) logger.LogError("Failed to create diet-plan notification for trainee {TraineeId}: {Error}", traineeId, result.Error);
        }
        finally { CultureInfo.CurrentUICulture = priorCulture; }
    }

    private CultureInfo ResolveCulture(string? preferredLanguage)
    {
        var cultureName = string.IsNullOrWhiteSpace(preferredLanguage) ? defaults.PreferredLanguage : preferredLanguage;
        try { return CultureInfo.GetCultureInfo(cultureName); }
        catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo(defaults.PreferredLanguage); }
    }
}
