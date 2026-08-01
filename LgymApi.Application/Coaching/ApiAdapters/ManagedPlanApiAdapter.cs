using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Coaching.ManagedPlans.Assign;
using LgymApi.Application.Coaching.ManagedPlans.Create;
using LgymApi.Application.Coaching.ManagedPlans.Delete;
using LgymApi.Application.Coaching.ManagedPlans.List;
using LgymApi.Application.Coaching.ManagedPlans.Unassign;
using LgymApi.Application.Coaching.ManagedPlans.Update;
using LgymApi.Application.Coaching.ApiAdapters;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;

namespace LgymApi.Application.Coaching.ApiAdapters;

internal sealed class ManagedPlanApiAdapter : IManagedPlanAccountApiAdapter
{
    private readonly IListManagedPlansUseCase _list;
    private readonly ICreateTraineeManagedPlanUseCase _create;
    private readonly IUpdateTraineeManagedPlanUseCase _update;
    private readonly IDeleteTraineeManagedPlanUseCase _delete;
    private readonly IAssignTraineeManagedPlanUseCase _assign;
    private readonly IUnassignTraineeManagedPlanUseCase _unassign;
    private readonly IMapper _mapper;

    public ManagedPlanApiAdapter(IListManagedPlansUseCase list, ICreateTraineeManagedPlanUseCase create, IUpdateTraineeManagedPlanUseCase update, IDeleteTraineeManagedPlanUseCase delete, IAssignTraineeManagedPlanUseCase assign, IUnassignTraineeManagedPlanUseCase unassign, IMapper mapper)
    {
        _list = list;
        _create = create;
        _update = update;
        _delete = delete;
        _assign = assign;
        _unassign = unassign;
        _mapper = mapper;
    }

    public Task<Result<IReadOnlyList<ManagedPlanReadModel>, AppError>> ListAsync(ManagedPlanListAccountQuery query, CancellationToken cancellationToken = default)
        => _list.ExecuteAsync(_mapper.Map<ManagedPlanListAccountQuery, ListManagedPlansQuery>(query), cancellationToken);

    public Task<Result<ManagedPlanReadModel, AppError>> CreateAsync(ManagedPlanCreateAccountCommand command, CancellationToken cancellationToken = default)
        => _create.ExecuteAsync(_mapper.Map<ManagedPlanCreateAccountCommand, CreateTraineeManagedPlanCommand>(command), cancellationToken);

    public Task<Result<ManagedPlanReadModel, AppError>> UpdateAsync(ManagedPlanUpdateAccountCommand command, CancellationToken cancellationToken = default)
        => _update.ExecuteAsync(_mapper.Map<ManagedPlanUpdateAccountCommand, UpdateTraineeManagedPlanCommand>(command), cancellationToken);

    public Task<Result<Unit, AppError>> DeleteAsync(ManagedPlanDeleteAccountCommand command, CancellationToken cancellationToken = default)
        => _delete.ExecuteAsync(_mapper.Map<ManagedPlanDeleteAccountCommand, DeleteTraineeManagedPlanCommand>(command), cancellationToken);

    public Task<Result<Unit, AppError>> AssignAsync(ManagedPlanAssignAccountCommand command, CancellationToken cancellationToken = default)
        => _assign.ExecuteAsync(_mapper.Map<ManagedPlanAssignAccountCommand, AssignTraineeManagedPlanCommand>(command), cancellationToken);

    public Task<Result<Unit, AppError>> UnassignAsync(ManagedPlanUnassignAccountCommand command, CancellationToken cancellationToken = default)
        => _unassign.ExecuteAsync(_mapper.Map<ManagedPlanUnassignAccountCommand, UnassignTraineeManagedPlanCommand>(command), cancellationToken);
}
