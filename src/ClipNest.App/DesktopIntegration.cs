using System.Runtime.InteropServices;

namespace ClipNest.App;

/// <summary>Owns the global hotkey and restores the explicitly remembered paste target.</summary>
internal sealed class DesktopIntegration : IDisposable
{
    private readonly nint _window;
    private readonly SubclassProc _procedure;
    private nint _pasteTarget;
    public event EventHandler? ToggleRequested;
    public bool HotkeyRegistered { get; private set; }
    public double DpiScale => GetDpiForWindow(_window) / 96d;

    public DesktopIntegration(nint window)
    {
        _window = window;
        _procedure = WindowProc;
        if (!SetWindowSubclass(window, _procedure, 1, 0))
            throw new InvalidOperationException("无法注册窗口消息处理程序。");
        TryRegisterHotkey();
        RememberForeground();
    }

    public void TryRegisterHotkey()
    {
        if (HotkeyRegistered) return;
        HotkeyRegistered = RegisterHotKey(_window, 1, 0x4000 | 0x0002 | 0x0004, 0x56);
        if (HotkeyRegistered) Serilog.Log.Information("Global hotkey Ctrl+Shift+V registered");
        else Serilog.Log.Warning("Global hotkey registration failed: {Error}", Marshal.GetLastWin32Error());
    }

    public void BringToForeground()
    {
        // Presenter state can lag the HWND after hiding/minimizing. Restore both.
        ShowWindow(_window, IsIconic(_window) ? 9 : 5);
        var activated = SetForegroundWindow(_window);
        Serilog.Log.Information("History activation: foreground={Foreground}, minimized={Minimized}",
            activated || IsForeground, IsIconic(_window));
    }

    public void RememberForeground()
    {
        var target = GetForegroundWindow();
        if (GetWindowThreadProcessId(target, out var pid) == 0) return;
        if (target != 0 && pid != (uint)System.Environment.ProcessId) _pasteTarget = target;
    }

    public bool HasPasteTarget => _pasteTarget != 0 && IsWindow(_pasteTarget);
    public bool IsForeground => GetForegroundWindow() == _window;

    public async Task<bool> PasteAsync()
    {
        var target = _pasteTarget;
        if (target == 0 || !IsWindow(target)) { Serilog.Log.Warning("Paste target no longer exists"); return false; }
        // Let WinUI finish the hide/activation transition before restoring focus.
        await Task.Delay(80);
        if (IsIconic(target)) ShowWindow(target, 9);
        var activated = SetForegroundWindow(target);
        if (!activated && GetForegroundWindow() != target)
        { Serilog.Log.Warning("Could not activate paste target {Target}", target); return false; }
        // Do not synthesize Ctrl+V until the trigger keys have been released.
        for (var i = 0; i < 50 && KeysHeld(); i++) await Task.Delay(20);
        if (KeysHeld() || GetForegroundWindow() != target)
        { Serilog.Log.Warning("Paste cancelled: trigger held or focus changed"); return false; }
        INPUT[] inputs = [Key(0x11), Key(0x56), Key(0x56, true), Key(0x11, true)];
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length) Serilog.Log.Warning("Paste input rejected: {Sent} events, error {Error}", sent, Marshal.GetLastWin32Error());
        return sent == inputs.Length;
    }

    private static bool KeysHeld() => (GetAsyncKeyState(0x10) & 0x8000) != 0
        || (GetAsyncKeyState(0x11) & 0x8000) != 0 || (GetAsyncKeyState(0x12) & 0x8000) != 0
        || (GetAsyncKeyState(0x0D) & 0x8000) != 0 || (GetAsyncKeyState(0x01) & 0x8000) != 0;

    private static INPUT Key(ushort key, bool up = false) => new()
    { type = 1, data = new InputUnion { keyboard = new KEYBDINPUT { vk = key, flags = up ? 2u : 0u } } };

    private nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x0312 && wParam == 1)
        {
            Serilog.Log.Information("Global hotkey received");
            RememberForeground();
            ToggleRequested?.Invoke(this, EventArgs.Empty);
            return 0;
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        UnregisterHotKey(_window, 1);
        RemoveWindowSubclass(_window, _procedure, 1);
    }

    private delegate nint SubclassProc(nint hwnd, uint msg, nuint wp, nint lp, nuint id, nuint data);
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    { [FieldOffset(0)] public KEYBDINPUT keyboard; [FieldOffset(0)] public MOUSEINPUT mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT
    { public ushort vk, scan; public uint flags, time; public nuint extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT
    { public int x, y; public uint mouseData, flags, time; public nuint extra; }
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint msg, nuint wp, nint lp);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
