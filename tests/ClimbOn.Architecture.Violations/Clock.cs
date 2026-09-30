using System.Diagnostics;

namespace ClimbOn.Architecture.Violations.Clock;

public static class ReadsDateTimeNow
{
    public static DateTime Read() => DateTime.Now;
}

public static class ReadsDateTimeUtcNow
{
    public static DateTime Read() => DateTime.UtcNow;
}

public static class ReadsDateTimeToday
{
    public static DateTime Read() => DateTime.Today;
}

public static class ReadsDateTimeOffsetNow
{
    public static DateTimeOffset Read() => DateTimeOffset.Now;
}

public static class ReadsDateTimeOffsetUtcNow
{
    public static DateTimeOffset Read() => DateTimeOffset.UtcNow;
}

public static class ReadsTimeProviderSystem
{
    public static TimeProvider Read() => TimeProvider.System;
}

public static class ReadsTickCount
{
    public static int Read() => Environment.TickCount;
}

public static class ReadsTickCount64
{
    public static long Read() => Environment.TickCount64;
}

public static class CallsTaskDelay
{
    public static Task Wait() => Task.Delay(1);
}

public static class CallsThreadSleep
{
    public static void Wait() => Thread.Sleep(1);
}

public static class UsesStopwatch
{
    public static bool IsRunning(Stopwatch stopwatch) => stopwatch.IsRunning;
}

public static class UsesThreadingTimer
{
    public static void Stop(Timer timer) => timer.Dispose();
}

public static class UsesPeriodicTimer
{
    public static void Stop(PeriodicTimer timer) => timer.Dispose();
}

public static class UsesTimersTimer
{
    public static void Stop(System.Timers.Timer timer) => timer.Dispose();
}

public static class TakesTimeAsParameter
{
    public static DateTimeOffset Later(DateTimeOffset now) => now + TimeSpan.FromDays(1);
}
