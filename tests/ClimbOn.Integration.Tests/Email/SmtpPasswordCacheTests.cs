using ClimbOn.Infrastructure.Email;
using ClimbOn.Integration.Tests.Harness;
using Xunit;

namespace ClimbOn.Integration.Tests.Email;

// Design §3 'Email': the SMTP password is cached for at most 5 minutes by the clock (C4).
public sealed class SmtpPasswordCacheTests
{
    private readonly FixedClock clock = new();

    [Fact]
    public async Task Reads_vault_again_after_5_minutes()
    {
        var vault = new CountingSource();
        var cached = new CachedSmtpPasswordSource(vault, clock);

        Assert.Equal("password 1", await cached.GetAsync(TestContext.Current.CancellationToken));
        clock.Advance(new TimeSpan(0, 4, 59));
        Assert.Equal("password 1", await cached.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, vault.Reads);

        clock.Set(FixedClock.Start + TimeSpan.FromMinutes(5));
        Assert.Equal("password 2", await cached.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, vault.Reads);
    }

    [Fact]
    public async Task Failure_is_not_cached()
    {
        var vault = new CountingSource { FailNext = true };
        var cached = new CachedSmtpPasswordSource(vault, clock);

        await Assert.ThrowsAsync<InvalidOperationException>(() => cached.GetAsync(TestContext.Current.CancellationToken));

        Assert.Equal("password 2", await cached.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, vault.Reads);
    }

    // Returns "password <n>" on the nth read.
    private sealed class CountingSource : ISmtpPasswordSource
    {
        private int reads;

        public bool FailNext { get; set; }

        public int Reads => reads;

        public Task<string> GetAsync(CancellationToken cancellationToken)
        {
            var read = Interlocked.Increment(ref reads);
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("The vault could not be read.");
            }

            return Task.FromResult($"password {read}");
        }
    }
}
