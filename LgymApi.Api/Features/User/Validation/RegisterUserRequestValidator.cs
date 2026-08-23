using FluentValidation;
using LgymApi.Api.Features.User.Contracts;
using LgymApi.Resources;

namespace LgymApi.Api.Features.User.Validation;

public class RegisterUserRequestValidator : AbstractValidator<RegisterUserRequest>
{
    public RegisterUserRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage(Messages.NameIsRequired);

        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage(Messages.FieldRequired)
            .EmailAddress()
            .WithMessage(Messages.EmailInvalid);

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage(Messages.PasswordRequired)
            .MinimumLength(6)
            .WithMessage(Messages.PasswordMin);

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty()
            .WithMessage(Messages.PasswordRequired)
            .Equal(x => x.Password)
            .WithMessage(Messages.SamePassword);

        RuleFor(x => x.AdultConfirmed)
            .Equal(true)
            .WithMessage(Messages.AdultConfirmationRequired);
    }
}
