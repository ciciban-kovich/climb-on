using System.Data.Common;
using System.Net.Http;
using System.Reactive.Subjects;
using Microsoft.Win32.SafeHandles;

namespace ClimbOn.Architecture.Violations.Dependencies;

public static class UsesHttp
{
    public static HttpClient Create() => new();
}

public static class UsesDatabase
{
    public static string Describe(DbConnection connection) => connection.Database;
}

public static class UsesFileSystem
{
    public static bool Exists(string path) => File.Exists(path);
}

public static class UsesNonBclType
{
    public static bool IsInvalid(SafeFileHandle handle) => handle.IsInvalid;
}

public static class UsesHttpInAsyncMethod
{
    public static async Task<int> Read()
    {
        await Task.Yield();
        using var client = new HttpClient();
        return client.DefaultRequestHeaders.Count();
    }
}

public static class UsesPackageTypeInSystemNamespace
{
    public static bool HasObservers(Subject<int> subject) => subject.HasObservers;
}

public static class UsesOnlyBcl
{
    public static int Sum(IEnumerable<int> values) => values.Sum();

    public static long Length(Stream stream) => new MemoryStream().Length + stream.Length;

    // Both compile to <PrivateImplementationDetails> inline-array helpers.
    public static string Join(string system, string grade, string suffix) => string.Join(" ", system, grade, suffix);

    public static int First(int a, int b)
    {
        ReadOnlySpan<int> values = [a, b];
        return values[0];
    }
}

public static class UsesHttpAsTypeArgument
{
    public static List<HttpClient> Create() => [];
}

public static class UsesHttpInAsyncLambda
{
    public static Func<Task<int>> Read() => async () =>
    {
        await Task.Yield();
        using var client = new HttpClient();
        return client.DefaultRequestHeaders.Count();
    };
}

public static class UsesHttpInAsyncIterator
{
    public static async IAsyncEnumerable<int> Read()
    {
        await Task.Yield();
        using var client = new HttpClient();
        yield return client.DefaultRequestHeaders.Count();
    }
}

public static class UsesHttpInLocalFunctionOfAsyncMethod
{
    public static async Task<int> Read()
    {
        await Task.Yield();
        return Count();

        static int Count()
        {
            using var client = new HttpClient();
            return client.DefaultRequestHeaders.Count();
        }
    }
}
