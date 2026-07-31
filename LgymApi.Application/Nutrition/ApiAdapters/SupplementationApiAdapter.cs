using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.ApiAdapters;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.Supplementation.AssignTraineePlan;
using LgymApi.Application.Nutrition.Supplementation.CheckOffIntake.Contracts;
using LgymApi.Application.Nutrition.Supplementation.CheckOffIntake.Models;
using LgymApi.Application.Nutrition.Supplementation.CreateTraineePlan;
using LgymApi.Application.Nutrition.Supplementation.DeleteTraineePlan.Contracts;
using LgymApi.Application.Nutrition.Supplementation.DeleteTraineePlan.Models;
using LgymApi.Application.Nutrition.Supplementation.GetComplianceSummary;
using LgymApi.Application.Nutrition.Supplementation.GetSchedule.Contracts;
using LgymApi.Application.Nutrition.Supplementation.GetSchedule.Models;
using LgymApi.Application.Nutrition.Supplementation.GetTraineePlans;
using LgymApi.Application.Nutrition.Supplementation.Models;
using LgymApi.Application.Nutrition.Supplementation.UnassignTraineePlan;
using LgymApi.Application.Nutrition.Supplementation.UnassignTraineePlan.Contracts;
using LgymApi.Application.Nutrition.Supplementation.UpdateTraineePlan;

namespace LgymApi.Application.Nutrition.ApiAdapters;

internal sealed class SupplementationApiAdapter : ISupplementationApiAdapter
{
    private readonly IGetTraineeSupplementPlansUseCase _list;
    private readonly ICreateTraineeSupplementPlanUseCase _create;
    private readonly IUpdateTraineeSupplementPlanUseCase _update;
    private readonly IDeleteTraineeSupplementPlanUseCase _delete;
    private readonly IAssignTraineeSupplementPlanUseCase _assign;
    private readonly IUnassignTraineeSupplementPlanUseCase _unassign;
    private readonly IGetSupplementComplianceSummaryUseCase _compliance;
    private readonly IGetSupplementScheduleUseCase _schedule;
    private readonly ICheckOffSupplementIntakeUseCase _checkOff;
    private readonly IMapper _mapper;

    public SupplementationApiAdapter(IGetTraineeSupplementPlansUseCase list, ICreateTraineeSupplementPlanUseCase create, IUpdateTraineeSupplementPlanUseCase update, IDeleteTraineeSupplementPlanUseCase delete, IAssignTraineeSupplementPlanUseCase assign, IUnassignTraineeSupplementPlanUseCase unassign, IGetSupplementComplianceSummaryUseCase compliance, IGetSupplementScheduleUseCase schedule, ICheckOffSupplementIntakeUseCase checkOff, IMapper mapper)
    {
        _list = list;
        _create = create;
        _update = update;
        _delete = delete;
        _assign = assign;
        _unassign = unassign;
        _compliance = compliance;
        _schedule = schedule;
        _checkOff = checkOff;
        _mapper = mapper;
    }

    public Task<Result<IReadOnlyList<SupplementPlanReadModel>, AppError>> GetTraineePlansAsync(SupplementPlanListAccountQuery query, CancellationToken cancellationToken = default) => _list.ExecuteAsync(_mapper.Map<SupplementPlanListAccountQuery, GetTraineeSupplementPlansQuery>(query), cancellationToken);
    public Task<Result<SupplementPlanReadModel, AppError>> CreateAsync(SupplementPlanCreateAccountCommand command, CancellationToken cancellationToken = default) => _create.ExecuteAsync(_mapper.Map<SupplementPlanCreateAccountCommand, CreateTraineeSupplementPlanCommand>(command), cancellationToken);
    public Task<Result<SupplementPlanReadModel, AppError>> UpdateAsync(SupplementPlanUpdateAccountCommand command, CancellationToken cancellationToken = default) => _update.ExecuteAsync(_mapper.Map<SupplementPlanUpdateAccountCommand, UpdateTraineeSupplementPlanCommand>(command), cancellationToken);
    public Task<Result<Unit, AppError>> DeleteAsync(SupplementPlanDeleteAccountCommand command, CancellationToken cancellationToken = default) => _delete.ExecuteAsync(_mapper.Map<SupplementPlanDeleteAccountCommand, DeleteTraineeSupplementPlanCommand>(command), cancellationToken);
    public Task<Result<Unit, AppError>> AssignAsync(SupplementPlanAssignAccountCommand command, CancellationToken cancellationToken = default) => _assign.ExecuteAsync(_mapper.Map<SupplementPlanAssignAccountCommand, AssignTraineeSupplementPlanCommand>(command), cancellationToken);
    public Task<Result<Unit, AppError>> UnassignAsync(SupplementPlanUnassignAccountCommand command, CancellationToken cancellationToken = default) => _unassign.ExecuteAsync(_mapper.Map<SupplementPlanUnassignAccountCommand, UnassignTraineeSupplementPlanCommand>(command), cancellationToken);
    public Task<Result<SupplementComplianceSummaryReadModel, AppError>> GetComplianceAsync(SupplementComplianceAccountQuery query, CancellationToken cancellationToken = default) => _compliance.ExecuteAsync(_mapper.Map<SupplementComplianceAccountQuery, GetSupplementComplianceSummaryQuery>(query), cancellationToken);
    public Task<Result<IReadOnlyList<SupplementScheduleEntryReadModel>, AppError>> GetScheduleAsync(SupplementScheduleAccountQuery query, CancellationToken cancellationToken = default) => _schedule.ExecuteAsync(_mapper.Map<SupplementScheduleAccountQuery, GetSupplementScheduleQuery>(query), cancellationToken);
    public Task<Result<SupplementScheduleEntryReadModel, AppError>> CheckOffAsync(SupplementCheckOffAccountCommand command, CancellationToken cancellationToken = default) => _checkOff.ExecuteAsync(_mapper.Map<SupplementCheckOffAccountCommand, CheckOffSupplementIntakeCommand>(command), cancellationToken);
}
