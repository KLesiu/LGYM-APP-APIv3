using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class MiddlewareRegistrationGuardTests
{
    private const string ExpectedRateLimiterCondition = "!app.Environment.IsEnvironment(TestingEnvironment)";

    private static readonly string[] ExpectedPipeline =
    {
        "UseRequestLocalization",
        "UseSerilogRequestLogging",
        "UseMiddleware<ExceptionHandlingMiddleware>",
        "UseCors",
        "UseAuthentication",
        "UseAuthorization",
        "UseRateLimiter",
        "UseMiddleware<UserContextMiddleware>",
        "UseMiddleware<ApiIdempotencyMiddleware>"
    };

    [Test]
    public void Program_Should_Preserve_Exact_Middleware_Order_From_Localization_Through_Idempotency()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var programPath = Path.Combine(repoRoot, "LgymApi.Api", "Program.cs");
        Assert.That(File.Exists(programPath), Is.True, $"Program.cs not found at '{programPath}'");

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var programContent = File.ReadAllText(programPath);
        var tree = CSharpSyntaxTree.ParseText(programContent, parseOptions, programPath);
        var root = tree.GetCompilationUnitRoot();
        var violations = FindPipelineViolations(root);

        Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
    }

    [Test]
    public void SwappedMiddlewareOrderFixture_IsRejected()
    {
        var root = CSharpSyntaxTree.ParseText("""
            var app = builder.Build();
            app.UseRequestLocalization(localizationOptions);
            app.UseSerilogRequestLogging();
            app.UseMiddleware<ExceptionHandlingMiddleware>();
            app.UseCors();
            app.UseAuthorization();
            app.UseAuthentication();
            if (!app.Environment.IsEnvironment(TestingEnvironment))
            {
                app.UseRateLimiter();
            }
            app.UseMiddleware<UserContextMiddleware>();
            app.UseMiddleware<ApiIdempotencyMiddleware>();
            """).GetCompilationUnitRoot();

        var violations = FindPipelineViolations(root);

        Assert.That(violations, Has.Count.EqualTo(1));
        Assert.That(violations[0], Does.Contain("UseAuthentication -> UseAuthorization"));
        Assert.That(violations[0], Does.Contain("UseAuthorization -> UseAuthentication"));
    }

    [Test]
    public void UnconditionalRateLimiterFixture_IsRejected()
    {
        var root = ParsePipelineFixture("app.UseRateLimiter();");

        var violations = FindPipelineViolations(root);

        Assert.That(violations, Has.Count.EqualTo(1));
        Assert.That(violations[0], Does.Contain("UseRateLimiter"));
        Assert.That(violations[0], Does.Contain(ExpectedRateLimiterCondition));
    }

    [TestCase(
        "if (app.Environment.IsEnvironment(TestingEnvironment)) { app.UseRateLimiter(); }",
        TestName = "TestingOnlyRateLimiterFixture_IsRejected")]
    [TestCase(
        "if (!app.Environment.IsEnvironment(DevelopmentEnvironment)) { app.UseRateLimiter(); }",
        TestName = "WrongEnvironmentRateLimiterFixture_IsRejected")]
    public void InvalidRateLimiterConditionFixture_IsRejected(string rateLimiterRegistration)
    {
        var root = ParsePipelineFixture(rateLimiterRegistration);

        var violations = FindPipelineViolations(root);

        Assert.That(violations, Has.Count.EqualTo(1));
        Assert.That(violations[0], Does.Contain("UseRateLimiter"));
        Assert.That(violations[0], Does.Contain(ExpectedRateLimiterCondition));
    }

    private static IReadOnlyList<string> FindPipelineViolations(CompilationUnitSyntax root)
    {
        var violations = new List<string>();
        var actualPipeline = ExtractPipeline(root);
        if (!actualPipeline.SequenceEqual(ExpectedPipeline, StringComparer.Ordinal))
        {
            violations.Add(
                "Middleware pipeline order changed. " +
                $"Expected: {string.Join(" -> ", ExpectedPipeline)}. " +
                $"Actual: {string.Join(" -> ", actualPipeline)}.");
        }

        var rateLimiterRegistrations = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => FormatAppUseInvocation(invocation) == "UseRateLimiter")
            .ToList();
        if (rateLimiterRegistrations.Count != 1
            || !HasExpectedRateLimiterCondition(rateLimiterRegistrations[0]))
        {
            violations.Add(
                "UseRateLimiter must be registered exactly once and directly within the condition " +
                $"'{ExpectedRateLimiterCondition}'.");
        }

        return violations;
    }

    private static bool HasExpectedRateLimiterCondition(InvocationExpressionSyntax rateLimiterRegistration)
    {
        var containingConditions = rateLimiterRegistration.Ancestors().OfType<IfStatementSyntax>().ToList();
        if (containingConditions.Count != 1)
        {
            return false;
        }

        var containingCondition = containingConditions[0];
        return IsDirectlyControlledBy(containingCondition.Statement, rateLimiterRegistration)
            && IsExpectedRateLimiterCondition(containingCondition.Condition);
    }

    private static bool IsDirectlyControlledBy(StatementSyntax controlledStatement, InvocationExpressionSyntax invocation)
    {
        var invocationStatement = invocation.FirstAncestorOrSelf<ExpressionStatementSyntax>();
        return invocationStatement != null
            && (controlledStatement == invocationStatement
                || controlledStatement is BlockSyntax block && block.Statements.Contains(invocationStatement));
    }

    private static bool IsExpectedRateLimiterCondition(ExpressionSyntax condition)
    {
        condition = RemoveParentheses(condition);
        var negation = condition as PrefixUnaryExpressionSyntax;
        if (negation == null || !negation.IsKind(SyntaxKind.LogicalNotExpression))
        {
            return false;
        }

        var negatedExpression = RemoveParentheses(negation.Operand);
        return negatedExpression is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.ValueText: "app" },
                    Name: IdentifierNameSyntax { Identifier.ValueText: "Environment" }
                },
                Name: IdentifierNameSyntax { Identifier.ValueText: "IsEnvironment" }
            },
            ArgumentList.Arguments:
            [
                {
                    Expression: IdentifierNameSyntax { Identifier.ValueText: "TestingEnvironment" }
                }
            ]
        };
    }

    private static ExpressionSyntax RemoveParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static IReadOnlyList<string> ExtractPipeline(CompilationUnitSyntax root)
    {
        var registrations = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(FormatAppUseInvocation)
            .Where(registration => registration != null)
            .Cast<string>()
            .ToList();

        var start = registrations.FindIndex(registration => registration == ExpectedPipeline[0]);
        if (start < 0)
        {
            return registrations;
        }

        var end = registrations.FindIndex(start, registration => registration == ExpectedPipeline[^1]);
        return end < 0
            ? registrations.Skip(start).ToList()
            : registrations.GetRange(start, end - start + 1);
    }

    private static CompilationUnitSyntax ParsePipelineFixture(string rateLimiterRegistration)
    {
        return CSharpSyntaxTree.ParseText($$"""
            var app = builder.Build();
            app.UseRequestLocalization(localizationOptions);
            app.UseSerilogRequestLogging();
            app.UseMiddleware<ExceptionHandlingMiddleware>();
            app.UseCors();
            app.UseAuthentication();
            app.UseAuthorization();
            {{rateLimiterRegistration}}
            app.UseMiddleware<UserContextMiddleware>();
            app.UseMiddleware<ApiIdempotencyMiddleware>();
            """).GetCompilationUnitRoot();
    }

    private static string? FormatAppUseInvocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "app" },
                Name: var memberName
            })
        {
            return null;
        }

        return memberName switch
        {
            IdentifierNameSyntax identifier when identifier.Identifier.ValueText.StartsWith("Use", StringComparison.Ordinal)
                => identifier.Identifier.ValueText,
            GenericNameSyntax
            {
                Identifier.ValueText: "UseMiddleware",
                TypeArgumentList.Arguments: [var middlewareType]
            } => $"UseMiddleware<{ExtractTypeName(middlewareType)}>",
            _ => null
        };
    }

    private static string ExtractTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => ExtractTypeName(qualified.Right),
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
            _ => type.ToString()
        };
    }
}
