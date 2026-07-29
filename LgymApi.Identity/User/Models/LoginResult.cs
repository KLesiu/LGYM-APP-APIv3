namespace LgymApi.Application.Features.User.Models;

public sealed class LoginResult
{
    public string Token { get; init; } = string.Empty;
    public UserInfoResult User { get; init; } = null!;
    public List<string> PermissionClaims { get; init; } = new();
}
