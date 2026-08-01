using FluentAssertions;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker;
using LgymApi.BackgroundWorker.Actions.Contracts;
using LgymApi.BackgroundWorker.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class BackgroundActionRegistrationTests
{
    [Test]
    public void AddBackgroundAction_RegistersTypedHandler_WithCorrectGenericConstraint()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddBackgroundAction<TestCommand, TestAction>();
        var provider = services.BuildServiceProvider();

        // Assert
        var handlers = provider.GetServices<IBackgroundAction<TestCommand>>();
        handlers.Should().NotBeNull();
        handlers.Count().Should().Be(1);
        handlers.First().Should().BeOfType<TestAction>();
    }

    [Test]
    public void AddBackgroundAction_RegistersMultipleHandlers_ForSameExactCommandType()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddBackgroundAction<TestCommand, TestAction>();
        services.AddBackgroundAction<TestCommand, SecondTestAction>();
        var provider = services.BuildServiceProvider();

        // Assert
        var handlers = provider.GetServices<IBackgroundAction<TestCommand>>().ToList();
        handlers.Should().HaveCount(2);
        handlers.Any(h => h is TestAction).Should().BeTrue("TestAction should be registered");
        handlers.Any(h => h is SecondTestAction).Should().BeTrue("SecondTestAction should be registered");
    }

    [Test]
    public void AddBackgroundAction_ResolvesByExactTypeOnly_NoDerivedTypeMatching()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act - register handler for derived command
        services.AddBackgroundAction<DerivedCommand, DerivedCommandAction>();
        var provider = services.BuildServiceProvider();

        // Assert - base command type has no handlers
        var baseHandlers = provider.GetServices<IBackgroundAction<TestCommand>>().ToList();
        baseHandlers.Should().BeEmpty("Base command type should not resolve derived handlers");

        // Assert - derived command type has exact handler
        var derivedHandlers = provider.GetServices<IBackgroundAction<DerivedCommand>>().ToList();
        derivedHandlers.Should().HaveCount(1);
        derivedHandlers.First().Should().BeOfType<DerivedCommandAction>();
    }

    [Test]
    public void AddBackgroundAction_RegistersConcreteImplementation_ForDependencyGraph()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddBackgroundAction<TestCommand, TestAction>();
        var provider = services.BuildServiceProvider();

        // Assert - concrete implementation is resolvable
        var concreteAction = provider.GetService<TestAction>();
        concreteAction.Should().NotBeNull();
        concreteAction.Should().BeOfType<TestAction>();
    }

    [Test]
    public void AddBackgroundAction_AllowsFluentChaining()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act - chain multiple registrations
        var result = services
            .AddBackgroundAction<TestCommand, TestAction>()
            .AddBackgroundAction<AnotherCommand, AnotherCommandAction>();

        // Assert
        result.Should().BeSameAs(services, "Should return same IServiceCollection for chaining");
        var provider = services.BuildServiceProvider();
        provider.GetServices<IBackgroundAction<TestCommand>>().Count().Should().Be(1);
        provider.GetServices<IBackgroundAction<AnotherCommand>>().Count().Should().Be(1);
    }

    [Test]
    public void AddBackgroundAction_SupportsMultipleCallsForSameCommandType()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act - register three handlers for same command
        services
            .AddBackgroundAction<TestCommand, TestAction>()
            .AddBackgroundAction<TestCommand, SecondTestAction>()
            .AddBackgroundAction<TestCommand, ThirdTestAction>();

        var provider = services.BuildServiceProvider();

        // Assert
        var handlers = provider.GetServices<IBackgroundAction<TestCommand>>().ToList();
        handlers.Should().HaveCount(3);
        handlers.Select(h => h.GetType()).Should().BeEquivalentTo([
            typeof(TestAction),
            typeof(SecondTestAction),
            typeof(ThirdTestAction)
        ]);
    }

    [Test]
    public void ServiceProvider_RegistersTrainingCompletedCommand_WithTwoHandlers()
    {
        // Arrange
        var services = new ServiceCollection();

        // Register all handlers as ServiceProvider does
        services.AddBackgroundAction<LgymApi.Application.Identity.Contracts.BackgroundCommands.UserRegisteredCommand, LgymApi.BackgroundWorker.Actions.SendRegistrationEmailHandler>();
        services.AddBackgroundAction<LgymApi.Application.Coaching.Contracts.BackgroundCommands.InvitationCreatedCommand, LgymApi.BackgroundWorker.Actions.SendInvitationEmailHandler>();
        services.AddBackgroundAction<LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands.TrainingCompletedCommand, LgymApi.BackgroundWorker.Actions.TrainingCompletedEmailCommandHandler>();
        services.AddBackgroundAction<LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands.TrainingCompletedCommand, LgymApi.BackgroundWorker.Actions.UpdateTrainingMainRecordsHandler>();

        // Act
        var trainingDescriptors = services
            .Where(d => d.ServiceType == typeof(IBackgroundAction<LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands.TrainingCompletedCommand>))
            .ToList();
        var userDescriptors = services
            .Where(d => d.ServiceType == typeof(IBackgroundAction<LgymApi.Application.Identity.Contracts.BackgroundCommands.UserRegisteredCommand>))
            .ToList();
        var invitationDescriptors = services
            .Where(d => d.ServiceType == typeof(IBackgroundAction<LgymApi.Application.Coaching.Contracts.BackgroundCommands.InvitationCreatedCommand>))
            .ToList();

        // Assert - TrainingCompletedCommand has exactly 2 handlers
        trainingDescriptors.Should().HaveCount(2, "TrainingCompletedCommand must have exactly 2 handlers");
        trainingDescriptors.Any(d => d.ImplementationType == typeof(LgymApi.BackgroundWorker.Actions.TrainingCompletedEmailCommandHandler)).Should().BeTrue("Email handler must be registered");
        trainingDescriptors.Any(d => d.ImplementationType == typeof(LgymApi.BackgroundWorker.Actions.UpdateTrainingMainRecordsHandler)).Should().BeTrue("Main record handler must be registered");

        // Assert - Other commands have exactly 1 handler each
        userDescriptors.Should().HaveCount(1, "UserRegisteredCommand must have exactly 1 handler");
        invitationDescriptors.Should().HaveCount(1, "InvitationCreatedCommand must have exactly 1 handler");
    }

    [Test]
    public void ServiceProvider_Registers_NewTrainerNotificationHandlers()
    {
        var services = new ServiceCollection();

        services.AddBackgroundAction<LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportSubmissionCreatedInAppNotificationCommand, LgymApi.BackgroundWorker.Actions.ReportSubmissionCreatedInAppNotificationCommandHandler>();
        services.AddBackgroundAction<LgymApi.Application.Coaching.Contracts.BackgroundCommands.TrainerRelationshipEndedInAppNotificationCommand, LgymApi.BackgroundWorker.Actions.TrainerRelationshipEndedInAppNotificationCommandHandler>();

        var reportSubmissionDescriptors = services
            .Where(d => d.ServiceType == typeof(IBackgroundAction<LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportSubmissionCreatedInAppNotificationCommand>))
            .ToList();
        var relationshipEndedDescriptors = services
            .Where(d => d.ServiceType == typeof(IBackgroundAction<LgymApi.Application.Coaching.Contracts.BackgroundCommands.TrainerRelationshipEndedInAppNotificationCommand>))
            .ToList();

        reportSubmissionDescriptors.Should().ContainSingle();
        reportSubmissionDescriptors[0].ImplementationType.Should().Be(typeof(LgymApi.BackgroundWorker.Actions.ReportSubmissionCreatedInAppNotificationCommandHandler));

        relationshipEndedDescriptors.Should().ContainSingle();
        relationshipEndedDescriptors[0].ImplementationType.Should().Be(typeof(LgymApi.BackgroundWorker.Actions.TrainerRelationshipEndedInAppNotificationCommandHandler));
    }

    [Test]
    public void ServiceProvider_Registers_DietPlanUpdatedNotificationHandler()
    {
        var services = new ServiceCollection();

        services.AddBackgroundAction<LgymApi.Application.Nutrition.Contracts.BackgroundCommands.DietPlanUpdatedInAppNotificationCommand, LgymApi.BackgroundWorker.Actions.DietPlanUpdatedInAppNotificationCommandHandler>();

        var descriptors = services
            .Where(d => d.ServiceType == typeof(IBackgroundAction<LgymApi.Application.Nutrition.Contracts.BackgroundCommands.DietPlanUpdatedInAppNotificationCommand>))
            .ToList();

        descriptors.Should().ContainSingle();
        descriptors[0].ImplementationType.Should().Be(typeof(LgymApi.BackgroundWorker.Actions.DietPlanUpdatedInAppNotificationCommandHandler));
    }

    [Test]
    public void ServiceProvider_Registers_TraineeNoteUpdatedNotificationHandler()
    {
        var services = new ServiceCollection();

        services.AddBackgroundAction<LgymApi.Application.Coaching.Contracts.BackgroundCommands.TraineeNoteUpdatedInAppNotificationCommand, LgymApi.BackgroundWorker.Actions.TraineeNoteUpdatedInAppNotificationCommandHandler>();

        var descriptors = services
            .Where(d => d.ServiceType == typeof(IBackgroundAction<LgymApi.Application.Coaching.Contracts.BackgroundCommands.TraineeNoteUpdatedInAppNotificationCommand>))
            .ToList();

        descriptors.Should().ContainSingle();
        descriptors[0].ImplementationType.Should().Be(typeof(LgymApi.BackgroundWorker.Actions.TraineeNoteUpdatedInAppNotificationCommandHandler));
    }

    [Test]
    public void AddBackgroundAction_HandlersAreScoped_NewInstancePerScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddBackgroundAction<TestCommand, TestAction>();
        var provider = services.BuildServiceProvider();

        // Act - resolve in two different scopes
        IBackgroundAction<TestCommand> handler1;
        IBackgroundAction<TestCommand> handler2;

        using (var scope1 = provider.CreateScope())
        {
            handler1 = scope1.ServiceProvider.GetRequiredService<IBackgroundAction<TestCommand>>();
        }

        using (var scope2 = provider.CreateScope())
        {
            handler2 = scope2.ServiceProvider.GetRequiredService<IBackgroundAction<TestCommand>>();
        }

        // Assert - different instances per scope
        handler1.Should().NotBeSameAs(handler2, "Scoped services should be different instances across scopes");
    }

    [Test]
    public void AddBackgroundAction_GenericConstraint_RequiresIActionCommand()
    {
        // This test validates the compile-time constraint by attempting to use a valid command type.
        // Invalid types (not implementing IActionCommand) will fail at compile time, which is the desired behavior.

        // Arrange
        var services = new ServiceCollection();

        // Act - this compiles because TestCommand implements IActionCommand
        services.AddBackgroundAction<TestCommand, TestAction>();

        // Assert - if we reach here, the generic constraint is correctly enforced
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IBackgroundAction<TestCommand>));
    }

    [Test]
    public void ServiceProvider_RegistersTrainingCompletedCommand_WithTwoEmailHandlers()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act - register two handlers for same training completed command
        services.AddBackgroundAction<TrainingCompletedCommand, MockEmailCommandHandler>();
        services.AddBackgroundAction<TrainingCompletedCommand, MockMainRecordsCommandHandler>();
        var provider = services.BuildServiceProvider();

        // Assert - verify both handlers are registered
        var handlers = provider.GetServices<IBackgroundAction<TrainingCompletedCommand>>().ToList();
        handlers.Should().HaveCount(2);
        handlers.Any(h => h is MockEmailCommandHandler).Should().BeTrue("MockEmailCommandHandler should be registered");
        handlers.Any(h => h is MockMainRecordsCommandHandler).Should().BeTrue("MockMainRecordsCommandHandler should be registered");
    }

    [Test]
    public void AddBackgroundWorkerServices_RegistersTheLegacyFifteenCommandManifest()
    {
        var services = new ServiceCollection();
        services.AddBackgroundWorkerServices(isTesting: true);

        var action = () => ValidateClosedWorldBackgroundActionRegistrations(services);

        action.Should().NotThrow();
        GetClosedBackgroundActionRegistrations(services).Should().HaveCount(16);
    }

    [Test]
    public void ClosedWorldRegistrationValidation_RejectsMissingRegistration()
    {
        var services = new ServiceCollection();
        services.AddBackgroundWorkerServices(isTesting: true);
        var serviceType = typeof(IBackgroundAction<global::LgymApi.Application.Identity.Contracts.BackgroundCommands.UserRegisteredCommand>);
        services.Remove(services.Single(descriptor => descriptor.ServiceType == serviceType));

        var action = () => ValidateClosedWorldBackgroundActionRegistrations(services);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Closed background action registration mismatch*SendRegistrationEmailHandler*");
    }

    [Test]
    public void ClosedWorldRegistrationValidation_RejectsDuplicateRegistration()
    {
        var services = new ServiceCollection();
        services.AddBackgroundWorkerServices(isTesting: true);
        services.AddScoped<
            IBackgroundAction<global::LgymApi.Application.Identity.Contracts.BackgroundCommands.UserRegisteredCommand>,
            global::LgymApi.BackgroundWorker.Actions.SendRegistrationEmailHandler>();

        var action = () => ValidateClosedWorldBackgroundActionRegistrations(services);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Closed background action registration mismatch*SendRegistrationEmailHandler*");
    }

    [Test]
    public void ClosedWorldRegistrationValidation_RejectsWrongHandlerForListedCommand()
    {
        var services = new ServiceCollection();
        services.AddBackgroundWorkerServices(isTesting: true);
        var serviceType = typeof(IBackgroundAction<global::LgymApi.Application.Identity.Contracts.BackgroundCommands.UserRegisteredCommand>);
        services.Remove(services.Single(descriptor => descriptor.ServiceType == serviceType));
        services.AddScoped<
            IBackgroundAction<global::LgymApi.Application.Identity.Contracts.BackgroundCommands.UserRegisteredCommand>,
            WrongUserRegisteredHandler>();

        var action = () => ValidateClosedWorldBackgroundActionRegistrations(services);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Closed background action registration mismatch*WrongUserRegisteredHandler*");
    }

    [Test]
    public void ClosedWorldRegistrationValidation_RejectsUnlistedCommandRegistration()
    {
        var services = new ServiceCollection();
        services.AddBackgroundWorkerServices(isTesting: true);
        services.AddBackgroundAction<UnlistedManifestCommand, UnlistedManifestHandler>();

        var action = () => ValidateClosedWorldBackgroundActionRegistrations(services);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Closed background action registration mismatch*UnlistedManifestCommand*UnlistedManifestHandler*");
    }

    private static void ValidateClosedWorldBackgroundActionRegistrations(IServiceCollection services)
    {
        var actual = GetClosedBackgroundActionRegistrations(services);
        var registry = CommandContractRegistry.CreateDefault();
        var expected = registry.Contracts
            .SelectMany(contract => contract.ExpectedHandlerTypes.Select(handlerType =>
                new LegacyHandlerRegistration(contract.RuntimeType.FullName!, handlerType.FullName!)))
            .OrderBy(registration => registration.CommandTypeFullName, StringComparer.Ordinal)
            .ThenBy(registration => registration.HandlerTypeFullName, StringComparer.Ordinal)
            .ToArray();

        if (!actual.SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                "Closed background action registration mismatch. "
                + $"Expected [{FormatRegistrations(expected)}]; "
                + $"actual [{FormatRegistrations(actual)}].");
        }
    }

    private static LegacyHandlerRegistration[] GetClosedBackgroundActionRegistrations(IServiceCollection services) =>
        services
            .Where(descriptor => descriptor.ServiceType.IsConstructedGenericType
                && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IBackgroundAction<>))
            .Select(descriptor => new LegacyHandlerRegistration(
                descriptor.ServiceType.GetGenericArguments()[0].FullName!,
                descriptor.ImplementationType?.FullName
                    ?? throw new InvalidOperationException(
                        $"Background action registration '{descriptor.ServiceType}' must use an implementation type.")))
            .OrderBy(registration => registration.CommandTypeFullName, StringComparer.Ordinal)
            .ThenBy(registration => registration.HandlerTypeFullName, StringComparer.Ordinal)
            .ToArray();

    private static string FormatRegistrations(IEnumerable<LegacyHandlerRegistration> registrations) =>
        string.Join(", ", registrations.Select(registration =>
            $"{registration.CommandTypeFullName} -> {registration.HandlerTypeFullName}"));

    #region Test Fixtures

    private class TestCommand : IActionCommand
    {
    }

    private sealed class DerivedCommand : TestCommand, IActionCommand
    {
    }

    private sealed class AnotherCommand : IActionCommand
    {
    }

    private sealed class TestAction : IBackgroundAction<TestCommand>
    {
        public Task ExecuteAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class SecondTestAction : IBackgroundAction<TestCommand>
    {
        public Task ExecuteAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ThirdTestAction : IBackgroundAction<TestCommand>
    {
        public Task ExecuteAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class DerivedCommandAction : IBackgroundAction<DerivedCommand>
    {
        public Task ExecuteAsync(DerivedCommand command, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class AnotherCommandAction : IBackgroundAction<AnotherCommand>
    {
        public Task ExecuteAsync(AnotherCommand command, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class MockEmailCommandHandler : IBackgroundAction<TrainingCompletedCommand>
    {
        public Task ExecuteAsync(TrainingCompletedCommand command, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class MockMainRecordsCommandHandler : IBackgroundAction<TrainingCompletedCommand>
    {
        public Task ExecuteAsync(TrainingCompletedCommand command, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TrainingCompletedCommand : IActionCommand
    {
    }

    private sealed class WrongUserRegisteredHandler :
        IBackgroundAction<global::LgymApi.Application.Identity.Contracts.BackgroundCommands.UserRegisteredCommand>
    {
        public Task ExecuteAsync(
            global::LgymApi.Application.Identity.Contracts.BackgroundCommands.UserRegisteredCommand command,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class UnlistedManifestCommand : IActionCommand
    {
    }

    private sealed class UnlistedManifestHandler : IBackgroundAction<UnlistedManifestCommand>
    {
        public Task ExecuteAsync(
            UnlistedManifestCommand command,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed record LegacyHandlerRegistration(string CommandTypeFullName, string HandlerTypeFullName);

    #endregion
}
