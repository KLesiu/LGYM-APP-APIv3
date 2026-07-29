using FluentAssertions;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ReportingProgressIntegrationCutoverGuardTests
{
    private static readonly string[] ForbiddenReportingProgressDependencies =
    [
        "IReportSubmissionAcceptedProgressConsumer",
        "ReportSubmissionAcceptedProgressConsumer",
        "IMeasurementRepository",
        "MeasurementEntity",
        "IReportSubmissionMeasurementWriter",
        "ReportSubmissionMeasurementWriter",
        "ReportSubmissionAcceptedProgressEvent",
        "ReportSubmissionAcceptedMeasurement",
        "ReportSubmissionAcceptedProgressValidationResult",
        "ReportSubmissionAcceptedProgressValidationOutcome"
    ];

    private const string AcceptedProgressCommandRelativePath =
        "LgymApi.Application/Reporting/Contracts/BackgroundCommands/ReportSubmissionAcceptedProgressCommand.cs";

    private const string ProgressConsumerRelativePath =
        "LgymApi.Application/WorkoutProgress/ReportingIntegration/ReportSubmissionAcceptedProgressConsumer.cs";

    [Test]
    public void Reporting_May_Publish_The_Accepted_Progress_Contract_But_Must_Not_Consume_Or_Persist_Progress_Data()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var reportingSources = EnumerateReportingSources(repoRoot).ToArray();
        var acceptedProgressCommandPath = Path.Combine(repoRoot, AcceptedProgressCommandRelativePath);

        reportingSources.Should().NotBeEmpty();
        File.Exists(acceptedProgressCommandPath).Should().BeTrue(
            "Reporting must publish the accepted-progress command contract from its own contracts folder before it can stage the durable outbox record");
        reportingSources.Should().NotContain(source =>
            ForbiddenReportingProgressDependencies.Any(dependency =>
                source.Content.Contains(dependency, StringComparison.Ordinal)));
    }

    [Test]
    public void Workout_And_Progress_Must_Own_The_Accepted_Progress_Consumer_And_Measurement_Writes()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var consumerPath = Path.Combine(repoRoot, ProgressConsumerRelativePath);
        var workoutProgressRegistrationPath = Path.Combine(
            repoRoot,
            "LgymApi.Application",
            "WorkoutProgress",
            "ServiceCollectionExtensions.cs");

        File.Exists(consumerPath).Should().BeTrue(
            "the Reporting-to-Progress boundary requires a Workout & Progress-owned accepted-progress consumer");

        var consumerSource = File.ReadAllText(consumerPath);
        var registrationSource = File.ReadAllText(workoutProgressRegistrationPath);

        Assert.Multiple(() =>
        {
            consumerSource.Should().Contain("IReportSubmissionAcceptedProgressConsumer");
            consumerSource.Should().Contain("IReportSubmissionAcceptedProgressPersistence");
            consumerSource.Should().Contain("AcceptedReportMeasurementPersistenceModel");
            consumerSource.Should().NotContain("IMeasurementRepository");
            registrationSource.Should().Contain(
                "IReportSubmissionAcceptedProgressConsumer, ReportSubmissionAcceptedProgressConsumer");
        });
    }

    [Test]
    public void Application_Must_Not_Reference_Worker_Or_Common_Runtime_Namespaces()
    {
        var applicationSources = ArchitectureTestHelpers.EnumerateProductionSourceFiles("LgymApi.Application")
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .ToArray();
        var violations = applicationSources
            .Where(source => source.Content.Contains("LgymApi.BackgroundWorker", StringComparison.Ordinal))
            .Select(source => ArchitectureTestHelpers.NormalizePath(source.Path))
            .ToArray();

        violations.Should().BeEmpty(
            "Application contracts and services must remain independent from Worker and Common runtime namespaces");
    }

    private static IEnumerable<SourceFile> EnumerateReportingSources(string repoRoot)
    {
        var reportingRoots = new[]
        {
            Path.Combine(repoRoot, "LgymApi.Application", "Features", "Reporting"),
            Path.Combine(repoRoot, "LgymApi.Application", "Reporting")
        };

        return reportingRoots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !ArchitectureTestHelpers.IsInBuildArtifacts(path))
            .OrderBy(path => ArchitectureTestHelpers.NormalizePath(path), StringComparer.Ordinal)
            .Select(path => new SourceFile(path, File.ReadAllText(path)));
    }

    private sealed record SourceFile(string Path, string Content);
}
