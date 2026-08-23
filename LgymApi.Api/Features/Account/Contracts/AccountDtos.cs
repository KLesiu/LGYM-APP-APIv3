using System.Text.Json.Serialization;
using LgymApi.Api.Interfaces;

namespace LgymApi.Api.Features.Account.Contracts;

public sealed class ConfirmAdultRequest : IDto
{
    [JsonPropertyName("adultConfirmed")]
    public bool? AdultConfirmed { get; set; }
}

public sealed class LinkGoogleRequest : IDto
{
    [JsonPropertyName("idToken")]
    public string IdToken { get; set; } = string.Empty;

    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }
}

public sealed class ExternalLoginDto : IResultDto
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("providerEmail")]
    public string? ProviderEmail { get; set; }
}
