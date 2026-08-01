using System.Text.Json;
using FluentAssertions;
using LgymApi.Application.Identity.Contracts.BackgroundCommands;
using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Application.Nutrition.Contracts.BackgroundCommands;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Application.WorkoutProgress.Contracts.BackgroundActions;
using LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker.Actions;
using LgymApi.BackgroundWorker.Notifications;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class WorkerOwnerPortBridgeTests
{
    [Test]
    public async Task EmailJobHandlerService_ForwardsStringIdentityAndCancellation()
    {
        var port = Substitute.For<IEmailJobExecutionPort>();
        using var cancellationSource = new CancellationTokenSource();

        await new EmailJobHandlerService(port).ProcessAsync("notification-42", cancellationSource.Token);

        await port.Received(1).ProcessAsync("notification-42", cancellationSource.Token);
    }

    [TestCaseSource(nameof(WorkerCommands))]
    public async Task RawWorkerHandlers_SerializeCanonicalCommandToOwnerPort(object command, Func<object, Task> execute, Func<string, Task> verify)
    {
        await execute(command);
        await verify(JsonSerializer.Serialize(command, SharedSerializationOptions.Current));
    }

    private static IEnumerable<TestCaseData> WorkerCommands()
    {
        var userRegistered = new UserRegisteredCommand { UserId = Id<User>.New() };
        var registrationPort = Substitute.For<IUserRegisteredActionExecutionPort>();
        yield return new TestCaseData(userRegistered,
            (Func<object, Task>)(command => new SendRegistrationEmailHandler(registrationPort).ExecuteAsync((UserRegisteredCommand)command)),
            (Func<string, Task>)(payload => registrationPort.Received(1).ExecuteAsync(payload, Arg.Any<CancellationToken>())));

        var trainingCompleted = new TrainingCompletedCommand { UserId = Id<User>.New(), TrainingId = Id<Training>.New() };
        var mainRecordsPort = Substitute.For<ITrainingMainRecordsUpdatePort>();
        yield return new TestCaseData(trainingCompleted,
            (Func<object, Task>)(command => new UpdateTrainingMainRecordsHandler(mainRecordsPort).ExecuteAsync((TrainingCompletedCommand)command)),
            (Func<string, Task>)(payload => mainRecordsPort.Received(1).UpdateAsync(payload, Arg.Any<CancellationToken>())));

        var dietPlanUpdated = new DietPlanUpdatedInAppNotificationCommand
        {
            DietPlanId = Id<DietPlan>.New(), TraineeId = Id<User>.New(), TrainerId = Id<User>.New(), DietPlanName = "Strength", TriggeredAt = DateTimeOffset.UtcNow
        };
        var dietPlanPort = Substitute.For<IDietPlanUpdatedActionExecutionPort>();
        yield return new TestCaseData(dietPlanUpdated,
            (Func<object, Task>)(command => new DietPlanUpdatedInAppNotificationCommandHandler(dietPlanPort).ExecuteAsync((DietPlanUpdatedInAppNotificationCommand)command)),
            (Func<string, Task>)(payload => dietPlanPort.Received(1).ExecuteAsync(payload, Arg.Any<CancellationToken>())));
    }
}
