using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Tokens;
using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Errors;

namespace ClimbOn.Application.Accounts;

// A new session's token, which only its cookie carries, and when the session expires unless used.
public sealed record StartedSession(string Token, DateTimeOffset ExpiresAt);

// S78, S88 (password accounts): email and password start a session (S82).
public sealed class Login(IAccountStore accounts, ISessionStore sessions, IPasswordHasher hasher, IClock clock)
{
    // An unknown address and a wrong password get the same answer.
    public const string FailedCode = "login_failed";

    public async Task<StartedSession> RunAsync(string? email, string? password, CancellationToken cancellationToken)
    {
        var credentials = string.IsNullOrWhiteSpace(email)
            ? null
            : await accounts.FindCredentialsAsync(email.Trim(), cancellationToken);
        if (!hasher.Verify(credentials?.PasswordHash, password ?? string.Empty) || credentials is null)
        {
            throw new DomainError(FailedCode, DomainErrorKind.Unauthenticated);
        }

        // The same 256-bit random tokens as links, stored the same way, as a hash.
        var now = clock.UtcNow;
        var token = LinkTokens.Create();
        await sessions.StartAsync(credentials.Id, LinkTokens.Hash(token), now, cancellationToken);
        return new StartedSession(token, SessionPolicy.ExpiresAt(now));
    }
}
