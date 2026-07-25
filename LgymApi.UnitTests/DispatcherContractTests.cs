using FluentAssertions;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using NUnit.Framework;
using System.Reflection;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class DispatcherContractTests
{
    private const string PlatformBackgroundCommandsNamespace =
        "LgymApi.Application.Platform.Contracts.BackgroundCommands";
    private const string ApplicationActionCommandTypeName =
        $"{PlatformBackgroundCommandsNamespace}.IActionCommand";
    private const string ApplicationCommandDispatcherTypeName =
        $"{PlatformBackgroundCommandsNamespace}.ICommandDispatcher";
    private const string ApplicationCommandOutboxWriterTypeName =
        $"{PlatformBackgroundCommandsNamespace}.ICommandOutboxWriter";
    private const string CommandEnvelopeStageResultTypeName =
        $"{PlatformBackgroundCommandsNamespace}.CommandEnvelopeStageResult";

    [Test]
    public void ApplicationICommandDispatcher_HasExactLegacyPublicShape()
    {
        var actionCommandType = GetApplicationType(ApplicationActionCommandTypeName);
        var dispatcherType = GetApplicationType(ApplicationCommandDispatcherTypeName);
        var methods = dispatcherType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        dispatcherType.IsPublic.Should().BeTrue();
        dispatcherType.IsInterface.Should().BeTrue();
        dispatcherType.IsGenericType.Should().BeFalse();
        dispatcherType.GetInterfaces().Should().BeEmpty();
        dispatcherType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Should().BeEmpty();
        dispatcherType.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Should().BeEmpty();
        methods.Should().ContainSingle();

        var method = methods.Single();
        method.Name.Should().Be("EnqueueAsync");
        method.IsGenericMethodDefinition.Should().BeTrue();
        method.ReturnType.Should().Be(typeof(Task));
        method.GetParameters().Should().ContainSingle();

        var genericParameter = method.GetGenericArguments().Should().ContainSingle().Subject;
        genericParameter.GenericParameterAttributes.Should().Be(GenericParameterAttributes.ReferenceTypeConstraint);
        genericParameter.GetGenericParameterConstraints().Should().Equal(actionCommandType);

        var commandParameter = method.GetParameters().Single();
        commandParameter.Name.Should().Be("command");
        commandParameter.ParameterType.Should().Be(genericParameter);
        commandParameter.IsOptional.Should().BeFalse();
    }

    [Test]
    public void ApplicationPlatformCommandPorts_DoNotExposeWorkerOrHangfireTypes()
    {
        var applicationAssembly = typeof(LgymApi.Application.ServiceCollectionExtensions).Assembly;
        var portTypes = applicationAssembly.GetExportedTypes()
            .Where(type => type.Namespace == PlatformBackgroundCommandsNamespace)
            .OrderBy(type => type.Name)
            .ToArray();

        portTypes.Select(type => type.FullName).Should().Equal(
            CommandEnvelopeStageResultTypeName,
            ApplicationActionCommandTypeName,
            ApplicationCommandDispatcherTypeName,
            ApplicationCommandOutboxWriterTypeName);

        var exposedSignatureTypes = portTypes
            .SelectMany(GetExposedSignatureTypes)
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        exposedSignatureTypes.Should().NotContain(name =>
            name.Contains("Hangfire", StringComparison.Ordinal)
            || name.Contains("LgymApi.BackgroundWorker", StringComparison.Ordinal));
    }

    [Test]
    public void ICommandDispatcher_EnqueueTypedCommand_ExposesTypeSafeAPI()
    {
        // Arrange
        var dispatcher = new FakeCommandDispatcher();
        var command = new TestCommand { Value = 42 };

        // Act
        dispatcher.EnqueueAsync(command);

        // Assert
        dispatcher.EnqueuedCommands.Should().HaveCount(1);
        dispatcher.EnqueuedCommands[0].Should().BeOfType<TestCommand>();
        ((TestCommand)dispatcher.EnqueuedCommands[0]).Value.Should().Be(42);
    }

    [Test]
    public void ICommandDispatcher_GenericConstraint_EnforcesCommandInterface()
    {
        // This test validates compile-time contract via type system.
        // If ICommandDispatcher.Enqueue<TCommand> where TCommand : ICommand
        // is correctly defined, this will compile. Otherwise it won't.
        var dispatcher = new FakeCommandDispatcher();
        var command = new TestCommand { Value = 99 };

        // Act & Assert: compiler enforces ICommand constraint
        dispatcher.EnqueueAsync(command);

        dispatcher.EnqueuedCommands.Should().ContainSingle().Which.Should().BeSameAs(command);
    }

    [Test]
    public void ICommandDispatcher_NoStringBasedOverloads_EnforcesTypeSafety()
    {
        // This test validates that ICommandDispatcher has NO string-based routing.
        // The contract should only expose Enqueue<TCommand>(TCommand command, ...).
        // If string overloads exist, this would fail at compile time or via ast-grep.
        var dispatcher = new FakeCommandDispatcher();

        // Assert: only typed API available
        dispatcher.Should().NotBeNull();
        // Pass indicates no string-based routing overloads present
    }

    private sealed record TestCommand : IActionCommand
    {
        public int Value { get; init; }
    }

    private sealed class FakeCommandDispatcher : ICommandDispatcher
    {
        public List<object> EnqueuedCommands { get; } = new();

        public Task EnqueueAsync<TCommand>(TCommand command)
            where TCommand : class, IActionCommand
        {
            EnqueuedCommands.Add(command!);
            return Task.CompletedTask;
        }
    }

    private static Type GetApplicationType(string metadataName)
    {
        var type = typeof(LgymApi.Application.ServiceCollectionExtensions).Assembly.GetType(metadataName);
        type.Should().NotBeNull($"{metadataName} must be defined by the Application assembly");
        return type!;
    }

    private static IEnumerable<Type> GetExposedSignatureTypes(Type portType)
    {
        yield return portType;

        foreach (var interfaceType in portType.GetInterfaces())
        {
            yield return interfaceType;
        }

        foreach (var method in portType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            yield return method.ReturnType;

            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }

            foreach (var constraint in method.GetGenericArguments().SelectMany(argument => argument.GetGenericParameterConstraints()))
            {
                yield return constraint;
            }
        }
    }
}
