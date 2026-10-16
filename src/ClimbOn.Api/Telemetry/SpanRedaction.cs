using System.Diagnostics;
using OpenTelemetry;

namespace ClimbOn.Api.Telemetry;

// C32: a span keeps the route template (http.route, and the span name the instrumentation derives
// from it), never the raw path, the query string or who called.
public sealed class SpanRedaction : BaseProcessor<Activity>
{
    private static readonly string[] DroppedTags =
    [
        "url.full",
        "url.path",
        "url.query",
        "http.target",
        "http.url",
        "client.address",
        "client.port",
        "network.peer.address",
        "network.peer.port",
        "user_agent.original",
    ];

    public override void OnEnd(Activity data)
    {
        foreach (var tag in DroppedTags)
        {
            data.SetTag(tag, null);
        }
    }
}
