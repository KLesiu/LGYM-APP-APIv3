using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Pagination;
using LgymApi.Resources;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.Relationships.TrainerDashboard;

internal sealed class GetTrainerDashboardUseCase : IGetTrainerDashboardUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly ICoachingFactReader _facts;
    private readonly IAccountReadService _accounts;
    private readonly IQueryPaginationService _pagination;
    private readonly IMapper _mapper;

    public GetTrainerDashboardUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        ICoachingFactReader facts,
        IAccountReadService accounts,
        IQueryPaginationService pagination,
        IMapper mapper)
    {
        _relationshipAccess = relationshipAccess;
        _facts = facts;
        _accounts = accounts;
        _pagination = pagination;
        _mapper = mapper;
    }

    public async Task<Result<Pagination<TrainerDashboardTraineeReadModel>, AppError>> ExecuteAsync(
        GetTrainerDashboardQuery query,
        CancellationToken cancellationToken = default)
    {
        var access = await _relationshipAccess.GetAccessDecisionAsync(
            query.TrainerId,
            LgymApi.Domain.ValueObjects.Id<UserEntity>.Empty,
            cancellationToken);
        if (!access.IsTrainer)
        {
            return Result<Pagination<TrainerDashboardTraineeReadModel>, AppError>.Failure(
                new TrainerRelationshipForbiddenError(Messages.TrainerRoleRequired));
        }

        var now = DateTimeOffset.UtcNow;
        var facts = await _facts.GetDashboardFactsAsync(query.TrainerId, cancellationToken);
        var traineeIds = facts.Select(fact => fact.TraineeId).ToList();
        var accounts = await _accounts.GetByIdsAsync(traineeIds, cancellationToken);
        var accountsById = accounts
            .GroupBy(account => account.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var sources = facts
            .Where(fact => accountsById.ContainsKey(fact.TraineeId))
            .Select(fact => new TrainerDashboardSource(fact, accountsById[fact.TraineeId], now))
            .ToList();
        var enriched = _mapper.MapList<TrainerDashboardSource, TrainerDashboardTraineeReadModel>(
            sources,
            _mapper.CreateContext());

        var logicalRows = ApplySearch(enriched, query.Search)
            .Where(row => row.Status != TrainerDashboardTraineeStatus.NoRelationship);
        logicalRows = ApplyStatusFilter(logicalRows, query.Status);

        return await _pagination.ExecuteAsync(
            () => logicalRows.AsQueryable(),
            BuildFilterInput(query),
            cancellationToken);
    }

    private static IEnumerable<TrainerDashboardTraineeReadModel> ApplySearch(
        IEnumerable<TrainerDashboardTraineeReadModel> rows,
        string? searchValue)
    {
        if (string.IsNullOrWhiteSpace(searchValue))
        {
            return rows;
        }

        var search = searchValue.Trim();
        return rows.Where(row =>
            row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Email.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<TrainerDashboardTraineeReadModel> ApplyStatusFilter(
        IEnumerable<TrainerDashboardTraineeReadModel> rows,
        string? statusValue)
    {
        return Enum.TryParse<TrainerDashboardTraineeStatus>(statusValue, true, out var status)
            ? rows.Where(row => row.Status == status)
            : rows;
    }

    private static FilterInput BuildFilterInput(GetTrainerDashboardQuery query)
    {
        var sortField = string.IsNullOrWhiteSpace(query.SortBy)
            ? "name"
            : query.SortBy.Trim().ToLowerInvariant() switch
            {
                "status" => "statusOrder",
                var field => field
            };

        return new FilterInput
        {
            Page = query.Page < 1 ? 1 : query.Page,
            PageSize = query.PageSize <= 0 ? 20 : query.PageSize,
            SortDescriptors =
            [
                new SortDescriptor
                {
                    FieldName = sortField,
                    Descending = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase)
                }
            ]
        };
    }
}
