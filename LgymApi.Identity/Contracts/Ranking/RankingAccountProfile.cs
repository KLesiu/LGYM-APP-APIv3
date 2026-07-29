using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Identity.Contracts.Ranking;

public sealed record RankingAccountProfile(
    Id<AccountReference> Id,
    string Name,
    string? Avatar,
    string ProfileRank);
