using System.Text.RegularExpressions;
using System.Xml.Linq;
using Centertized.Core.Settings;

namespace Centertized.Tests.Localization;

/// <summary>
/// Guards the UI translations (Resources/Strings.&lt;code&gt;.xaml): every language must define exactly the same keys
/// as the English base and keep the same {0}/{1} placeholders, otherwise the UI would show raw keys or crash on format.
/// </summary>
public class TranslationDictionaryTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string ResourcesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Centertized.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src", "Centertized", "Resources");
    }

    private static Dictionary<string, string> Load(string path) =>
        XDocument.Load(path).Root!.Elements()
            .Where(e => e.Attribute(Xaml + "Key") is not null)
            .ToDictionary(e => e.Attribute(Xaml + "Key")!.Value, e => e.Value);

    private static string[] Placeholders(string value) =>
        Regex.Matches(value, @"\{\d+\}").Select(m => m.Value).OrderBy(v => v).ToArray();

    public static IEnumerable<object[]> Languages() =>
        Directory.GetFiles(ResourcesDirectory(), "Strings.*.xaml")
            .Where(f => !f.EndsWith("Strings.en.xaml", StringComparison.Ordinal))
            .Select(f => new object[] { Path.GetFileName(f).Split('.')[1] });

    [Theory]
    [MemberData(nameof(Languages))]
    public void Translation_HasSameKeysAsEnglish(string code)
    {
        var english = Load(Path.Combine(ResourcesDirectory(), "Strings.en.xaml"));
        var translation = Load(Path.Combine(ResourcesDirectory(), $"Strings.{code}.xaml"));

        Assert.Empty(english.Keys.Except(translation.Keys));
        Assert.Empty(translation.Keys.Except(english.Keys));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Translation_KeepsPlaceholdersAndIsNotEmpty(string code)
    {
        var english = Load(Path.Combine(ResourcesDirectory(), "Strings.en.xaml"));
        var translation = Load(Path.Combine(ResourcesDirectory(), $"Strings.{code}.xaml"));

        foreach (var (key, value) in translation)
        {
            Assert.False(string.IsNullOrWhiteSpace(value), $"{code}: '{key}' is empty");
            Assert.Equal(Placeholders(english[key]), Placeholders(value));
        }
    }

    [Fact]
    public void EveryLanguageValue_HasADictionary()
    {
        // English is the base file; System has no dictionary of its own.
        var languageCount = Enum.GetValues<AppLanguage>().Count(l => l != AppLanguage.System);

        Assert.Equal(languageCount, Directory.GetFiles(ResourcesDirectory(), "Strings.*.xaml").Length);
    }
}
