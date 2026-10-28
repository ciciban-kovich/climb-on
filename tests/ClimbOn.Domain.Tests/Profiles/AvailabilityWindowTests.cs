using ClimbOn.Domain.Errors;
using ClimbOn.Domain.Profiles;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Xunit;

namespace ClimbOn.Domain.Tests.Profiles;

[Trait("Spec", "S61")]
public sealed class AvailabilityWindowTests
{
    // Up to six windows, each on one of two days so that collisions are common, with any hours in -1..25.
    private static readonly Arbitrary<AvailabilityWindow[]> AnyWindows = Arb.From(
        from count in Gen.Choose(1, 6)
        from windows in Gen.ArrayOf(
            from day in Gen.Elements(DayOfWeek.Monday, DayOfWeek.Tuesday)
            from start in Gen.Choose(-1, 25)
            from end in Gen.Choose(-1, 25)
            select new AvailabilityWindow(day, start, end),
            count)
        select windows);

    [Theory]
    [InlineData(18, 21, 20, 22)]
    [InlineData(18, 21, 18, 21)]
    [InlineData(18, 22, 19, 20)]
    [InlineData(0, 24, 23, 24)]
    [InlineData(0, 1, 0, 24)]
    public void Overlapping_windows_on_one_day_are_rejected(int firstStart, int firstEnd, int secondStart, int secondEnd) =>
        AssertRejected(
            AvailabilityWindow.OverlapCode,
            new AvailabilityWindow(DayOfWeek.Tuesday, firstStart, firstEnd),
            new AvailabilityWindow(DayOfWeek.Tuesday, secondStart, secondEnd));

    // Sorted neighbours are not the only pairs: the first window overlaps the third, not the second.
    [Fact]
    public void Overlap_with_a_window_that_is_not_the_next_one_is_rejected() =>
        AssertRejected(
            AvailabilityWindow.OverlapCode,
            new AvailabilityWindow(DayOfWeek.Friday, 10, 20),
            new AvailabilityWindow(DayOfWeek.Friday, 11, 12),
            new AvailabilityWindow(DayOfWeek.Friday, 15, 16));

    [Fact]
    public void Windows_that_touch_or_are_on_other_days_are_accepted()
    {
        var windows = AvailabilityWindow.EnsureValid(
        [
            new AvailabilityWindow(DayOfWeek.Wednesday, 20, 22),
            new AvailabilityWindow(DayOfWeek.Wednesday, 18, 20),
            new AvailabilityWindow(DayOfWeek.Thursday, 18, 22),
            new AvailabilityWindow(DayOfWeek.Sunday, 0, 24),
        ]);

        Assert.Equal(
            [
                new AvailabilityWindow(DayOfWeek.Sunday, 0, 24),
                new AvailabilityWindow(DayOfWeek.Wednesday, 18, 20),
                new AvailabilityWindow(DayOfWeek.Wednesday, 20, 22),
                new AvailabilityWindow(DayOfWeek.Thursday, 18, 22),
            ],
            windows);
    }

    [Theory]
    [InlineData(20, 20)]
    [InlineData(22, 18)]
    [InlineData(22, 25)]
    [InlineData(-1, 2)]
    [InlineData(24, 24)]
    public void Window_that_does_not_end_later_the_same_day_is_rejected(int start, int end) =>
        AssertRejected(AvailabilityWindow.InvalidCode, new AvailabilityWindow(DayOfWeek.Monday, start, end));

    [Fact]
    public void Window_on_a_day_that_is_not_a_weekday_is_rejected() =>
        AssertRejected(AvailabilityWindow.InvalidCode, new AvailabilityWindow((DayOfWeek)7, 10, 12));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(23, 24)]
    [InlineData(0, 24)]
    public void Window_within_the_day_is_accepted(int start, int end) =>
        Assert.Single(AvailabilityWindow.EnsureValid([new AvailabilityWindow(DayOfWeek.Monday, start, end)]));

    // S3: one or more windows.
    [Fact]
    [Trait("SpecPartial", "S3")]
    public void At_least_one_window_is_required()
    {
        Assert.Equal(AvailabilityWindow.RequiredCode, Assert.Throws<DomainError>(() => AvailabilityWindow.EnsureValid([])).Code);
        Assert.Equal(AvailabilityWindow.RequiredCode, Assert.Throws<DomainError>(() => AvailabilityWindow.EnsureValid(null)).Code);
    }

    // Against a brute-force reading of S61: every hour of a day is in at most one window.
    [Property(MaxTest = 1000)]
    public Property Windows_are_accepted_exactly_when_S61_holds() =>
        Prop.ForAll(AnyWindows, windows =>
        {
            var wellFormed = windows.All(window => window.StartHour >= 0 && window.EndHour <= 24 && window.EndHour > window.StartHour);
            var disjoint = windows
                .SelectMany(window => Enumerable.Range(window.StartHour, Math.Max(0, window.EndHour - window.StartHour))
                    .Select(hour => (window.Day, hour)))
                .GroupBy(slot => slot)
                .All(slot => slot.Count() == 1);

            string? code = null;
            try
            {
                AvailabilityWindow.EnsureValid(windows);
            }
            catch (DomainError error)
            {
                code = error.Code;
            }

            return code == (!wellFormed ? AvailabilityWindow.InvalidCode : !disjoint ? AvailabilityWindow.OverlapCode : null);
        });

    private static void AssertRejected(string code, params AvailabilityWindow[] windows) =>
        Assert.Equal(code, Assert.Throws<DomainError>(() => AvailabilityWindow.EnsureValid(windows)).Code);
}
