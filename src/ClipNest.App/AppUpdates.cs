using System.Reflection;
using System.IO;
using Velopack;
using Velopack.Sources;

namespace ClipNest.App;

/// <summary>Only installation metadata and version requests are sent to the public release source.</summary>
internal sealed class AppUpdates : IDisposable
{
    public const string ReleasesUrl = "https://github.com/Karl-Dai/ClipNest/releases/latest";
    private readonly UpdateManager _manager;
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _token;
    private Task? _background;
    private int _checking;
    public event EventHandler? Changed;
    public bool Automatic { get; set; } = true;
    public bool IsInstalled => _manager.IsInstalled && !_manager.IsPortable;
    public bool Ready => IsInstalled && _manager.UpdatePendingRestart is not null;
    public string Version => _manager.CurrentVersion?.ToString()
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "未知";
    public string Status { get; private set; } = "尚未检查更新";
    public string? InstallationRoot => IsInstalled ? Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.FullName : null;
    public AppUpdates(UpdateManager? manager = null)
    {
        _manager = manager ?? new UpdateManager(new GithubSource("https://github.com/Karl-Dai/ClipNest", null, false));
        _token = _stop.Token;
    }

    public void Start()
    {
        if (!IsInstalled) { Status = "便携版：请到发布页下载安装版以启用自动更新。"; return; }
        _background ??= RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), _token);
            while (!_stop.IsCancellationRequested)
            {
                if (Automatic) await CheckAsync();
                await Task.Delay(TimeSpan.FromHours(6), _token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async Task CheckAsync()
    {
        if (!IsInstalled) { Notify("便携版请到发布页下载安装版，以启用自动更新。"); return; }
        if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0) return;
        try
        {
            if (Ready) { Notify("更新已下载，下次启动时自动应用，也可以立即重启更新。"); return; }
            Notify("正在检查 GitHub Releases…");
            var update = await _manager.CheckForUpdatesAsync();
            if (update is null) { Notify("当前已是最新版本。"); return; }
            Notify("正在下载 " + update.TargetFullRelease.Version + "…");
            await _manager.DownloadUpdatesAsync(update, p => Notify($"正在下载更新：{p}%"), _token);
            Notify("新版本已下载并校验。下次启动自动应用，或点击「重启更新」。");
            Serilog.Log.Information("Update {Version} downloaded and verified", update.TargetFullRelease.Version);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Update check/download failed");
            Notify("检查或下载更新失败，请稍后重试，或到发布页手动下载。");
        }
        finally { Interlocked.Exchange(ref _checking, 0); }
    }

    public void PrepareRestart()
    {
        if (!Ready) throw new InvalidOperationException("没有已下载的更新。");
        _manager.WaitExitThenApplyUpdates(_manager.UpdatePendingRestart, silent: true, restart: true);
    }

    private void Notify(string status)
    {
        if (_token.IsCancellationRequested) return;
        Status = status;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
