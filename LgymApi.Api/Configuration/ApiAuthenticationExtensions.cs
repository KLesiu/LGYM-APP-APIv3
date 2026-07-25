using System.Text;
using LgymApi.Api.Constants;
using LgymApi.Api.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace LgymApi.Api.Configuration;

public static class ApiAuthenticationExtensions
{
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtSigningKey = configuration[ConfigKeys.JwtSigningKey];
        if (string.IsNullOrWhiteSpace(jwtSigningKey) || jwtSigningKey.Length < 32)
        {
            throw new InvalidOperationException($"{ConfigKeys.JwtSigningKey} is not configured or is too short. Set a strong key value.");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
                    ClockSkew = TimeSpan.Zero
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = context =>
                    {
                        if (context.Exception is SecurityTokenExpiredException)
                        {
                            return ErrorResponseWriter.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, Messages.ExpiredToken, context.HttpContext.RequestAborted);
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        if (!context.Response.HasStarted)
                        {
                            context.HandleResponse();
                            return ErrorResponseWriter.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, Messages.InvalidToken, context.HttpContext.RequestAborted);
                        }

                        return Task.CompletedTask;
                    },
                    OnForbidden = context =>
                    {
                        return ErrorResponseWriter.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden, Messages.Unauthorized, context.HttpContext.RequestAborted);
                    }
                };
            });

        return services;
    }
}
