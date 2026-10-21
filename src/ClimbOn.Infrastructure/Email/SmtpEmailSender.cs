using ClimbOn.Application.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ClimbOn.Infrastructure.Email;

// Creates the SmtpClient for each send. The default validates the server certificate as usual.
public interface ISmtpClientFactory
{
    SmtpClient Create();
}

public sealed class SmtpClientFactory : ISmtpClientFactory
{
    public SmtpClient Create() => new();
}

// Design §3 'Email': provider-neutral SMTP over STARTTLS, which is required, never opportunistic. No
// protocol logger is attached, and a failure is logged by exception type and SMTP status code only,
// never by message, which can carry the recipient address (C32).
public sealed partial class SmtpEmailSender(
    IOptions<EmailOptions> options,
    ISmtpPasswordSource passwords,
    ISmtpClientFactory clients,
    EmailMetrics metrics,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;

        // Only the caller's own cancellation passes through unwrapped. Anything else, the deadline
        // included, is a failed attempt, whatever the caller's token says by the time it is caught.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(settings.SendTimeout);
        try
        {
            var password = await passwords.GetAsync(deadline.Token);
            using var client = clients.Create();
            await client.ConnectAsync(Required(settings.Smtp.Host, "Email:Smtp:Host"), settings.Smtp.Port, SecureSocketOptions.StartTls, deadline.Token);
            await client.AuthenticateAsync(Required(settings.Smtp.Username, "Email:Smtp:Username"), password, deadline.Token);
            await client.SendAsync(Mime(Required(settings.Sender, "Email:Sender"), message), deadline.Token);
            await client.DisconnectAsync(quit: true, deadline.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var type = exception.GetType().Name;
            int? status = exception is SmtpCommandException command ? (int)command.StatusCode : null;
            metrics.SendFailed();
            LogSendFailed(type, status);
            throw new EmailSendFailedException(type, status);
        }
    }

    // Missing configuration fails the send attempt, not startup.
    private static string Required(string? value, string key) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"{key} is not configured.") : value;

    private static MimeMessage Mime(string sender, EmailMessage message)
    {
        var mime = new MimeMessage { Subject = message.Subject, Body = new TextPart("plain") { Text = message.Body } };
        mime.From.Add(MailboxAddress.Parse(sender));
        mime.To.Add(MailboxAddress.Parse(message.To));
        return mime;
    }

    [LoggerMessage(
        EventName = "EmailSendFailed",
        Level = LogLevel.Warning,
        Message = "Sending email failed: {ExceptionType}, SMTP status {SmtpStatusCode}")]
    private partial void LogSendFailed(string exceptionType, int? smtpStatusCode);
}
