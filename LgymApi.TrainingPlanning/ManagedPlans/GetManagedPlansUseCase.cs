using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Resources;
using PlanEntity = LgymApi.Domain.Entities.Plan;

namespace LgymApi.Application.TrainingPlanning.ManagedPlans;

internal sealed class GetManagedPlansUseCase : IGetManagedPlansUseCase
{
    private readonly IPlanRepository _planRepository;
    private readonly IMapper _mapper;

    public GetManagedPlansUseCase(IPlanRepository planRepository, IMapper mapper)
    {
        _planRepository = planRepository;
        _mapper = mapper;
    }

    public async Task<Result<IReadOnlyList<ManagedPlanReadModel>, AppError>> ExecuteAsync(
        GetManagedPlansQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query is null || query.TraineeId.IsEmpty)
        {
            return Result<IReadOnlyList<ManagedPlanReadModel>, AppError>.Failure(
                new InvalidPlanError(Messages.UserIdRequired));
        }

        var plans = await _planRepository.GetByUserIdAsync(query.TraineeId, cancellationToken);
        var result = _mapper.MapList<PlanEntity, ManagedPlanReadModel>(
            plans.OrderByDescending(plan => plan.CreatedAt),
            _mapper.CreateContext());

        return Result<IReadOnlyList<ManagedPlanReadModel>, AppError>.Success(result);
    }
}
