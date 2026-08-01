using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.User.Contracts;
using LgymApi.Api.Idempotency;
using LgymApi.Api.Middleware;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.PasswordReset;
using LgymApi.Application.Features.EloRegistry;
using LgymApi.Application.Features.User.Models;
using LgymApi.Application.Identity.ApiAdapters;
using LgymApi.Application.Identity.Contracts.Authentication;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.WorkoutProgress.Ranking;
using LgymApi.Application.WorkoutProgress.Ranking.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.User.Controllers;

[ApiController]
[Route("api")]
public sealed class UserController : ControllerBase
{
    private readonly IUserCredentialLoginService _userCredentialLoginService;
    private readonly IAuthenticatedAccountApiAdapter _authenticatedAccountApiAdapter;
    private readonly IWorkoutProgressRankingReadService _workoutProgressRankingReadService;
    private readonly IAccountAccessApiAdapter _accountAccessApiAdapter;
    private readonly IAccountEloApiAdapter _accountEloApiAdapter;
    private readonly IEloRegistryService _eloRegistryService;
    private readonly IPasswordResetService _passwordResetService;
    private readonly IMapper _mapper;

    public UserController(
        IUserCredentialLoginService userCredentialLoginService,
        IAuthenticatedAccountApiAdapter authenticatedAccountApiAdapter,
        IWorkoutProgressRankingReadService workoutProgressRankingReadService,
        IAccountAccessApiAdapter accountAccessApiAdapter,
        IAccountEloApiAdapter accountEloApiAdapter,
        IEloRegistryService eloRegistryService,
        IPasswordResetService passwordResetService,
        IMapper mapper)
    {
        _userCredentialLoginService = userCredentialLoginService;
        _authenticatedAccountApiAdapter = authenticatedAccountApiAdapter;
        _workoutProgressRankingReadService = workoutProgressRankingReadService;
        _accountAccessApiAdapter = accountAccessApiAdapter;
        _accountEloApiAdapter = accountEloApiAdapter;
        _eloRegistryService = eloRegistryService;
        _passwordResetService = passwordResetService;
        _mapper = mapper;
    }

    [HttpPost("register")]
    [ApiIdempotency("/api/register", ApiIdempotencyScopeSource.NormalizedEmail)]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Register([FromBody] RegisterUserRequest request, CancellationToken cancellationToken = default)
    {
        var preferredLanguage = Request.Headers.TryGetValue("Accept-Language", out var langs)
            ? langs.ToString()
            : null;

        var input = new RegisterUserInput(
            request.Name,
            request.Email,
            request.Password,
            request.ConfirmPassword,
            request.IsVisibleInRanking,
            preferredLanguage);

        var result = await _eloRegistryService.RegisterUserAsync(input, trainer: false, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Created));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _userCredentialLoginService.LoginAsync(request.Name, request.Password, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        await _eloRegistryService.PopulateLatestEloAsync(result.Value.User, cancellationToken);
        var mapped = _mapper.Map<LoginResult, LoginResponseDto>(result.Value);
        return Ok(mapped);
    }

    [HttpGet("{id}/isAdmin")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<IActionResult> IsAdmin([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var userId = ParseAccountId(id);
        var result = await _accountAccessApiAdapter.IsAdminAsync(userId, cancellationToken);
        return Ok(result);
    }

    [HttpGet("checkToken")]
    [ProducesResponseType(typeof(UserInfoDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckToken(CancellationToken cancellationToken = default)
    {
        var result = await _authenticatedAccountApiAdapter.CheckTokenAsync(GetCurrentAccountId(), cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var account = await _accountEloApiAdapter.PopulateLatestEloAsync(result.Value, cancellationToken);
        var mapped = _mapper.Map<AccountProfileProjection, UserInfoDto>(account);
        return Ok(mapped);
    }

    [HttpPost("logout")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken = default)
    {
        var accountContext = HttpContext.GetAuthenticatedAccountContext();
        var result = await _authenticatedAccountApiAdapter.LogoutAsync(
            accountContext?.Id ?? Id<AccountReference>.Empty,
            accountContext?.SessionId,
            cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpGet("getUsersRanking")]
    [ProducesResponseType(typeof(List<UserBaseInfoDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsersRanking(CancellationToken cancellationToken = default)
    {
        var result = await _workoutProgressRankingReadService.GetUsersRankingAsync(cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var mapped = _mapper.MapList<RankingReadModel, UserBaseInfoDto>(result.Value);
        return Ok(mapped);
    }

    [HttpGet("userInfo/{id}/getUserEloPoints")]
    [ProducesResponseType(typeof(UserEloDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserElo([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var userId = ParseAccountId(id);
        var result = await _accountEloApiAdapter.GetUserEloAsync(userId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<int, UserEloDto>(result.Value));
    }

    private static Id<AccountReference> ParseAccountId(string value)
    {
        return Id<AccountReference>.TryParse(value, out var parsedUserId)
            ? parsedUserId
            : Id<AccountReference>.Empty;
    }

    [HttpGet("deleteAccount")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteAccount(CancellationToken cancellationToken = default)
    {
        var result = await _authenticatedAccountApiAdapter.DeleteAccountAsync(GetCurrentAccountId(), cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }

    [HttpPost("changeVisibilityInRanking")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeVisibilityInRanking([FromBody] ChangeVisibilityInRankingRequest request, CancellationToken cancellationToken = default)
    {
        var isVisibleInRanking = request.IsVisibleInRanking.GetValueOrDefault();
        var result = await _authenticatedAccountApiAdapter.ChangeVisibilityInRankingAsync(GetCurrentAccountId(), isVisibleInRanking, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("updateTimeZone")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTimeZone([FromBody] UpdateTimeZoneRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _authenticatedAccountApiAdapter.UpdateTimeZoneAsync(GetCurrentAccountId(), request.PreferredTimeZone, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var preferences = HttpContext.GetCulturePreferences();
        await _passwordResetService.RequestPasswordResetAsync(request.Email, preferences.First(), cancellationToken);
        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.ForgotPasswordRequested));
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _passwordResetService.ResetPasswordAsync(request.Token, request.NewPassword, cancellationToken);
        if (result.IsFailure)
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.PasswordResetInvalidOrExpiredToken));
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.PasswordResetSucceeded));
    }

    private Id<AccountReference> GetCurrentAccountId()
        => HttpContext.GetAuthenticatedAccountContext()?.Id ?? Id<AccountReference>.Empty;
}
