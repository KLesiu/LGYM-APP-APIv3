using System.IO;
using FluentAssertions;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ApplicationCommandProducerContractTests
{
    public static IEnumerable<TestCaseData> ProducerContractCases()
    {
        yield return CreateCase("WorkoutProgress/TrainingExecution/CompleteTrainingUseCase.cs", "LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands");
        yield return CreateCase("Nutrition/DietPlans/CreateTraineePlan/CreateTraineeDietPlanUseCase.cs", "LgymApi.Application.Platform.Contracts.BackgroundCommands", "LgymApi.Application.Nutrition.Contracts.BackgroundCommands");
        yield return CreateCase("Nutrition/DietPlans/UpdateTraineePlan/UpdateTraineeDietPlanUseCase.cs", "LgymApi.Application.Platform.Contracts.BackgroundCommands", "LgymApi.Application.Nutrition.Contracts.BackgroundCommands");
        yield return CreateCase("Nutrition/DietPlans/ActivateTraineePlan/ActivateTraineeDietPlanUseCase.cs", "LgymApi.Application.Platform.Contracts.BackgroundCommands", "LgymApi.Application.Nutrition.Contracts.BackgroundCommands");
        yield return CreateCase("Features/Reporting/ReportingService.cs", "LgymApi.Application.Platform.Contracts.BackgroundCommands");
        yield return CreateCase("Features/Reporting/ReportingService.Requests.cs", "LgymApi.Application.Reporting.Contracts.BackgroundCommands");
        yield return CreateCase("Features/Reporting/ReportingService.Submissions.cs", "LgymApi.Application.Reporting.Contracts.BackgroundCommands");
        yield return CreateCase("Features/Reporting/RecurringReportAssignmentService.cs", "LgymApi.Application.Platform.Contracts.BackgroundCommands");
        yield return CreateCase("Features/Reporting/RecurringReportAssignmentService.RequestNow.cs", "LgymApi.Application.Reporting.Contracts.BackgroundCommands");
    }

    [TestCaseSource(nameof(ProducerContractCases))]
    public void Producer_UsesOnlyPlatformAndModuleOwnedCommandContracts(ProducerContractExpectation expectation)
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LgymApi.Application", expectation.RelativePath));

        source.Should().NotContain("LgymApi.BackgroundWorker.Common");
        foreach (var requiredNamespace in expectation.RequiredNamespaces)
        {
            source.Should().Contain($"using {requiredNamespace};");
        }
    }

    private static TestCaseData CreateCase(string relativePath, params string[] requiredNamespaces)
        => new TestCaseData(new ProducerContractExpectation(relativePath, requiredNamespaces))
            .SetName(relativePath.Replace('/', '_'));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LgymApi.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the repository root from the test directory.");
    }

    public sealed record ProducerContractExpectation(string RelativePath, IReadOnlyCollection<string> RequiredNamespaces);
}
