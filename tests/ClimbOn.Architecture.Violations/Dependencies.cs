using System.Data.Common;
using System.Net.Http;
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

public static class UsesOnlyBcl
{
    public static int Sum(IEnumerable<int> values) => values.Sum();

    public static long Length(Stream stream) => new MemoryStream().Length + stream.Length;
}

public static class UsesHttpAsTypeArgument
{
    public static List<HttpClient> Create() => [];
}
