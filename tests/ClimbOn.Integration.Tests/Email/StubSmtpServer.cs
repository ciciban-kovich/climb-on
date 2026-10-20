using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ClimbOn.Infrastructure.Email;
using MailKit.Net.Smtp;

namespace ClimbOn.Integration.Tests.Email;

// An SMTP server on a loopback port that speaks just enough of the protocol for SmtpEmailSender. It
// offers STARTTLS with a self-signed certificate made here (unless told not to) and AUTH PLAIN/LOGIN,
// and records the commands in order, the AUTH user and password, and MAIL FROM. ClientFactory makes
// clients that trust this server's certificate and no other.
public sealed class StubSmtpServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentQueue<string> commands = new();
    private readonly bool offerStartTls;
    private readonly Func<string, string>? recipientReply;
    private readonly Task accepting;

    // recipientReply answers RCPT TO for an address, e.g. "550 5.1.1 <address> unknown"; by default 250.
    public StubSmtpServer(bool offerStartTls = true, Func<string, string>? recipientReply = null)
    {
        this.offerStartTls = offerStartTls;
        this.recipientReply = recipientReply;
        Certificate = CreateCertificate();
        listener.Start();
        accepting = AcceptAsync();
    }

    public X509Certificate2 Certificate { get; }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    // The command verbs received, in order (EHLO, STARTTLS, AUTH, MAIL, RCPT, DATA, QUIT, ...).
    public IReadOnlyList<string> Commands => commands.ToArray();

    public string? AuthUser { get; private set; }

    public string? AuthPassword { get; private set; }

    public string? MailFrom { get; private set; }

    public ISmtpClientFactory ClientFactory => new TrustingClientFactory(Certificate);

    public SmtpOptions Smtp(string username) => new() { Host = IPAddress.Loopback.ToString(), Port = Port, Username = username };

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        listener.Stop();
        try
        {
            await accepting;
        }
        catch (OperationCanceledException)
        {
        }

        stopping.Dispose();
        Certificate.Dispose();
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

        // SslStream on Windows cannot serve with an ephemeral key; a PKCS#12 round trip gives it a stored one.
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12), null);
    }

    private async Task AcceptAsync()
    {
        var sessions = new List<Task>();
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stopping.Token);
                sessions.Add(ServeAsync(client));
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }

        await Task.WhenAll(sessions);
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        Stream stream = client.GetStream();
        try
        {
            await WriteAsync(stream, "220 stub ESMTP");
            var tls = false;
            var reader = Reader(stream);
            while (await reader.ReadLineAsync(stopping.Token) is { } line)
            {
                var space = line.IndexOf(' ');
                var verb = (space < 0 ? line : line[..space]).ToUpperInvariant();
                var argument = space < 0 ? string.Empty : line[(space + 1)..];
                commands.Enqueue(verb);
                switch (verb)
                {
                    case "EHLO" or "HELO":
                        List<string> extensions = ["stub"];
                        if (offerStartTls && !tls)
                        {
                            extensions.Add("STARTTLS");
                        }

                        extensions.Add("AUTH PLAIN LOGIN");
                        await WriteAsync(stream, string.Join("\r\n", extensions.Select((extension, index) =>
                            (index == extensions.Count - 1 ? "250 " : "250-") + extension)));
                        break;
                    case "STARTTLS" when offerStartTls && !tls:
                        await WriteAsync(stream, "220 2.0.0 ready");
                        var secure = new SslStream(stream);
                        await secure.AuthenticateAsServerAsync(Certificate);
                        stream = secure;
                        reader = Reader(stream);
                        tls = true;
                        break;
                    case "AUTH":
                        await AuthenticateAsync(stream, reader, argument);
                        break;
                    case "MAIL":
                        MailFrom = Address(argument);
                        await WriteAsync(stream, "250 2.1.0 ok");
                        break;
                    case "RCPT":
                        await WriteAsync(stream, recipientReply?.Invoke(Address(argument)) ?? "250 2.1.5 ok");
                        break;
                    case "DATA":
                        await WriteAsync(stream, "354 end with <CRLF>.<CRLF>");
                        while (await reader.ReadLineAsync(stopping.Token) is { } data && data != ".")
                        {
                        }

                        await WriteAsync(stream, "250 2.0.0 queued");
                        break;
                    case "RSET" or "NOOP":
                        await WriteAsync(stream, "250 2.0.0 ok");
                        break;
                    case "QUIT":
                        await WriteAsync(stream, "221 2.0.0 bye");
                        return;
                    default:
                        await WriteAsync(stream, "502 5.5.2 not implemented");
                        break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // The client went away.
        }
        finally
        {
            await stream.DisposeAsync();
        }
    }

    // AUTH PLAIN, with or without an initial response, and AUTH LOGIN.
    private async Task AuthenticateAsync(Stream stream, StreamReader reader, string argument)
    {
        var parts = argument.Split(' ', 2);
        if (parts[0].Equals("PLAIN", StringComparison.OrdinalIgnoreCase))
        {
            var response = parts.Length > 1 ? parts[1] : await ChallengeAsync(stream, reader, string.Empty);
            var fields = Decode(response).Split('\0');
            AuthUser = fields[1];
            AuthPassword = fields[2];
        }
        else
        {
            AuthUser = Decode(parts.Length > 1 ? parts[1] : await ChallengeAsync(stream, reader, "VXNlcm5hbWU6"));
            AuthPassword = Decode(await ChallengeAsync(stream, reader, "UGFzc3dvcmQ6"));
        }

        await WriteAsync(stream, "235 2.7.0 authenticated");
    }

    private async Task<string> ChallengeAsync(Stream stream, StreamReader reader, string challenge)
    {
        await WriteAsync(stream, "334 " + challenge);
        return await reader.ReadLineAsync(stopping.Token) ?? string.Empty;
    }

    private static string Decode(string base64) => Encoding.UTF8.GetString(Convert.FromBase64String(base64));

    // The address in "FROM:<address> ..." or "TO:<address>".
    private static string Address(string argument)
    {
        var start = argument.IndexOf('<');
        var end = argument.IndexOf('>');
        return start >= 0 && end > start ? argument[(start + 1)..end] : argument;
    }

    // The client never sends ahead of a reply (no PIPELINING is offered), so nothing is left buffered
    // in a reader when STARTTLS replaces it.
    private static StreamReader Reader(Stream stream) => new(stream, Encoding.ASCII, false, 1024, leaveOpen: true);

    private async Task WriteAsync(Stream stream, string reply)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(reply + "\r\n"), stopping.Token);
        await stream.FlushAsync(stopping.Token);
    }

    private sealed class TrustingClientFactory(X509Certificate2 certificate) : ISmtpClientFactory
    {
        public SmtpClient Create() => new()
        {
            ServerCertificateValidationCallback = (_, presented, _, _) =>
                presented is not null && presented.GetCertHashString() == certificate.GetCertHashString(),
        };
    }
}
