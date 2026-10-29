using ClimbOn.Domain.Matching;
using ClimbOn.Domain.Profiles;
using Xunit;
using static ClimbOn.Domain.Tests.Matching.Climbers;

namespace ClimbOn.Domain.Tests.Matching;

[Trait("SpecPartial", "S60")]
public sealed class CandidateComparerTests
{
    private static readonly Guid Low = new("00000000-0000-4000-8000-000000000001");
    private static readonly Guid High = new("f0000000-0000-4000-8000-000000000001");
    private static readonly Guid GymA = new("6f1c2a10-0000-4000-8000-000000000001");
    private static readonly Guid GymB = new("6f1c2a10-0000-4000-8000-000000000002");

    private static readonly CandidateKey Base = new(60, 1, false, 10, Noon, Low);

    // For each key and the one after it: First wins on the earlier key and loses on the later one.
    public static TheoryData<string, CandidateKey, CandidateKey> AdjacentKeys => new()
    {
        { "S60.1 before S60.2", Base with { OverlapMinutes = 120, GradeFit = 0 }, Base with { GradeFit = 2 } },
        { "S60.2 before S60.3", Base with { GradeFit = 2 }, Base with { SharesActiveGym = true } },
        { "S60.3 before S60.4", Base with { SharesActiveGym = true, DistanceKm = 50 }, Base with { DistanceKm = 1 } },
        { "S60.4 before S60.5", Base with { DistanceKm = 1, LastActivity = Noon.AddDays(-1) }, Base with { DistanceKm = 2 } },
        {
            "S60.5 activity before account ID",
            Base with { AccountId = High },
            Base with { LastActivity = Noon.AddHours(-1), AccountId = Low }
        },
    };

    // First comes before Second on this key alone, every other key equal.
    public static TheoryData<string, CandidateKey, CandidateKey> EachKey => new()
    {
        { "S60.1 more overlap first", Base with { OverlapMinutes = 61 }, Base },
        { "S60.2 better fit first", Base with { GradeFit = 2 }, Base },
        { "S60.3 shared active gym first", Base with { SharesActiveGym = true }, Base },
        { "S60.4 nearer first", Base with { DistanceKm = 9.999 }, Base },
        { "S60.5 more recent activity first", Base with { LastActivity = Noon.AddTicks(1) }, Base },
        { "S60.5 lower account ID first", Base, Base with { AccountId = High } },
    };

    [Theory]
    [Trait("SpecPartial", "S60.1")]
    [Trait("SpecPartial", "S60.2")]
    [Trait("SpecPartial", "S60.3")]
    [Trait("SpecPartial", "S60.4")]
    [Trait("SpecPartial", "S60.5")]
    [MemberData(nameof(AdjacentKeys))]
    public void Key_order_is_S60_order(string pair, CandidateKey first, CandidateKey second) =>
        AssertFirst(pair, first, second);

    [Theory]
    [Trait("SpecPartial", "S60.1")]
    [Trait("SpecPartial", "S60.2")]
    [Trait("SpecPartial", "S60.3")]
    [Trait("SpecPartial", "S60.4")]
    [Trait("SpecPartial", "S60.5")]
    [MemberData(nameof(EachKey))]
    public void Each_key_orders_in_its_direction(string key, CandidateKey first, CandidateKey second) =>
        AssertFirst(key, first, second);

    [Fact]
    public void A_key_equals_itself()
    {
        Assert.Equal(0, CandidateComparer.Instance.Compare(Base, Base));
        Assert.Equal(0, CandidateComparer.Instance.Compare(Base, Base with { }));
    }

    [Fact]
    [Trait("SpecPartial", "S60.1")]
    [Trait("SpecPartial", "S60.2")]
    [Trait("SpecPartial", "S60.4")]
    [Trait("SpecPartial", "S16")]
    public void Keys_are_taken_from_the_two_climbers()
    {
        // Only the requester's own range meets the candidate's partner range, so the pair's fit is 1,
        // while the requester's fit with itself is 2 and the candidate's is 0.
        var requester = Climber(Draft with { Windows = [new("tuesday", 18, 21), new("friday", 8, 10)] });
        var candidate = Climber(
            Draft with
            {
                Windows = [new("tuesday", 19, 22), new("friday", 9, 12)],
                Disciplines = [new("bouldering", ["indoor"], Range("7A", "7B"), Range("5", "6A"))],
            },
            NorthOfOslo(12.5),
            Noon.AddMinutes(-5));

        var key = CandidateKey.For(requester, candidate, new HashSet<Guid>());

        Assert.Equal(180, key.OverlapMinutes);
        Assert.Equal(1, key.GradeFit);
        Assert.False(key.SharesActiveGym);
        Assert.Equal(12.5, key.DistanceKm, 6);
        Assert.Equal(Noon.AddMinutes(-5), key.LastActivity);
        Assert.Equal(candidate.AccountId, key.AccountId);
    }

    [Theory]
    [Trait("SpecPartial", "S60.3")]
    [InlineData(new[] { 0 }, new[] { 0 }, new[] { 0 }, true)]
    [InlineData(new[] { 0, 1 }, new[] { 1 }, new[] { 1 }, true)]
    [InlineData(new[] { 0 }, new[] { 0 }, new int[0], false)]
    [InlineData(new[] { 0 }, new[] { 1 }, new[] { 0, 1 }, false)]
    [InlineData(new int[0], new[] { 0 }, new[] { 0 }, false)]
    public void Shared_gym_counts_only_when_active(int[] requesterGyms, int[] candidateGyms, int[] activeGyms, bool shares)
    {
        Guid[] gyms = [GymA, GymB];
        var requester = Climber(Draft with { HomeGyms = requesterGyms.Select(index => gyms[index]).ToList() });
        var candidate = Climber(Draft with { HomeGyms = candidateGyms.Select(index => gyms[index]).ToList() });

        var key = CandidateKey.For(requester, candidate, activeGyms.Select(index => gyms[index]).ToHashSet());

        Assert.Equal(shares, key.SharesActiveGym);
    }

    [Fact]
    public void Candidates_sort_into_S60_order()
    {
        CandidateKey[] expected =
        [
            Base with { OverlapMinutes = 120, GradeFit = 0, DistanceKm = 90 },
            Base with { GradeFit = 2, DistanceKm = 90 },
            Base with { SharesActiveGym = true, DistanceKm = 90 },
            Base with { DistanceKm = 1, LastActivity = Noon.AddDays(-30) },
            Base with { AccountId = High },
            Base with { LastActivity = Noon.AddHours(-1) },
        ];

        Assert.Equal(expected, expected.Reverse().Order(CandidateComparer.Instance));
    }

    private static void AssertFirst(string label, CandidateKey first, CandidateKey second)
    {
        Assert.True(CandidateComparer.Instance.Compare(first, second) < 0, label);
        Assert.True(CandidateComparer.Instance.Compare(second, first) > 0, label);
    }
}
