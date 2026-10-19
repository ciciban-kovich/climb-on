using ClimbOn.Domain.Geo;

namespace ClimbOn.Domain.Postcodes;

// A postcode area in the dataset (C2), by its reference point (S99).
public sealed record PostcodeArea(string Code, GeoPoint Point);

// Counts only (C32). ActiveTotal is the number of postcodes not retired after the import.
public sealed record PostcodeImportResult(int Added, int Changed, int Retired, int Reactivated, int ActiveTotal);

// The rows an import writes, each a full new row, and what it reports.
public sealed record PostcodeImportPlan(IReadOnlyList<StoredPostcode> Writes, PostcodeImportResult Result);

// S102: what an import of a dataset version changes in the postcode table.
public static class PostcodeDiff
{
    // Writes nothing for a postcode already as the dataset has it, so a rerun of the same version
    // changes nothing (S101.1). dataset holds each code once.
    public static PostcodeImportPlan Plan(IReadOnlyCollection<StoredPostcode> current, IReadOnlyCollection<PostcodeArea> dataset)
    {
        var areas = dataset.ToDictionary(area => area.Code, StringComparer.Ordinal);
        var writes = new List<StoredPostcode>();
        int added = 0, changed = 0, retired = 0, reactivated = 0;

        foreach (var row in current)
        {
            if (!areas.TryGetValue(row.Code, out var area))
            {
                // S102, S102.1: retired, keeping its last reference point.
                if (!row.Retired)
                {
                    writes.Add(row with { Retired = true });
                    retired++;
                }
            }
            else if (row.Retired)
            {
                // S102.4
                writes.Add(new StoredPostcode(row.Code, area.Point, Retired: false));
                reactivated++;
            }
            else if (row.Point != area.Point)
            {
                writes.Add(row with { Point = area.Point });
                changed++;
            }
        }

        var known = current.Select(row => row.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var area in dataset.Where(area => !known.Contains(area.Code)))
        {
            writes.Add(new StoredPostcode(area.Code, area.Point, Retired: false));
            added++;
        }

        return new PostcodeImportPlan(writes, new PostcodeImportResult(added, changed, retired, reactivated, areas.Count));
    }
}
