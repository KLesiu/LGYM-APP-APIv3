using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class TrainingPlanningWorkoutPortBoundaryGuardTests
{
    [Test]
    public void PlanningWorkoutPorts_ShouldExposeExactMarkerOnlySignatures()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(IPlanExerciseCatalogPort).GetMethods().Single().GetParameters().Select(parameter => parameter.ParameterType), Is.EqualTo(new[]
            {
                typeof(IReadOnlyCollection<Id<PlanExerciseReference>>), typeof(IReadOnlyList<string>), typeof(CancellationToken)
            }));
            Assert.That(typeof(IPlanExerciseClonePort).GetMethods().Single().GetParameters().Select(parameter => parameter.ParameterType), Is.EqualTo(new[]
            {
                typeof(Id<AccountReference>), typeof(IReadOnlyCollection<Id<PlanExerciseReference>>), typeof(CancellationToken)
            }));
            Assert.That(typeof(IPlanTrainingActivityPort).GetMethods().Single().GetParameters().Select(parameter => parameter.ParameterType), Is.EqualTo(new[]
            {
                typeof(IReadOnlyCollection<Id<PlanDayReference>>), typeof(CancellationToken)
            }));
            Assert.That(typeof(PlanExerciseCatalogItem).IsSealed, Is.True);
        });
    }

    [Test]
    public void TrainingPlanningProduction_ShouldNotReferenceWorkoutRepositoriesOrEntities()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var source = Directory.GetFiles(Path.Combine(root, "LgymApi.TrainingPlanning"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(source, Has.All.Not.Contains("IExerciseRepository"));
            Assert.That(source, Has.All.Not.Contains("ITrainingRepository"));
            Assert.That(source, Has.All.Not.Contains("using LgymApi.Application.WorkoutProgress"));
            Assert.That(source, Has.All.Not.Contains("using LgymApi.Domain.Entities.Exercise"));
            Assert.That(source, Has.All.Not.Contains("using LgymApi.Domain.Entities.Training"));
        });
    }
}
