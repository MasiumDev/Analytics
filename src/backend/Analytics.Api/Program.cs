var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new ServiceStatus("Analytics.Api", "ready")))
    .WithName("GetServiceStatus");

app.MapHealthChecks("/health");

app.Run();

public sealed record ServiceStatus(string Service, string Status);

public partial class Program;
