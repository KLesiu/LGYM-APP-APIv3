using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using PlanEntity = LgymApi.Domain.Entities.Plan;

namespace LgymApi.Application.TrainingPlanning.ManagedPlans;

internal sealed class CreateManagedPlanUseCase : ICreateManagedPlanUseCase
{
    private readonly IPlanRepository _planRepository;
    private readonly IMapper _mapper;
    private readonly IUnitOfWork _unitOfWork;

    public CreateManagedPlanUseCase(IPlanRepository planRepository, IMapper mapper, IUnitOfWork unitOfWork)
    {
        _planRepository = planRepository;
        _mapper = mapper;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ManagedPlanReadModel, AppError>> ExecuteAsync(
        CreateManagedPlanCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command is null || command.TrainerId.IsEmpty || command.TraineeId.IsEmpty)
        {
            return Result<ManagedPlanReadModel, AppError>.Failure(
                new InvalidPlanError(Messages.UserIdRequired));
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result<ManagedPlanReadModel, AppError>.Failure(
                new InvalidPlanError(Messages.FieldRequired));
        }

        var plan = new PlanEntity
        {
            Id = Id<PlanEntity>.New(),
            UserId = command.TrainerId.Rebind<LgymApi.Domain.Entities.User>(),
            Name = command.Name.Trim(),
            IsActive = false,
            IsDeleted = false
        };

        await _planRepository.AddAsync(plan, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ManagedPlanReadModel, AppError>.Success(
            _mapper.Map<PlanEntity, ManagedPlanReadModel>(plan, _mapper.CreateContext()));
    }
}
