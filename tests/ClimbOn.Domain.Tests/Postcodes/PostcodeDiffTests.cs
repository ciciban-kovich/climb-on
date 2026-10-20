using ClimbOn.Domain.Geo;
using ClimbOn.Domain.Postcodes;
using Xunit;

namespace ClimbOn.Domain.Tests.Postcodes;

public sealed class PostcodeDiffTests
{
    private static readonly GeoPoint Here = new(60, 10);
    private static readonly GeoPoint There = new(61, 11);

    [Fact]
    [Trait("SpecPartial", "S101.3")]
    public void Postcode_new_to_the_table_is_added()
    {
        var plan = PostcodeDiff.Plan([], [new("0010", Here)]);

        Assert.Equal([new StoredPostcode("0010", Here, Retired: false)], plan.Writes);
        Assert.Equal(new PostcodeImportResult(1, 0, 0, 0, 1), plan.Result);
    }

    [Fact]
    [Trait("SpecPartial", "S101.3")]
    public void Moved_postcode_takes_the_new_point()
    {
        var plan = PostcodeDiff.Plan([new("0010", Here, Retired: false)], [new("0010", There)]);

        Assert.Equal([new StoredPostcode("0010", There, Retired: false)], plan.Writes);
        Assert.Equal(new PostcodeImportResult(0, 1, 0, 0, 1), plan.Result);
    }

    [Fact]
    [Trait("SpecPartial", "S102")]
    [Trait("Spec", "S102.1")]
    public void Missing_postcode_is_retired_keeping_its_last_point()
    {
        var plan = PostcodeDiff.Plan([new("0010", Here, Retired: false), new("0150", There, Retired: false)], [new("0150", There)]);

        Assert.Equal([new StoredPostcode("0010", Here, Retired: true)], plan.Writes);
        Assert.Equal(new PostcodeImportResult(0, 0, 1, 0, 1), plan.Result);
    }

    // The table's state every month after a postcode has gone: it is neither written nor counted again.
    [Fact]
    [Trait("SpecPartial", "S102")]
    [Trait("Spec", "S101.1")]
    public void Postcode_already_retired_and_still_missing_is_left_alone()
    {
        var plan = PostcodeDiff.Plan([new("0010", Here, Retired: true), new("0150", There, Retired: false)], [new("0150", There)]);

        Assert.Empty(plan.Writes);
        Assert.Equal(new PostcodeImportResult(0, 0, 0, 0, 1), plan.Result);
    }

    [Fact]
    [Trait("Spec", "S102.4")]
    public void Retired_postcode_that_reappears_is_reactivated_with_the_new_point()
    {
        var plan = PostcodeDiff.Plan([new("0010", Here, Retired: true)], [new("0010", There)]);

        Assert.Equal([new StoredPostcode("0010", There, Retired: false)], plan.Writes);
        Assert.Equal(new PostcodeImportResult(0, 0, 0, 1, 1), plan.Result);
    }

    [Fact]
    [Trait("Spec", "S101.1")]
    public void Postcode_as_the_dataset_has_it_is_not_written()
    {
        var plan = PostcodeDiff.Plan([new("0010", Here, Retired: false)], [new("0010", Here)]);

        Assert.Empty(plan.Writes);
        Assert.Equal(new PostcodeImportResult(0, 0, 0, 0, 1), plan.Result);
    }

    // Applying a plan and planning the same dataset again writes nothing (S101.1).
    [Fact]
    [Trait("Spec", "S101.1")]
    public void Second_plan_of_the_same_dataset_writes_nothing()
    {
        List<StoredPostcode> table = [new("0010", Here, Retired: false), new("0150", Here, Retired: true), new("0160", Here, Retired: false)];
        List<PostcodeArea> dataset = [new("0150", There), new("0160", There), new("0170", Here)];

        var first = PostcodeDiff.Plan(table, dataset);
        var applied = table.Where(row => first.Writes.All(write => write.Code != row.Code)).Concat(first.Writes).ToList();
        var second = PostcodeDiff.Plan(applied, dataset);

        Assert.Equal(new PostcodeImportResult(1, 1, 1, 1, 3), first.Result);
        Assert.Empty(second.Writes);
        Assert.Equal(new PostcodeImportResult(0, 0, 0, 0, 3), second.Result);
    }
}
