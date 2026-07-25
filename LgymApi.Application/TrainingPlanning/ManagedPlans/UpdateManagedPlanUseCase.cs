using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Resources;
using PlanEntity = LgymApi.Domain.Entities.Plan;

namespace LgymApi.Application.TrainingPlanning.ManagedPlans;

internal sealed class UpdateManagedPlanUseCase : IUpdateManagedPlanUseCase
{
    private readonly IPlanRepository _planRepository;
    private readonly IMapper _mapper;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateManagedPlanUseCase(IPlanRepository planRepository, IMapper mapper, IUnitOfWork unitOfWork)
    {
        _planRepository = planRepository;
        _mapper = mapper;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ManagedPlanReadModel, AppError>> ExecuteAsync(
        UpdateManagedPlanCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command is null || command.TrainerId.IsEmpty || command.TraineeId.IsEmpty)
        {
            return Result<ManagedPlanReadModel, AppError>.Failure(
                new InvalidPlanError(Messages.UserIdRequired));
        }

        if (command.PlanId.IsEmpty || string.IsNullOrWhiteSpace(command.Name))
        {
            return Result<ManagedPlanReadModel, AppError>.Failure(
                new InvalidPlanError(Messages.FieldRequired));
        }

        var plan = await _planRepository.FindByIdAsync(command.PlanId, cancellationToken);
        if (plan is null || (plan.UserId != command.TraineeId && plan.UserId != command.TrainerId))
        {
            return Result<ManagedPlanReadModel, AppError>.Failure(
                new PlanNotFoundError(Messages.DidntFind));
        }

        plan.Name = command.Name.Trim();
        await _planRepository.UpdateAsync(plan, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ManagedPlanReadModel, AppError>.Success(
            _mapper.Map<PlanEntity, ManagedPlanReadModel>(plan, _mapper.CreateContext()));
    }
}
