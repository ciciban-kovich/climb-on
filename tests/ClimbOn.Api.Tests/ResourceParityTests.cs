using System.Xml.Linq;
using Xunit;

namespace ClimbOn.Api.Tests;

// C46, read from the .resx sources as XML: a ResourceManager would hide a missing nb key behind English.
public sealed class ResourceParityTests
{
    private static readonly string ResourcesDirectory =
        Path.Combine(RepoRoot(), "src", "ClimbOn.Api", "Resources");

    [Fact]
    public void Both_languages_have_the_same_keys()
    {
        var english = Entries("Texts.resx");
        var bokmal = Entries("Texts.nb.resx");

        Assert.NotEmpty(english);
        Assert.Empty(english.Keys.Except(bokmal.Keys).Select(key => "missing in nb: " + key)
            .Concat(bokmal.Keys.Except(english.Keys).Select(key => "missing in en: " + key)));
    }

    [Theory]
    [InlineData("Texts.resx")]
    [InlineData("Texts.nb.resx")]
    public void No_text_is_empty(string file) =>
        Assert.Empty(Entries(file).Where(entry => string.IsNullOrWhiteSpace(entry.Value)).Select(entry => entry.Key));

    [Theory]
    [InlineData("Texts.resx")]
    [InlineData("Texts.nb.resx")]
    public void No_key_is_duplicated(string file) =>
        Assert.Empty(Names(file).GroupBy(name => name).Where(group => group.Count() > 1).Select(group => group.Key));

    private static Dictionary<string, string> Entries(string file) =>
        Data(file).ToDictionary(data => (string)data.Attribute("name")!, data => (string?)data.Element("value") ?? string.Empty);

    private static IEnumerable<string> Names(string file) => Data(file).Select(data => (string)data.Attribute("name")!);

    private static IEnumerable<XElement> Data(string file) =>
        XDocument.Load(Path.Combine(ResourcesDirectory, file)).Root!.Elements("data");

    private static string RepoRoot()
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
