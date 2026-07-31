using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class PartialServiceContributionGuardTests
{
    private static readonly PartialServiceContributionAnalyzer.PartialFamily[] Families =
    [
        new PartialServiceContributionAnalyzer.PartialFamily("LgymApi.Application.Features.Reporting.ReportingService", "LgymApi.Application.Features.Reporting.IReportingService", "LgymApi.Application/Reporting/ServiceCollectionExtensions.cs"),
        new PartialServiceContributionAnalyzer.PartialFamily("LgymApi.Application.Features.Reporting.RecurringReportAssignmentService", "LgymApi.Application.Features.Reporting.IRecurringReportAssignmentService", "LgymApi.Application/Reporting/ServiceCollectionExtensions.cs"),
        new PartialServiceContributionAnalyzer.PartialFamily("LgymApi.Application.WorkoutProgress.ProgressData.WorkoutProgressReadWriteService", "LgymApi.Application.WorkoutProgress.ProgressData.IWorkoutProgressReadWriteService", "LgymApi.Application/WorkoutProgress/ServiceCollectionExtensions.cs"),
        new PartialServiceContributionAnalyzer.PartialFamily("LgymApi.Application.Features.Exercise.ExerciseService", "LgymApi.Application.Features.Exercise.IExerciseService", "LgymApi.Application/WorkoutProgress/ServiceCollectionExtensions.cs"),
        new PartialServiceContributionAnalyzer.PartialFamily("LgymApi.Application.Features.Training.TrainingService", "LgymApi.Application.Features.Training.ITrainingService", "LgymApi.Application/WorkoutProgress/ServiceCollectionExtensions.cs"),
        new PartialServiceContributionAnalyzer.PartialFamily("LgymApi.BackgroundWorker.BackgroundActionOrchestratorService", null, "LgymApi.BackgroundWorker/ServiceProvider.cs")
    ];

    private static readonly PartialServiceContributionAnalyzer.PartialContribution[] Contributions =
    [
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Features/Reporting/ReportingService.cs", "LgymApi.Application.Features.Reporting.ReportingService", "EnsureTrainer", "CreateTemplateAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Features/Reporting/ReportingService.Templates.cs", "LgymApi.Application.Features.Reporting.ReportingService", "CreateTemplateAsync", "CreateTemplateAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Features/Reporting/ReportingService.Requests.cs", "LgymApi.Application.Features.Reporting.ReportingService", "CreateReportRequestAsync", "CreateReportRequestAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Features/Reporting/ReportingService.Submissions.cs", "LgymApi.Application.Features.Reporting.ReportingService", "SubmitReportRequestAsync", "SubmitReportRequestAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Features/Reporting/ReportingService.Submissions.Read.cs", "LgymApi.Application.Features.Reporting.ReportingService", "GetTraineeSubmissionsAsync", "GetTraineeSubmissionsAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Features/Reporting/ReportingService.Submissions.Helpers.cs", "LgymApi.Application.Features.Reporting.ReportingService", "ValidateAnswersAgainstTemplate", "SubmitReportRequestAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Features/Reporting/ReportingService.Submissions.PhotoValidation.cs", "LgymApi.Application.Features.Reporting.ReportingService", "ValidateRequiredPhotosAsync", "SubmitReportRequestAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Features/Reporting/ReportingService.Photos.cs", "LgymApi.Application.Features.Reporting.ReportingService", "CompletePhotoUploadAsync", "CompletePhotoUploadAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Features/Reporting/ReportingService.Photos.Read.cs", "LgymApi.Application.Features.Reporting.ReportingService", "GetSignedReadUrlAsync", "GetSignedReadUrlAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Features/Reporting/ReportingService.Photos.Support.cs", "LgymApi.Application.Features.Reporting.ReportingService", "ValidatePhotoAccessAsync", "InitiatePhotoUploadAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Features/Reporting/ReportingService.Photos.Completion.cs", "LgymApi.Application.Features.Reporting.ReportingService", "ValidateCompletePhotoUploadRequestAsync", "CompletePhotoUploadAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Features/Reporting/RecurringReportAssignmentService.cs", "LgymApi.Application.Features.Reporting.RecurringReportAssignmentService", "CreateAsync", "CreateAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Features/Reporting/RecurringReportAssignmentService.Support.cs", "LgymApi.Application.Features.Reporting.RecurringReportAssignmentService", "ValidateTrainerAndCommandAsync", "CreateAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/WorkoutProgress/ProgressData/WorkoutProgressReadWriteService.cs", "LgymApi.Application.WorkoutProgress.ProgressData.WorkoutProgressReadWriteService", "GetExerciseScoreChartAsync", "GetExerciseScoreChartAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/WorkoutProgress/ProgressData/WorkoutProgressReadWriteService.Measurements.cs", "LgymApi.Application.WorkoutProgress.ProgressData.WorkoutProgressReadWriteService", "AddMeasurementsAsync", "AddMeasurementsAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/WorkoutProgress/ProgressData/WorkoutProgressReadWriteService.MainRecords.cs", "LgymApi.Application.WorkoutProgress.ProgressData.WorkoutProgressReadWriteService", "AddMainRecordAsync", "AddMainRecordAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Exercise/ExerciseService.cs", "LgymApi.Application.Features.Exercise.ExerciseService", "GetTranslationsForExercisesAsync", "GetAllExercisesAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Exercise/ExerciseService.Queries.cs", "LgymApi.Application.Features.Exercise.ExerciseService", "GetAllExercisesAsync", "GetAllExercisesAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Exercise/ExerciseService.Management.cs", "LgymApi.Application.Features.Exercise.ExerciseService", "AddExerciseAsync", "AddExerciseAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Exercise/ExerciseService.Scores.cs", "LgymApi.Application.Features.Exercise.ExerciseService", "GetLastExerciseScoresAsync", "GetLastExerciseScoresAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Training/TrainingService.cs", "LgymApi.Application.Features.Training.TrainingService", ".ctor", ".ctor", PartialServiceContributionAnalyzer.ContributionRoute.DependencyInjection),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Training/TrainingService.Queries.cs", "LgymApi.Application.Features.Training.TrainingService", "GetLastTrainingAsync", "GetLastTrainingAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.Application/Training/TrainingService.AddTraining.cs", "LgymApi.Application.Features.Training.TrainingService", "AddTrainingAsync", "AddTrainingAsync", PartialServiceContributionAnalyzer.ContributionRoute.Interface),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.BackgroundWorker/BackgroundActionOrchestratorService.cs", "LgymApi.BackgroundWorker.BackgroundActionOrchestratorService", "OrchestrateAsync", "OrchestrateAsync", PartialServiceContributionAnalyzer.ContributionRoute.ConcreteCaller),
        new PartialServiceContributionAnalyzer.PartialContribution("LgymApi.BackgroundWorker/BackgroundActionOrchestratorService.HandlerInvocation.cs", "LgymApi.BackgroundWorker.BackgroundActionOrchestratorService", "ExecuteHandlerInIsolatedScopeAsync", "OrchestrateAsync", PartialServiceContributionAnalyzer.ContributionRoute.ConcreteCaller)
    ];

    [Test]
    public void Approved_Partial_Service_Contributors_Must_Be_Exact_Compiled_And_Live()
    {
        var (repoRoot, compilation, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Application", "LgymApi.BackgroundWorker");

        PartialServiceContributionAnalyzer.AssertExactPartialManifest(repoRoot, compilation, syntaxTrees, Families, Contributions);
        PartialServiceContributionAnalyzer.AssertFamilyRegistrations(repoRoot, compilation, syntaxTrees, Families);
        var callGraph = PartialServiceContributionAnalyzer.BuildCallGraph(compilation, syntaxTrees);

        foreach (var contribution in Contributions)
        {
            PartialServiceContributionAnalyzer.AssertContribution(repoRoot, compilation, syntaxTrees, Families, contribution, callGraph);
        }
    }

    [Test]
    public void Empty_Unreferenced_And_Unlisted_Partial_Fixtures_Must_Fail_With_Exact_Diagnostics()
    {
        var emptyFixture = PartialServiceContributionAnalyzer.CreateFixture("""
            namespace Fixtures;
            public interface IFixture { void Live(); }
            public partial class Fixture : IFixture { public void Live() { } }
            """, "Fixtures/Fixture.cs", "namespace Fixtures; public partial class Fixture { }");
        var family = new PartialServiceContributionAnalyzer.PartialFamily("Fixtures.Fixture", "Fixtures.IFixture", "Fixtures/Fixture.cs");
        var emptyContribution = new PartialServiceContributionAnalyzer.PartialContribution("Fixtures/EmptyPartial.cs", "Fixtures.Fixture", "Missing", "Live", PartialServiceContributionAnalyzer.ContributionRoute.Interface);
        var emptyFailure = Assert.Throws<InvalidOperationException>(() => PartialServiceContributionAnalyzer.AssertContribution(
            string.Empty,
            emptyFixture.Compilation,
            emptyFixture.Trees,
            [family],
            emptyContribution,
            PartialServiceContributionAnalyzer.BuildCallGraph(emptyFixture.Compilation, emptyFixture.Trees)));

        Assert.That(emptyFailure!.Message, Is.EqualTo("Partial contributor 'Fixtures/EmptyPartial.cs#Fixtures.Fixture.Missing' does not declare a compiled member named 'Missing'."));

        var unreferencedFixture = PartialServiceContributionAnalyzer.CreateFixture("""
            namespace Fixtures;
            public interface IFixture { void Live(); }
            public partial class Fixture : IFixture { public void Live() { } }
            """, "Fixtures/Fixture.cs", "namespace Fixtures; public partial class Fixture { private void Unused() { } }");
        var unreferencedContribution = new PartialServiceContributionAnalyzer.PartialContribution("Fixtures/EmptyPartial.cs", "Fixtures.Fixture", "Unused", "Live", PartialServiceContributionAnalyzer.ContributionRoute.Interface);
        var unreferencedFailure = Assert.Throws<InvalidOperationException>(() => PartialServiceContributionAnalyzer.AssertContribution(
            string.Empty,
            unreferencedFixture.Compilation,
            unreferencedFixture.Trees,
            [family],
            unreferencedContribution,
            PartialServiceContributionAnalyzer.BuildCallGraph(unreferencedFixture.Compilation, unreferencedFixture.Trees)));

        Assert.That(unreferencedFailure!.Message, Is.EqualTo("Partial contributor 'Fixtures/EmptyPartial.cs#Fixtures.Fixture.Unused' has no compiled live path from 'Fixtures.Fixture.Live'."));

        var manifestFailure = Assert.Throws<InvalidOperationException>(() => PartialServiceContributionAnalyzer.AssertExactPartialManifest(
            string.Empty,
            unreferencedFixture.Compilation,
            unreferencedFixture.Trees,
            [family],
            [new PartialServiceContributionAnalyzer.PartialContribution("Fixtures/Fixture.cs", "Fixtures.Fixture", "Live", "Live", PartialServiceContributionAnalyzer.ContributionRoute.Interface)]));

        Assert.That(manifestFailure!.Message, Is.EqualTo("Approved partial source manifest mismatch. Missing: none; unexpected: Fixtures/EmptyPartial.cs."));
    }

}
