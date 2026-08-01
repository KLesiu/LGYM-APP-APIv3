using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.TrainingPlanning.ApiAdapters;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Plan.CheckIsUserHavePlan;
using LgymApi.Application.TrainingPlanning.Plan.CopyPlan;
using LgymApi.Application.TrainingPlanning.Plan.CreatePlan;
using LgymApi.Application.TrainingPlanning.Plan.DeletePlan;
using LgymApi.Application.TrainingPlanning.Plan.GenerateShareCode;
using LgymApi.Application.TrainingPlanning.Plan.GetPlanConfig;
using LgymApi.Application.TrainingPlanning.Plan.GetPlansList;
using LgymApi.Application.TrainingPlanning.Plan.Models;
using LgymApi.Application.TrainingPlanning.Plan.SetActivePlan;
using LgymApi.Application.TrainingPlanning.Plan.UpdatePlan;

namespace LgymApi.Application.TrainingPlanning.ApiAdapters;

internal sealed class PlanApiAdapter : IPlanAccountApiAdapter
{
    private readonly ICreatePlanUseCase _createPlan;
    private readonly IUpdatePlanUseCase _updatePlan;
    private readonly IDeletePlanUseCase _deletePlan;
    private readonly IGetPlanConfigUseCase _getConfig;
    private readonly IGetPlansListUseCase _getList;
    private readonly ISetActivePlanUseCase _setActive;
    private readonly ICopyPlanUseCase _copyPlan;
    private readonly IGenerateShareCodeUseCase _generateShareCode;
    private readonly ICheckIsUserHavePlanUseCase _hasPlan;
    private readonly IMapper _mapper;

    public PlanApiAdapter(
        ICreatePlanUseCase createPlan,
        IUpdatePlanUseCase updatePlan,
        IDeletePlanUseCase deletePlan,
        IGetPlanConfigUseCase getConfig,
        IGetPlansListUseCase getList,
        ISetActivePlanUseCase setActive,
        ICopyPlanUseCase copyPlan,
        IGenerateShareCodeUseCase generateShareCode,
        ICheckIsUserHavePlanUseCase hasPlan,
        IMapper mapper)
    {
        _createPlan = createPlan;
        _updatePlan = updatePlan;
        _deletePlan = deletePlan;
        _getConfig = getConfig;
        _getList = getList;
        _setActive = setActive;
        _copyPlan = copyPlan;
        _generateShareCode = generateShareCode;
        _hasPlan = hasPlan;
        _mapper = mapper;
    }

    public Task<Result<Unit, AppError>> CreateAsync(PlanCreateAccountCommand command, CancellationToken cancellationToken = default)
        => _createPlan.ExecuteAsync(_mapper.Map<PlanCreateAccountCommand, CreatePlanCommand>(command), cancellationToken);

    public Task<Result<Unit, AppError>> UpdateAsync(PlanUpdateAccountCommand command, CancellationToken cancellationToken = default)
        => _updatePlan.ExecuteAsync(_mapper.Map<PlanUpdateAccountCommand, UpdatePlanCommand>(command), cancellationToken);

    public Task<Result<PlanReadModel, AppError>> GetConfigAsync(PlanGetConfigAccountQuery query, CancellationToken cancellationToken = default)
        => _getConfig.ExecuteAsync(_mapper.Map<PlanGetConfigAccountQuery, GetPlanConfigQuery>(query), cancellationToken);

    public Task<Result<bool, AppError>> HasPlanAsync(PlanHasAccountQuery query, CancellationToken cancellationToken = default)
        => _hasPlan.ExecuteAsync(_mapper.Map<PlanHasAccountQuery, CheckIsUserHavePlanQuery>(query), cancellationToken);

    public Task<Result<List<PlanReadModel>, AppError>> GetListAsync(PlanGetListAccountQuery query, CancellationToken cancellationToken = default)
        => _getList.ExecuteAsync(_mapper.Map<PlanGetListAccountQuery, GetPlansListQuery>(query), cancellationToken);

    public Task<Result<Unit, AppError>> SetActiveAsync(PlanSetActiveAccountCommand command, CancellationToken cancellationToken = default)
        => _setActive.ExecuteAsync(_mapper.Map<PlanSetActiveAccountCommand, SetActivePlanCommand>(command), cancellationToken);

    public Task<Result<PlanReadModel, AppError>> CopyAsync(PlanCopyAccountCommand command, CancellationToken cancellationToken = default)
        => _copyPlan.ExecuteAsync(_mapper.Map<PlanCopyAccountCommand, CopyPlanCommand>(command), cancellationToken);

    public Task<Result<string, AppError>> GenerateShareCodeAsync(PlanGenerateShareCodeAccountCommand command, CancellationToken cancellationToken = default)
        => _generateShareCode.ExecuteAsync(_mapper.Map<PlanGenerateShareCodeAccountCommand, GenerateShareCodeCommand>(command), cancellationToken);

    public Task<Result<Unit, AppError>> DeleteAsync(PlanDeleteAccountCommand command, CancellationToken cancellationToken = default)
        => _deletePlan.ExecuteAsync(_mapper.Map<PlanDeleteAccountCommand, DeletePlanCommand>(command), cancellationToken);
}
