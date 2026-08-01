using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Jobs;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Services;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class HangfireEmailBackgroundSchedulerTests
{
    [Test]
    public void Enqueue_CreatesHangfireJob_ForWelcomeEmailJob()
    {
        var client = new FakeBackgroundJobClient();
        var scheduler = new HangfireEmailBackgroundScheduler(client);
        var notificationId = Id<NotificationMessage>.New();

        scheduler.Enqueue(notificationId);

        client.CreatedJobs.Should().HaveCount(1);
        var created = client.CreatedJobs[0];
        created.Job.Type.Should().Be(typeof(IEmailJob));
        created.Job.Method.Name.Should().Be("ExecuteAsync");
        created.Job.Args[0].Should().Be(notificationId.ToString());
        created.State.Should().BeOfType<EnqueuedState>();
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
