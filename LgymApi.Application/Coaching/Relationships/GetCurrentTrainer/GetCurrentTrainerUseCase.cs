using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Mapping.Core;
using LgymApi.Resources;

namespace LgymApi.Application.Coaching.Relationships.GetCurrentTrainer;

internal sealed class GetCurrentTrainerUseCase : IGetCurrentTrainerUseCase
{
    private readonly ICoachingActiveLinkPersistence _activeLinks;
    private readonly IAccountReadService _accounts;
    private readonly IMapper _mapper;

    public GetCurrentTrainerUseCase(
        ICoachingActiveLinkPersistence activeLinks,
        IAccountReadService accounts,
        IMapper mapper)
    {
        _activeLinks = activeLinks;
        _accounts = accounts;
        _mapper = mapper;
    }

    public async Task<Result<CurrentTrainerReadModel, AppError>> ExecuteAsync(
        GetCurrentTrainerQuery query,
        CancellationToken cancellationToken = default)
    {
        var link = await _activeLinks.FindByTraineeAsync(query.TraineeId, cancellationToken);
        if (link is null)
        {
            return Result<CurrentTrainerReadModel, AppError>.Failure(
                new TrainerRelationshipNotFoundError(Messages.DidntFind));
        }

        var trainer = await _accounts.GetByIdAsync(link.TrainerId, cancellationToken);
        if (trainer is null)
        {
            return Result<CurrentTrainerReadModel, AppError>.Failure(
                new TrainerRelationshipNotFoundError(Messages.DidntFind));
        }

        var profile = _mapper.Map<CurrentTrainerSource, CurrentTrainerReadModel>(
            new CurrentTrainerSource(trainer, link.CreatedAt),
            _mapper.CreateContext());
        return Result<CurrentTrainerReadModel, AppError>.Success(profile);
    }
}
