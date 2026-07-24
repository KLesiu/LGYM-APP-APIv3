using System.Text.Json;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Nutrition.DietPlans;

internal sealed class DietPlanHistorySnapshotFactory
{
    private readonly IMapper _mapper;

    public DietPlanHistorySnapshotFactory(IMapper mapper)
    {
        _mapper = mapper;
    }

    public DietPlanHistory Create(DietPlan plan, Id<UserEntity> changedByUserId, string changeType)
    {
        var snapshot = _mapper.Map<DietPlan, DietPlanReadModel>(plan, _mapper.CreateContext());

        return new DietPlanHistory
        {
            Id = Id<DietPlanHistory>.New(),
            DietPlanId = plan.Id,
            ChangedByUserId = changedByUserId,
            ChangeDate = DateTimeOffset.UtcNow,
            ChangeType = changeType,
            SnapshotJson = JsonSerializer.Serialize(snapshot)
        };
    }
}
