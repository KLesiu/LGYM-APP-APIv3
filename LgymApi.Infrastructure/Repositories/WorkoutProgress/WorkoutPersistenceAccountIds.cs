using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Infrastructure.Repositories.WorkoutProgress;

internal static class WorkoutPersistenceAccountIds
{
    public static Id<User> ToPersisted(Id<AccountReference> accountId) => accountId.Rebind<User>();

    public static Id<AccountReference> ToContract(Id<User> userId) => userId.Rebind<AccountReference>();
}
