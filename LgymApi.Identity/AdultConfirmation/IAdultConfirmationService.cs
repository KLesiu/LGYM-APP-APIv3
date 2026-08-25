using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Identity.Contracts.AdultConfirmation;

public interface IAdultConfirmationService
{
    Task<Result<Unit, AppError>> ConfirmAsync(
        Id<AccountReference> accountId,
        bool adultConfirmed,
        CancellationToken cancellationToken = default);
}
