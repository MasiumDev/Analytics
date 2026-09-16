using Analytics.Api.Configuration;
using Analytics.Api.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace Analytics.Api.Identity;

public static class ApplicationIdentityServiceCollectionExtensions
{
    public const string WebClientCorsPolicy = "WebClient";

    public static IServiceCollection AddIdentityFoundation(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddOptions<AuthenticationSessionOptions>()
            .Bind(configuration.GetSection(AuthenticationSessionOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.CookieName),
                $"{AuthenticationSessionOptions.SectionName}:CookieName is required.")
            .Validate(
                options => options.Lifetime > TimeSpan.Zero,
                $"{AuthenticationSessionOptions.SectionName}:Lifetime must be positive.")
            .ValidateOnStart();

        services
            .AddOptions<WebClientOptions>()
            .Bind(configuration.GetSection(WebClientOptions.SectionName))
            .Validate(
                options => options.AllowedOrigins.All(IsValidOrigin),
                $"{WebClientOptions.SectionName}:AllowedOrigins must contain absolute HTTP(S) origins.")
            .ValidateOnStart();

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = false;
                options.Password.RequiredLength = 10;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AnalyticsDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
                options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
            })
            .AddIdentityCookies();

        var sessionOptions = configuration
            .GetSection(AuthenticationSessionOptions.SectionName)
            .Get<AuthenticationSessionOptions>() ?? new AuthenticationSessionOptions();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = sessionOptions.CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = sessionOptions.Lifetime;
            options.SlidingExpiration = sessionOptions.SlidingExpiration;
            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                },
                OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                },
            };
        });

        services.AddAuthorization();

        var allowedOrigins = configuration
            .GetSection(WebClientOptions.SectionName)
            .Get<WebClientOptions>()?
            .AllowedOrigins ?? [];

        services.AddCors(options =>
        {
            options.AddPolicy(WebClientCorsPolicy, policy =>
            {
                if (allowedOrigins.Length > 0)
                {
                    policy
                        .WithOrigins(allowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                }
            });
        });

        return services;
    }

    private static bool IsValidOrigin(string origin)
    {
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(uri.PathAndQuery.Trim('/'));
    }
}
