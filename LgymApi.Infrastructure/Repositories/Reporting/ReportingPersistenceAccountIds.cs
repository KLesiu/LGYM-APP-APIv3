using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Infrastructure.Repositories.Reporting;

internal static class ReportingPersistenceAccountIds
{
    public static Id<User> ToPersisted(Id<AccountReference> accountId) => accountId.Rebind<User>();

    public static Id<AccountReference> ToReference(Id<User> userId) => userId.Rebind<AccountReference>();
}
