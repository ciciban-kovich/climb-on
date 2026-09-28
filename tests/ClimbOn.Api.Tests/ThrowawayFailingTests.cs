using Xunit;

namespace ClimbOn.Api.Tests;

// Throwaway: proves a failing test turns ci red (task 03). Never merged.
public sealed class ThrowawayFailingTests
{
    [Fact]
    public void Deliberately_fails() => Assert.Fail("deliberate failure");
}
