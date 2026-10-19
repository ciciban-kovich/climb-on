using System.Net;
using System.Text;
using ClimbOn.Domain.Geo;
using ClimbOn.Infrastructure.Postcodes;
using Xunit;
using static ClimbOn.Infrastructure.Tests.Postcodes.KartverketFiles;

namespace ClimbOn.Infrastructure.Tests.Postcodes;

// Downloading and unpacking the dataset. Each failure is raised before anything is imported (S101.2).
[Trait("SpecPartial", "S101")]
public sealed class KartverketDownloadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Zipped_excerpt_loads_as_degrees()
    {
        var areas = await new KartverketSource(Client(() => Ok(ZippedExcerpt()))).LoadAsync(Ct);

        Assert.Equal(8, areas.Count);
        var palace = areas.Single(area => area.Code == "0010").Point;
        Assert.InRange(Distance.Kilometres(palace, new GeoPoint(59.91722, 10.72750)), 0, 5);
    }

    [Fact]
    public async Task Failed_download_is_refused() =>
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new KartverketSource(Client(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))).LoadAsync(Ct));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Archive_without_exactly_one_geojson_file_is_refused(int files)
    {
        var dataset = File.ReadAllBytes(PathOf(Excerpt));
        var entries = Enumerable.Range(0, files)
            .Select(index => (Name: $"{index}.geojson", Content: dataset))
            .Append((Name: "readme.txt", Content: "x"u8.ToArray()));
        var archive = Zip([.. entries]);

        await Assert.ThrowsAsync<InvalidDataException>(() => new KartverketSource(Client(() => Ok(archive))).LoadAsync(Ct));
    }

    [Fact]
    public async Task Response_that_is_not_a_zip_is_refused() =>
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new KartverketSource(Client(() => Ok(Encoding.UTF8.GetBytes("<html></html>")))).LoadAsync(Ct));

    [Fact]
    public async Task Archive_larger_than_its_limit_is_refused()
    {
        var archive = ZippedExcerpt();
        var source = new KartverketSource(Client(() => Ok(archive))) { MaxArchiveBytes = archive.Length - 1 };

        await Assert.ThrowsAsync<InvalidDataException>(() => source.LoadAsync(Ct));
    }

    [Fact]
    public async Task Archive_at_its_limit_is_read()
    {
        var archive = ZippedExcerpt();
        var source = new KartverketSource(Client(() => Ok(archive))) { MaxArchiveBytes = archive.Length };

        Assert.Equal(8, (await source.LoadAsync(Ct)).Count);
    }

    [Fact]
    public async Task Dataset_larger_than_its_limit_is_refused()
    {
        var size = new FileInfo(PathOf(Excerpt)).Length;
        var source = new KartverketSource(Client(() => Ok(ZippedExcerpt()))) { MaxGeoJsonBytes = size - 1 };

        await Assert.ThrowsAsync<InvalidDataException>(() => source.LoadAsync(Ct));
    }

    [Fact]
    public async Task Dataset_at_its_limit_is_read()
    {
        var size = new FileInfo(PathOf(Excerpt)).Length;
        var source = new KartverketSource(Client(() => Ok(ZippedExcerpt()))) { MaxGeoJsonBytes = size };

        Assert.Equal(8, (await source.LoadAsync(Ct)).Count);
    }

    [Fact(Timeout = 10_000)]
    public async Task Download_that_stalls_after_the_headers_times_out()
    {
        var source = new KartverketSource(Client(Stalled)) { DownloadTimeout = TimeSpan.FromMilliseconds(200) };

        await Assert.ThrowsAsync<TimeoutException>(() => source.LoadAsync(Ct));
    }

    // Stopping the job is not reported as a timeout.
    [Fact(Timeout = 10_000)]
    public async Task Download_cancelled_by_the_caller_is_cancelled()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        stop.CancelAfter(TimeSpan.FromMilliseconds(200));
        var source = new KartverketSource(Client(Stalled));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.LoadAsync(stop.Token));
    }

    private static HttpResponseMessage Stalled() => new(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) };

    // A body that sends nothing until the read is cancelled.
    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
