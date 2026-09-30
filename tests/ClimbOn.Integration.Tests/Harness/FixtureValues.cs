using System.Collections.Concurrent;

namespace ClimbOn.Integration.Tests.Harness;

// Every personal value the fixtures produce; the C33 scan fails if any of them reaches logs or traces.
public sealed class FixtureValues
{
    private readonly ConcurrentDictionary<string, string> kinds = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> All => kinds.Keys.ToArray();

    public void Register(string kind, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        kinds.TryAdd(value, kind);
    }

    public bool Contains(string value) => kinds.ContainsKey(value);
}
