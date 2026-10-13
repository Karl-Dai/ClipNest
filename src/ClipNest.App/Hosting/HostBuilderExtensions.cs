using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using ClipNest.Core.Environment;
using ClipNest.Platform;

namespace ClipNest.App.Hosting;

internal static class HostBuilderExtensions
{
    /// <summary>
    /// Builds the application host: configures Serilog into the user's log
    /// directory, ensures the data scaffold exists, and registers DI services.
    /// </summary>
    public static IHost BuildAppHost()
    {
        var testRoot = System.Environment.GetEnvironmentVariable("CLIPNEST_DATA_ROOT");
        var paths = string.IsNullOrWhiteSpace(testRoot) ? AppPaths.Default : new AppPaths(testRoot);
        AppEnvironmentBootstrapper.Ensure(paths);

        var logPath = Path.Combine(paths.Logs, "clipnest-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Debug()
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} :: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Services.AddLogging(lb => lb.AddSerilog(Log.Logger, dispose: true));

        builder.Services.AddSingleton<IAppPaths>(paths);
        builder.Services.AddClipNestPlatform();

        return builder.Build();
    }
}
