namespace ClimbOn.Domain.Matching;

// A candidate's S60 keys, as seen by the climber whose list it is in.
public sealed record CandidateKey(
    int OverlapMinutes, int GradeFit, bool SharesActiveGym, double DistanceKm, DateTimeOffset LastActivity, Guid AccountId)
{
    // activeGyms holds the ids of the gyms that are active (S21).
    public static CandidateKey For(Climber requester, Climber candidate, IReadOnlySet<Guid> activeGyms)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(activeGyms);

        return new CandidateKey(
            WindowOverlap.TotalMinutes(requester.Profile.Windows, candidate.Profile.Windows),
            Matching.GradeFit.Of(requester.Profile, candidate.Profile),
            requester.Profile.HomeGyms.Intersect(candidate.Profile.HomeGyms).Any(activeGyms.Contains),
            CandidateRule.DistanceKm(requester, candidate),
            candidate.LastActivity,
            candidate.AccountId);
    }
}

// S60: each key decides only when every earlier one is equal; the account ID makes the order total.
public sealed class CandidateComparer : IComparer<CandidateKey>
{
    public static CandidateComparer Instance { get; } = new();

    private CandidateComparer()
    {
    }

    public int Compare(CandidateKey? x, CandidateKey? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null || y is null)
        {
            return x is null ? -1 : 1;
        }

        var order = y.OverlapMinutes.CompareTo(x.OverlapMinutes);
        if (order == 0)
        {
            order = y.GradeFit.CompareTo(x.GradeFit);
        }

        if (order == 0)
        {
            order = y.SharesActiveGym.CompareTo(x.SharesActiveGym);
        }

        if (order == 0)
        {
            order = x.DistanceKm.CompareTo(y.DistanceKm);
        }

        if (order == 0)
        {
            order = y.LastActivity.CompareTo(x.LastActivity);
        }

        return order == 0 ? x.AccountId.CompareTo(y.AccountId) : order;
    }
}
