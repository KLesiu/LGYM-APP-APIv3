using Hangfire;
using LgymApi.Api.Configuration;
using LgymApi.BackgroundWorker;

namespace LgymApi.Api;

internal static class ProgramHangfire
{
    public static void ConfigureRecurringJobs(WebApplication app, string testingEnvironment)
    {
        if (app.Environment.IsEnvironment(testingEnvironment) ||
            ApiEnvironmentNames.IsE2E(app.Environment.EnvironmentName))
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/hangfire"))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                await next(context);
            });
            return;
        }

        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = new[] { new HangfireDashboardAuthorizationFilter() }
        });

        BackgroundWorkerRecurringJobs.Configure();
    }
}
