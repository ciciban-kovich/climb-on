namespace ClimbOn.Api.Hosting;

// Design §2 'Same-origin climber client': the climber PWA's files, and its index.html for every client route,
// so a link such as /confirm or /login (S87) opens the client. Paths under /api never fall back: an unknown
// API route stays a JSON 404 (C45), and a known one keeps its 405.
public static class ClimberClientHosting
{
    public const string IndexFile = "index.html";

    // The first segment carries the /api exclusion, so routing drops these fallbacks from the api's own route
    // tree when it builds it. A catch-all over the whole path would stay a candidate there and hide the api's
    // 405s. The regex constraint ignores case, as routing does.
    private const string OneSegment = "{segment:nonfile:regex(^(?!api$))}";

    // required: an empty catch-all would match one segment, file names included.
    private const string MoreSegments = "{segment:regex(^(?!api$))}/{**path:required:nonfile}";

    public static IEndpointRouteBuilder MapClimberClient(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapStaticAssets();
        endpoints.MapFallbackToFile("/", IndexFile);
        endpoints.MapFallbackToFile(OneSegment, IndexFile);
        endpoints.MapFallbackToFile(MoreSegments, IndexFile);
        return endpoints;
    }
}
