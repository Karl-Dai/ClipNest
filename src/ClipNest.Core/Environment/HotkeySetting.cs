using System.Globalization;
using System.Text.Json.Serialization;

namespace ClipNest.Core.Environment;

public sealed record HotkeySetting(uint Modifiers, uint Key)
{
    public static HotkeySetting Default { get; } = new(6, 0x56);
    // Ctrl or Alt is required; do not reserve ordinary typing, Windows keys, or F12.
    [JsonIgnore]
    public bool IsValid => (Modifiers & ~7u) == 0 && (Modifiers & 3u) != 0
        && (Key is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 or >= 0x70 and <= 0x7A);
    [JsonIgnore]
    public string KeyLabel => Key is >= 0x70 and <= 0x7A
        ? "F" + (Key - 0x6F).ToString(CultureInfo.InvariantCulture) : ((char)Key).ToString();
    [JsonIgnore]
    public string Label => string.Join(" + ", new[]
    {
        (Modifiers & 2) != 0 ? "Ctrl" : null,
        (Modifiers & 4) != 0 ? "Shift" : null,
        (Modifiers & 1) != 0 ? "Alt" : null,
        KeyLabel
    }.Where(s => s is not null));
}
