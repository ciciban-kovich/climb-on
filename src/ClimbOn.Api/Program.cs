using ClimbOn.Api.Health;
using ClimbOn.Application.Abstractions;
using ClimbOn.Infrastructure.Persistence;
using ClimbOn.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddDbContext<ClimbOnDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IConfiguration>().GetConnectionString(ClimbOnDbContext.ConnectionStringName)));
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>(DatabaseHealthCheck.Name);

var app = builder.Build();

app.MapHealthChecks("/health", HealthResponse.Options);

app.Run();

public partial class Program;
