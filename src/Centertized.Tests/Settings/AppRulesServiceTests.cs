using Centertized.Core.Settings;
using Centertized.Core.WindowManagement;

namespace Centertized.Tests.Settings;

public class AppRulesServiceTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"centertized-tests-{Guid.NewGuid():N}.json");
    private readonly AppIdentity _notepad = new("notepad.exe", "Notepad");

    [Fact]
    public void RememberedSize_IsStoredAndPersisted()
    {
        var service = new AppRulesService(new JsonSettingsStore(_tempFile));

        service.SetRememberedSize(_notepad, 900, 600);

        Assert.True(service.TryGetRememberedSize("notepad.exe", out var width, out var height));
        Assert.Equal((900, 600), (width, height));

        // A new instance over the same file = simulating an app restart.
        var reloaded = new AppRulesService(new JsonSettingsStore(_tempFile));
        Assert.True(reloaded.TryGetRememberedSize("notepad.exe", out width, out height));
        Assert.Equal((900, 600), (width, height));
    }

    [Fact]
    public void Key_IsCaseInsensitive()
    {
        var service = new AppRulesService(new JsonSettingsStore(_tempFile));
        service.SetExcluded(_notepad, true);

        Assert.True(service.IsExcluded("NOTEPAD.EXE"));
    }

    [Fact]
    public void RuleThatSetsNothing_IsRemoved()
    {
        var service = new AppRulesService(new JsonSettingsStore(_tempFile));
        service.SetExcluded(_notepad, true);

        service.SetExcluded(_notepad, false);

        Assert.Empty(service.All());
    }

    [Fact]
    public void ClearingSize_KeepsExclusion()
    {
        var service = new AppRulesService(new JsonSettingsStore(_tempFile));
        service.SetRememberedSize(_notepad, 900, 600);
        service.SetExcluded(_notepad, true);

        service.ClearRememberedSize("notepad.exe");

        Assert.False(service.TryGetRememberedSize("notepad.exe", out _, out _));
        Assert.True(service.IsExcluded("notepad.exe"));
    }

    [Fact]
    public void Changes_RaiseEventWithDetails()
    {
        var service = new AppRulesService(new JsonSettingsStore(_tempFile));
        AppRuleChange? seen = null;
        service.Changed += change => seen = change;

        service.SetRememberedSize(_notepad, 800, 500);

        Assert.NotNull(seen);
        Assert.Equal(AppRuleChangeKind.SizeRemembered, seen.Kind);
        Assert.Equal("Notepad", seen.DisplayName);
        Assert.Equal((800, 500), (seen.Width, seen.Height));
    }

    [Fact]
    public void SavingRules_DoesNotOverwriteOtherSettings()
    {
        var store = new JsonSettingsStore(_tempFile);
        var service = new AppRulesService(store);
        var settings = store.Load();
        settings.StartMinimized = true;
        store.Save(settings);

        service.SetExcluded(_notepad, true);

        Assert.True(store.Load().StartMinimized);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }
}
