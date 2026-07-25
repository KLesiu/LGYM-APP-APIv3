using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Resources;

namespace LgymApi.Application.Coaching.Relationships.DetachFromTrainer;

internal sealed class DetachFromTrainerUseCase : IDetachFromTrainerUseCase
{
    private readonly ICoachingActiveLinkPersistence _activeLinks;
    private readonly ICommandDispatcher _commands;
    private readonly IUnitOfWork _unitOfWork;

    public DetachFromTrainerUseCase(
        ICoachingActiveLinkPersistence activeLinks,
        ICommandDispatcher commands,
        IUnitOfWork unitOfWork)
    {
        _activeLinks = activeLinks;
        _commands = commands;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(
        DetachFromTrainerCommand command,
        CancellationToken cancellationToken = default)
    {
        var link = await _activeLinks.FindByTraineeAsync(command.TraineeId, cancellationToken);
        if (link is null)
        {
            return Result<Unit, AppError>.Failure(new TrainerRelationshipNotFoundError(Messages.DidntFind));
        }

        await _activeLinks.RemoveAsync(link.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _commands.EnqueueAsync(new TrainerRelationshipEndedInAppNotificationCommand
        {
            TrainerId = link.TrainerId,
            TraineeId = command.TraineeId
        });

        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
