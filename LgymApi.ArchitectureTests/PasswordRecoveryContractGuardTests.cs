using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class PasswordRecoveryContractGuardTests
{
    private const string ContractsNamespace = "LgymApi.Application.Features.PasswordReset.Contracts";
    private const string RequestTypeName = $"{ContractsNamespace}.PasswordRecoveryEmailRequest";
    private const string SchedulerTypeName = $"{ContractsNamespace}.IPasswordRecoveryEmailScheduler";

    [Test]
    public void PasswordRecoveryEmailRequest_HasExactlySevenOrderedValues()
    {
        var requestType = GetApplicationType(RequestTypeName);
        var constructor = requestType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Should().ContainSingle().Subject;
        var expectedTypes = new[]
        {
            "LgymApi.Domain.ValueObjects.Id`1[LgymApi.Domain.Entities.User]",
            "LgymApi.Domain.ValueObjects.Id`1[LgymApi.Domain.Entities.PasswordResetToken]",
            typeof(string).FullName!,
            typeof(string).FullName!,
            typeof(string).FullName!,
            typeof(string).FullName!,
            typeof(string).FullName!
        };
        var expectedNames = new[]
        {
            "UserId",
            "TokenId",
            "UserName",
            "RecipientEmail",
            "ResetToken",
            "ResetUrl",
            "CultureName"
        };

        requestType.IsPublic.Should().BeTrue();
        requestType.IsSealed.Should().BeTrue();
        constructor.GetParameters().Select(parameter => parameter.Name).Should().Equal(expectedNames);
        constructor.GetParameters().Select(parameter => parameter.ParameterType.ToString()).Should().Equal(expectedTypes);

        var properties = requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        properties.Select(property => property.Name).Should().Equal(expectedNames);
        properties.Select(property => property.PropertyType.ToString()).Should().Equal(expectedTypes);
    }

    [Test]
    public void IPasswordRecoveryEmailScheduler_HasOnlyTheExactScheduleAsyncSignature()
    {
        var requestType = GetApplicationType(RequestTypeName);
        var schedulerType = GetApplicationType(SchedulerTypeName);
        var method = schedulerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Should().ContainSingle().Subject;

        schedulerType.IsPublic.Should().BeTrue();
        schedulerType.IsInterface.Should().BeTrue();
        schedulerType.IsGenericType.Should().BeFalse();
        schedulerType.GetInterfaces().Should().BeEmpty();
        schedulerType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Should().BeEmpty();
        schedulerType.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Should().BeEmpty();

        method.Name.Should().Be("ScheduleAsync");
        method.IsGenericMethod.Should().BeFalse();
        method.ReturnType.Should().Be(typeof(Task));

        var parameters = method.GetParameters();
        parameters.Select(parameter => parameter.Name).Should().Equal("request", "cancellationToken");
        parameters.Select(parameter => parameter.ParameterType).Should().Equal(requestType, typeof(CancellationToken));
        parameters[0].IsOptional.Should().BeFalse();
        parameters[1].IsOptional.Should().BeTrue();
        parameters[1].HasDefaultValue.Should().BeTrue();
    }

    [Test]
    public void PasswordRecoveryContracts_DoNotExposeForbiddenDependencies()
    {
        var (_, compilation, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Identity");
        var contractTrees = syntaxTrees
            .Where(tree => ArchitectureTestHelpers.NormalizePath(tree.FilePath)
                .Contains("/Features/PasswordReset/Contracts/", StringComparison.Ordinal))
            .OrderBy(tree => tree.FilePath, StringComparer.Ordinal)
            .ToArray();

        contractTrees.Select(tree => Path.GetFileName(tree.FilePath)).Should().Equal(
            "IPasswordRecoveryEmailScheduler.cs",
            "PasswordRecoveryEmailRequest.cs");

        var exposedTypeNames = contractTrees
            .SelectMany(tree => GetReferencedTypeNames(compilation, tree))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        exposedTypeNames.Should().NotContain(typeName =>
            typeName.Contains("LgymApi.BackgroundWorker", StringComparison.Ordinal)
            || typeName.Contains("LgymApi.Application.Notifications", StringComparison.Ordinal)
            || typeName.Contains("LgymApi.Infrastructure", StringComparison.Ordinal)
            || typeName.Contains("Hangfire", StringComparison.Ordinal));
    }

    private static IEnumerable<string> GetReferencedTypeNames(Compilation compilation, SyntaxTree tree)
    {
        var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);

        foreach (var typeSyntax in tree.GetRoot().DescendantNodes().OfType<TypeSyntax>())
        {
            if (semanticModel.GetTypeInfo(typeSyntax).Type is ITypeSymbol typeSymbol)
            {
                yield return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }
    }

    private static Type GetApplicationType(string metadataName)
    {
        var type = typeof(LgymApi.Identity.IdentityModule).Assembly.GetType(metadataName);
        type.Should().NotBeNull($"{metadataName} must be defined by the Identity assembly");
        return type!;
    }
}
