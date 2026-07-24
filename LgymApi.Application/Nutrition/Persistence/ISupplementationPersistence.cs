using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Nutrition.Persistence;

public interface ISupplementationPersistence
{
    Task AddPlanAsync(SupplementPlan plan, CancellationToken cancellationToken = default);
    Task<SupplementPlan?> FindTrackedPlanByIdAsync(Id<SupplementPlan> planId, CancellationToken cancellationToken = default);
    Task<List<SupplementPlan>> ListTrackedPlansByTrainerAndTraineeAsync(Id<User> trainerId, Id<User> traineeId, CancellationToken cancellationToken = default);
    Task<SupplementPlan?> GetTrackedActivePlanForTraineeAsync(Id<User> traineeId, CancellationToken cancellationToken = default);
    Task<SupplementIntakeLog?> FindTrackedIntakeLogAsync(Id<User> traineeId, Id<SupplementPlanItem> planItemId, DateOnly intakeDate, CancellationToken cancellationToken = default);
    Task<List<SupplementPlan>> ListPlansByTrainerAndTraineeAsync(Id<User> trainerId, Id<User> traineeId, CancellationToken cancellationToken = default);
    Task<SupplementPlan?> GetActivePlanForTraineeAsync(Id<User> traineeId, CancellationToken cancellationToken = default);
    Task<List<SupplementIntakeLog>> ListIntakeLogsForPlanAsync(Id<User> traineeId, Id<SupplementPlan> planId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
    Task<SupplementIntakeLog?> FindIntakeLogAsync(Id<User> traineeId, Id<SupplementPlanItem> planItemId, DateOnly intakeDate, CancellationToken cancellationToken = default);
    Task AddIntakeLogAsync(SupplementIntakeLog intakeLog, CancellationToken cancellationToken = default);
}
