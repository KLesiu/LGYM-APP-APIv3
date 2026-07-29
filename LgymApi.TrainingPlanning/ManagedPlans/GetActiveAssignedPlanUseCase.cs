using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Resources;
using PlanEntity = LgymApi.Domain.Entities.Plan;

namespace LgymApi.Application.TrainingPlanning.ManagedPlans;

internal sealed class GetActiveAssignedPlanUseCase : IGetActiveAssignedPlanUseCase
{
    private readonly IPlanRepository _planRepository;
    private readonly IMapper _mapper;

    public GetActiveAssignedPlanUseCase(IPlanRepository planRepository, IMapper mapper)
    {
        _planRepository = planRepository;
        _mapper = mapper;
    }

    public async Task<Result<ManagedPlanReadModel, AppError>> ExecuteAsync(
        GetActiveAssignedPlanQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query is null || query.TraineeId.IsEmpty)
        {
            return Result<ManagedPlanReadModel, AppError>.Failure(
                new InvalidPlanError(Messages.UserIdRequired));
        }

        var activePlan = await _planRepository.FindActiveByUserIdAsync(query.TraineeId, cancellationToken);
        if (activePlan is null)
        {
            return Result<ManagedPlanReadModel, AppError>.Failure(
                new PlanNotFoundError(Messages.DidntFind));
        }

        return Result<ManagedPlanReadModel, AppError>.Success(
            _mapper.Map<PlanEntity, ManagedPlanReadModel>(activePlan, _mapper.CreateContext()));
    }
}
