using System.IO.Compression;
using System.Net;
using ClimbOn.Infrastructure.Postcodes;

namespace ClimbOn.Infrastructure.Tests.Postcodes;

// The dataset excerpts in Fixtures/kartverket (shared with the integration tests), and small
// hand-written datasets in the same format.
public static class KartverketFiles
{
    // An unmodified excerpt of the real dataset (see Api/Jobs/README.md).
    public const string Excerpt = "postnummeromrader-v1.geojson";

    // The excerpt edited as a later version; 0101 is present without an area.
    public const string NextVersion = "postnummeromrader-v2.geojson";

    public static string PathOf(string file) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "kartverket", file);

    public const string Square = """{ "type": "Polygon", "coordinates": [[[10, 60], [10.1, 60], [10.1, 60.1], [10, 60.1], [10, 60]]] }""";

    public static string Feature(string postnummer = "\"0010\"", string geometry = Square) =>
        $$"""{ "type": "Feature", "properties": { "postnummer": {{postnummer}} }, "geometry": {{geometry}} }""";

    // The real file's shape: the areas under their own top-level member, with a named crs.
    public static string Dataset(params string[] features) => DatasetIn("\"EPSG:4258\"", features);

    // crs is the JSON value of the crs name.
    public static string DatasetIn(string crs, params string[] features) =>
        $$"""
        {
          "postnummeromrader.postnummeromrade": {
            "type": "FeatureCollection",
            "crs": { "type": "name", "properties": { "name": {{crs}} } },
            "features": [{{string.Join(", ", features)}}]
          }
        }
        """;

    public static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(content);
            }
        }

        return archive.ToArray();
    }

    public static byte[] ZippedExcerpt() => Zip((Excerpt, File.ReadAllBytes(PathOf(Excerpt))));

    // A client whose every request for the dataset is answered by respond.
    public static HttpClient Client(Func<HttpResponseMessage> respond) => new(new Handler(respond));

    public static HttpResponseMessage Ok(byte[] archive) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };

    private sealed class Handler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri == KartverketSource.DatasetUri ? respond() : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
