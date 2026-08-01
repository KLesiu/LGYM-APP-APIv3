using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.EloRegistry.Models;

public sealed class EloRegistryChartEntry
{
    public Id<LgymApi.Domain.Entities.EloRegistry> Id { get; init; }
    public int Value { get; init; }
    public string Date { get; init; } = string.Empty;
}
