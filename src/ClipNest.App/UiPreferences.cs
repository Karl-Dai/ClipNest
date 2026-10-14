using System.IO;
using System.Text.Json;
using Microsoft.UI.Xaml;
using ClipNest.Core.Environment;

namespace ClipNest.App;

internal sealed class UiPreferences
{
    public ElementTheme Theme { get; set; }
    public bool ShowPreview { get; set; } = true;
    public int LayoutVersion { get; set; }
    public HotkeySetting Hotkey { get; set; } = HotkeySetting.Default;

    public static UiPreferences Load(string root)
    {
        try
        {
            var path = Path.Combine(root, "appearance.json");
            var settings = File.Exists(path) ? JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(path)) : null;
            if (settings is not null && Enum.IsDefined(settings.Theme))
            {
                if (settings.Hotkey is null || !settings.Hotkey.IsValid) settings.Hotkey = HotkeySetting.Default;
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { Serilog.Log.Warning(ex, "Could not load appearance preferences"); }
        return new UiPreferences();
    }

    public void Save(string root)
    {
        var path = Path.Combine(root, "appearance.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this));
        File.Move(path + ".tmp", path, true);
    }
}
