using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using OwnerUnassignCommand = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.UnassignManagedPlanCommand;
using OwnerUnassignUseCase = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.IUnassignManagedPlanUseCase;

namespace LgymApi.Application.Coaching.ManagedPlans.Unassign;

internal sealed class UnassignTraineeManagedPlanUseCase : IUnassignTraineeManagedPlanUseCase
{
    private readonly IMarkerCoachingRelationshipAccessService _relationshipAccess;
    private readonly OwnerUnassignUseCase _owner;
    private readonly IMapper _mapper;

    public UnassignTraineeManagedPlanUseCase(
        IMarkerCoachingRelationshipAccessService relationshipAccess,
        OwnerUnassignUseCase owner,
        IMapper mapper)
    {
        _relationshipAccess = relationshipAccess;
        _owner = owner;
        _mapper = mapper;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(
        UnassignTraineeManagedPlanCommand command,
        CancellationToken cancellationToken = default)
    {
        var access = await _relationshipAccess.GetAccessDecisionAsync(
            command.TrainerId,
            command.TraineeId,
            cancellationToken);
        var accessError = ManagedPlanAccess.GetError(access, command.TraineeId);
        if (accessError is not null)
        {
            return Result<Unit, AppError>.Failure(accessError);
        }

        var ownerCommand = _mapper.Map<UnassignTraineeManagedPlanCommand, OwnerUnassignCommand>(command, _mapper.CreateContext());
        return await _owner.ExecuteAsync(ownerCommand, cancellationToken);
    }
}
