using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.EloRegistry.Models;
using LgymApi.Application.Features.User.Models;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.EloRegistry;

public interface IEloRegistryService
{
    Task<Result<List<EloRegistryChartEntry>, AppError>> GetChartAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> RegisterUserAsync(RegisterUserInput input, bool trainer, CancellationToken cancellationToken = default);
    Task PopulateLatestEloAsync(UserInfoResult userInfo, CancellationToken cancellationToken = default);
    Task<Result<int, AppError>> GetUserEloAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default);
    Task<int> GetLatestEloOrDefaultAsync(Id<LgymApi.Identity.Contracts.AccountReference> accountId, CancellationToken cancellationToken = default);
}
