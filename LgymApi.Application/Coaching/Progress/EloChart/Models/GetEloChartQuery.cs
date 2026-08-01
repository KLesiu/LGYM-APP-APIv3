using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Coaching.Progress.EloChart;

public sealed record GetEloChartQuery(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId);
