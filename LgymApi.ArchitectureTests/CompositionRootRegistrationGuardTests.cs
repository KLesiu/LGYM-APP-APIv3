using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class CompositionRootRegistrationGuardTests
{
    private static readonly string[] RequiredFacadeOrder =
    {
        "AddPlatformModule",
        "AddIdentityModule",
        "AddTrainingPlanningModule",
        "AddNotificationsModule",
        "AddApplication",
        "AddInfrastructure",
        "AddApplicationApiAdapters",
        "AddNotificationsApiAdapters",
        "AddBackgroundWorkerServices"
    };

    private static readonly string[] RequiredCompositionMethods =
    {
        "AddPlatformModule",
        "AddIdentityModule",
        "AddTrainingPlanningModule",
        "AddNotificationsModule",
        "AddApplication",
        "AddInfrastructure",
        "AddApplicationApiAdapters",
        "AddNotificationsApiAdapters",
        "AddBackgroundWorkerServices",
        "AddApplicationMapping"
    };

    private static readonly string[] InternalCompositionMethods =
    {
        "AddApplicationServices",
        "AddPlatformServices",
        "AddWorkoutAndProgressModule",
        "AddCoachingModule",
        "AddNutritionModule",
        "AddReportingModule",
        "AddTrainingPlanningInfrastructure",
        "AddWorkoutProgressInfrastructure",
        "AddCoachingInfrastructure",
        "AddNutritionInfrastructure",
        "AddReportingInfrastructure",
        "AddNotificationsInfrastructure"
    };

    private static readonly string[] TransitionalFacadeMethods =
    {
        "AddTask7ApiCompatibility"
    };

    private static readonly string[] RequiredHostRegistrationHelpers =
    {
        "AddStrictHttpJsonOptions",
        "AddApiLocalization",
        "AddApiAuthentication",
        "AddApiAuthorizationPolicies"
    };

    private static readonly HashSet<string> InlineHostRegistrationMethods = new(StringComparer.Ordinal)
    {
        "AddControllers",
        "AddJsonOptions",
        "AddLocalization",
        "AddAuthentication",
        "AddJwtBearer",
        "AddAuthorization",
        "AddAuthorizationBuilder",
        "AddPolicy"
    };

    [Test]
    public void Program_Should_Register_Required_Composition_Methods()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var programPath = Path.Combine(repoRoot, "LgymApi.Api", "Program.cs");

        Assert.That(File.Exists(programPath), Is.True, $"Program.cs not found at '{programPath}'");

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var programContent = File.ReadAllText(programPath);
        var tree = CSharpSyntaxTree.ParseText(programContent, parseOptions, programPath);
        var root = tree.GetCompilationUnitRoot();

        var invocations = root
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => ExtractMethodName(invocation))
            .Where(name => name != null)
            .Cast<string>()
            .ToList();

        var missing = RequiredCompositionMethods
            .Where(method => !invocations.Contains(method))
            .ToList();

        var internalCalls = InternalCompositionMethods
            .Where(method => invocations.Contains(method))
            .ToList();
        var hostRegistrationViolations = FindHostRegistrationViolations(root);
        var facadeViolations = FindFacadeCompositionViolations(root);
        var apiBindingViolations = FindApiInternalBindingViolations(root);

        Assert.Multiple(() =>
        {
            Assert.That(
                missing,
                Is.Empty,
                $"Program.cs must call the following composition methods: {string.Join(", ", RequiredCompositionMethods)}. " +
                $"Missing: {string.Join(", ", missing)}");

            Assert.That(
                internalCalls,
                Is.Empty,
                $"Program.cs must not call internal composition helpers: {string.Join(", ", InternalCompositionMethods)}. " +
                $"Found: {string.Join(", ", internalCalls)}");

            Assert.That(facadeViolations, Is.Empty, string.Join(Environment.NewLine, facadeViolations));
            Assert.That(apiBindingViolations, Is.Empty, string.Join(Environment.NewLine, apiBindingViolations));
            Assert.That(hostRegistrationViolations, Is.Empty, string.Join(Environment.NewLine, hostRegistrationViolations));
        });
    }

    [Test]
    public void ReversedWorkerCompositionFixture_IsRejected()
    {
        var calls = RequiredFacadeOrder.Where(method => method != "AddBackgroundWorkerServices").ToList();
        calls.Insert(Array.IndexOf(RequiredFacadeOrder, "AddInfrastructure"), "AddBackgroundWorkerServices");
        var root = ParseFacadeCompositionFixture(calls);

        var violations = FindFacadeCompositionViolations(root);

        Assert.That(violations, Has.Count.EqualTo(1));
        Assert.That(violations[0], Does.Contain("ordered").And.Contain("AddBackgroundWorkerServices"));
    }

    [Test]
    public void DuplicateFacadeCompositionFixture_IsRejected()
    {
        var calls = RequiredFacadeOrder.Append("AddApplication");
        var root = ParseFacadeCompositionFixture(calls);

        var violations = FindFacadeCompositionViolations(root);

        Assert.That(violations, Has.Count.EqualTo(1));
        Assert.That(violations[0], Does.Contain("AddApplication").And.Contain("exactly once"));
    }

    [Test]
    public void TransitionalApplicationApiAdapterFacadeFixture_IsRejected()
    {
        var calls = RequiredFacadeOrder
            .Select(method => method == "AddApplicationApiAdapters" ? "AddTask7ApiCompatibility" : method);
        var root = ParseFacadeCompositionFixture(calls);

        var violations = FindFacadeCompositionViolations(root);

        Assert.That(violations, Does.Contain("Program.cs must not call transitional facade AddTask7ApiCompatibility."));
    }

    [Test]
    public void ApplicationApiAdapterFacadeBeforeInfrastructureFixture_IsRejected()
    {
        var calls = RequiredFacadeOrder.ToList();
        calls.Remove("AddApplicationApiAdapters");
        calls.Insert(Array.IndexOf(RequiredFacadeOrder, "AddInfrastructure"), "AddApplicationApiAdapters");
        var root = ParseFacadeCompositionFixture(calls);

        var violations = FindFacadeCompositionViolations(root);

        Assert.That(violations, Has.Count.EqualTo(1));
        Assert.That(violations[0], Does.Contain("ordered").And.Contain("AddApplicationApiAdapters"));
    }

    [Test]
    public void ApiInternalBindingFixture_IsRejected()
    {
        var root = CSharpSyntaxTree.ParseText(
            "builder.Services.AddScoped<IPlanRepository, LgymApi.TrainingPlanning.Persistence.PlanRepository>();")
            .GetCompilationUnitRoot();

        var violations = FindApiInternalBindingViolations(root);

        Assert.That(violations, Has.Count.EqualTo(1));
        Assert.That(violations[0], Does.Contain("PlanRepository").And.Contain("module facade"));
    }

    [Test]
    public void DuplicateHostRegistrationHelperFixture_IsRejected()
    {
        var root = ParseHostRegistrationFixture("builder.Services.AddApiAuthentication(builder.Configuration);");

        var violations = FindHostRegistrationViolations(root);

        Assert.That(violations, Has.Count.EqualTo(1));
        Assert.That(violations[0], Does.Contain("AddApiAuthentication").And.Contain("exactly once"));
    }

    [Test]
    public void InlineHostRegistrationFixture_IsRejected()
    {
        var root = ParseHostRegistrationFixture("builder.Services.Configure<JsonOptions>(_ => { });");

        var violations = FindHostRegistrationViolations(root);

        Assert.That(violations, Has.Count.EqualTo(1));
        Assert.That(violations[0], Does.Contain("Configure"));
    }

    [Test]
    public void Program_Should_Not_Directly_RegisterPasswordRecoverySchedulers()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var programPath = Path.Combine(repoRoot, "LgymApi.Api", "Program.cs");
        var programContent = File.ReadAllText(programPath);
        var root = CSharpSyntaxTree.ParseText(programContent).GetCompilationUnitRoot();

        var directPasswordRegistrations = root
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => ExtractMethodName(invocation) == "AddScoped")
            .Where(invocation => invocation.ToString().Contains("PasswordRecoveryEmail", StringComparison.Ordinal))
            .Select(invocation => invocation.ToString())
            .ToArray();

        Assert.That(
            directPasswordRegistrations,
            Is.Empty,
            "Program.cs must obtain password recovery scheduler registrations from AddBackgroundWorkerServices().");
    }

    [Test]
    public void Notifications_Should_RegisterFcmOnlyAndHaveNoEnvironmentSchedulerSelection()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var sourcePath = Path.Combine(repoRoot, "LgymApi.Notifications", "ServiceCollectionExtensions.cs");
        var sourceContent = File.ReadAllText(sourcePath);
        var root = CSharpSyntaxTree.ParseText(sourceContent).GetCompilationUnitRoot();
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();

        var pushSchedulerRegistrations = root
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.ToString().Contains("IPushBackgroundScheduler", StringComparison.Ordinal))
            .Select(invocation => invocation.ToString())
            .ToArray();
        var fcmRegistrations = root
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.ToString().Contains("IPushProviderSender", StringComparison.Ordinal)
                && invocation.ToString().Contains("FcmPushSender", StringComparison.Ordinal))
            .ToArray();
        var environmentParameters = methods
            .Where(method => method.Identifier.ValueText == "AddNotificationsModule")
            .SelectMany(method => method.ParameterList.Parameters)
            .Where(parameter => parameter.Type?.ToString() == "bool")
            .Select(parameter => parameter.Identifier.ValueText)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(pushSchedulerRegistrations, Is.Empty);
            Assert.That(fcmRegistrations, Has.Length.EqualTo(1));
            Assert.That(environmentParameters, Is.Empty);
        });
    }

    [Test]
    public void InfrastructureNotifications_Should_NotRegisterFcmOrSelectPushSchedulers()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var sourcePath = Path.Combine(repoRoot, "LgymApi.Infrastructure", "NotificationsServiceCollectionExtensions.cs");
        var sourceContent = File.ReadAllText(sourcePath);
        var root = CSharpSyntaxTree.ParseText(sourceContent).GetCompilationUnitRoot();

        AssertNoFcmProviderRegistrations(root);
        var pushSchedulerRegistrations = root
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.ToString().Contains("IPushBackgroundScheduler", StringComparison.Ordinal))
            .Select(invocation => invocation.ToString())
            .ToArray();
        Assert.That(pushSchedulerRegistrations, Is.Empty);
    }

    [Test]
    public void Application_Should_Not_RegisterFcmProvider()
    {
        var applicationFiles = ArchitectureTestHelpers.EnumerateProjectSourceFiles("LgymApi.Application");
        var roots = applicationFiles
            .Where(path => Path.GetFileName(path).EndsWith("ServiceCollectionExtensions.cs", StringComparison.Ordinal))
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetCompilationUnitRoot())
            .ToArray();

        Assert.That(roots, Is.Not.Empty);
        foreach (var root in roots)
        {
            AssertNoFcmProviderRegistrations(root);
        }
    }

    [Test]
    public void ApplicationFcmProviderRegistrationFixture_IsRejected()
    {
        var root = CSharpSyntaxTree.ParseText("services.AddScoped<IPushProviderSender, FcmPushSender>();")
            .GetCompilationUnitRoot();

        var action = () => AssertNoFcmProviderRegistrations(root);

        var exception = Assert.Throws<AssertionException>(action);

        Assert.That(exception!.Message, Does.Contain("Application registration helpers must not register FCM providers"));
    }

    private static IReadOnlyList<string> FindHostRegistrationViolations(CompilationUnitSyntax root)
    {
        var invocationExpressions = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .ToList();
        var invocations = invocationExpressions
            .Select(ExtractMethodName)
            .Where(name => name != null)
            .Cast<string>()
            .ToList();
        var violations = new List<string>();

        foreach (var helperName in RequiredHostRegistrationHelpers)
        {
            var callCount = invocations.Count(name => name == helperName);
            if (callCount != 1)
            {
                violations.Add($"Program.cs must call {helperName} exactly once; found {callCount} calls.");
            }
        }

        var inlineRegistrations = invocationExpressions
            .Where(IsInlineHostRegistration)
            .Select(ExtractMethodName)
            .Where(name => name != null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        if (inlineRegistrations.Count > 0)
        {
            violations.Add(
                "Program.cs must delegate JSON, localization, authentication, and authorization registration to host helpers. " +
                $"Direct registrations found: {string.Join(", ", inlineRegistrations)}.");
        }

        return violations;
    }

    private static IReadOnlyList<string> FindFacadeCompositionViolations(CompilationUnitSyntax root)
    {
        var facadeInvocations = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => new
            {
                MethodName = ExtractMethodName(invocation),
                SpanEnd = invocation.Span.End
            })
            .Where(invocation => invocation.MethodName != null
                && RequiredFacadeOrder.Contains(invocation.MethodName, StringComparer.Ordinal))
            .ToArray();
        var violations = new List<string>();

        foreach (var facade in TransitionalFacadeMethods)
        {
            if (root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Any(invocation => ExtractMethodName(invocation) == facade))
            {
                violations.Add($"Program.cs must not call transitional facade {facade}.");
            }
        }

        foreach (var facade in RequiredFacadeOrder)
        {
            var count = facadeInvocations.Count(invocation => invocation.MethodName == facade);
            if (count != 1)
            {
                violations.Add($"Program.cs must call {facade} exactly once; found {count} calls.");
            }
        }

        if (violations.Count > 0)
        {
            return violations;
        }

        var actualOrder = facadeInvocations
            .OrderBy(invocation => invocation.SpanEnd)
            .Select(invocation => invocation.MethodName!)
            .ToArray();
        if (!actualOrder.SequenceEqual(RequiredFacadeOrder))
        {
            violations.Add(
                $"Program.cs facades must be ordered [{string.Join(", ", RequiredFacadeOrder)}]; " +
                $"actual [{string.Join(", ", actualOrder)}].");
        }

        return violations;
    }

    private static IReadOnlyList<string> FindApiInternalBindingViolations(CompilationUnitSyntax root)
    {
        var registrationMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            "AddScoped",
            "AddSingleton",
            "AddTransient"
        };

        return root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression is MemberAccessExpressionSyntax
            {
                Name: GenericNameSyntax genericName
            }
                && registrationMethods.Contains(genericName.Identifier.ValueText)
                && genericName.TypeArgumentList.Arguments.Count == 2
                && !genericName.TypeArgumentList.Arguments[1].ToString()
                    .StartsWith("LgymApi.Api.", StringComparison.Ordinal))
            .Select(invocation =>
                $"Program.cs must obtain internal implementations from their module facade instead of binding " +
                $"'{invocation}' directly.")
            .ToArray();
    }

    private static void AssertNoFcmProviderRegistrations(CompilationUnitSyntax root)
    {
        var registrations = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.ToString().Contains("IPushProviderSender", StringComparison.Ordinal)
                || invocation.ToString().Contains("FcmPushSender", StringComparison.Ordinal))
            .Select(invocation => invocation.ToString())
            .ToArray();

        Assert.That(
            registrations,
            Is.Empty,
            "Application registration helpers must not register FCM providers. " + string.Join(Environment.NewLine, registrations));
    }

    private static bool IsInlineHostRegistration(InvocationExpressionSyntax invocation)
    {
        return InlineHostRegistrationMethods.Contains(ExtractMethodName(invocation) ?? string.Empty)
            || invocation.Expression is MemberAccessExpressionSyntax
            {
                Name: GenericNameSyntax
                {
                    Identifier.ValueText: "Configure",
                    TypeArgumentList.Arguments: [IdentifierNameSyntax { Identifier.ValueText: "JsonOptions" }]
                }
            };
    }

    private static CompilationUnitSyntax ParseHostRegistrationFixture(string additionalRegistration)
    {
        return CSharpSyntaxTree.ParseText($$"""
            builder.Services.AddStrictHttpJsonOptions();
            builder.Services.AddApiLocalization();
            builder.Services.AddApiAuthentication(builder.Configuration);
            builder.Services.AddApiAuthorizationPolicies();
            {{additionalRegistration}}
            """).GetCompilationUnitRoot();
    }

    private static CompilationUnitSyntax ParseFacadeCompositionFixture(IEnumerable<string> methodNames)
    {
        var source = string.Join(
            Environment.NewLine,
            methodNames.Select(methodName => $"builder.Services.{methodName}();"));
        return CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
    }

    private static string? ExtractMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            _ => null
        };
    }
}
