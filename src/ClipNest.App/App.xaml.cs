using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Serilog;
using ClipNest.App.Hosting;
using ClipNest.App.Tray;

namespace ClipNest.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "WinUI owns Application; Shutdown disposes the main window and host before Exit.")]
public partial class App : Application
{
    private IHost? _host;
    private ILogger<App>? _logger;
    private MainWindow? _mainWindow;
    private TrayIconHost? _tray;

    /// <summary>The DI container, available after <see cref="OnLaunched"/>.</summary>
    internal static IServiceProvider Services =>
        ((App)Current)._host?.Services
        ?? throw new InvalidOperationException("Host not built yet.");

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;

        // When a secondary instance redirects activation here, surface the window.
        Microsoft.Windows.AppLifecycle.AppInstance
            .GetCurrent()
            .Activated += OnActivatedFromSecondaryInstance;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (System.Environment.GetCommandLineArgs().Contains("--quit", StringComparer.Ordinal)) { Exit(); return; }
        _host = HostBuilderExtensions.BuildAppHost();
        // Start synchronously so future IHostedService workers (P1+ clipboard
        // monitor, hotkey listener, etc.) actually run.
        _host.Start();
        _logger = _host.Services.GetRequiredService<ILogger<App>>();
        _logger.LogInformation("App starting (pid={Pid})", System.Environment.ProcessId);

        _mainWindow = new MainWindow();
        if (_mainWindow.Updates.IsInstalled && StartupRegistration.IsEnabled)
            StartupRegistration.SetEnabled(true); // Keep an existing startup preference at the stable installed path.
        if (!System.Environment.GetCommandLineArgs().Contains("--background", StringComparer.Ordinal))
            _mainWindow.ShowHistory();

        _tray = new TrayIconHost();
        _tray.ShowRequested += (_, _) =>
        {
            _mainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                _mainWindow.ShowHistory();
            });
        };
        _tray.QuitRequested += (_, _) => Shutdown();
    }

    private void OnActivatedFromSecondaryInstance(
        object? sender,
        Microsoft.Windows.AppLifecycle.AppActivationArguments e)
    {
        if (_mainWindow is null)
        {
            return;
        }

        // Marshal to UI thread, show + bring to front.
        _mainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            if (e.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch
                && (launch.Arguments.Trim() == "--quit" || launch.Arguments.TrimEnd().EndsWith(" --quit", StringComparison.Ordinal))) { Shutdown(); return; }
            _logger?.LogInformation("Secondary instance activation redirected; surfacing window");
            _mainWindow.ShowHistory();
        });
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled exception in UI thread");
        // Allow default WinUI handling to continue; we just record it.
    }

    internal async Task MoveDataAndRestartAsync(string destination)
    {
        var paths = Services.GetRequiredService<ClipNest.Core.Environment.IAppPaths>();
        var database = Services.GetRequiredService<ClipNest.Core.Persistence.ClipNestDatabase>();
        var monitor = Services.GetServices<IHostedService>().OfType<ClipNest.Platform.ClipboardMonitorHostedService>().Single();
        var previousLocation = ClipNest.Core.Environment.DataLocationSettings.Load(HostBuilderExtensions.LocationFile);
        var locationChanged = false;
        // Stop on the UI thread, then await the final in-flight clipboard write.
        await monitor.StopAsync(CancellationToken.None);
        try
        {
            var target = await Task.Run(() => ClipNest.Core.Environment.DataDirectoryMigration.Copy(paths, destination, database));
            new ClipNest.Core.Environment.DataLocationSettings(target.Root).Save(HostBuilderExtensions.LocationFile);
            locationChanged = true;
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = System.Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序位置。"),
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                WorkingDirectory = AppContext.BaseDirectory
            };
            start.ArgumentList.Add("--wait-for-exit");
            start.ArgumentList.Add(System.Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var restarted = System.Diagnostics.Process.Start(start)
                ?? throw new InvalidOperationException("无法重新启动程序。");
        }
        catch
        {
            try
            {
                if (locationChanged)
                {
                    if (previousLocation is null) System.IO.File.Delete(HostBuilderExtensions.LocationFile);
                    else previousLocation.Save(HostBuilderExtensions.LocationFile);
                }
            }
            finally { await monitor.StartAsync(CancellationToken.None); }
            throw;
        }
        Shutdown();
    }

    /// <summary>Invoked by the tray "Quit" command. Flushes logs then exits.</summary>
    internal void Shutdown()
    {
        _logger?.LogInformation("App stopping");
        _mainWindow?.Dispose();
        _tray?.Dispose();
        _host?.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        Log.CloseAndFlush();
        _host?.Dispose();
        Exit();
    }
}
