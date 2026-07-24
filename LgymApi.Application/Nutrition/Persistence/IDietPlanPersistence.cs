using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Nutrition.Persistence;

public interface IDietPlanPersistence
{
    Task AddPlanAsync(DietPlan plan, CancellationToken cancellationToken = default);
    Task AddHistoryEntryAsync(DietPlanHistory historyEntry, CancellationToken cancellationToken = default);
    Task<DietPlan?> FindTrackedPlanByIdAsync(Id<DietPlan> planId, CancellationToken cancellationToken = default);
    Task<DietPlan?> GetPlanByIdAsync(Id<DietPlan> planId, CancellationToken cancellationToken = default);
    Task<List<DietPlan>> ListPlansByTrainerAndTraineeAsync(Id<User> trainerId, Id<User> traineeId, CancellationToken cancellationToken = default);
    Task<List<DietPlan>> ListActivePlansForTraineeAsync(Id<User> traineeId, CancellationToken cancellationToken = default);
    Task<DietPlan?> GetActivePlanForTraineeAsync(Id<User> traineeId, CancellationToken cancellationToken = default);
    Task<List<DietPlanHistory>> ListPlanHistoryAsync(Id<DietPlan> planId, CancellationToken cancellationToken = default);
}
