using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using OwnerDeleteCommand = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.DeleteManagedPlanCommand;
using OwnerDeleteUseCase = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.IDeleteManagedPlanUseCase;

namespace LgymApi.Application.Coaching.ManagedPlans.Delete;

internal sealed class DeleteTraineeManagedPlanUseCase : IDeleteTraineeManagedPlanUseCase
{
    private readonly IMarkerCoachingRelationshipAccessService _relationshipAccess;
    private readonly OwnerDeleteUseCase _owner;
    private readonly IMapper _mapper;

    public DeleteTraineeManagedPlanUseCase(
        IMarkerCoachingRelationshipAccessService relationshipAccess,
        OwnerDeleteUseCase owner,
        IMapper mapper)
    {
        _relationshipAccess = relationshipAccess;
        _owner = owner;
        _mapper = mapper;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(
        DeleteTraineeManagedPlanCommand command,
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

        var ownerCommand = _mapper.Map<DeleteTraineeManagedPlanCommand, OwnerDeleteCommand>(command, _mapper.CreateContext());
        return await _owner.ExecuteAsync(ownerCommand, cancellationToken);
    }
}
