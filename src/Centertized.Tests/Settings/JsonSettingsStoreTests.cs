using Centertized.Core.Settings;

namespace Centertized.Tests.Settings;

public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"centertized-tests-{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = new JsonSettingsStore(_tempFile);

        var settings = store.Load();

        Assert.Equal(1, settings.SchemaVersion);
        Assert.Empty(settings.Hotkeys);
        Assert.False(settings.StartWithWindows);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new JsonSettingsStore(_tempFile);
        var settings = new AppSettings { StartWithWindows = true };
        settings.Hotkeys["center-active-window"] = "Ctrl+Alt+C";

        store.Save(settings);
        var loaded = store.Load();

        Assert.True(loaded.StartWithWindows);
        Assert.Equal("Ctrl+Alt+C", loaded.Hotkeys["center-active-window"]);
    }

    [Fact]
    public void SaveThenLoad_EnumsRoundTripAsReadableStrings()
    {
        var store = new JsonSettingsStore(_tempFile);
        var settings = new AppSettings { ThemeMode = ThemePreference.Dark };

        store.Save(settings);
        var rawJson = File.ReadAllText(_tempFile);
        var loaded = store.Load();

        // The enum must be stored as a readable string (not 0/1/2) - see JsonStringEnumConverter.
        Assert.Contains("\"Dark\"", rawJson);
        Assert.Equal(ThemePreference.Dark, loaded.ThemeMode);
    }

    [Fact]
    public void Load_CorruptedFile_FallsBackToDefaultsInsteadOfThrowing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_tempFile)!);
        File.WriteAllText(_tempFile, "{ toto neni platny json");
        var store = new JsonSettingsStore(_tempFile);

        var settings = store.Load();

        Assert.Equal(1, settings.SchemaVersion);
    }

    [Fact]
    public void Save_CreatesDirectoryIfMissing()
    {
        var nestedPath = Path.Combine(Path.GetTempPath(), $"centertized-tests-{Guid.NewGuid():N}", "settings.json");
        var store = new JsonSettingsStore(nestedPath);

        store.Save(new AppSettings());

        Assert.True(File.Exists(nestedPath));
        Directory.Delete(Path.GetDirectoryName(nestedPath)!, recursive: true);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }
}
