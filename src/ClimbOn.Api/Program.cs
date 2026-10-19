using ClimbOn.Api.Errors;
using ClimbOn.Api.Health;
using ClimbOn.Api.OpenApi;
using ClimbOn.Api.Telemetry;
using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Postcodes;
using ClimbOn.Infrastructure.Persistence;
using ClimbOn.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    EnvironmentName = DocumentGeneration.IsRunning ? DocumentGeneration.EnvironmentName : null,
});

builder.AddTelemetry();
builder.Services.AddErrorResponses();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddDbContext<ClimbOnDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IConfiguration>().GetConnectionString(ClimbOnDbContext.ConnectionStringName)));
builder.Services.AddScoped<IPostcodeStore, PostcodeStore>();
builder.Services.AddScoped<PostcodeValidator>();
builder.Services.AddScoped<PostcodeLookup>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>(DatabaseHealthCheck.Name);
builder.Services.AddApiDocuments();

var app = builder.Build();

app.MapHealthChecks("/health", HealthResponse.Options);
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();

public partial class Program;
