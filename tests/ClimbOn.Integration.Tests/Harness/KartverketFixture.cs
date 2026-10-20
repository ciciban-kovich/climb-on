using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using ClimbOn.Infrastructure.Postcodes;

namespace ClimbOn.Integration.Tests.Harness;

// Stands in for the Kartverket download (C2): answers the dataset URL with a file from
// Fixtures/kartverket, zipped as Geonorge delivers it. ApiFactory routes KartverketSource through it.
public sealed class KartverketFixture
{
    // An unmodified excerpt of the real dataset (see Api/Jobs/README.md).
    public const string Excerpt = "postnummeromrader-v1.geojson";

    // The excerpt edited as a later version: 9008 missing, 4006 moved 0.01° north, 8006 added,
    // and 0101 present without an area.
    public const string NextVersion = "postnummeromrader-v2.geojson";

    private volatile string served = Excerpt;

    public static string PathOf(string file) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "kartverket", file);

    public void Serve(string file) => served = file;

    public void Reset() => served = Excerpt;

    public HttpMessageHandler CreateHandler() => new Handler(this);

    private sealed class Handler(KartverketFixture fixture) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri != KartverketSource.DatasetUri)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var file = fixture.served;
            var archive = new MemoryStream();
            using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
            {
                await using var entry = await zip.CreateEntry(file).OpenAsync(cancellationToken);
                await using var source = File.OpenRead(PathOf(file));
                await source.CopyToAsync(entry, cancellationToken);
            }

            archive.Position = 0;
            var content = new StreamContent(archive);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }
}
