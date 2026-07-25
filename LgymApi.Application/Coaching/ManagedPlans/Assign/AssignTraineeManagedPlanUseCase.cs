using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using OwnerAssignCommand = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.AssignManagedPlanCommand;
using OwnerAssignUseCase = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.IAssignManagedPlanUseCase;

namespace LgymApi.Application.Coaching.ManagedPlans.Assign;

internal sealed class AssignTraineeManagedPlanUseCase : IAssignTraineeManagedPlanUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly OwnerAssignUseCase _owner;
    private readonly IMapper _mapper;

    public AssignTraineeManagedPlanUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        OwnerAssignUseCase owner,
        IMapper mapper)
    {
        _relationshipAccess = relationshipAccess;
        _owner = owner;
        _mapper = mapper;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(
        AssignTraineeManagedPlanCommand command,
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

        var ownerCommand = _mapper.Map<AssignTraineeManagedPlanCommand, OwnerAssignCommand>(command, _mapper.CreateContext());
        return await _owner.ExecuteAsync(ownerCommand, cancellationToken);
    }
}
