using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Coaching.Progress.MainRecordsHistory;

public sealed record GetMainRecordsHistoryQuery(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId);
