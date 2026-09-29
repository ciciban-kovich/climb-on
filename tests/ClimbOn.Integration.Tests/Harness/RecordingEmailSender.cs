using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ClimbOn.Application.Abstractions;

namespace ClimbOn.Integration.Tests.Harness;

// C22: email is replaced at the boundary by a fake that records every message.
public sealed partial class RecordingEmailSender(FixtureValues fixtures) : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> sent = new();

    public IReadOnlyList<EmailMessage> Sent => sent.ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        // Link tokens travel in URL fragments; each one is a personal value for the C33 scan.
        foreach (Match link in LinkFragment().Matches(message.Body))
        {
            var fragment = link.Groups[1].Value;
            fixtures.Register("link-token", fragment);
            foreach (var pair in fragment.Split('&'))
            {
                var separator = pair.IndexOf('=');
                if (separator >= 0 && separator < pair.Length - 1)
                {
                    fixtures.Register("link-token", pair[(separator + 1)..]);
                }
            }
        }

        sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public void Clear() => sent.Clear();

    [GeneratedRegex("""https?://[^\s"'<>#]+#([^\s"'<>]+)""")]
    private static partial Regex LinkFragment();
}
