using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using ClimbOn.Application.Abstractions;
using ClimbOn.Infrastructure.Email;
using ClimbOn.Integration.Tests.Harness;
using MailKit.Net.Smtp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ClimbOn.Integration.Tests.Email;

// Design §3 'Email', §4.4; C32, C50.
[Collection(EmailFailureCollection.Name)]
public sealed class SmtpEmailSenderTests(ApiFactory factory) : IntegrationTest(factory)
{
    private const string Username = "tem-project-id";
    private const string Password = "tem-key-for-tests";
    private const string Sender = "noreply@climbon.test";

    [Fact]
    public async Task Starttls_precedes_auth_with_configured_credentials()
    {
        await using var server = new StubSmtpServer();
        using var meters = new StandaloneMeters();
        var sender = Standalone(server, meters, new ListLogger());

        await sender.SendAsync(Message(Factory.Data.Account().Email), TestContext.Current.CancellationToken);

        var commands = server.Commands.ToList();
        Assert.InRange(commands.IndexOf("STARTTLS"), 0, commands.IndexOf("AUTH") - 1);
        Assert.Equal(Username, server.AuthUser);
        Assert.Equal(Password, server.AuthPassword);
        Assert.Equal(Sender, server.MailFrom);
        Assert.Contains("DATA", commands);
        Assert.Equal(0, meters.SendFailed);
    }

    // The forms registration accepts at their edges reach the server as given (an IDN domain punycoded).
    [Theory]
    [InlineData("ola+klatring@example.no", "ola+klatring@example.no")]
    [InlineData("o'brien@example.ie", "o'brien@example.ie")]
    [InlineData("ola@ærø.no", "ola@xn--r-3fa9c.no")]
    public async Task Accepted_address_forms_are_sent(string address, string recipient)
    {
        await using var server = new StubSmtpServer();
        using var meters = new StandaloneMeters();
        var sender = Standalone(server, meters, new ListLogger());

        await sender.SendAsync(Message(address), TestContext.Current.CancellationToken);

        Assert.Equal(recipient, server.RecipientTo);
        Assert.Equal(0, meters.SendFailed);
    }

    [Fact]
    public async Task Smtp_failure_logs_type_and_status_only()
    {
        await using var server = new StubSmtpServer(recipientReply: address => $"550 5.1.1 <{address}> unknown");
        using var meters = new StandaloneMeters();
        var logger = new ListLogger();
        var sender = Standalone(server, meters, logger);
        var recipient = Factory.Data.Account().Email;

        var failure = await Assert.ThrowsAsync<EmailSendFailedException>(
            () => sender.SendAsync(Message(recipient), TestContext.Current.CancellationToken));

        var log = Assert.Single(logger.Entries);
        Assert.Contains(nameof(SmtpCommandException), log, StringComparison.Ordinal);
        Assert.Contains("550", log, StringComparison.Ordinal);
        Assert.DoesNotContain(recipient, log, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, meters.SendFailed);
        Assert.Equal(550, failure.StatusCode);
        Assert.Null(failure.InnerException);
        Assert.DoesNotContain(recipient, failure.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // The host's own password source reads Key Vault, which the test host has no URI for.
    [Fact]
    public async Task Missing_secret_fails_attempt_not_startup()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = Factory.CreateClient();
        using var health = await client.GetAsync("/health", ct);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        await using var server = new StubSmtpServer();
        using var failures = new SendFailedCount(Factory.Services.GetRequiredService<IMeterFactory>());
        var sender = ActivatorUtilities.CreateInstance<SmtpEmailSender>(Factory.Services, Settings(server), server.ClientFactory);

        await Assert.ThrowsAsync<EmailSendFailedException>(() => sender.SendAsync(Message(Factory.Data.Account().Email), ct));

        Assert.DoesNotContain("AUTH", server.Commands);
        Assert.Equal(1, failures.Total);
    }

    // A sender with SecureSocketOptions.Auto or None would authenticate in plain text here.
    [Fact]
    public async Task Starttls_required_when_not_offered()
    {
        await using var server = new StubSmtpServer(offerStartTls: false);
        using var meters = new StandaloneMeters();
        var sender = Standalone(server, meters, new ListLogger());

        await Assert.ThrowsAsync<EmailSendFailedException>(
            () => sender.SendAsync(Message(Factory.Data.Account().Email), TestContext.Current.CancellationToken));

        Assert.Contains("EHLO", server.Commands);
        Assert.DoesNotContain("AUTH", server.Commands);
        Assert.Equal(1, meters.SendFailed);
    }

    // A server that accepts and never answers fails the attempt at SendTimeout, not after MailKit's
    // 2 minutes per operation.
    [Fact]
    public async Task Stalled_server_fails_at_send_timeout()
    {
        await using var server = new StubSmtpServer(silent: true);
        using var meters = new StandaloneMeters();
        var settings = Options.Create(new EmailOptions
        {
            Sender = Sender,
            Smtp = server.Smtp(Username),
            SendTimeout = TimeSpan.FromSeconds(1),
        });
        var sender = new SmtpEmailSender(settings, new FixedPassword(), server.ClientFactory, new EmailMetrics(meters.Factory), new ListLogger());
        var started = Stopwatch.GetTimestamp();

        await Assert.ThrowsAsync<EmailSendFailedException>(
            () => sender.SendAsync(Message(Factory.Data.Account().Email), TestContext.Current.CancellationToken));

        Assert.InRange(Stopwatch.GetElapsedTime(started), TimeSpan.Zero, TimeSpan.FromSeconds(30));
        Assert.Equal(1, meters.SendFailed);
    }

    // C32: the caller's token is cancelled as the attempt fails. The original exception, which can carry
    // the recipient, is still wrapped, so it never reaches the request's error log.
    [Fact]
    public async Task Failure_after_cancellation_is_still_wrapped()
    {
        await using var server = new StubSmtpServer();
        using var meters = new StandaloneMeters();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var recipient = Factory.Data.Account().Email;
        var sender = new SmtpEmailSender(
            Settings(server), new CancellingPassword(caller, recipient), server.ClientFactory, new EmailMetrics(meters.Factory), new ListLogger());

        var failure = await Assert.ThrowsAsync<EmailSendFailedException>(() => sender.SendAsync(Message(recipient), caller.Token));

        Assert.Equal(nameof(InvalidOperationException), failure.ExceptionType);
        Assert.DoesNotContain(recipient, failure.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, meters.SendFailed);
    }

    // What the Azure Monitor exporter would receive (TelemetryCapture), not only a MeterListener.
    [Fact]
    public async Task Failure_counter_reaches_otel_exporter()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = new StubSmtpServer(recipientReply: address => $"550 5.1.1 <{address}> unknown");
        var sender = ActivatorUtilities.CreateInstance<SmtpEmailSender>(
            Factory.Services, Settings(server), (ISmtpPasswordSource)new FixedPassword(), server.ClientFactory);
        await TakeTelemetryAsync();

        await Assert.ThrowsAsync<EmailSendFailedException>(() => sender.SendAsync(Message(Factory.Data.Account().Email), ct));

        var captured = await TakeTelemetryAsync();
        Assert.Equal(1d, captured
            .Where(item => item.Signal == "metric" && item.Name == EmailMetrics.SendFailedName)
            .Sum(item => Convert.ToDouble(item.Field("value"), CultureInfo.InvariantCulture)));
    }

    private static EmailMessage Message(string to) => new(to, "Probe", "A probe message.");

    private static IOptions<EmailOptions> Settings(StubSmtpServer server) =>
        Options.Create(new EmailOptions { Sender = Sender, Smtp = server.Smtp(Username) });

    private static SmtpEmailSender Standalone(StubSmtpServer server, StandaloneMeters meters, ListLogger logger) =>
        new(Settings(server), new FixedPassword(), server.ClientFactory, new EmailMetrics(meters.Factory), logger);

    // Everything captured so far, scanned for leaks (C33) as the lease would have.
    private async Task<IReadOnlyList<TelemetryItem>> TakeTelemetryAsync()
    {
        var captured = await Factory.Telemetry.TakeAsync();
        Assert.Empty(TelemetryCapture.FindLeaks(captured, Factory.Fixtures));
        return captured;
    }

    private sealed class FixedPassword : ISmtpPasswordSource
    {
        public Task<string> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Password);
    }

    // Cancels the caller's token, then fails with an exception that names the recipient.
    private sealed class CancellingPassword(CancellationTokenSource caller, string recipient) : ISmtpPasswordSource
    {
        public async Task<string> GetAsync(CancellationToken cancellationToken)
        {
            await caller.CancelAsync();
            throw new InvalidOperationException($"No password for {recipient}.");
        }
    }

    // Each entry is the formatted message followed by any exception passed to the logger.
    private sealed class ListLogger : ILogger<SmtpEmailSender>
    {
        private readonly ConcurrentQueue<string> entries = new();

        public IReadOnlyList<string> Entries => entries.ToArray();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(formatter(state, exception) + exception);
    }

    // Counts email_send_failed on the meters one meter factory created.
    private sealed class SendFailedCount : IDisposable
    {
        private readonly MeterListener listener = new();
        private long total;

        public SendFailedCount(IMeterFactory factory)
        {
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Scope == factory && instrument.Name == EmailMetrics.SendFailedName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref total, value));
            listener.Start();
        }

        public long Total => Interlocked.Read(ref total);

        public void Dispose() => listener.Dispose();
    }

    // A meter factory of the test's own, outside any host.
    private sealed class StandaloneMeters : IDisposable
    {
        private readonly ServiceProvider provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        private readonly SendFailedCount count;

        public StandaloneMeters()
        {
            Factory = provider.GetRequiredService<IMeterFactory>();
            count = new SendFailedCount(Factory);
        }

        public IMeterFactory Factory { get; }

        public long SendFailed => count.Total;

        public void Dispose()
        {
            count.Dispose();
            provider.Dispose();
        }
    }
}
