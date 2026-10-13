using System;
using Microsoft.Extensions.DependencyInjection;
using ClipNest.Core.Abstractions;
using ClipNest.Core.Capture;
using ClipNest.Core.Persistence;
using ClipNest.Platform.Clipboard;

namespace ClipNest.Platform;

/// <summary>
/// Registers Windows-specific service implementations and the P1 clipboard-core
/// pipeline with the App-layer DI container.
/// </summary>
public static class PlatformServiceCollectionExtensions
{
    public static IServiceCollection AddClipNestPlatform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Shared infrastructure.
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ClipNestDatabase>();

        // Persistence (Core).
        services.AddSingleton<SqliteClipboardStore>();
        services.AddSingleton<IClipboardStore>(sp => sp.GetRequiredService<SqliteClipboardStore>());
        services.AddSingleton<IAppSettingsStore, SqliteAppSettingsStore>();
        services.AddSingleton<IBlobStore, FileBlobStore>();

        // Capture logic (Core).
        services.AddSingleton<Blacklist>();
        services.AddSingleton<OrphanBlobReconciler>();
        services.AddSingleton<ClipboardCaptureService>();

        // Win32 implementations (Platform).
        services.AddSingleton<IClipboardChangeSource, Win32ClipboardChangeSource>();
        services.AddSingleton<IClipboardReader, Win32ClipboardReader>();
        services.AddSingleton<IForegroundAppProvider, Win32ForegroundAppProvider>();

        // The monitor itself.
        services.AddHostedService<ClipboardMonitorHostedService>();

        return services;
    }
}
