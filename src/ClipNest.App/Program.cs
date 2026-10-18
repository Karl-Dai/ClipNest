using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using ClipNest.App.SingleInstance;

namespace ClipNest.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Installer/update hooks must run before WinRT, single-instance registration, or UI.
        Velopack.VelopackApp.Build().SetAutoApplyOnStartup(true).Run();
        if (args.Length == 2 && args[0] == "--wait-for-exit" && int.TryParse(args[1], out var previousId))
        {
            try
            {
                using var previous = System.Diagnostics.Process.GetProcessById(previousId);
                if (!previous.WaitForExit(30000)) return 1;
            }
            catch (ArgumentException) { /* The previous instance already exited. */ }
        }
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();

        // Block on the async claim during startup — UI hasn't started yet.
        var claim = SingleInstanceController.ClaimOrRedirectAsync(activationArgs)
            .GetAwaiter()
            .GetResult();

        if (!claim.IsPrimary)
        {
            // Activation already redirected to primary. Exit silently.
            return 0;
        }

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        return 0;
    }
}
