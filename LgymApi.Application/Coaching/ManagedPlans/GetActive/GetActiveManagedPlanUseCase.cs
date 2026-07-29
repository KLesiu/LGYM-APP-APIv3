using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Resources;
using OwnerGetActiveQuery = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.GetActiveAssignedPlanQuery;
using OwnerGetActiveUseCase = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.IGetActiveAssignedPlanUseCase;

namespace LgymApi.Application.Coaching.ManagedPlans.GetActive;

internal sealed class GetActiveManagedPlanUseCase : IGetActiveManagedPlanUseCase
{
    private readonly ICoachingActiveLinkPersistence _activeLinks;
    private readonly OwnerGetActiveUseCase _owner;
    private readonly IMapper _mapper;

    public GetActiveManagedPlanUseCase(
        ICoachingActiveLinkPersistence activeLinks,
        OwnerGetActiveUseCase owner,
        IMapper mapper)
    {
        _activeLinks = activeLinks;
        _owner = owner;
        _mapper = mapper;
    }

    public async Task<Result<ManagedPlanReadModel, AppError>> ExecuteAsync(
        GetActiveManagedPlanQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!await _activeLinks.HasForTraineeAsync(query.TraineeId, cancellationToken))
        {
            return Result<ManagedPlanReadModel, AppError>.Failure(
                new TrainerRelationshipNotFoundError(Messages.DidntFind));
        }

        var ownerQuery = _mapper.Map<GetActiveManagedPlanQuery, OwnerGetActiveQuery>(query, _mapper.CreateContext());
        return await _owner.ExecuteAsync(ownerQuery, cancellationToken);
    }
}
