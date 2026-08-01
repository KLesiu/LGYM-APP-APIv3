using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class CancellationTokenPropagationGuardTests
{
    [Test]
    public void Controllers_Should_Pass_Request_Cancellation_Token_To_Service_Async_Calls()
    {
        var repoRoot = ResolveRepositoryRoot();
        var controllersRoot = Path.Combine(repoRoot, "LgymApi.Api", "Features");
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

        var controllerFiles = Directory
            .EnumerateFiles(controllersRoot, "*Controller.cs", SearchOption.AllDirectories)
            .ToList();

        Assert.That(controllerFiles, Is.Not.Empty, $"No controller files found in '{controllersRoot}'.");

        var violations = new List<Violation>();

        foreach (var file in controllerFiles)
        {
            var source = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(source, options: parseOptions, path: file);
            var root = tree.GetCompilationUnitRoot();

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!IsServiceAsyncCall(invocation))
                {
                    continue;
                }

                if (ContainsControllerCancellationArgument(invocation.ArgumentList))
                {
                    continue;
                }

                violations.Add(CreateViolation(repoRoot, tree, invocation, "Missing request cancellation token in service async call"));
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            "Controllers must pass request cancellation token to async service calls. Violations count: " + violations.Count + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
    }

    [Test]
    public void Services_Should_Expose_And_Propagate_CancellationToken()
    {
        var repoRoot = ResolveRepositoryRoot();
        var applicationRoot = Path.Combine(repoRoot, "LgymApi.Application");
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

        var serviceFiles = Directory
            .EnumerateFiles(applicationRoot, "*Service.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("EnumService.cs", StringComparison.Ordinal))
            .ToList();

        Assert.That(serviceFiles, Is.Not.Empty, $"No service files found in '{applicationRoot}'.");

        var violations = new List<Violation>();

        foreach (var file in serviceFiles)
        {
            var source = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(source, options: parseOptions, path: file);
            var root = tree.GetCompilationUnitRoot();

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (!IsPublicAsyncContractMethod(method))
                {
                    continue;
                }

                if (!HasCancellationTokenParameter(method.ParameterList))
                {
                    violations.Add(CreateViolation(repoRoot, tree, method, "Missing CancellationToken parameter in async service method"));
                    continue;
                }

                foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (!RequiresCancellationTokenPropagation(invocation))
                    {
                        continue;
                    }

                    if (ContainsCancellationArgument(invocation) || IsPostClaimPushRecoveryInvocation(invocation))
                    {
                        continue;
                    }

                    violations.Add(CreateViolation(repoRoot, tree, invocation, "Missing cancellationToken propagation to repository/UoW async call"));
                }
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            "Async service methods must accept CancellationToken and propagate it to repositories/unit of work. Violations count: " + violations.Count + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
    }

    [Test]
    public void PostClaimPushRecoveryException_Should_Only_Allow_The_Explicit_Recovery_Save()
    {
        var recoveryInvocation = GetSingleInvocation("""
            class PushNotificationDeliveryService
            {
                private Task RecoverFromPostClaimFailureAsync() => _unitOfWork.SaveChangesAsync(CancellationToken.None);
            }
            """);
        var processInvocation = GetSingleInvocation("""
            class PushNotificationDeliveryService
            {
                public Task ProcessAsync() => _pushNotificationMessageRepository.FindByIdAsync(id, CancellationToken.None);
            }
            """);

        Assert.That(IsPostClaimPushRecoveryInvocation(recoveryInvocation), Is.True);
        Assert.That(IsPostClaimPushRecoveryInvocation(processInvocation), Is.False);
    }

    private static bool IsServiceAsyncCall(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        if (memberAccess.Expression is not IdentifierNameSyntax receiver)
        {
            return false;
        }

        var methodName = memberAccess.Name.Identifier.ValueText;
        if (!methodName.EndsWith("Async", StringComparison.Ordinal))
        {
            return false;
        }

        var receiverName = receiver.Identifier.ValueText;
        return receiverName.StartsWith('_')
            && receiverName.EndsWith("Service", StringComparison.Ordinal);
    }

    private static bool ContainsControllerCancellationArgument(ArgumentListSyntax argumentList)
    {
        return argumentList.Arguments.Any(argument =>
            IsNamedCancellationToken(argument)
            || argument.Expression is IdentifierNameSyntax { Identifier.ValueText: "cancellationToken" }
            || argument.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "RequestAborted" });
    }

    private static bool IsPublicAsyncContractMethod(MethodDeclarationSyntax method)
    {
        if (!method.Modifiers.Any(SyntaxKind.PublicKeyword))
        {
            return false;
        }

        if (!method.Identifier.ValueText.EndsWith("Async", StringComparison.Ordinal))
        {
            return false;
        }

        return method.ReturnType is IdentifierNameSyntax { Identifier.ValueText: "Task" }
            || method.ReturnType is GenericNameSyntax { Identifier.ValueText: "Task" };
    }

    private static bool HasCancellationTokenParameter(ParameterListSyntax parameterList)
    {
        return parameterList.Parameters.Any(parameter =>
            parameter.Type is IdentifierNameSyntax { Identifier.ValueText: "CancellationToken" }
            && parameter.Identifier.ValueText == "cancellationToken");
    }

    private static bool RequiresCancellationTokenPropagation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        var methodName = memberAccess.Name.Identifier.ValueText;
        if (!methodName.EndsWith("Async", StringComparison.Ordinal))
        {
            return false;
        }

        if (memberAccess.Expression is IdentifierNameSyntax receiver)
        {
            var receiverName = receiver.Identifier.ValueText;
            if (receiverName == "_unitOfWork" || receiverName == "transaction")
            {
                return true;
            }

            if (receiverName.StartsWith('_') && receiverName.EndsWith("Repository", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsCancellationArgument(InvocationExpressionSyntax invocation)
    {
        var argumentList = invocation.ArgumentList;

        if (argumentList.Arguments.Any(argument =>
            IsNamedCancellationToken(argument)
            || argument.Expression is IdentifierNameSyntax { Identifier.ValueText: "cancellationToken" }))
        {
            return true;
        }

        if (!IsTransactionRollbackCall(invocation))
        {
            return false;
        }

        return argumentList.Arguments.Any(argument => IsCancellationTokenNone(argument.Expression));
    }

    private static bool IsPostClaimPushRecoveryInvocation(InvocationExpressionSyntax invocation)
    {
        if (!invocation.ArgumentList.Arguments.Any(argument => IsCancellationTokenNone(argument.Expression)))
        {
            return false;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess
            || memberAccess.Expression is not IdentifierNameSyntax receiver)
        {
            return false;
        }

        var containingMethod = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        var containingClass = invocation.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (containingClass?.Identifier.ValueText != "PushNotificationDeliveryService"
            || containingMethod?.Identifier.ValueText != "RecoverFromPostClaimFailureAsync")
        {
            return false;
        }

        return (receiver.Identifier.ValueText == "_unitOfWork" && memberAccess.Name.Identifier.ValueText == "SaveChangesAsync")
            || (receiver.Identifier.ValueText == "_pushNotificationMessageRepository" && memberAccess.Name.Identifier.ValueText == "FindByIdAsync");
    }

    private static InvocationExpressionSyntax GetSingleInvocation(string source)
    {
        return CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest))
            .GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single();
    }

    private static bool IsNamedCancellationToken(ArgumentSyntax argument)
    {
        return argument.NameColon?.Name.Identifier.ValueText == "cancellationToken";
    }

    private static bool IsTransactionRollbackCall(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        return memberAccess.Expression is IdentifierNameSyntax { Identifier.ValueText: "transaction" }
            && memberAccess.Name.Identifier.ValueText == "RollbackAsync";
    }

    private static bool IsCancellationTokenNone(ExpressionSyntax expression)
    {
        return expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.ValueText: "CancellationToken" },
            Name: IdentifierNameSyntax { Identifier.ValueText: "None" }
        };
    }

    private static Violation CreateViolation(string repoRoot, SyntaxTree tree, SyntaxNode node, string reason)
    {
        var span = tree.GetLineSpan(node.Span);
        var line = span.StartLinePosition.Line + 1;
        var relativePath = Path.GetRelativePath(repoRoot, tree.FilePath);
        return new Violation(relativePath, line, reason);
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LgymApi.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }

    private sealed record Violation(string File, int Line, string Reason)
    {
        public override string ToString() => $"{File}:{Line} [{Reason}]";
    }
}
