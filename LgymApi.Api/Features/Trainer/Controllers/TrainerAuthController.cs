using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.User.Contracts;
using LgymApi.Api.Idempotency;
using LgymApi.Api.Middleware;
using LgymApi.Application.Features.EloRegistry;
using LgymApi.Application.Features.User.Models;
using LgymApi.Application.Identity.ApiCompatibility;
using LgymApi.Application.Identity.Contracts.Authentication;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.Trainer.Controllers;

[ApiController]
[Route("api/trainer")]
public sealed class TrainerAuthController : ControllerBase
{
    private readonly IUserCredentialLoginService _userCredentialLoginService;
    private readonly IAuthenticatedAccountApiAdapter _authenticatedAccountApiAdapter;
    private readonly IAccountEloApiAdapter _accountEloApiAdapter;
    private readonly IEloRegistryService _eloRegistryService;
    private readonly IMapper _mapper;

    public TrainerAuthController(
        IUserCredentialLoginService userCredentialLoginService,
        IAuthenticatedAccountApiAdapter authenticatedAccountApiAdapter,
        IAccountEloApiAdapter accountEloApiAdapter,
        IEloRegistryService eloRegistryService,
        IMapper mapper)
    {
        _userCredentialLoginService = userCredentialLoginService;
        _authenticatedAccountApiAdapter = authenticatedAccountApiAdapter;
        _accountEloApiAdapter = accountEloApiAdapter;
        _eloRegistryService = eloRegistryService;
        _mapper = mapper;
    }

    [HttpPost("register")]
    [ApiIdempotency("/api/trainer/register", ApiIdempotencyScopeSource.NormalizedEmail)]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Register([FromBody] RegisterUserRequest request, CancellationToken cancellationToken = default)
    {
        var input = new RegisterUserInput(
            request.Name,
            request.Email,
            request.Password,
            request.ConfirmPassword,
            IsVisibleInRanking: null,
            PreferredLanguage: null);

        var result = await _eloRegistryService.RegisterUserAsync(input, trainer: true, cancellationToken);
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
        var result = await _userCredentialLoginService.LoginTrainerAsync(request.Name, request.Password, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        await _eloRegistryService.PopulateLatestEloAsync(result.Value.User, cancellationToken);
        return Ok(_mapper.Map<LgymApi.Application.Features.User.Models.LoginResult, LoginResponseDto>(result.Value));
    }

    [HttpGet("checkToken")]
    [Authorize(Policy = AuthConstants.Policies.TrainerAccess)]
    [ProducesResponseType(typeof(UserInfoDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckToken(CancellationToken cancellationToken = default)
    {
        var result = await _authenticatedAccountApiAdapter.CheckTokenAsync(HttpContext.GetCurrentAccountId(), cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var account = await _accountEloApiAdapter.PopulateLatestEloAsync(result.Value, cancellationToken);
        return Ok(_mapper.Map<AccountProfileProjection, UserInfoDto>(account));
    }
}
