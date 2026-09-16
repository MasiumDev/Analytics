using Analytics.Api.Configuration;
using Analytics.Api.Data;
using Analytics.Api.Health;
using Analytics.Api.Identity;
using Analytics.Api.InstagramAccounts;
using Analytics.Api.InstagramAccounts.Application;
using Analytics.Api.InstagramAccounts.Infrastructure;
using Analytics.Api.InstagramIntegration;
using Analytics.Api.InstagramIntegration.Application;
using Analytics.Api.InstagramIntegration.Infrastructure;
using Analytics.Api.InstagramCredentials.Application;
using Analytics.Api.InstagramCredentials.Infrastructure;
using Analytics.Api.InstagramMedia.Application;
using Analytics.Api.InstagramMedia.Infrastructure;
using Analytics.Api.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationConfiguration(builder.Configuration);
builder.Services.AddInstagramIntegrationConfiguration(
    builder.Configuration,
    builder.Environment);
builder.Services.AddInstagramCredentialProtection(builder.Configuration);
builder.Services.AddApplicationDatabase(builder.Configuration);
builder.Services.AddIdentityFoundation(builder.Configuration, builder.Environment);
builder.Services.AddApplicationSecurity(builder.Environment);
builder.Services.AddScoped<IInstagramAccountRepository, EfInstagramAccountRepository>();
builder.Services.AddScoped<IInstagramAccountService, InstagramAccountService>();
builder.Services.AddScoped<IInstagramCredentialRepository, EfInstagramCredentialRepository>();
builder.Services.AddScoped<IInstagramCredentialService, InstagramCredentialService>();
builder.Services.AddScoped<IInstagramTokenLifecycleService, InstagramTokenLifecycleService>();
builder.Services.AddSingleton<IInstagramDependentJobController, NoOpInstagramDependentJobController>();
builder.Services.AddScoped<IInstagramAccountProfileSyncService, InstagramAccountProfileSyncService>();
builder.Services.AddScoped<IAccountCurrentStatsRepository, EfAccountCurrentStatsRepository>();
builder.Services.AddScoped<IInstagramOAuthStateRepository, EfInstagramOAuthStateRepository>();
builder.Services.AddScoped<IInstagramOAuthStateService, InstagramOAuthStateService>();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions.TryAdd(
            "traceId",
            context.HttpContext.TraceIdentifier);
    };
});
builder.Services
    .AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy(),
        tags: [HealthCheckTags.Live])
    .AddDbContextCheck<AnalyticsDbContext>(
        "database",
        tags: [HealthCheckTags.Ready]);

var app = builder.Build();

var instagramConfiguration = app.Services
    .GetRequiredService<InstagramConfigurationDiagnostics>();
app.Logger.LogInformation(
    "Instagram integration configuration: Enabled={Enabled}, AppIdConfigured={AppIdConfigured}, AppSecretConfigured={AppSecretConfigured}, RedirectUriConfigured={RedirectUriConfigured}, DevelopmentAccessTokenConfigured={DevelopmentAccessTokenConfigured}",
    instagramConfiguration.Enabled,
    instagramConfiguration.AppIdConfigured,
    instagramConfiguration.AppSecretConfigured,
    instagramConfiguration.RedirectUriConfigured,
    instagramConfiguration.DevelopmentAccessTokenConfigured);

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseProductionTransportSecurity();
app.UseCors(ApplicationIdentityServiceCollectionExtensions.WebClientCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/", (IOptions<BrandingOptions> branding) =>
        Results.Ok(new ServiceStatus(
            "Analytics.Api",
            "ready",
            branding.Value.ProductName)))
    .WithName("GetServiceStatus");

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains(HealthCheckTags.Live),
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains(HealthCheckTags.Ready),
});

app.MapGet("/api/auth/validate", () => Results.NoContent())
    .RequireAuthorization()
    .WithName("ValidateAuthentication");
app.MapAuthenticationEndpoints();
app.MapInstagramAccountEndpoints();
app.MapInstagramOAuthEndpoints();
app.MapSecurityEndpoints();

app.Run();

public sealed record ServiceStatus(
    string Service,
    string Status,
    string ProductName);

public partial class Program;
