using System.Reflection;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.Logging;
using Hangfire.States;
using Hangfire.Storage;
using LgymApi.Api;
using LgymApi.BackgroundWorker.Common.Jobs;
using LgymApi.BackgroundWorker.Jobs;
using WorkerHangfirePushBackgroundScheduler = LgymApi.BackgroundWorker.Services.HangfirePushBackgroundScheduler;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure;
using LgymApi.Infrastructure.Services;
using LgymApi.TestUtils;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NSubstitute.Core;
using Newtonsoft.Json;
using EmailJob = LgymApi.Infrastructure.Jobs.EmailJob;
using InvitationEmailJob = LgymApi.Infrastructure.Jobs.InvitationEmailJob;
using WelcomeEmailJob = LgymApi.Infrastructure.Jobs.WelcomeEmailJob;

namespace LgymApi.UnitTests;

[TestFixture]
[NonParallelizable]
public sealed class HangfireContractCompatibilityTests
{
    private static readonly MethodInfo ResolveLogProvider = typeof(LogProvider).GetMethod(
        "ResolveLogProvider",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly PersistedJobContract[] PersistedJobs =
    [
        new("ActionMessage", typeof(IActionMessageJob), typeof(ActionMessageJob), "LgymApi.BackgroundWorker.Jobs", [typeof(Id<CommandEnvelope>)], []),
        new("Email", typeof(IEmailJob), typeof(EmailJob), "LgymApi.Infrastructure.Jobs", [typeof(Id<NotificationMessage>)], []),
        new("InvitationEmail", typeof(IInvitationEmailJob), typeof(InvitationEmailJob), "LgymApi.Infrastructure.Jobs", [typeof(Id<NotificationMessage>)], []),
        new("WelcomeEmail", typeof(IWelcomeEmailJob), typeof(WelcomeEmailJob), "LgymApi.Infrastructure.Jobs", [typeof(Id<NotificationMessage>)], []),
        new("PushNotification", typeof(IPushNotificationJob), typeof(PushNotificationJob), "LgymApi.BackgroundWorker.Jobs", [typeof(Id<PushNotificationMessage>), typeof(CancellationToken)], [1]),
        new("CommittedIntentDispatch", typeof(ICommittedIntentDispatchJob), typeof(CommittedIntentDispatchJob), "LgymApi.BackgroundWorker.Jobs", [typeof(CancellationToken)], [0]),
        new("ExpiredPhotoUploadCleanup", typeof(IExpiredPhotoUploadCleanupJob), typeof(ExpiredPhotoUploadCleanupJob), "LgymApi.BackgroundWorker.Jobs", [typeof(CancellationToken)], [0]),
        new("RecurringReportAssignmentProcessing", typeof(IRecurringReportAssignmentProcessingJob), typeof(RecurringReportAssignmentProcessingJob), "LgymApi.BackgroundWorker.Jobs", [typeof(CancellationToken)], [0]),
        new("StalePushInstallationCleanup", typeof(IStalePushInstallationCleanupJob), typeof(StalePushInstallationCleanupJob), "LgymApi.BackgroundWorker.Jobs", [typeof(CancellationToken)], [0])
    ];

    private ILogProvider _originalLogProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _originalLogProvider = (ILogProvider)ResolveLogProvider.Invoke(null, null)!;
    }

    [TearDown]
    public void TearDown()
    {
        LogProvider.SetCurrentLogProvider(_originalLogProvider);
    }

    [TestCaseSource(nameof(GetPersistedJobContracts))]
    public void PersistedJobContract_HasFrozenIdentityAndSignature(PersistedJobContract contract)
    {
        contract.ContractType.Assembly.GetName().Name.Should().Be("LgymApi.BackgroundWorker.Common");
        contract.ContractType.Namespace.Should().Be("LgymApi.BackgroundWorker.Common.Jobs");
        contract.ContractType.FullName.Should().Be($"LgymApi.BackgroundWorker.Common.Jobs.I{contract.Name}Job");

        contract.WorkerType.Assembly.GetName().Name.Should().Be("LgymApi.BackgroundWorker");
        contract.WorkerType.Namespace.Should().Be(contract.WorkerNamespace);
        contract.WorkerType.FullName.Should().Be($"{contract.WorkerNamespace}.{contract.Name}Job");
        contract.WorkerType.IsSealed.Should().BeTrue();
        contract.WorkerType.Should().Implement(contract.ContractType);

        AssertExecuteAsyncSignature(contract.ContractType, contract);
        AssertExecuteAsyncSignature(contract.WorkerType, contract);
    }

    [TestCase(typeof(ActionMessageJob), true)]
    [TestCase(typeof(EmailJob), false)]
    [TestCase(typeof(InvitationEmailJob), false)]
    [TestCase(typeof(WelcomeEmailJob), false)]
    public void PersistedJobContract_RetryPolicyUsesThreeInterruptedRunBackoffs(Type workerType, bool isClassAttribute)
    {
        var attribute = isClassAttribute
            ? workerType.GetCustomAttribute<AutomaticRetryAttribute>()
            : workerType.GetMethod("ExecuteAsync")!.GetCustomAttribute<AutomaticRetryAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Attempts.Should().Be(3);
        attribute.DelaysInSeconds.Should().Equal(60, 300, 900);
    }

    [Test]
    public void PersistedJobContract_PushDisablesAutomaticRetry()
    {
        var attribute = typeof(PushNotificationJob).GetCustomAttribute<AutomaticRetryAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Attempts.Should().Be(0);
    }

    [TestCase(typeof(EmailJob), 60)]
    [TestCase(typeof(RecurringReportAssignmentProcessingJob), 300)]
    public void PersistedJobContract_ExecutionLockPreventsOverlappingInterruptedRuns(Type workerType, int timeoutInSeconds)
    {
        var method = workerType.GetMethod("ExecuteAsync")!;
        var attribute = method.CustomAttributes.SingleOrDefault(candidate => candidate.AttributeType == typeof(DisableConcurrentExecutionAttribute));

        attribute.Should().NotBeNull();
        attribute!.ConstructorArguments.Should().ContainSingle();
        attribute.ConstructorArguments[0].Value.Should().Be(timeoutInSeconds);
    }

    [Test]
    public void SchedulerAdapters_CapturePersistedInterfaceExpressionsAndIdOnlyPayloads()
    {
        var client = new CapturingBackgroundJobClient();
        var actionMessageId = Id<CommandEnvelope>.New();
        var notificationId = Id<NotificationMessage>.New();
        var pushNotificationId = Id<PushNotificationMessage>.New();

        new HangfireActionMessageScheduler(client).Enqueue(actionMessageId);
        new HangfireEmailBackgroundScheduler(client).Enqueue(notificationId);
        var pushScheduler = new WorkerHangfirePushBackgroundScheduler(client);
        pushScheduler.Enqueue(pushNotificationId);
        pushScheduler.ScheduleRetry(pushNotificationId, TimeSpan.FromMinutes(5));

        client.CreatedJobs.Should().HaveCount(4);
        AssertCapturedJob(client.CreatedJobs[0], typeof(IActionMessageJob), actionMessageId, typeof(EnqueuedState));
        AssertCapturedJob(client.CreatedJobs[1], typeof(IEmailJob), notificationId, typeof(EnqueuedState));
        AssertCapturedJob(client.CreatedJobs[2], typeof(IPushNotificationJob), pushNotificationId, typeof(EnqueuedState), hasCancellationToken: true);
        AssertCapturedJob(client.CreatedJobs[3], typeof(IPushNotificationJob), pushNotificationId, typeof(ScheduledState), hasCancellationToken: true);
    }

    [Test]
    public void RecurringJobs_PersistFrozenIdsCronsAndInterfaceTargets()
    {
        var storage = new CapturingJobStorage();
        var previousStorage = JobStorage.Current;
        JobStorage.Current = storage;

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "HangfireContract" });
            builder.Services.AddHangfire(_ => { });
            using var app = builder.Build();

            ProgramHangfire.ConfigureRecurringJobs(app, "Testing");
        }
        finally
        {
            JobStorage.Current = previousStorage;
        }

        var expectedJobs = new[]
        {
            new RecurringJobContract("reliability-committed-intent-dispatch", "* * * * *", typeof(ICommittedIntentDispatchJob)),
            new RecurringJobContract("reporting-expired-photo-upload-cleanup", "* * * * *", typeof(IExpiredPhotoUploadCleanupJob)),
            new RecurringJobContract("reporting-recurring-report-assignments", "* * * * *", typeof(IRecurringReportAssignmentProcessingJob)),
            new RecurringJobContract("push-stale-installation-cleanup", Cron.Daily(3), typeof(IStalePushInstallationCleanupJob))
        };

        storage.RecurringJobHashes.Should().HaveCount(4);
        foreach (var expectedJob in expectedJobs)
        {
            var hash = storage.RecurringJobHashes[$"recurring-job:{expectedJob.Id}"];
            hash["Cron"].Should().Be(expectedJob.Cron);

            var job = InvocationData.DeserializePayload(hash["Job"]).DeserializeJob();
            job.Type.Should().Be(expectedJob.ContractType);
            job.Method.Name.Should().Be("ExecuteAsync");
            job.Method.GetParameters().Select(parameter => parameter.ParameterType).Should().Equal(typeof(CancellationToken));
            job.Args.Should().ContainSingle().Which.Should().Be(CancellationToken.None);
        }
    }

    [Test]
    public void PlatformComposition_UsesFrozenHangfireCompatibilitySerializerStorageAndServerConditions()
    {
        var configuration = TestConfigurationBuilder.BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=hangfire-contracts;Username=test;Password=test"
        });

        var testingServices = new ServiceCollection();
        testingServices.AddPlatformServices(configuration, enableSensitiveLogging: false, isTesting: true, hostBackgroundServer: true);
        testingServices.Should().NotContain(descriptor => descriptor.ServiceType == typeof(JobStorage));
        FindHangfireServerDescriptor(testingServices).Should().BeNull();

        var clientOnlyServices = new ServiceCollection();
        clientOnlyServices.AddPlatformServices(configuration, enableSensitiveLogging: false, isTesting: false, hostBackgroundServer: false);
        using var clientOnlyProvider = clientOnlyServices.BuildServiceProvider();

        clientOnlyProvider.GetRequiredService<JobStorage>().GetType().FullName.Should().Be("Hangfire.PostgreSql.PostgreSqlStorage");
        FindHangfireServerDescriptor(clientOnlyServices).Should().BeNull();
        GetCompatibilityLevel().Should().Be((int)CompatibilityLevel.Version_180);
        GetConfiguredTypeSerializer().DynamicInvoke(typeof(ICommittedIntentDispatchJob))
            .Should().Be("LgymApi.BackgroundWorker.Common.Jobs.ICommittedIntentDispatchJob, LgymApi.BackgroundWorker.Common");
        AssertRecommendedSerializerSettings(GetConfiguredSerializerSettings());

        var serverServices = new ServiceCollection();
        serverServices.AddPlatformServices(configuration, enableSensitiveLogging: false, isTesting: false, hostBackgroundServer: true);
        var serverDescriptor = FindHangfireServerDescriptor(serverServices);
        serverDescriptor.Should().NotBeNull();
        serverDescriptor!.Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Test]
    public void HangfireServerDescriptor_DoesNotTreatUnrelatedHostedServiceAsHangfireServer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostedService, UnrelatedHostedService>();

        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IHostedService));
        FindHangfireServerDescriptor(services).Should().BeNull();
    }

    private static IEnumerable<TestCaseData> GetPersistedJobContracts()
        => PersistedJobs.Select(contract => new TestCaseData(contract).SetName($"PersistedJobContract_{contract.Name}_IsFrozen"));

    private static void AssertExecuteAsyncSignature(Type type, PersistedJobContract contract)
    {
        var methods = type.GetMethods().Where(method => method.Name == "ExecuteAsync").ToArray();
        methods.Should().ContainSingle();

        var method = methods[0];
        method.ReturnType.Should().Be(typeof(Task));
        method.GetParameters().Select(parameter => parameter.ParameterType).Should().Equal(contract.ParameterTypes);
        method.GetParameters().Select(parameter => parameter.IsOptional)
            .Should().Equal(Enumerable.Range(0, contract.ParameterTypes.Length)
                .Select(index => contract.OptionalParameterIndexes.Contains(index)));

        method.GetParameters()
            .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
            .Select(parameter => parameter.ParameterType)
            .Should().OnlyContain(parameterType => parameterType.IsGenericType && parameterType.GetGenericTypeDefinition() == typeof(Id<>));
    }

    private static void AssertCapturedJob(CapturedBackgroundJob captured, Type expectedContractType, object expectedId, Type expectedStateType, bool hasCancellationToken = false)
    {
        captured.Job.Type.Should().Be(expectedContractType);
        captured.Job.Method.DeclaringType.Should().Be(expectedContractType);
        captured.Job.Method.Name.Should().Be("ExecuteAsync");
        captured.Job.Args[0].Should().Be(expectedId);
        captured.Job.Args[0].GetType().Should().Be(expectedId.GetType());
        captured.Job.Args.Should().HaveCount(hasCancellationToken ? 2 : 1);
        if (hasCancellationToken)
        {
            captured.Job.Args[1].Should().Be(CancellationToken.None);
        }

        captured.State.GetType().Should().Be(expectedStateType);
    }

    private static int GetCompatibilityLevel()
        => (int)typeof(GlobalConfiguration)
            .GetField("_compatibilityLevel", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

    private static Delegate GetConfiguredTypeSerializer()
        => (Delegate)typeof(Job)
            .Assembly.GetType("Hangfire.Common.TypeHelper")!
            .GetField("_currentTypeSerializer", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

    private static JsonSerializerSettings GetConfiguredSerializerSettings()
        => (JsonSerializerSettings)typeof(Job)
            .Assembly.GetType("Hangfire.Common.SerializationHelper")!
            .GetField("_userSerializerSettings", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

    private static void AssertRecommendedSerializerSettings(JsonSerializerSettings settings)
    {
        settings.TypeNameHandling.Should().Be(TypeNameHandling.Auto);
        settings.DefaultValueHandling.Should().Be(DefaultValueHandling.IgnoreAndPopulate);
        settings.NullValueHandling.Should().Be(NullValueHandling.Ignore);
        settings.CheckAdditionalContent.Should().BeTrue();
        settings.MaxDepth.Should().Be(128);
        settings.TypeNameAssemblyFormatHandling.Should().Be(TypeNameAssemblyFormatHandling.Simple);
        settings.SerializationBinder!.GetType().FullName.Should().Be("Hangfire.Common.TypeHelperSerializationBinder");
    }

    private static ServiceDescriptor? FindHangfireServerDescriptor(IServiceCollection services)
        => services.SingleOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationFactory?.Method.ReturnType == typeof(BackgroundJobServerHostedService));

    public sealed record PersistedJobContract(
        string Name,
        Type ContractType,
        Type WorkerType,
        string WorkerNamespace,
        Type[] ParameterTypes,
        int[] OptionalParameterIndexes);

    private sealed record RecurringJobContract(string Id, string Cron, Type ContractType);

    private sealed record CapturedBackgroundJob(Job Job, IState State);

    private sealed class UnrelatedHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CapturingBackgroundJobClient : IBackgroundJobClient
    {
        public List<CapturedBackgroundJob> CreatedJobs { get; } = [];

        public string Create(Job job, IState state)
        {
            CreatedJobs.Add(new CapturedBackgroundJob(job, state));
            return CreatedJobs.Count.ToString();
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => throw new NotSupportedException();
    }

    private sealed class CapturingJobStorage : JobStorage
    {
        private readonly IStorageConnection _connection;

        public CapturingJobStorage()
        {
            _connection = Substitute.For<IStorageConnection>();
            var transaction = Substitute.For<IWriteOnlyTransaction>();
            _connection.CreateWriteTransaction().Returns(transaction);
            _connection.GetAllEntriesFromHash(Arg.Any<string>()).Returns(_ => new Dictionary<string, string>());
            transaction.When(call => call.SetRangeInHash(Arg.Any<string>(), Arg.Any<IEnumerable<KeyValuePair<string, string>>>()))
                .Do(CaptureRecurringJobHash);
        }

        public Dictionary<string, Dictionary<string, string>> RecurringJobHashes { get; } = [];

        public override IMonitoringApi GetMonitoringApi() => Substitute.For<IMonitoringApi>();

        public override IStorageConnection GetConnection() => _connection;

        private void CaptureRecurringJobHash(CallInfo call)
        {
            var key = call.ArgAt<string>(0);
            if (key.StartsWith("recurring-job:", StringComparison.Ordinal))
            {
                RecurringJobHashes[key] = call.ArgAt<IEnumerable<KeyValuePair<string, string>>>(1)
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
            }
        }
    }
}
