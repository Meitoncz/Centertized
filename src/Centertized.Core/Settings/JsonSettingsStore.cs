using System.Text.Json;
using System.Text.Json.Serialization;

namespace Centertized.Core.Settings;

public sealed class JsonSettingsStore : ISettingsStore
{
    // Bez JsonStringEnumConverter by se ThemeMode/Backdrop ukládaly jako čísla (0,1,2) -
    // nečitelné a křehké, kdyby se pořadí hodnot v enumu někdy změnilo.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    /// <param name="filePath">Volitelné – pro testy, ať nesahají na skutečné %AppData%.</param>
    public JsonSettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Centertized",
            "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            // Poškozený/neplatný soubor – radši spustit s výchozím nastavením než appku shodit.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(_filePath, json);
    }
}
