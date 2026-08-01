using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Enums;

namespace LgymApi.Application.Coaching.Relationships.TrainerDashboard;

public sealed class TrainerDashboardMappingProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<TrainerDashboardSource, TrainerDashboardTraineeReadModel>((source, _) =>
        {
            var linkedAt = source.Fact.ActiveLink?.CreatedAt;
            var invitation = source.Fact.LatestInvitation;
            var isLinked = linkedAt.HasValue;
            var hasPendingInvitation = !isLinked
                && invitation?.Status == TrainerInvitationStatus.Pending
                && invitation.ExpiresAt > source.Now;
            var hasExpiredInvitation = !isLinked
                && (invitation?.Status == TrainerInvitationStatus.Expired
                    || (invitation?.Status == TrainerInvitationStatus.Pending
                        && invitation.ExpiresAt <= source.Now));

            return new TrainerDashboardTraineeReadModel(
                source.Account.Id,
                source.Account.Name,
                source.Account.Email,
                source.Account.Avatar,
                ResolveStatus(isLinked, invitation, source.Now),
                isLinked,
                hasPendingInvitation,
                hasExpiredInvitation,
                linkedAt,
                invitation?.ExpiresAt,
                invitation?.RespondedAt,
                source.Account.CreatedAt,
                ResolveStatusOrder(isLinked, invitation, source.Now));
        });
    }

    private static TrainerDashboardTraineeStatus ResolveStatus(
        bool isLinked,
        CoachingInvitationFact? invitation,
        DateTimeOffset now)
    {
        if (isLinked)
        {
            return TrainerDashboardTraineeStatus.Linked;
        }

        if (invitation is null)
        {
            return TrainerDashboardTraineeStatus.NoRelationship;
        }

        return invitation.Status switch
        {
            TrainerInvitationStatus.Accepted => TrainerDashboardTraineeStatus.InvitationAccepted,
            TrainerInvitationStatus.Rejected => TrainerDashboardTraineeStatus.InvitationRejected,
            TrainerInvitationStatus.Expired => TrainerDashboardTraineeStatus.InvitationExpired,
            TrainerInvitationStatus.Pending when invitation.ExpiresAt <= now => TrainerDashboardTraineeStatus.InvitationExpired,
            TrainerInvitationStatus.Pending => TrainerDashboardTraineeStatus.InvitationPending,
            _ => TrainerDashboardTraineeStatus.NoRelationship
        };
    }

    private static int ResolveStatusOrder(
        bool isLinked,
        CoachingInvitationFact? invitation,
        DateTimeOffset now)
    {
        if (isLinked)
        {
            return 0;
        }

        return invitation?.Status switch
        {
            TrainerInvitationStatus.Pending when invitation.ExpiresAt > now => 1,
            TrainerInvitationStatus.Pending => 2,
            TrainerInvitationStatus.Expired => 2,
            TrainerInvitationStatus.Rejected => 3,
            TrainerInvitationStatus.Accepted => 4,
            null => 5,
            _ => 6
        };
    }
}

internal sealed record TrainerDashboardSource(
    CoachingDashboardFact Fact,
    AccountReadModel Account,
    DateTimeOffset Now);
