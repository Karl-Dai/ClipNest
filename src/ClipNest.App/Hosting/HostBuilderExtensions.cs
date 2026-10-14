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
    internal static string? StartupWarning { get; private set; }
    internal static string DefaultBase => System.Environment.GetEnvironmentVariable("CLIPNEST_DATA_ROOT") is { Length: > 0 } testRoot
        ? testRoot : System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
    internal static string LocationFile => Path.Combine(DefaultBase, "ClipNest-config", "location.json");
    /// <summary>
    /// Builds the application host: configures Serilog into the user's log
    /// directory, ensures the data scaffold exists, and registers DI services.
    /// </summary>
    public static IHost BuildAppHost()
    {
        var paths = new AppPaths(DefaultBase);
        try
        {
            var location = DataLocationSettings.Load(LocationFile);
            if (location is not null)
            {
                var root = DataDirectoryMigration.Normalize(location.DataRoot);
                if (!File.Exists(Path.Combine(root, "db", "clipnest.db")))
                    throw new IOException("找不到自定义目录中的历史数据库。");
                paths = AppPaths.FromRoot(root);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        { StartupWarning = "自定义数据位置暂不可用，本次使用默认目录。请在设置中检查路径。" + ex.Message; }
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
