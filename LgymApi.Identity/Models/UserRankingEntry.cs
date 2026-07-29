using LgymApi.Domain.Entities;

namespace LgymApi.Application.Models;

public sealed record UserRankingEntry(User User, int Elo);
