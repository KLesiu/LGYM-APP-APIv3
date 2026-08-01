using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Identity.Contracts.Accounts;

public sealed record AccountReadModel(
    Id<UserEntity> Id,
    string Name,
    string Email,
    string? Avatar,
    string PreferredLanguage,
    string PreferredTimeZone,
    DateTimeOffset CreatedAt = default);
