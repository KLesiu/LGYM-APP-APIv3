using LgymApi.Application.Platform.Contracts.BackgroundCommands;

namespace LgymApi.Application.Reporting.Contracts.BackgroundCommands;

public sealed class ReportSubmissionAcceptedProgressCommand : IActionCommand
{
    public required ReportSubmissionAcceptedProgressPayload Event { get; init; }

    public ReportSubmissionAcceptedProgressPayloadValidationResult Validate()
    {
        if (Event is null)
        {
            return ReportSubmissionAcceptedProgressPayloadValidationResult.Poison(
                "The accepted report submission payload is missing.");
        }

        return Event.Validate();
    }
}
