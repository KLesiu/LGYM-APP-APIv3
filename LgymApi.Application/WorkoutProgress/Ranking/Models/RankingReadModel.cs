namespace LgymApi.Application.WorkoutProgress.Ranking.Models;

public sealed record RankingReadModel(
    string Name,
    string? Avatar,
    int Elo,
    string ProfileRank);
