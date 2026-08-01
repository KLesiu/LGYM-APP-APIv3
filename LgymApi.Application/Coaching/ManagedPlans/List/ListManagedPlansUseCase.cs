using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using OwnerListQuery = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.GetManagedPlansQuery;
using OwnerListUseCase = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.IGetManagedPlansUseCase;

namespace LgymApi.Application.Coaching.ManagedPlans.List;

internal sealed class ListManagedPlansUseCase : IListManagedPlansUseCase
{
    private readonly IMarkerCoachingRelationshipAccessService _relationshipAccess;
    private readonly OwnerListUseCase _owner;
    private readonly IMapper _mapper;

    public ListManagedPlansUseCase(
        IMarkerCoachingRelationshipAccessService relationshipAccess,
        OwnerListUseCase owner,
        IMapper mapper)
    {
        _relationshipAccess = relationshipAccess;
        _owner = owner;
        _mapper = mapper;
    }

    public async Task<Result<IReadOnlyList<ManagedPlanReadModel>, AppError>> ExecuteAsync(
        ListManagedPlansQuery query,
        CancellationToken cancellationToken = default)
    {
        var access = await _relationshipAccess.GetAccessDecisionAsync(
            query.TrainerId,
            query.TraineeId,
            cancellationToken);
        var accessError = ManagedPlanAccess.GetError(access, query.TraineeId);
        if (accessError is not null)
        {
            return Result<IReadOnlyList<ManagedPlanReadModel>, AppError>.Failure(accessError);
        }

        var ownerQuery = _mapper.Map<ListManagedPlansQuery, OwnerListQuery>(query, _mapper.CreateContext());
        return await _owner.ExecuteAsync(ownerQuery, cancellationToken);
    }
}
