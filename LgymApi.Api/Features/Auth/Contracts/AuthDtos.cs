using System.Text.Json.Serialization;
using LgymApi.Api.Interfaces;

namespace LgymApi.Api.Features.Auth.Contracts;

public sealed class GoogleSignInRequest : IDto
{
    [JsonPropertyName("idToken")]
    public string IdToken { get; set; } = string.Empty;

    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("adultConfirmed")]
    public bool? AdultConfirmed { get; set; }
}
