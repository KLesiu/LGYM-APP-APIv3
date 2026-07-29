using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Application.TrainingPlanning.Plan.ActivePlanPointer;
using LgymApi.Application.TrainingPlanning;
using LgymApi.Resources;

namespace LgymApi.Application.TrainingPlanning.ManagedPlans;

internal sealed class UnassignManagedPlanUseCase : IUnassignManagedPlanUseCase
{
    private readonly IPlanRepository _planRepository;
    private readonly IActivePlanPointerStore _activePlanPointerStore;
    private readonly IAccountReadService _accountReadService;
    private readonly IUnitOfWork _unitOfWork;

    public UnassignManagedPlanUseCase(
        IPlanRepository planRepository,
        IActivePlanPointerStore activePlanPointerStore,
        IAccountReadService accountReadService,
        IUnitOfWork unitOfWork)
    {
        _planRepository = planRepository;
        _activePlanPointerStore = activePlanPointerStore;
        _accountReadService = accountReadService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(
        UnassignManagedPlanCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command is null || command.TraineeId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanError(Messages.UserIdRequired));
        }

        if (await _accountReadService.GetByIdAsync(command.TraineeId, cancellationToken) is null)
        {
            return Result<Unit, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind));
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _planRepository.ClearActivePlansAsync(command.TraineeId, cancellationToken);
            await _activePlanPointerStore.StageActivePlanIdAsync(command.TraineeId, null, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<Unit, AppError>.Success(Unit.Value);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
