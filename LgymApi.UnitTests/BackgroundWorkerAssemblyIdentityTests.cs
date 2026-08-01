using FluentAssertions;
using LgymApi.BackgroundWorker;
using LgymApi.BackgroundWorker.Actions;
using LgymApi.BackgroundWorker.Notifications;
using LgymApi.BackgroundWorker.Runtime;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class BackgroundWorkerAssemblyIdentityTests
{
    [Test]
    public void FrozenWorkerRuntimeTypes_ResolveFromWorkerAssembly()
    {
        var workerAssemblyName = "LgymApi.BackgroundWorker";
        var runtimeTypes = new[]
        {
            typeof(BackgroundActionOrchestratorService),
            typeof(BackgroundActionResolver),
            typeof(CommandDispatcher),
            typeof(CommandOutboxWriter),
            typeof(CommandContractRegistry),
            typeof(EmailJobHandlerService),
            typeof(EmailSchedulerService<>),
            typeof(SendRegistrationEmailHandler),
            typeof(TrainingCompletedEmailCommandHandler),
            typeof(UpdateTrainingMainRecordsHandler),
            typeof(DietPlanUpdatedInAppNotificationCommandHandler)
        };

        runtimeTypes.Should().OnlyContain(type => type.Assembly.GetName().Name == workerAssemblyName);
    }
}
