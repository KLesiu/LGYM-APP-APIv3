namespace LgymApi.Application.Features.User.Models;

public sealed record RegisterUserInput(
    string Name,
    string Email,
    string Password,
    string ConfirmPassword,
    bool? IsVisibleInRanking,
    string? PreferredLanguage);
