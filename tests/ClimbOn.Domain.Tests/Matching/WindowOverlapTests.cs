using ClimbOn.Domain.Matching;
using ClimbOn.Domain.Profiles;
using Xunit;

namespace ClimbOn.Domain.Tests.Matching;

[Trait("Spec", "S60.1")]
public sealed class WindowOverlapTests
{
    // The specs glossary's example.
    [Fact]
    public void Overlap_is_the_intersection_on_the_same_weekday()
    {
        AvailabilityWindow[] a = [new(DayOfWeek.Tuesday, 18, 21)];
        AvailabilityWindow[] b = [new(DayOfWeek.Tuesday, 19, 22)];

        Assert.Equal([new AvailabilityWindow(DayOfWeek.Tuesday, 19, 21)], WindowOverlap.Of(a, b));
        Assert.Equal(120, WindowOverlap.TotalMinutes(a, b));
    }

    [Fact]
    public void Total_is_the_sum_over_every_overlap()
    {
        AvailabilityWindow[] a =
        [
            new(DayOfWeek.Monday, 8, 12), new(DayOfWeek.Monday, 18, 22), new(DayOfWeek.Saturday, 0, 24),
        ];
        AvailabilityWindow[] b =
        [
            new(DayOfWeek.Monday, 10, 20), new(DayOfWeek.Saturday, 6, 7), new(DayOfWeek.Saturday, 23, 24),
        ];

        Assert.Equal(
            [
                new AvailabilityWindow(DayOfWeek.Monday, 10, 12), new AvailabilityWindow(DayOfWeek.Monday, 18, 20),
                new AvailabilityWindow(DayOfWeek.Saturday, 6, 7), new AvailabilityWindow(DayOfWeek.Saturday, 23, 24),
            ],
            WindowOverlap.Of(a, b));
        Assert.Equal(6 * 60, WindowOverlap.TotalMinutes(a, b));
    }

    [Fact]
    public void Touching_windows_and_other_weekdays_do_not_overlap()
    {
        AvailabilityWindow[] a = [new(DayOfWeek.Tuesday, 18, 20), new(DayOfWeek.Wednesday, 18, 20)];
        AvailabilityWindow[] b = [new(DayOfWeek.Tuesday, 20, 22), new(DayOfWeek.Thursday, 18, 20)];

        Assert.Empty(WindowOverlap.Of(a, b));
        Assert.Equal(0, WindowOverlap.TotalMinutes(a, b));
    }
}
