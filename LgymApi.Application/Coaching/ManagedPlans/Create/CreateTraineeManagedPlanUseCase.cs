using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using OwnerCreateCommand = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.CreateManagedPlanCommand;
using OwnerCreateUseCase = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.ICreateManagedPlanUseCase;

namespace LgymApi.Application.Coaching.ManagedPlans.Create;

internal sealed class CreateTraineeManagedPlanUseCase : ICreateTraineeManagedPlanUseCase
{
    private readonly IMarkerCoachingRelationshipAccessService _relationshipAccess;
    private readonly OwnerCreateUseCase _owner;
    private readonly IMapper _mapper;

    public CreateTraineeManagedPlanUseCase(
        IMarkerCoachingRelationshipAccessService relationshipAccess,
        OwnerCreateUseCase owner,
        IMapper mapper)
    {
        _relationshipAccess = relationshipAccess;
        _owner = owner;
        _mapper = mapper;
    }

    public async Task<Result<ManagedPlanReadModel, AppError>> ExecuteAsync(
        CreateTraineeManagedPlanCommand command,
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

        var ownerCommand = _mapper.Map<CreateTraineeManagedPlanCommand, OwnerCreateCommand>(command, _mapper.CreateContext());
        return await _owner.ExecuteAsync(ownerCommand, cancellationToken);
    }
}
