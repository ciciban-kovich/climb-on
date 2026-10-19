using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClimbOn.Application.Postcodes;
using ClimbOn.Domain.Geo;
using ClimbOn.Domain.Postcodes;
using NetTopologySuite;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;

namespace ClimbOn.Infrastructure.Postcodes;

// A postcode's whole area, every part of it, and its reference point inside it (S99).
public sealed record KartverketArea(string Code, Geometry Area, Point ReferencePoint);

// C2: Kartverket's Postnummerområder for all of Norway, as GeoJSON in EUREF89 geographic
// coordinates (EPSG:4258, treated as WGS 84), so no reprojection is needed. See Api/Jobs/README.md.
public sealed class KartverketSource(HttpClient http) : IPostcodeSource
{
    public static readonly Uri DatasetUri = new(
        "https://nedlasting.geonorge.no/geonorge/Basisdata/Postnummeromrader/GeoJSON/Basisdata_0000_Norge_4258_Postnummeromrader_GeoJSON.zip");

    public const string GeographicCrs = "EPSG:4258";

    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new GeoJsonConverterFactory() } };

    // Limits a few times the measured size (Api/Jobs/README.md), so a broken or hostile response
    // fails the job instead of exhausting its memory or running until the platform stops it.
    public long MaxArchiveBytes { get; init; } = 32 * 1024 * 1024;

    public long MaxGeoJsonBytes { get; init; } = 192 * 1024 * 1024;

    // HttpClient.Timeout ends at the response headers; this covers the body too.
    public TimeSpan DownloadTimeout { get; init; } = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyList<PostcodeArea>> LoadAsync(CancellationToken cancellationToken)
    {
        using var archive = await DownloadAsync(cancellationToken);
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read);
        var entries = zip.Entries.Where(entry => entry.Name.EndsWith(".geojson", StringComparison.OrdinalIgnoreCase)).ToList();
        if (entries.Count != 1)
        {
            throw new InvalidDataException($"Expected one GeoJSON file in the dataset archive, found {entries.Count}.");
        }

        await using var entry = await entries[0].OpenAsync(cancellationToken);
        await using var geojson = new BoundedStream(entry, MaxGeoJsonBytes);
        var areas = await ReadAreasAsync(geojson, cancellationToken);
        return areas
            .Select(area => new PostcodeArea(area.Code, new GeoPoint(area.ReferencePoint.Y, area.ReferencePoint.X)))
            .ToList();
    }

    // Features without an area (no or empty geometry) are left out (S100). A postcode may be several
    // features, one per part; its reference point is the interior point of all of them together.
    public static async Task<IReadOnlyList<KartverketArea>> ReadAreasAsync(Stream geojson, CancellationToken cancellationToken)
    {
        var dataset = await JsonSerializer.DeserializeAsync<Dataset>(geojson, JsonOptions, cancellationToken);
        var collection = dataset?.Areas ?? throw new InvalidDataException("The dataset has no postcode area collection.");
        if (collection.Crs?.Properties?.Name != GeographicCrs)
        {
            throw new InvalidDataException($"Expected coordinates in {GeographicCrs}, got '{collection.Crs?.Properties?.Name}'.");
        }

        var factory = NtsGeometryServices.Instance.CreateGeometryFactory();
        var areas = (collection.Features ?? [])
            .Where(feature => feature.Geometry is { IsEmpty: false })
            .GroupBy(Code, StringComparer.Ordinal)
            .Select(postcode =>
            {
                var parts = postcode.SelectMany(feature => Polygons(feature.Geometry)).ToArray();
                Geometry area = parts.Length == 1 ? parts[0] : factory.CreateMultiPolygon(parts);
                return new KartverketArea(postcode.Key, area, area.InteriorPoint);
            })
            .ToList();

        // An empty dataset would retire every postcode.
        if (areas.Count == 0)
        {
            throw new InvalidDataException("The dataset has no postcode areas.");
        }

        return areas;
    }

    // About 9 MB; the GeoJSON inside is then read from it as a stream.
    private async Task<MemoryStream> DownloadAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DownloadTimeout);
        var archive = new MemoryStream();
        try
        {
            using var response = await http.GetAsync(DatasetUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            await using var bounded = new BoundedStream(body, MaxArchiveBytes);
            await bounded.CopyToAsync(archive, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await archive.DisposeAsync();
            throw new TimeoutException($"The dataset download took longer than {DownloadTimeout}.");
        }
        catch
        {
            await archive.DisposeAsync();
            throw;
        }

        archive.Position = 0;
        return archive;
    }

    private static string Code(IFeature feature) =>
        feature.Attributes?.GetOptionalValue("postnummer") is string code && PostcodeRules.IsWellFormed(code)
            ? code
            : throw new InvalidDataException("A postcode area has no valid four-digit postcode.");

    private static IEnumerable<Polygon> Polygons(Geometry geometry) => geometry switch
    {
        Polygon polygon => [polygon],
        MultiPolygon multi => multi.Geometries.Cast<Polygon>(),
        _ => throw new InvalidDataException($"A postcode area is a {geometry.GeometryType}, not a polygon."),
    };

    private sealed class Dataset
    {
        [JsonPropertyName("postnummeromrader.postnummeromrade")]
        public AreaCollection? Areas { get; init; }
    }

    // The FeatureCollection with its pre-RFC 7946 "crs" member, which NetTopologySuite does not read.
    private sealed class AreaCollection
    {
        [JsonPropertyName("crs")]
        public NamedCrs? Crs { get; init; }

        [JsonPropertyName("features")]
        public List<IFeature>? Features { get; init; }
    }

    private sealed class NamedCrs
    {
        [JsonPropertyName("properties")]
        public NamedCrsProperties? Properties { get; init; }
    }

    private sealed class NamedCrsProperties
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }

    // Reads through to inner and fails once more than limit bytes have come through. Leaves inner open.
    private sealed class BoundedStream(Stream inner, long limit) : Stream
    {
        private long read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Count(int bytes)
        {
            read += bytes;
            return read > limit ? throw new InvalidDataException($"The dataset is larger than {limit} bytes.") : bytes;
        }
    }
}
