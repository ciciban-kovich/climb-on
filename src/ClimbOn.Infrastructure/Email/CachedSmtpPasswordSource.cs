using ClimbOn.Application.Abstractions;

namespace ClimbOn.Infrastructure.Email;

// Design §3 'Email': the password is cached for at most 5 minutes (C4 clock), so a rotated key is in
// use 5 minutes after the secret changes. A failed read is never cached.
public sealed class CachedSmtpPasswordSource(ISmtpPasswordSource inner, IClock clock) : ISmtpPasswordSource
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private Cached? cached;

    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (Volatile.Read(ref cached) is { } current && now - current.ReadAt < Lifetime)
        {
            return current.Password;
        }

        var password = await inner.GetAsync(cancellationToken);
        Volatile.Write(ref cached, new Cached(password, now));
        return password;
    }

    private sealed record Cached(string Password, DateTimeOffset ReadAt);
}
