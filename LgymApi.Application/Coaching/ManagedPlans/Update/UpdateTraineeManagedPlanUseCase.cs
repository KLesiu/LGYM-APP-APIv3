using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using OwnerUpdateCommand = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.UpdateManagedPlanCommand;
using OwnerUpdateUseCase = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.IUpdateManagedPlanUseCase;

namespace LgymApi.Application.Coaching.ManagedPlans.Update;

internal sealed class UpdateTraineeManagedPlanUseCase : IUpdateTraineeManagedPlanUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly OwnerUpdateUseCase _owner;
    private readonly IMapper _mapper;

    public UpdateTraineeManagedPlanUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        OwnerUpdateUseCase owner,
        IMapper mapper)
    {
        _relationshipAccess = relationshipAccess;
        _owner = owner;
        _mapper = mapper;
    }

    public async Task<Result<ManagedPlanReadModel, AppError>> ExecuteAsync(
        UpdateTraineeManagedPlanCommand command,
        CancellationToken cancellationToken = default)
    {
        var access = await _relationshipAccess.GetAccessDecisionAsync(
            command.TrainerId,
            command.TraineeId,
            cancellationToken);
        var accessError = ManagedPlanAccess.GetError(access, command.TraineeId);
        if (accessError is not null)
        {
            return Result<ManagedPlanReadModel, AppError>.Failure(accessError);
        }

        var ownerCommand = _mapper.Map<UpdateTraineeManagedPlanCommand, OwnerUpdateCommand>(command, _mapper.CreateContext());
        return await _owner.ExecuteAsync(ownerCommand, cancellationToken);
    }
}
