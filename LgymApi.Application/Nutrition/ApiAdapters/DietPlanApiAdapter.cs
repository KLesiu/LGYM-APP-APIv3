using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.ApiAdapters;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.DietPlans.ActivateTraineePlan.Contracts;
using LgymApi.Application.Nutrition.DietPlans.ActivateTraineePlan.Models;
using LgymApi.Application.Nutrition.DietPlans.CreateTraineePlan;
using LgymApi.Application.Nutrition.DietPlans.DeleteTraineePlan.Contracts;
using LgymApi.Application.Nutrition.DietPlans.DeleteTraineePlan.Models;
using LgymApi.Application.Nutrition.DietPlans.GetCurrentDietPlan;
using LgymApi.Application.Nutrition.DietPlans.GetCurrentDietPlans;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlan;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlanHistory.Contracts;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlanHistory.Models;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlans;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Application.Nutrition.DietPlans.UpdateTraineePlan;
using LgymApi.Application.Nutrition.DietPlans.UpdateTraineePlan.Contracts;

namespace LgymApi.Application.Nutrition.ApiAdapters;

internal sealed class DietPlanApiAdapter : IDietPlanAccountApiAdapter
{
    private readonly IGetTraineeDietPlansUseCase _list;
    private readonly IGetTraineeDietPlanUseCase _get;
    private readonly ICreateTraineeDietPlanUseCase _create;
    private readonly IUpdateTraineeDietPlanUseCase _update;
    private readonly IActivateTraineeDietPlanUseCase _activate;
    private readonly IDeleteTraineeDietPlanUseCase _delete;
    private readonly IGetTraineeDietPlanHistoryUseCase _history;
    private readonly IGetCurrentDietPlansUseCase _currentPlans;
    private readonly IGetCurrentDietPlanUseCase _currentPlan;
    private readonly IMapper _mapper;

    public DietPlanApiAdapter(IGetTraineeDietPlansUseCase list, IGetTraineeDietPlanUseCase get, ICreateTraineeDietPlanUseCase create, IUpdateTraineeDietPlanUseCase update, IActivateTraineeDietPlanUseCase activate, IDeleteTraineeDietPlanUseCase delete, IGetTraineeDietPlanHistoryUseCase history, IGetCurrentDietPlansUseCase currentPlans, IGetCurrentDietPlanUseCase currentPlan, IMapper mapper)
    {
        _list = list;
        _get = get;
        _create = create;
        _update = update;
        _activate = activate;
        _delete = delete;
        _history = history;
        _currentPlans = currentPlans;
        _currentPlan = currentPlan;
        _mapper = mapper;
    }

    public Task<Result<IReadOnlyList<DietPlanReadModel>, AppError>> GetTraineePlansAsync(DietPlanListAccountQuery query, CancellationToken cancellationToken = default) => _list.ExecuteAsync(_mapper.Map<DietPlanListAccountQuery, GetTraineeDietPlansQuery>(query), cancellationToken);
    public Task<Result<DietPlanReadModel, AppError>> GetTraineePlanAsync(DietPlanGetAccountQuery query, CancellationToken cancellationToken = default) => _get.ExecuteAsync(_mapper.Map<DietPlanGetAccountQuery, GetTraineeDietPlanQuery>(query), cancellationToken);
    public Task<Result<DietPlanReadModel, AppError>> CreateAsync(DietPlanCreateAccountCommand command, CancellationToken cancellationToken = default) => _create.ExecuteAsync(_mapper.Map<DietPlanCreateAccountCommand, CreateTraineeDietPlanCommand>(command), cancellationToken);
    public Task<Result<DietPlanReadModel, AppError>> UpdateAsync(DietPlanUpdateAccountCommand command, CancellationToken cancellationToken = default) => _update.ExecuteAsync(_mapper.Map<DietPlanUpdateAccountCommand, UpdateTraineeDietPlanCommand>(command), cancellationToken);
    public Task<Result<Unit, AppError>> ActivateAsync(DietPlanActivateAccountCommand command, CancellationToken cancellationToken = default) => _activate.ExecuteAsync(_mapper.Map<DietPlanActivateAccountCommand, ActivateTraineeDietPlanCommand>(command), cancellationToken);
    public Task<Result<Unit, AppError>> DeleteAsync(DietPlanDeleteAccountCommand command, CancellationToken cancellationToken = default) => _delete.ExecuteAsync(_mapper.Map<DietPlanDeleteAccountCommand, DeleteTraineeDietPlanCommand>(command), cancellationToken);
    public Task<Result<IReadOnlyList<DietPlanHistoryReadModel>, AppError>> GetHistoryAsync(DietPlanHistoryAccountQuery query, CancellationToken cancellationToken = default) => _history.ExecuteAsync(_mapper.Map<DietPlanHistoryAccountQuery, GetTraineeDietPlanHistoryQuery>(query), cancellationToken);
    public Task<Result<IReadOnlyList<DietPlanReadModel>, AppError>> GetCurrentPlansAsync(DietPlanCurrentAccountQuery query, CancellationToken cancellationToken = default) => _currentPlans.ExecuteAsync(_mapper.Map<DietPlanCurrentAccountQuery, GetCurrentDietPlansQuery>(query), cancellationToken);
    public Task<Result<DietPlanReadModel, AppError>> GetCurrentPlanAsync(DietPlanCurrentAccountQuery query, CancellationToken cancellationToken = default) => _currentPlan.ExecuteAsync(_mapper.Map<DietPlanCurrentAccountQuery, GetCurrentDietPlanQuery>(query), cancellationToken);
}
