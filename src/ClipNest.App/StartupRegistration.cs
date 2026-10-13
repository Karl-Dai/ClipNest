using Microsoft.Win32;

namespace ClipNest.App;

internal static class StartupRegistration
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipNestWindows";
    public static bool IsEnabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RunPath); return key?.GetValue(ValueName) is string; }
    }
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunPath);
        if (enabled)
        {
            var path = System.Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序位置。");
            key.SetValue(ValueName, "\"" + path + "\" --background");
        }
        else key.DeleteValue(ValueName, false);
    }
}
