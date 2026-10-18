using System.Security.Cryptography;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

if (args.Length != 2) throw new ArgumentException("Usage: UpdateSmoke <installed-root> <local-feed>");
var root = Path.GetFullPath(args[0]);
var current = Path.Combine(root, "current");
var updater = Path.Combine(root, "Update.exe");
if (!File.Exists(updater) || !File.Exists(Path.Combine(current, "sq.version")))
    throw new IOException("The target must be an existing test installation.");
var locator = new TestVelopackLocator("ClipNest.Windows", "0.3.9", Path.Combine(root, "packages"), current, root,
    updater, channel: "win", processPath: Path.Combine(current, "ClipNest.exe"));
var manager = new UpdateManager(new SimpleFileSource(new DirectoryInfo(args[1])), locator: locator);
var update = await manager.CheckForUpdatesAsync() ?? throw new InvalidOperationException("No newer test update found.");
Console.WriteLine($"Update offered: {manager.CurrentVersion} -> {update.TargetFullRelease.Version}");
await manager.DownloadUpdatesAsync(update);
var pending = manager.UpdatePendingRestart ?? throw new InvalidOperationException("Downloaded update is not ready.");
var package = Path.Combine(root, "packages", update.TargetFullRelease.FileName);
await using (var file = File.OpenRead(package))
{
    var hash = Convert.ToHexString(await SHA256.HashDataAsync(file));
    if (!string.Equals(hash, update.TargetFullRelease.SHA256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Checksum mismatch.");
    Console.WriteLine($"Downloaded package SHA256 verified: {hash}");
}
manager.WaitExitThenApplyUpdates(pending, silent: true, restart: false);
Console.WriteLine("Updater launched; it will apply the update after this test process exits.");
