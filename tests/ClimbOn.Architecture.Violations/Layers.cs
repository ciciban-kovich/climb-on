namespace ClimbOn.Infrastructure.StandIn
{
    public sealed class OuterLayerService;
}

namespace ClimbOn.Api.StandIn
{
    public sealed class OuterLayerEndpoint;
}

namespace ClimbOn.Architecture.Violations.Layers
{
    using ClimbOn.Api.StandIn;
    using ClimbOn.Infrastructure.StandIn;

    public static class UsesInfrastructure
    {
        public static OuterLayerService Create() => new();
    }

    public static class UsesApi
    {
        public static OuterLayerEndpoint Create() => new();
    }

    public static class UsesApiInAsyncMethod
    {
        public static async Task<string> Create()
        {
            await Task.Yield();
            return new OuterLayerEndpoint().ToString()!;
        }
    }

    public static class UsesNoOuterLayer
    {
        public static string Name() => nameof(UsesNoOuterLayer);
    }
}
