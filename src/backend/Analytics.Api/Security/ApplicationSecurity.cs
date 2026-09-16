using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;

namespace Analytics.Api.Security;

public static class ApplicationSecurity
{
    public const string AuthenticatedUserPolicy = "AuthenticatedUser";
    public const string AuthenticationRateLimitPolicy = "Authentication";
    public const string TenantMutationRateLimitPolicy = "TenantMutation";

    public static IServiceCollection AddApplicationSecurity(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = environment.IsDevelopment()
                ? "analytics.csrf"
                : "__Host-analytics.csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(
                AuthenticatedUserPolicy,
                policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(ClaimTypes.NameIdentifier));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(
                AuthenticationRateLimitPolicy,
                context => CreateFixedWindowPartition(
                    GetClientKey(context),
                    permitLimit: 10));
            options.AddPolicy(
                TenantMutationRateLimitPolicy,
                context => CreateFixedWindowPartition(
                    GetUserOrClientKey(context),
                    permitLimit: 30));
        });

        services.AddHttpsRedirection(options =>
        {
            options.HttpsPort = 443;
            options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
        });

        return services;
    }

    public static WebApplication UseProductionTransportSecurity(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            return app;
        }

        app.UseHsts();
        app.UseHttpsRedirection();
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.TryAdd("X-Content-Type-Options", "nosniff");
                headers.TryAdd("X-Frame-Options", "DENY");
                headers.TryAdd("Referrer-Policy", "no-referrer");
                headers.TryAdd(
                    "Permissions-Policy",
                    "camera=(), microphone=(), geolocation=()");
                headers.TryAdd(
                    "Content-Security-Policy",
                    "default-src 'none'; frame-ancestors 'none'; base-uri 'none'");
                return Task.CompletedTask;
            });

            await next();
        });

        return app;
    }

    public static IEndpointRouteBuilder MapSecurityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/auth/csrf",
                (HttpContext context, IAntiforgery antiforgery) =>
                {
                    var tokens = antiforgery.GetAndStoreTokens(context);
                    context.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(new AntiforgeryTokenResponse(
                        tokens.RequestToken!,
                        tokens.HeaderName!));
                })
            .WithName("GetAntiforgeryToken");

        return endpoints;
    }

    private static RateLimitPartition<string> CreateFixedWindowPartition(
        string partitionKey,
        int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1),
                AutoReplenishment = true,
            });

    private static string GetUserOrClientKey(HttpContext context)
    {
        return context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? GetClientKey(context);
    }

    private static string GetClientKey(HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown-client";
    }
}

public sealed record AntiforgeryTokenResponse(string RequestToken, string HeaderName);

public static class AntiforgeryEndpointConventionExtensions
{
    public static RouteHandlerBuilder RequireAntiforgery(this RouteHandlerBuilder builder)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var antiforgery = context.HttpContext.RequestServices
                .GetRequiredService<IAntiforgery>();
            try
            {
                await antiforgery.ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid CSRF token",
                    detail: "A valid CSRF cookie and request token are required.");
            }

            return await next(context);
        });
    }
}
