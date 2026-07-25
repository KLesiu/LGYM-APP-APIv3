using FluentAssertions;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker.Actions.Contracts;
using NUnit.Framework;
using System.Reflection;

[TestFixture]
public sealed class BackgroundActionContractTests
{
    private const string ApplicationActionCommandTypeName =
        "LgymApi.Application.Platform.Contracts.BackgroundCommands.IActionCommand";

    [Test]
    public void ApplicationIActionCommand_IsExactPublicMarkerInterface()
    {
        var type = GetApplicationType(ApplicationActionCommandTypeName);

        type.IsPublic.Should().BeTrue();
        type.IsInterface.Should().BeTrue();
        type.IsGenericType.Should().BeFalse();
        type.GetInterfaces().Should().BeEmpty();
        type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Should().BeEmpty();
    }

    [Test]
    public void IActionCommand_IsMarkerInterface()
    {
        // Verify IActionCommand is defined and can be implemented
        var type = typeof(IActionCommand);
        type.IsInterface.Should().BeTrue();
        type.GetProperties().Should().BeEmpty();
        type.GetMethods().Should().HaveCount(0);
    }

    [Test]
    public void IBackgroundAction_HasGenericConstraint()
    {
        // Verify IBackgroundAction<TCommand> requires TCommand to implement IActionCommand
        var type = typeof(IBackgroundAction<>);
        var typeParams = type.GetGenericArguments();

        typeParams.Should().HaveCount(1);
        var constraint = typeParams[0].GetGenericParameterConstraints();
        constraint.Should().Contain(typeof(IActionCommand));
    }

    [Test]
    public void IBackgroundAction_HasExecuteAsyncMethod()
    {
        // Verify ExecuteAsync method signature
        var type = typeof(IBackgroundAction<>);
        var method = type.GetMethod("ExecuteAsync");

        method.Should().NotBeNull();
        method.ReturnType.Should().Be(typeof(Task));

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(2);
        parameters[0].Name.Should().Be("command");
        parameters[1].Name.Should().Be("cancellationToken");
        parameters[1].ParameterType.Should().Be(typeof(CancellationToken));
    }

    [Test]
    public void Implementation_CanImplementIActionCommand()
    {
        // Arrange & Act
        var command = new TestCommand();

        // Assert
        command.Should().BeAssignableTo<IActionCommand>();
    }

    [Test]
    public void Implementation_CanImplementIBackgroundAction()
    {
        // Arrange & Act
        var handler = new TestActionHandler();

        // Assert
        handler.Should().BeAssignableTo<IBackgroundAction<TestCommand>>();
    }

    [Test]
    public async Task Implementation_ExecuteAsync_WorksWithCancellationToken()
    {
        // Arrange
        var handler = new TestActionHandler();
        var command = new TestCommand();
        using var cts = new CancellationTokenSource();

        // Act
        await handler.ExecuteAsync(command, cts.Token);

        // Assert
        handler.WasExecuted.Should().BeTrue();
        handler.ReceivedCommand.Should().BeSameAs(command);
    }

    [Test]
    public async Task Implementation_ExecuteAsync_WithDefaultCancellationToken()
    {
        // Arrange
        var handler = new TestActionHandler();
        var command = new TestCommand();

        // Act
        await handler.ExecuteAsync(command);

        // Assert
        handler.WasExecuted.Should().BeTrue();
    }

    [Test]
    public void MultipleHandlers_CanCoexist_ForDifferentCommands()
    {
        // Verify strong typing prevents mixing incompatible handlers and commands
        var testHandler = new TestActionHandler();
        var anotherHandler = new AnotherTestActionHandler();

        testHandler.Should().BeAssignableTo<IBackgroundAction<TestCommand>>();
        anotherHandler.Should().BeAssignableTo<IBackgroundAction<AnotherTestCommand>>();
    }

    [Test]
    public void GenericConstraint_PreventsNonActionCommandTypes()
    {
        // Compile-time check: IBackgroundAction<T> where T : IActionCommand
        // This test verifies the constraint is properly defined at runtime
        var type = typeof(IBackgroundAction<>);
        var constraint = type.GetGenericArguments()[0].GetGenericParameterConstraints();

        constraint.Should().Contain(typeof(IActionCommand));
    }

    // Test implementations
    private sealed class TestCommand : IActionCommand
    {
    }

    private sealed class AnotherTestCommand : IActionCommand
    {
    }

    private sealed class TestActionHandler : IBackgroundAction<TestCommand>
    {
        public bool WasExecuted { get; private set; }
        public TestCommand? ReceivedCommand { get; private set; }

        public Task ExecuteAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            WasExecuted = true;
            ReceivedCommand = command;
            return Task.CompletedTask;
        }
    }

    private sealed class AnotherTestActionHandler : IBackgroundAction<AnotherTestCommand>
    {
        public Task ExecuteAsync(AnotherTestCommand command, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private static Type GetApplicationType(string metadataName)
    {
        var type = typeof(LgymApi.Application.ServiceCollectionExtensions).Assembly.GetType(metadataName);
        type.Should().NotBeNull($"{metadataName} must be defined by the Application assembly");
        return type!;
    }
}
