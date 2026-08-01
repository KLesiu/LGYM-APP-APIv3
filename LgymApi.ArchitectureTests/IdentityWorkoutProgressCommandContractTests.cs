using FluentAssertions;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TrainingCompletedCommand = LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands.TrainingCompletedCommand;
using UserRegisteredCommand = LgymApi.Application.Identity.Contracts.BackgroundCommands.UserRegisteredCommand;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class IdentityWorkoutProgressCommandContractTests
{
    [Test]
    public void Commands_HaveExactApplicationOwnedPublicSurfaceAndDefaults()
    {
        AssertCommandContract<UserRegisteredCommand>(
            "LgymApi.Application.Identity.Contracts.BackgroundCommands",
            "LgymApi.Identity",
            ("UserId", typeof(Id<User>)));
        AssertCommandContract<TrainingCompletedCommand>(
            "LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands",
            "LgymApi.Application",
            ("UserId", typeof(Id<User>)),
            ("TrainingId", typeof(Id<Training>)));

        Assert.Multiple(() =>
        {
            new UserRegisteredCommand().UserId.Should().Be(default(Id<User>));
            new TrainingCompletedCommand().UserId.Should().Be(default(Id<User>));
            new TrainingCompletedCommand().TrainingId.Should().Be(default(Id<Training>));
        });
    }

    [Test]
    public void Commands_SerializeToExactLegacyCompatibleGoldenJson()
    {
        var userId = ParseId<User>("00000000-0000-0000-0000-000000000001");
        var trainingId = ParseId<Training>("00000000-0000-0000-0000-000000000002");
        var userRegistered = new UserRegisteredCommand { UserId = userId };
        var trainingCompleted = new TrainingCompletedCommand
        {
            UserId = userId,
            TrainingId = trainingId
        };

        var userRegisteredJson = JsonSerializer.Serialize(userRegistered, SharedSerializationOptions.Current);
        var trainingCompletedJson = JsonSerializer.Serialize(trainingCompleted, SharedSerializationOptions.Current);

        Assert.Multiple(() =>
        {
            userRegisteredJson.Should().Be("{\"userId\":\"00000000-0000-0000-0000-000000000001\"}");
            trainingCompletedJson.Should().Be("{\"userId\":\"00000000-0000-0000-0000-000000000001\",\"trainingId\":\"00000000-0000-0000-0000-000000000002\"}");
        });
    }

    private static void AssertCommandContract<TCommand>(
        string expectedNamespace,
        string expectedAssembly,
        params (string Name, Type Type)[] expectedProperties)
    {
        var commandType = typeof(TCommand);
        var properties = commandType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(property => property.MetadataToken)
            .ToArray();

        Assert.Multiple(() =>
        {
            commandType.Assembly.GetName().Name.Should().Be(expectedAssembly);
            commandType.Namespace.Should().Be(expectedNamespace);
            commandType.IsPublic.Should().BeTrue();
            commandType.IsSealed.Should().BeTrue();
            commandType.IsClass.Should().BeTrue();
            commandType.IsGenericType.Should().BeFalse();
            commandType.GetInterfaces().Should().Equal(typeof(IActionCommand));
            properties.Select(property => (property.Name, property.PropertyType))
                .Should().Equal(expectedProperties);
            properties.Should().OnlyContain(property =>
                property.GetMethod != null
                && property.GetMethod.IsPublic
                && property.SetMethod != null
                && property.SetMethod.IsPublic
                && property.SetMethod.ReturnParameter
                    .GetRequiredCustomModifiers()
                    .Contains(typeof(IsExternalInit)));
        });
    }

    private static Id<TEntity> ParseId<TEntity>(string value)
        where TEntity : class
    {
        Id<TEntity>.TryParse(value, out var id).Should().BeTrue();
        return id;
    }
}
