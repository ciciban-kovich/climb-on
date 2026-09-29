using ClimbOn.Application.Abstractions;

namespace ClimbOn.Integration.Tests.Harness;

public sealed class FixedClock : IClock
{
    public static readonly DateTimeOffset Start = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private long ticks = Start.UtcTicks;

    public DateTimeOffset UtcNow => new(Interlocked.Read(ref ticks), TimeSpan.Zero);

    public void Set(DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The clock holds UTC times only (C3).", nameof(now));
        }

        Interlocked.Exchange(ref ticks, now.UtcTicks);
    }

    public void Advance(TimeSpan by) => Interlocked.Add(ref ticks, by.Ticks);

    public void Reset() => Set(Start);
}
