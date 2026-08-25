using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Resources;

namespace LgymApi.Application.ExternalAuth;

internal sealed class AdultConfirmationRequiredForRegistrationError : AppError
{
    public const string ErrorCode = "AdultConfirmationRequiredForRegistration";

    public override string Message => Messages.AdultConfirmationRequired;
    public override int HttpStatusCode => 428;
    public override object GetPayload() => new { msg = Message, code = ErrorCode };
}
