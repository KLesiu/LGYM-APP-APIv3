namespace LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration;

public enum ReportSubmissionAcceptedProgressConsumeOutcome
{
    Applied = 0,
    Duplicate = 1,
    Invalid = 2,
    UnsupportedSchema = 3,
    Poison = 4
}

public sealed record ReportSubmissionAcceptedProgressConsumeResult(
    ReportSubmissionAcceptedProgressConsumeOutcome Outcome,
    string? Detail)
{
    public bool IsSuccess => Outcome is ReportSubmissionAcceptedProgressConsumeOutcome.Applied
        or ReportSubmissionAcceptedProgressConsumeOutcome.Duplicate;

    public bool IsNoOp => Outcome == ReportSubmissionAcceptedProgressConsumeOutcome.Duplicate;

    public bool RequiresPoisonHandling => Outcome is ReportSubmissionAcceptedProgressConsumeOutcome.UnsupportedSchema
        or ReportSubmissionAcceptedProgressConsumeOutcome.Poison;

    public static ReportSubmissionAcceptedProgressConsumeResult Applied()
        => new(ReportSubmissionAcceptedProgressConsumeOutcome.Applied, null);

    public static ReportSubmissionAcceptedProgressConsumeResult Duplicate()
        => new(ReportSubmissionAcceptedProgressConsumeOutcome.Duplicate, null);

    public static ReportSubmissionAcceptedProgressConsumeResult Invalid(string detail)
        => new(ReportSubmissionAcceptedProgressConsumeOutcome.Invalid, detail);

    public static ReportSubmissionAcceptedProgressConsumeResult UnsupportedSchema(string detail)
        => new(ReportSubmissionAcceptedProgressConsumeOutcome.UnsupportedSchema, detail);

    public static ReportSubmissionAcceptedProgressConsumeResult Poison(string detail)
        => new(ReportSubmissionAcceptedProgressConsumeOutcome.Poison, detail);
}
