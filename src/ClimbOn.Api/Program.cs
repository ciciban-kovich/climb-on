using ClimbOn.Api.Climber;
using ClimbOn.Api.Email;
using ClimbOn.Api.Errors;
using ClimbOn.Api.Health;
using ClimbOn.Api.Jobs;
using ClimbOn.Api.Middleware;
using ClimbOn.Api.OpenApi;
using ClimbOn.Api.Telemetry;
using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Accounts;
using ClimbOn.Application.Legal;
using ClimbOn.Application.Outbox;
using ClimbOn.Application.Postcodes;
using ClimbOn.Application.Tokens;
using ClimbOn.Infrastructure.Accounts;
using ClimbOn.Infrastructure.Email;
using ClimbOn.Infrastructure.Persistence;
using ClimbOn.Infrastructure.Persistence.Accounts;
using ClimbOn.Infrastructure.Persistence.Outbox;
using ClimbOn.Infrastructure.Persistence.Sessions;
using ClimbOn.Infrastructure.Postcodes;
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
builder.Services.AddHttpClient<IPostcodeSource, KartverketSource>();
builder.Services.AddScoped<PostcodeImport>();
builder.Services.AddScoped<IJob, PostcodesJob>();
builder.Services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
builder.Services.AddScoped<IAccountStore, AccountStore>();
builder.Services.AddScoped<ILegalDocumentStore, LegalDocumentStore>();
builder.Services.AddScoped<Register>();
builder.Services.AddScoped<ConfirmEmail>();
builder.Services.AddScoped<ISessionStore, SessionStore>();
builder.Services.AddScoped<Sessions>();
builder.Services.AddScoped<Login>();
builder.Services.AddScoped<ChangeLanguage>();
builder.Services.AddScoped<ILinkTokenStore, LinkTokenStore>();
builder.Services.AddScoped<IOutboxStore, OutboxStore>();
builder.Services.AddScoped<EmailOutbox>();
builder.Services.AddSingleton<IEmailContent, EmailContent>();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddSingleton<EmailMetrics>();
builder.Services.AddSingleton<ISmtpClientFactory, SmtpClientFactory>();
builder.Services.AddSingleton<KeyVaultSmtpPasswordSource>();
builder.Services.AddSingleton<ISmtpPasswordSource>(services => new CachedSmtpPasswordSource(
    services.GetRequiredService<KeyVaultSmtpPasswordSource>(), services.GetRequiredService<IClock>()));
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>(DatabaseHealthCheck.Name);
builder.Services.AddApiDocuments();

var app = builder.Build();

app.UseClimberPipeline();
app.MapHealthChecks("/health", HealthResponse.Options);
app.MapAccountsEndpoints();
app.MapConfirmationEndpoints();
app.MapSessionEndpoints();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();

public partial class Program;
