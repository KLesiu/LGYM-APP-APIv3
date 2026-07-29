namespace LgymApi.Application.Services;

internal sealed record GoogleTokenPayload(string Subject, string Email, bool EmailVerified, string? Name, string? Picture);
