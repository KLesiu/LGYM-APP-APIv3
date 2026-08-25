using System.Text.Json.Serialization;
using LgymApi.Api.Interfaces;

namespace LgymApi.Api.Features.User.Contracts;

public sealed class RegisterUserRequest : IDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("cpassword")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [JsonPropertyName("isVisibleInRanking")]
    public bool? IsVisibleInRanking { get; set; }

    [JsonPropertyName("adultConfirmed")]
    public bool? AdultConfirmed { get; set; }
}

public sealed class LoginRequest : IDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}

public sealed class UpdateTimeZoneRequest : IDto
{
    [JsonPropertyName("preferredTimeZone")]
    public string PreferredTimeZone { get; set; } = string.Empty;
}

public sealed class ForgotPasswordRequest : IDto
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordRequest : IDto
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("newPassword")]
    public string NewPassword { get; set; } = string.Empty;

    [JsonPropertyName("confirmPassword")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class RegisterPushInstallationRequest : IDto
{
    [JsonPropertyName("installationId")]
    public string InstallationId { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;

    [JsonPropertyName("fcmToken")]
    public string FcmToken { get; set; } = string.Empty;

    [JsonPropertyName("appVersion")]
    public string? AppVersion { get; set; }

    [JsonPropertyName("environment")]
    public string Environment { get; set; } = string.Empty;

    [JsonPropertyName("permissionStatus")]
    public string? PermissionStatus { get; set; }
}

public sealed class PushInstallationActionRequest : IDto
{
    [JsonPropertyName("installationId")]
    public string InstallationId { get; set; } = string.Empty;
}

public sealed class RankDto : IResultDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("needElo")]
    public int NeedElo { get; set; }
}

public sealed class UserInfoDto : IResultDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("_id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }

    [JsonPropertyName("profileRank")]
    public string ProfileRank { get; set; } = string.Empty;

    [JsonPropertyName("preferredTimeZone")]
    public string PreferredTimeZone { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("elo")]
    public int Elo { get; set; }

    [JsonPropertyName("nextRank")]
    public RankDto? NextRank { get; set; }

    [JsonPropertyName("isDeleted")]
    public bool IsDeleted { get; set; }

    [JsonPropertyName("isVisibleInRanking")]
    public bool IsVisibleInRanking { get; set; }

    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = new();

    [JsonPropertyName("permissionClaims")]
    public List<string> PermissionClaims { get; set; } = new();

    [JsonPropertyName("hasActiveTutorials")]
    public bool HasActiveTutorials { get; set; }

    [JsonPropertyName("adultConfirmationRequired")]
    public bool AdultConfirmationRequired { get; set; }
}

public sealed class UserBaseInfoDto : IResultDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }

    [JsonPropertyName("elo")]
    public int Elo { get; set; }

    [JsonPropertyName("profileRank")]
    public string ProfileRank { get; set; } = string.Empty;
}

public sealed class UserEloDto : IResultDto
{
    [JsonPropertyName("elo")]
    public int Elo { get; set; }
}

public sealed class LoginResponseDto : IResultDto
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("req")]
    public UserInfoDto? User { get; set; }

    [JsonPropertyName("permissionClaims")]
    public List<string> PermissionClaims { get; set; } = new();

    [JsonPropertyName("adultConfirmationRequired")]
    public bool AdultConfirmationRequired { get; set; }
}
