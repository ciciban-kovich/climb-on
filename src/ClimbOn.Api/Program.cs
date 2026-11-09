using ClimbOn.Api.Climber;
using ClimbOn.Api.Email;
using ClimbOn.Api.Errors;
using ClimbOn.Api.Health;
using ClimbOn.Api.Hosting;
using ClimbOn.Api.Jobs;
using ClimbOn.Api.Middleware;
using ClimbOn.Api.OpenApi;
using ClimbOn.Api.Telemetry;
using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Accounts;
using ClimbOn.Application.Chat;
using ClimbOn.Application.Invites;
using ClimbOn.Application.Legal;
using ClimbOn.Application.Matching;
using ClimbOn.Application.Notifications;
using ClimbOn.Application.Outbox;
using ClimbOn.Application.Postcodes;
using ClimbOn.Application.Profiles;
using ClimbOn.Application.Tokens;
using ClimbOn.Infrastructure.Accounts;
using ClimbOn.Infrastructure.Email;
using ClimbOn.Infrastructure.Persistence;
using ClimbOn.Infrastructure.Persistence.Accounts;
using ClimbOn.Infrastructure.Persistence.Chat;
using ClimbOn.Infrastructure.Persistence.Invites;
using ClimbOn.Infrastructure.Persistence.Jobs;
using ClimbOn.Infrastructure.Persistence.Matching;
using ClimbOn.Infrastructure.Persistence.Notifications;
using ClimbOn.Infrastructure.Persistence.Outbox;
using ClimbOn.Infrastructure.Persistence.Profiles;
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
builder.Services.AddScoped<JobLocks>();
builder.Services.AddScoped<ExpireInvites>();
builder.Services.AddScoped<IJob, SweeperJob>();
builder.Services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
builder.Services.AddScoped<IAccountStore, AccountStore>();
builder.Services.AddScoped<ILegalDocumentStore, LegalDocumentStore>();
builder.Services.AddScoped<Register>();
builder.Services.AddScoped<ConfirmEmail>();
builder.Services.AddScoped<ISessionStore, SessionStore>();
builder.Services.AddScoped<Sessions>();
builder.Services.AddScoped<Login>();
builder.Services.AddScoped<ChangeLanguage>();
builder.Services.AddScoped<IProfileStore, ProfileStore>();
builder.Services.AddScoped<SaveProfile>();
builder.Services.AddScoped<ICandidateStore, CandidateStore>();
builder.Services.AddScoped<GetSuggestions>();
builder.Services.AddScoped<IInviteStore, InviteStore>();
builder.Services.AddScoped<SafetyNotice>();
builder.Services.AddScoped<SendInvite>();
builder.Services.AddScoped<AnswerInvite>();
builder.Services.AddScoped<ListInvites>();
builder.Services.AddScoped<IConnectionStore, ConnectionStore>();
builder.Services.AddScoped<IChatStore, ChatStore>();
builder.Services.AddScoped<Messages>();
builder.Services.AddScoped<ListConnections>();
builder.Services.AddScoped<ILinkTokenStore, LinkTokenStore>();
builder.Services.AddScoped<IOutboxStore, OutboxStore>();
builder.Services.AddScoped<OutboxQueue>();
builder.Services.AddScoped<INotificationStore, NotificationStore>();
builder.Services.AddScoped<NotificationFanOut>();
builder.Services.AddScoped<ClimberNotifications>();
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
app.MapProfileEndpoints();
app.MapSuggestionEndpoints();
app.MapInviteEndpoints();
app.MapChatEndpoints();
app.MapNotificationEndpoints();
app.MapClimberClient();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();

public partial class Program;
