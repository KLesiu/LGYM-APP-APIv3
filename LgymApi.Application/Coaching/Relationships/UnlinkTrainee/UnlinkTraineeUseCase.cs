using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Repositories;
using LgymApi.Resources;

namespace LgymApi.Application.Coaching.Relationships.UnlinkTrainee;

internal sealed class UnlinkTraineeUseCase : IUnlinkTraineeUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly ICoachingActiveLinkPersistence _activeLinks;
    private readonly IUnitOfWork _unitOfWork;

    public UnlinkTraineeUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        ICoachingActiveLinkPersistence activeLinks,
        IUnitOfWork unitOfWork)
    {
        _relationshipAccess = relationshipAccess;
        _activeLinks = activeLinks;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(
        UnlinkTraineeCommand command,
        CancellationToken cancellationToken = default)
    {
        var access = await _relationshipAccess.GetAccessDecisionAsync(
            command.TrainerId,
            command.TraineeId,
            cancellationToken);
        if (!access.IsTrainer)
        {
            return Result<Unit, AppError>.Failure(new TrainerRelationshipForbiddenError(Messages.TrainerRoleRequired));
        }

        if (command.TraineeId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired));
        }

        if (!access.HasActiveRelationship)
        {
            return Result<Unit, AppError>.Failure(new TrainerRelationshipNotFoundError(Messages.DidntFind));
        }

        var link = await _activeLinks.FindByTrainerAndTraineeAsync(
            command.TrainerId,
            command.TraineeId,
            cancellationToken);
        if (link is null)
        {
            return Result<Unit, AppError>.Failure(new TrainerRelationshipNotFoundError(Messages.DidntFind));
        }

        await _activeLinks.RemoveAsync(link.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
