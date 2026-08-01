using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using DomainUser = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Features.PasswordReset.Contracts;

public sealed record PasswordRecoveryEmailRequest(
    Id<DomainUser> UserId,
    Id<PasswordResetToken> TokenId,
    string UserName,
    string RecipientEmail,
    string ResetToken,
    string ResetUrl,
    string CultureName);
