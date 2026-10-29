using ClimbOn.Domain.Geo;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Xunit;

namespace ClimbOn.Domain.Tests.Geo;

[Trait("Spec", "S16")]
public sealed class DistanceTests
{
    private const double R = Distance.EarthRadiusKm;

    // Coordinates in steps of 0.001 degree, poles and the antimeridian included.
    private static readonly Arbitrary<GeoPoint> AnyPoint = Arb.From(
        from latitude in Gen.Choose(-90_000, 90_000)
        from longitude in Gen.Choose(-180_000, 180_000)
        select new GeoPoint(latitude / 1000.0, longitude / 1000.0));

    // Expected values are arcs of a great circle, worked out by hand: angle in radians times R.
    public static TheoryData<double, double, double, double, double> KnownDistances => new()
    {
        { 0, 0, 0, 90, Math.PI / 2 * R },
        { 0, 0, 0, -90, Math.PI / 2 * R },
        { 0, 0, 90, 0, Math.PI / 2 * R },
        { 90, 0, -90, 0, Math.PI * R },
        { 0, 0, 0, 180, Math.PI * R },
        { 0, 179.5, 0, -179.5, Math.PI / 180 * R },
        { 59.5, 10, 60.5, 10, Math.PI / 180 * R },
        { 60, 5, 60, 6, 2 * R * Math.Asin(Math.Cos(Math.PI / 3) * Math.Sin(Math.PI / 360)) },
        { 89, 0, 89, 180, Math.PI / 90 * R },
    };

    [Theory]
    [MemberData(nameof(KnownDistances))]
    public void Distance_matches_the_great_circle_arc(
        double fromLatitude, double fromLongitude, double toLatitude, double toLongitude, double expected) =>
        Assert.Equal(
            expected,
            Distance.Kilometres(new GeoPoint(fromLatitude, fromLongitude), new GeoPoint(toLatitude, toLongitude)),
            1e-6);

    [Fact]
    public void One_degree_of_latitude_is_about_111_km() =>
        Assert.Equal(111.19, Distance.Kilometres(new GeoPoint(59, 10), new GeoPoint(60, 10)), 0.01);

    [Theory]
    [Trait("SpecPartial", "S52")]
    [InlineData(10.4, 10)]
    [InlineData(10.5, 11)]
    [InlineData(11.5, 12)]
    [InlineData(1.49, 1)]
    public void Distances_round_to_whole_km_halves_up(double km, double whole) =>
        Assert.Equal(whole, Distance.WholeKilometres(km));

    [Theory]
    [InlineData(-90.0001, 0)]
    [InlineData(90.0001, 0)]
    [InlineData(0, -180.0001)]
    [InlineData(0, 180.0001)]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(double.PositiveInfinity, 0)]
    public void Point_outside_the_coordinate_range_is_refused(double latitude, double longitude) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPoint(latitude, longitude));

    [Property(MaxTest = 1000)]
    public Property Distance_is_symmetric() =>
        Prop.ForAll(AnyPoint, AnyPoint, (a, b) => Distance.Kilometres(a, b) == Distance.Kilometres(b, a));

    [Property(MaxTest = 1000)]
    public Property Distance_to_itself_is_zero() =>
        Prop.ForAll(AnyPoint, point => Distance.Kilometres(point, point) == 0);

    [Property(MaxTest = 1000)]
    public Property Distance_is_between_zero_and_half_the_circumference() =>
        Prop.ForAll(AnyPoint, AnyPoint, (a, b) => Distance.Kilometres(a, b) is >= 0 and <= (Math.PI * R) + 1e-9);

    [Property(MaxTest = 1000)]
    public Property Distance_obeys_the_triangle_inequality() =>
        Prop.ForAll(AnyPoint, AnyPoint, AnyPoint, (a, b, c) =>
            Distance.Kilometres(a, c) <= Distance.Kilometres(a, b) + Distance.Kilometres(b, c) + 1e-6);
}
