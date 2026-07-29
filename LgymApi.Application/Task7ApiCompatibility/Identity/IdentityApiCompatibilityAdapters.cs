using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.ExternalAuth;
using LgymApi.Application.Features.EloRegistry;
using LgymApi.Application.Features.Tutorial;
using LgymApi.Application.Identity.Contracts.Administration;
using LgymApi.Application.Identity.Contracts.Profile;
using LgymApi.Application.Identity.Contracts.Ranking;
using LgymApi.Application.Identity.Contracts.Sessions;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Application.Identity.Contracts.Accounts;

namespace LgymApi.Application.Identity.ApiCompatibility;

internal sealed class AuthenticatedAccountApiAdapter : IAuthenticatedAccountApiAdapter
{
    private readonly IAuthenticatedAccountCompatibilityPort _compatibilityPort;
    private readonly IMapper _mapper;

    public AuthenticatedAccountApiAdapter(
        IAuthenticatedAccountCompatibilityPort compatibilityPort,
        IMapper mapper)
    {
        _compatibilityPort = compatibilityPort;
        _mapper = mapper;
    }

    public async Task<Result<AccountProfileProjection, AppError>> CheckTokenAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default)
    {
        var result = await _compatibilityPort.CheckTokenAsync(accountId, cancellationToken);
        return result.IsFailure
            ? Result<AccountProfileProjection, AppError>.Failure(result.Error)
            : Result<AccountProfileProjection, AppError>.Success(_mapper.Map<Features.User.Models.UserInfoResult, AccountProfileProjection>(result.Value));
    }

    public async Task<Result<Unit, AppError>> LogoutAsync(
        Id<AccountReference> accountId,
        Id<AccountSessionReference>? sessionId,
        CancellationToken cancellationToken = default)
        => await _compatibilityPort.LogoutAsync(accountId, sessionId, cancellationToken);

    public async Task<Result<Unit, AppError>> DeleteAccountAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default)
        => await _compatibilityPort.DeleteAccountAsync(accountId, cancellationToken);

    public async Task<Result<Unit, AppError>> ChangeVisibilityInRankingAsync(
        Id<AccountReference> accountId,
        bool isVisibleInRanking,
        CancellationToken cancellationToken = default)
        => await _compatibilityPort.ChangeVisibilityInRankingAsync(accountId, isVisibleInRanking, cancellationToken);

    public async Task<Result<Unit, AppError>> UpdateTimeZoneAsync(
        Id<AccountReference> accountId,
        string preferredTimeZone,
        CancellationToken cancellationToken = default)
        => await _compatibilityPort.UpdateTimeZoneAsync(accountId, preferredTimeZone, cancellationToken);
}

internal sealed class AccountAccessApiAdapter : IAccountAccessApiAdapter
{
    private readonly IUserAdminAccessService _userAdminAccessService;

    public AccountAccessApiAdapter(IUserAdminAccessService userAdminAccessService)
    {
        _userAdminAccessService = userAdminAccessService;
    }

    public Task<bool> IsAdminAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
        => _userAdminAccessService.IsAdminAsync(accountId.Rebind<User>(), cancellationToken);
}

internal sealed class AccountEloApiAdapter : IAccountEloApiAdapter
{
    private readonly IEloRegistryService _eloRegistryService;
    private readonly IMapper _mapper;

    public AccountEloApiAdapter(IEloRegistryService eloRegistryService, IMapper mapper)
    {
        _eloRegistryService = eloRegistryService;
        _mapper = mapper;
    }

    public async Task<AccountProfileProjection> PopulateLatestEloAsync(
        AccountProfileProjection account,
        CancellationToken cancellationToken = default)
    {
        var elo = await _eloRegistryService.GetLatestEloOrDefaultAsync(account.Id, cancellationToken);
        return account with { Elo = elo };
    }

    public Task<Result<int, AppError>> GetUserEloAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default)
        => _eloRegistryService.GetUserEloAsync(accountId, cancellationToken);
}

internal sealed class AccountExternalLoginApiAdapter : IAccountExternalLoginApiAdapter
{
    private readonly IAccountLinkingService _accountLinkingService;
    private readonly IMapper _mapper;

    public AccountExternalLoginApiAdapter(IAccountLinkingService accountLinkingService, IMapper mapper)
    {
        _accountLinkingService = accountLinkingService;
        _mapper = mapper;
    }

    public Task<Result<Unit, AppError>> LinkGoogleAsync(Id<AccountReference> accountId, string idToken, string? accessToken, CancellationToken cancellationToken = default)
        => _accountLinkingService.LinkGoogleAsync(accountId.Rebind<User>(), idToken, accessToken, cancellationToken);

    public Task<Result<Unit, AppError>> UnlinkGoogleAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
        => _accountLinkingService.UnlinkGoogleAsync(accountId.Rebind<User>(), cancellationToken);

    public async Task<Result<IReadOnlyList<ExternalLoginProjection>, AppError>> GetExternalLoginsAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default)
    {
        var result = await _accountLinkingService.GetExternalLoginsAsync(accountId.Rebind<User>(), cancellationToken);
        return result.IsFailure
            ? Result<IReadOnlyList<ExternalLoginProjection>, AppError>.Failure(result.Error)
            : Result<IReadOnlyList<ExternalLoginProjection>, AppError>.Success(_mapper.MapList<ExternalLoginInfo, ExternalLoginProjection>(result.Value));
    }
}

internal sealed class AccountTutorialApiAdapter : IAccountTutorialApiAdapter
{
    private readonly ITutorialService _tutorialService;
    private readonly IMapper _mapper;

    public AccountTutorialApiAdapter(ITutorialService tutorialService, IMapper mapper)
    {
        _tutorialService = tutorialService;
        _mapper = mapper;
    }

    public async Task<Result<IReadOnlyList<TutorialProgressProjection>, AppError>> GetActiveTutorialsAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
    {
        var result = await _tutorialService.GetActiveTutorialsAsync(accountId.Rebind<User>(), cancellationToken);
        return result.IsFailure
            ? Result<IReadOnlyList<TutorialProgressProjection>, AppError>.Failure(result.Error)
            : Result<IReadOnlyList<TutorialProgressProjection>, AppError>.Success(_mapper.MapList<Features.Tutorial.Models.TutorialProgressResult, TutorialProgressProjection>(result.Value));
    }

    public async Task<Result<TutorialProgressProjection?, AppError>> GetTutorialProgressAsync(Id<AccountReference> accountId, Domain.Enums.TutorialType tutorialType, CancellationToken cancellationToken = default)
    {
        var result = await _tutorialService.GetTutorialProgressAsync(accountId.Rebind<User>(), tutorialType, cancellationToken);
        return result.IsFailure
            ? Result<TutorialProgressProjection?, AppError>.Failure(result.Error)
            : Result<TutorialProgressProjection?, AppError>.Success(result.Value is null ? null : _mapper.Map<Features.Tutorial.Models.TutorialProgressResult, TutorialProgressProjection>(result.Value));
    }

    public Task<Result<Unit, AppError>> CompleteStepAsync(Id<AccountReference> accountId, Domain.Enums.TutorialType tutorialType, Domain.Enums.TutorialStep step, CancellationToken cancellationToken = default)
        => _tutorialService.CompleteStepAsync(accountId.Rebind<User>(), tutorialType, step, cancellationToken);

    public Task<Result<Unit, AppError>> CompleteTutorialAsync(Id<AccountReference> accountId, Domain.Enums.TutorialType tutorialType, CancellationToken cancellationToken = default)
        => _tutorialService.CompleteTutorialAsync(accountId.Rebind<User>(), tutorialType, cancellationToken);
}
