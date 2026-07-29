using Hangfire;
using LgymApi.Api.Configuration;
using LgymApi.BackgroundWorker;

namespace LgymApi.Api;

internal static class ProgramHangfire
{
    public static void ConfigureRecurringJobs(WebApplication app, string testingEnvironment)
    {
        if (app.Environment.IsEnvironment(testingEnvironment))
        {
            return;
        }

        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = new[] { new HangfireDashboardAuthorizationFilter() }
        });

        BackgroundWorkerRecurringJobs.Configure();
    }
}
