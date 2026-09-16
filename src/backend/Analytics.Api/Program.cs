using Analytics.Api.Configuration;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationConfiguration(builder.Configuration);
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions.TryAdd(
            "traceId",
            context.HttpContext.TraceIdentifier);
    };
});
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapGet("/", (IOptions<BrandingOptions> branding) =>
        Results.Ok(new ServiceStatus(
            "Analytics.Api",
            "ready",
            branding.Value.ProductName)))
    .WithName("GetServiceStatus");

app.MapHealthChecks("/health");

app.Run();

public sealed record ServiceStatus(
    string Service,
    string Status,
    string ProductName);

public partial class Program;
