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

    public static class UsesApiInAsyncLambda
    {
        public static Func<Task<string>> Create() => async () =>
        {
            await Task.Yield();
            return new OuterLayerEndpoint().ToString()!;
        };
    }

    public static class UsesApiInAsyncIterator
    {
        public static async IAsyncEnumerable<string> Create()
        {
            await Task.Yield();
            yield return new OuterLayerEndpoint().ToString()!;
        }
    }

    public static class UsesApiInLocalFunctionOfAsyncMethod
    {
        public static async Task<string> Create()
        {
            await Task.Yield();
            return Name();

            static string Name() => new OuterLayerEndpoint().ToString()!;
        }
    }

    public static class UsesNoOuterLayer
    {
        public static string Name() => nameof(UsesNoOuterLayer);
    }
}
