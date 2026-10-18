using System;
using System.Threading.Tasks;
using Microsoft.Windows.AppLifecycle;

namespace ClipNest.App.SingleInstance;

/// <summary>
/// Owns the AppInstance key for ClipNest and decides whether the current
/// process is the primary instance or should hand off and exit.
/// </summary>
internal static class SingleInstanceController
{
    private const string InstanceKey = "ClipNest.Singleton";

    /// <summary>
    /// Result of <see cref="ClaimOrRedirect"/>. When <see cref="IsPrimary"/>
    /// is false the caller must exit immediately.
    /// </summary>
    public readonly record struct InstanceClaim(bool IsPrimary, AppInstance Primary);

    public static async Task<InstanceClaim> ClaimOrRedirectAsync(AppActivationArguments activationArgs)
    {
        var key = InstanceKey;
#if CLIPNEST_DIAGNOSTICS
        if (System.Environment.GetCommandLineArgs().Any(argument => argument is "--diagnostics" or "--diagnostics-ui"))
        {
            var root = System.Environment.GetEnvironmentVariable("CLIPNEST_DATA_ROOT") ?? string.Empty;
            key += ".Diagnostics." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..16];
        }
#endif
        var primary = AppInstance.FindOrRegisterForKey(key);
        if (primary.IsCurrent)
        {
            return new InstanceClaim(IsPrimary: true, Primary: primary);
        }

        // Forward this activation to the primary instance, then signal caller to exit.
        await primary.RedirectActivationToAsync(activationArgs).AsTask().ConfigureAwait(false);
        return new InstanceClaim(IsPrimary: false, Primary: primary);
    }
}
