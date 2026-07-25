using LgymApi.Domain.ValueObjects;

namespace LgymApi.Domain.Entities;

public sealed class SupplementPlan : EntityBase<SupplementPlan>
{
    public Id<User> TrainerId { get; set; }
    public Id<User> TraineeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsActive { get; set; }

    public User Trainer { get; set; } = null!;
    public User Trainee { get; set; } = null!;
    public ICollection<SupplementPlanItem> Items { get; set; } = new List<SupplementPlanItem>();
}
