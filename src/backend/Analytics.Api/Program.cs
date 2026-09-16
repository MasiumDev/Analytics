using Analytics.Api.Configuration;
using Analytics.Api.Data;
using Analytics.Api.Health;
using Analytics.Api.Identity;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationConfiguration(builder.Configuration);
builder.Services.AddApplicationDatabase(builder.Configuration);
builder.Services.AddIdentityFoundation(builder.Configuration, builder.Environment);
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

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors(ApplicationIdentityServiceCollectionExtensions.WebClientCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

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

app.Run();

public sealed record ServiceStatus(
    string Service,
    string Status,
    string ProductName);

public partial class Program;
