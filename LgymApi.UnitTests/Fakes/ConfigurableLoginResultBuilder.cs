using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.ExternalAuth;
using LgymApi.Application.Features.User.Models;
using LgymApi.Domain.Entities;

namespace LgymApi.UnitTests.Fakes;

internal sealed class ConfigurableLoginResultBuilder : ILoginResultBuilder
{
    public List<(User User, string PreferredTimeZone, CancellationToken CancellationToken)> Calls { get; } = [];
    public Func<User, string, CancellationToken, Task<Result<LoginResult, AppError>>> Build { get; set; } = (_, _, _) => throw new NotSupportedException("Login result builder was not configured.");

    public Task<Result<LoginResult, AppError>> BuildAsync(User user, string preferredTimeZone, CancellationToken cancellationToken)
    {
        Calls.Add((user, preferredTimeZone, cancellationToken));
        return Build(user, preferredTimeZone, cancellationToken);
    }
}
