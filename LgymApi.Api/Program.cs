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

const string TestingEnvironment = "Testing";

var builder = WebApplication.CreateBuilder(args);

ExternalConfigBootstrap.Configure(
    builder.Configuration,
    builder.Environment.ContentRootPath,
    builder.Environment.EnvironmentName,
    args);

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
var corsAllowedOrigins = CorsOriginResolver.ResolveAllowedOrigins(configuredCorsOrigins, builder.Environment.IsDevelopment());

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
var isTesting = builder.Environment.IsEnvironment(TestingEnvironment);

builder.Services
    .AddPlatformModule()
    .AddIdentityModule()
    .AddTrainingPlanningModule()
    .AddNotificationsModule(builder.Configuration)
    .AddApplication()
    .AddInfrastructure(
        builder.Configuration,
        builder.Environment.IsDevelopment(),
        isTesting,
        hostBackgroundServer: false)
    .AddApplicationApiAdapters();
builder.Services.AddNotificationsApiAdapters();

builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, LgymApi.Api.Hubs.NotificationHubUserIdProvider>();
builder.Services.AddSingleton<LgymApi.Api.Hubs.IAccountSessionConnectionRegistry, LgymApi.Api.Hubs.AccountSessionConnectionRegistry>();
builder.Services.AddScoped<IInAppNotificationPushPublisher, LgymApi.Api.Features.InAppNotification.SignalRNotificationPushPublisher>();

builder.Services.AddApiAuthentication(builder.Configuration);

builder.Services.AddApiAuthorizationPolicies();

if (!builder.Environment.IsEnvironment(TestingEnvironment))
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
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
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
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    // Intentional: allow higher burst for auth retries from mobile networks/devices.
                    PermitLimit = 200,
                    Window = TimeSpan.FromMinutes(15)
                });
            }

            var userId = context.User.FindFirst(AuthConstants.ClaimNames.UserId)?.Value;
            var key = string.IsNullOrWhiteSpace(userId)
                ? context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
                : userId;

            return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                // Intentional: raise global throughput limit to reduce throttling in normal app usage.
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            });
        });
    });
}

builder.Services.AddBackgroundWorkerServices(isTesting, hostBackgroundServer: true);

var app = builder.Build();

await StartupMigrationBootstrap.ApplyAsync(app, TestingEnvironment);

if (!app.Environment.IsEnvironment(TestingEnvironment))
{
    await StartupRuntimeGuards.ValidateDatabaseSchemaAsync(app.Services);
}

app.LogPhotoStorageConfiguration();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

ProgramHangfire.ConfigureRecurringJobs(app, TestingEnvironment);

app.UseRequestLocalization(localizationOptions);
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors();
app.UseAuthentication();
if (!app.Environment.IsEnvironment(TestingEnvironment))
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
