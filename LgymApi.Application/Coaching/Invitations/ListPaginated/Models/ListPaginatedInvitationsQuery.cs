using LgymApi.Application.Pagination;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.Invitations.ListPaginated;

public sealed record ListPaginatedInvitationsQuery(Id<UserEntity> TrainerId, FilterInput Filter);
