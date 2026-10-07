namespace ClimbOn.Api.Tests;

internal static class Repo
{
    public static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ClimbOn.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("ClimbOn.slnx not found above " + AppContext.BaseDirectory);
    }
}
