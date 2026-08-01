using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using LgymApi.BackgroundWorker.Common;
using LgymApi.BackgroundWorker.Common.Jobs;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Services;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class HangfireBackgroundActionSchedulerTests
{
    [Test]
    public void Enqueue_CreatesHangfireJob_ForActionMessageJob()
    {
        var client = new FakeBackgroundJobClient();
        var scheduler = new HangfireActionMessageScheduler(client);
        var actionMessageId = Id<CommandEnvelope>.New();

        scheduler.Enqueue(actionMessageId);

        client.CreatedJobs.Should().HaveCount(1);
        var created = client.CreatedJobs[0];
        created.Job.Type.Should().Be(typeof(IActionMessageJob));
        created.Job.Method.Name.Should().Be("ExecuteAsync");
        created.Job.Args[0].Should().Be(actionMessageId.ToString());
        created.State.Should().BeOfType<EnqueuedState>();
    }

    [Test]
    public void Enqueue_PassesOnlyStringId_NoPayloadObject()
    {
        var client = new FakeBackgroundJobClient();
        var scheduler = new HangfireActionMessageScheduler(client);
        var actionMessageId = Id<CommandEnvelope>.New();

        scheduler.Enqueue(actionMessageId);

        var created = client.CreatedJobs[0];
        created.Job.Args.Should().HaveCount(1);
        created.Job.Args[0].Should().BeOfType<string>().Which.Should().Be(actionMessageId.ToString());
    }

    private sealed class FakeBackgroundJobClient : IBackgroundJobClient
    {
        public List<(Job Job, IState State)> CreatedJobs { get; } = [];

        public string Create(Job job, IState state)
        {
            CreatedJobs.Add((job, state));
            return Id<FakeBackgroundJobClient>.New().ToString();
        }

        public bool ChangeState(string jobId, IState state, string expectedState)
        {
            throw new NotSupportedException();
        }
    }
}
