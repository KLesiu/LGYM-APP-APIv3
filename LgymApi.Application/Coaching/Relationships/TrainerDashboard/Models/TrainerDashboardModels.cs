using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.Relationships.TrainerDashboard;

public sealed record GetTrainerDashboardQuery(
    Id<UserEntity> TrainerId,
    string? Search = null,
    string? Status = null,
    string? SortBy = null,
    string? SortDirection = null,
    int Page = 1,
    int PageSize = 20);

public enum TrainerDashboardTraineeStatus
{
    Linked,
    InvitationPending,
    InvitationExpired,
    InvitationRejected,
    InvitationAccepted,
    NoRelationship
}

public sealed record TrainerDashboardTraineeReadModel(
    Id<UserEntity> Id,
    string Name,
    string Email,
    string? Avatar,
    TrainerDashboardTraineeStatus Status,
    bool IsLinked,
    bool HasPendingInvitation,
    bool HasExpiredInvitation,
    DateTimeOffset? LinkedAt,
    DateTimeOffset? LastInvitationExpiresAt,
    DateTimeOffset? LastInvitationRespondedAt,
    DateTimeOffset CreatedAt,
    int StatusOrder);
