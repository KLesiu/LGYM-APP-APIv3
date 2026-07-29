using System.Globalization;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Notifications.Contracts.Events;
using LgymApi.Application.Notifications.Models;
using LgymApi.Application.Options;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;

namespace LgymApi.Application.Notifications;

internal sealed class CoachingNotificationIntentService(
    IInAppNotificationService inAppNotificationService,
    IEmailNotificationLogRepository emailNotificationLogRepository,
    ICoachingEmailNotificationFeature emailNotificationFeature,
    AppDefaultsOptions appDefaultsOptions) : ICoachingNotificationIntentService
{
    private const int MaximumEmailAttempts = 5;
    public Task<CoachingNotificationIntentResult> SubmitAsync(
        CoachingNotificationIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return intent switch
        {
            InvitationCreatedCoachingNotificationIntent created => SubmitInvitationCreatedAsync(created, cancellationToken),
            InvitationAcceptedCoachingNotificationIntent accepted => SubmitInvitationAcceptedAsync(accepted, cancellationToken),
            InvitationRejectedCoachingNotificationIntent rejected => SubmitInvitationRejectedAsync(rejected, cancellationToken),
            InvitationRevokedCoachingNotificationIntent revoked => SubmitInvitationRevokedAsync(revoked, cancellationToken),
            RelationshipEndedCoachingNotificationIntent ended => SubmitRelationshipEndedAsync(ended, cancellationToken),
            TraineeNoteUpdatedCoachingNotificationIntent noteUpdated => SubmitTraineeNoteUpdatedAsync(noteUpdated, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(intent), intent.GetType(), "Unsupported Coaching notification intent.")
        };
    }

    private async Task<CoachingNotificationIntentResult> SubmitInvitationCreatedAsync(
        InvitationCreatedCoachingNotificationIntent intent,
        CancellationToken cancellationToken)
    {
        EnsureEligibleChannel(intent, CoachingNotificationLegacyChannel.Email, CoachingNotificationLegacyChannel.InApp);

        if (intent.EligibleLegacyChannel == CoachingNotificationLegacyChannel.InApp)
        {
            if (!intent.TraineeId.HasValue)
            {
                throw new ArgumentException("An in-app invitation-created intent requires a trainee recipient.", nameof(intent));
            }

            var trainerName = DisplayNameOrFallback(intent.Trainer, Messages.GenericTrainerDisplayName);
            return await CreateInAppAsync(
                new CreateInAppNotificationInput(
                    intent.TraineeId.Value,
                    intent.TrainerId,
                    $"trainer-invitation:{intent.InvitationId}:sent",
                    false,
                    RenderMessage(null, () => string.Format(Messages.TrainerInvitationCreatedNotification, trainerName)),
                    $"/trainers/invitations/{intent.InvitationId}",
                    InAppNotificationTypes.InvitationSent),
                cancellationToken);
        }
        if (!emailNotificationFeature.Enabled || intent.Trainer is null)
        {
            return NoDelivery();
        }
        var recipient = intent.TraineeId.HasValue ? intent.Trainee?.Email : intent.InviteeEmail;
        if (intent.TraineeId.HasValue && intent.Trainee is null || string.IsNullOrWhiteSpace(recipient))
        {
            return NoDelivery();
        }
        if (!await CanScheduleEmailAsync(EmailNotificationTypes.TrainerInvitation, intent.InvitationId.Rebind<CorrelationScope>(), recipient, cancellationToken))
        {
            return NoDelivery();
        }

        return new CoachingNotificationIntentResult(
            new CoachingEmailSchedulingRequest(
                CoachingEmailSchedulingKind.InvitationCreated,
                EmailNotificationTypes.TrainerInvitation,
                intent.InvitationId,
                intent.InvitationId.Rebind<CorrelationScope>(),
                recipient,
                ResolveCultureName(intent.Trainer.PreferredLanguage),
                ResolveTimeZoneName(intent.Trainee?.PreferredTimeZone),
                intent.Trainer.Name,
                null,
                intent.InvitationCode,
                intent.ExpiresAt),
            null);
    }

    private async Task<CoachingNotificationIntentResult> SubmitInvitationAcceptedAsync(
        InvitationAcceptedCoachingNotificationIntent intent,
        CancellationToken cancellationToken)
    {
        EnsureEligibleChannel(intent, CoachingNotificationLegacyChannel.Email, CoachingNotificationLegacyChannel.InApp);

        if (intent.EligibleLegacyChannel == CoachingNotificationLegacyChannel.InApp)
        {
            return await CreateInAppAsync(
                new CreateInAppNotificationInput(
                    intent.TrainerId,
                    intent.TraineeId,
                    $"trainer-invitation:{intent.InvitationId}:accepted",
                    false,
                    RenderMessage(null, () => Messages.TrainerInvitationAccepted),
                    $"/trainer/members/{intent.TraineeId}",
                    InAppNotificationTypes.InvitationAccepted),
                cancellationToken);
        }

        if (!emailNotificationFeature.Enabled || intent.Trainer is null || intent.Trainee is null || string.IsNullOrWhiteSpace(intent.Trainer.Email))
        {
            return NoDelivery();
        }

        if (!await CanScheduleEmailAsync(EmailNotificationTypes.TrainerInvitationAccepted, intent.InvitationId.Rebind<CorrelationScope>(), intent.Trainer.Email, cancellationToken))
        {
            return NoDelivery();
        }

        return new CoachingNotificationIntentResult(
            new CoachingEmailSchedulingRequest(
                CoachingEmailSchedulingKind.InvitationAccepted,
                EmailNotificationTypes.TrainerInvitationAccepted,
                intent.InvitationId,
                intent.InvitationId.Rebind<CorrelationScope>(),
                intent.Trainer.Email,
                ResolveCultureName(intent.Trainer.PreferredLanguage),
                ResolveTimeZoneName(intent.Trainer.PreferredTimeZone),
                intent.Trainer.Name,
                intent.Trainee.Name,
                null,
                null),
            null);
    }

    private async Task<CoachingNotificationIntentResult> SubmitInvitationRejectedAsync(
        InvitationRejectedCoachingNotificationIntent intent,
        CancellationToken cancellationToken)
    {
        EnsureEligibleChannel(intent, CoachingNotificationLegacyChannel.InApp);
        return await CreateInAppAsync(
            new CreateInAppNotificationInput(
                intent.TrainerId,
                intent.TraineeId,
                $"trainer-invitation:{intent.InvitationId}:rejected",
                false,
                RenderMessage(null, () => Messages.TrainerInvitationRejected),
                "/trainer/invitations",
                InAppNotificationTypes.InvitationRejected),
            cancellationToken);
    }

    private async Task<CoachingNotificationIntentResult> SubmitInvitationRevokedAsync(
        InvitationRevokedCoachingNotificationIntent intent,
        CancellationToken cancellationToken)
    {
        EnsureEligibleChannel(intent, CoachingNotificationLegacyChannel.Email);
        if (!emailNotificationFeature.Enabled || intent.Trainer is null || string.IsNullOrWhiteSpace(intent.InviteeEmail))
        {
            return NoDelivery();
        }

        if (!await CanScheduleEmailAsync(EmailNotificationTypes.TrainerInvitationRevoked, intent.InvitationId.Rebind<CorrelationScope>(), intent.InviteeEmail, cancellationToken))
        {
            return NoDelivery();
        }

        return new CoachingNotificationIntentResult(
            new CoachingEmailSchedulingRequest(
                CoachingEmailSchedulingKind.InvitationRevoked,
                EmailNotificationTypes.TrainerInvitationRevoked,
                intent.InvitationId,
                intent.InvitationId.Rebind<CorrelationScope>(),
                intent.InviteeEmail,
                ResolveCultureName(null),
                ResolveTimeZoneName(null),
                intent.Trainer.Name,
                null,
                null,
                null),
            null);
    }

    private async Task<CoachingNotificationIntentResult> SubmitRelationshipEndedAsync(
        RelationshipEndedCoachingNotificationIntent intent,
        CancellationToken cancellationToken)
    {
        EnsureEligibleChannel(intent, CoachingNotificationLegacyChannel.InApp);
        var traineeName = DisplayNameOrFallback(intent.Trainee, Messages.GenericTraineeDisplayName);
        return await CreateInAppAsync(
            new CreateInAppNotificationInput(
                intent.TrainerId,
                intent.TraineeId,
                $"trainer-relationship-ended:{intent.TrainerId}:{intent.TraineeId}",
                false,
                RenderMessage(intent.Trainer?.PreferredLanguage, () => string.Format(Messages.TrainerRelationshipEnded, traineeName)),
                "/trainer/members",
                InAppNotificationTypes.TrainerRelationshipEnded),
            cancellationToken);
    }

    private async Task<CoachingNotificationIntentResult> SubmitTraineeNoteUpdatedAsync(
        TraineeNoteUpdatedCoachingNotificationIntent intent,
        CancellationToken cancellationToken)
    {
        EnsureEligibleChannel(intent, CoachingNotificationLegacyChannel.InApp);
        var trainerName = DisplayNameOrFallback(intent.Trainer, Messages.GenericTrainerDisplayName);
        var noteTitle = string.IsNullOrWhiteSpace(intent.NoteTitle)
            ? Messages.GenericTrainerNoteDisplayName
            : intent.NoteTitle.Trim();

        return await CreateInAppAsync(
            new CreateInAppNotificationInput(
                intent.TraineeId,
                intent.TrainerId,
                $"trainee-note:{intent.TraineeNoteId}:{intent.TriggeredAt:O}",
                false,
                RenderMessage(intent.Trainee?.PreferredLanguage, () => string.Format(Messages.TrainerTraineeNoteUpdated, trainerName, noteTitle)),
                $"/trainer/notes/{intent.TraineeNoteId}",
                InAppNotificationTypes.TraineeNoteUpdated),
            cancellationToken);
    }

    private async Task<CoachingNotificationIntentResult> CreateInAppAsync(
        CreateInAppNotificationInput input,
        CancellationToken cancellationToken)
    {
        var result = await inAppNotificationService.CreateAsync(input, cancellationToken);
        return new CoachingNotificationIntentResult(null, result.IsFailure ? result.Error : null);
    }

    private async Task<bool> CanScheduleEmailAsync(
        EmailNotificationType notificationType,
        Id<CorrelationScope> correlationId,
        string recipient,
        CancellationToken cancellationToken)
    {
        var existing = await emailNotificationLogRepository.FindByCorrelationAsync(notificationType, correlationId, recipient, cancellationToken);
        return existing is null || existing.Status == EmailNotificationStatus.Failed && existing.Attempts < MaximumEmailAttempts;
    }

    private CoachingNotificationIntentResult NoDelivery() => new(null, null);

    private static void EnsureEligibleChannel(CoachingNotificationIntent intent, params CoachingNotificationLegacyChannel[] eligibleChannels)
    {
        if (!eligibleChannels.Contains(intent.EligibleLegacyChannel))
        {
            throw new ArgumentException($"Channel '{intent.EligibleLegacyChannel}' is not eligible for {intent.GetType().Name}.", nameof(intent));
        }
    }

    private string RenderMessage(string? preferredLanguage, Func<string> render)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = ResolveCulture(preferredLanguage);
            return render();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    private string ResolveCultureName(string? preferredLanguage) => ResolveCulture(preferredLanguage).Name;

    private CultureInfo ResolveCulture(string? preferredLanguage)
    {
        var cultureName = string.IsNullOrWhiteSpace(preferredLanguage)
            ? appDefaultsOptions.PreferredLanguage
            : preferredLanguage;

        try
        {
            return CultureInfo.GetCultureInfo(cultureName);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo(appDefaultsOptions.PreferredLanguage);
        }
    }

    private string ResolveTimeZoneName(string? preferredTimeZone)
    {
        return string.IsNullOrWhiteSpace(preferredTimeZone)
            ? appDefaultsOptions.PreferredTimeZone
            : preferredTimeZone;
    }

    private static string DisplayNameOrFallback(AccountReadModel? account, string fallback)
    {
        return string.IsNullOrWhiteSpace(account?.Name) ? fallback : account.Name;
    }
}
