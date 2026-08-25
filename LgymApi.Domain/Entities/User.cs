using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Domain.Entities;

public sealed class User : EntityBase<User>
{
    public string Name { get; set; } = string.Empty;
    public Email Email { get; set; } = new("placeholder@example.com");
    public string PreferredLanguage { get; set; } = string.Empty;
    public string PreferredTimeZone { get; set; } = string.Empty;
    public Id<Plan>? PlanId { get; set; }
    public string ProfileRank { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public bool IsVisibleInRanking { get; set; } = true;
    public bool IsBlocked { get; set; }
    public DateTimeOffset? AdultConfirmedAt { get; set; }
    public string? AdultConfirmationVersion { get; set; }

    public string? LegacyHash { get; set; }
    public string? LegacySalt { get; set; }
    public int? LegacyIterations { get; set; }
    public int? LegacyKeyLength { get; set; }
    public string? LegacyDigest { get; set; }

    public Plan? Plan { get; set; }
    public ICollection<Plan> Plans { get; set; } = new List<Plan>();
    public ICollection<Exercise> Exercises { get; set; } = new List<Exercise>();
    public ICollection<Training> Trainings { get; set; } = new List<Training>();
    public ICollection<Measurement> Measurements { get; set; } = new List<Measurement>();
    public ICollection<MainRecord> MainRecords { get; set; } = new List<MainRecord>();
    public ICollection<EloRegistry> EloRegistries { get; set; } = new List<EloRegistry>();
    public ICollection<UserExternalLogin> ExternalLogins { get; set; } = new List<UserExternalLogin>();
    public ICollection<Gym> Gyms { get; set; } = new List<Gym>();
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
