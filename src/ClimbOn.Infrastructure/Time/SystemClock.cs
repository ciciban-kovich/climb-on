using ClimbOn.Application.Abstractions;

namespace ClimbOn.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
