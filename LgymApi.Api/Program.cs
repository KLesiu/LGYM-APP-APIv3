using System.Threading.RateLimiting;
using FluentValidation;
using FluentValidation.AspNetCore;
using LgymApi.BackgroundWorker;
using LgymApi.Application;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Identity;
using LgymApi.Application.Notifications;
using LgymApi.TrainingPlanning;
using LgymApi.Infrastructure;
using LgymApi.Platform;
using Microsoft.AspNetCore.RateLimiting;
using LgymApi.Api;
using LgymApi.Api.Configuration;
using LgymApi.Api.Extensions;
using LgymApi.Api.Middleware;
using LgymApi.Domain.Security;
using LgymApi.Api.Constants;
using Hangfire;
using LgymApi.Api.Serialization;
using LgymApi.Api.Logging;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Debugging;

var builder = WebApplication.CreateBuilder(args);

ExternalConfigBootstrap.Configure(
    builder.Configuration,
    builder.Environment.ContentRootPath,
    builder.Environment.EnvironmentName,
    args);

var environmentName = builder.Environment.EnvironmentName;
var isTesting = ApiEnvironmentNames.IsTesting(environmentName);
var isTestSafe = ApiEnvironmentNames.IsTestSafe(environmentName);

SelfLog.Enable(msg => Console.Error.WriteLine(msg));

SerilogBootstrap.ConfigureSerilog(builder);

builder.Services.AddStrictHttpJsonOptions();

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.UseInlineDefinitionsForEnums();
    options.SchemaFilter<EnumAsStringSchemaFilter>();
});
var configuredCorsOrigins = builder.Configuration.GetSection(ConfigKeys.CorsAllowedOrigins).Get<string[]>();
var corsAllowedOrigins = CorsOriginResolver.ResolveAllowedOrigins(configuredCorsOrigins, environmentName);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsAllowedOrigins.Length > 0)
        {
            policy.WithOrigins(corsAllowedOrigins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
            return;
        }

        throw new InvalidOperationException($"No CORS allowed origins are configured. Configure '{ConfigKeys.CorsAllowedOrigins}' or disable CORS explicitly.");
    });
});
builder.Services.AddHttpContextAccessor();
var localizationOptions = builder.Services.AddApiLocalization();
builder.Services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

builder.Services
    .AddPlatformModule()
    .AddIdentityModule()
    .AddTrainingPlanningModule()
    .AddNotificationsModule(builder.Configuration)
    .AddApplication()
    .AddInfrastructure(
        builder.Configuration,
        builder.Environment.IsDevelopment(),
        isTestSafe,
        hostBackgroundServer: false)
    .AddApplicationApiAdapters();
builder.Services.AddNotificationsApiAdapters();

builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, LgymApi.Api.Hubs.NotificationHubUserIdProvider>();
builder.Services.AddSingleton<LgymApi.Api.Hubs.IAccountSessionConnectionRegistry, LgymApi.Api.Hubs.AccountSessionConnectionRegistry>();
builder.Services.AddScoped<IInAppNotificationPushPublisher, LgymApi.Api.Features.InAppNotification.SignalRNotificationPushPublisher>();

builder.Services.AddApiAuthentication(builder.Configuration);

builder.Services.AddApiAuthorizationPolicies();

if (!isTesting)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var path = context.Request.Path.Value ?? string.Empty;

            // Stricter rate limit for password recovery endpoints
            var isPasswordRecovery = path.Contains("/forgot-password", StringComparison.OrdinalIgnoreCase)
                                     || path.Contains("/reset-password", StringComparison.OrdinalIgnoreCase);

            if (isPasswordRecovery)
            {
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter($"password-recovery:{ip}", _ => new FixedWindowRateLimiterOptions
                {
                    // Intentional: stricter rate limit for sensitive password recovery endpoints.
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(15)
                });
            }

            var isAuth = path.Contains("/login", StringComparison.OrdinalIgnoreCase)
                         || path.Contains("/register", StringComparison.OrdinalIgnoreCase);

            if (isAuth)
            {
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter($"auth:{ip}", _ => new FixedWindowRateLimiterOptions
                {
                    // Intentional: allow higher burst for auth retries from mobile networks/devices.
                    PermitLimit = 200,
                    Window = TimeSpan.FromMinutes(15)
                });
            }

            var userId = context.User.FindFirst(AuthConstants.ClaimNames.UserId)?.Value;
            var key = string.IsNullOrWhiteSpace(userId)
                ? $"anonymous:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}"
                : $"account:{userId}";

            return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                // Intentional: raise global throughput limit to reduce throttling in normal app usage.
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            });
        });
    });
}

builder.Services.AddBackgroundWorkerServices(isTestSafe, hostBackgroundServer: true);

var app = builder.Build();

await StartupMigrationBootstrap.ApplyAsync(app, ApiEnvironmentNames.Testing);

if (!ApiEnvironmentNames.IsTesting(app.Environment.EnvironmentName))
{
    await StartupRuntimeGuards.ValidateDatabaseSchemaAsync(app.Services);
}

app.LogPhotoStorageConfiguration();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

ProgramHangfire.ConfigureRecurringJobs(app, ApiEnvironmentNames.Testing);

app.UseRequestLocalization(localizationOptions);
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors();
app.UseAuthentication();
if (!ApiEnvironmentNames.IsTesting(app.Environment.EnvironmentName))
{
    app.UseRateLimiter();
}

app.UseMiddleware<LgymApi.Api.Middleware.UserContextMiddleware>();
app.UseAuthorization();
app.UseMiddleware<LgymApi.Api.Middleware.ApiIdempotencyMiddleware>();

app.MapGet("/health/live", static () => Results.Json(new { status = "ok" }))
    .AllowAnonymous();

app.MapLocalPhotoDevelopmentEndpoints();
app.MapControllers();
app.MapHub<LgymApi.Api.Hubs.NotificationHub>("/hubs/notifications", options =>
{
    options.CloseOnAuthenticationExpiration = true;
});

await app.RunAsync();
public partial class Program { }
