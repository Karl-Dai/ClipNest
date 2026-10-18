using ClipNest.App;
using FluentAssertions;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;
using Xunit;

namespace ClipNest.Core.Tests.Environment;

public sealed class AppUpdatesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clipnest-updates-" + Guid.NewGuid().ToString("N"));

    private sealed class Manager : UpdateManager
    {
        public Manager(string root) : base(new SimpleFileSource(new DirectoryInfo(root)), locator: new TestVelopackLocator("ClipNest.Windows", "0.3.9", root)) { }
        public bool Installed { get; set; } = true;
        public bool FailDownload { get; set; }
        public bool HasUpdate { get; set; } = true;
        public int Checks { get; private set; }
        public int Downloads { get; private set; }
        private VelopackAsset? _pending;
        public override bool IsInstalled => Installed;
        public override bool IsPortable => false;
        public override VelopackAsset? UpdatePendingRestart => _pending;
        public override Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            Checks++;
            return Task.FromResult(HasUpdate ? new UpdateInfo(new VelopackAsset { PackageId = "ClipNest.Windows", Version = SemanticVersion.Parse("0.4.0"), Type = VelopackAssetType.Full, FileName = "test.nupkg" }, false) : null);
        }
        public override Task DownloadUpdatesAsync(UpdateInfo updates, Action<int>? progress = null, CancellationToken cancelToken = default)
        {
            Downloads++;
            if (FailDownload) throw new IOException("Interrupted download / checksum failure");
            progress?.Invoke(100);
            _pending = updates.TargetFullRelease;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PortableBuild_DoesNotContactUpdateSource()
    {
        var manager = new Manager(_root) { Installed = false };
        using var updates = new AppUpdates(manager);
        await updates.CheckAsync();
        manager.Checks.Should().Be(0);
        manager.Downloads.Should().Be(0);
        updates.Ready.Should().BeFalse();
        updates.Status.Should().Contain("便携版");
    }

    [Fact]
    public async Task DownloadFailure_DoesNotOfferRestartAndCanBeRetried()
    {
        var manager = new Manager(_root) { FailDownload = true };
        using var updates = new AppUpdates(manager);
        await updates.CheckAsync();
        updates.Ready.Should().BeFalse();
        updates.Status.Should().Contain("失败");
        manager.FailDownload = false;
        await updates.CheckAsync();
        updates.Ready.Should().BeTrue();
        manager.Downloads.Should().Be(2);
    }

    [Fact]
    public async Task ReadyUpdate_IsNotDownloadedAgain()
    {
        var manager = new Manager(_root);
        using var updates = new AppUpdates(manager);
        await updates.CheckAsync();
        await updates.CheckAsync();
        manager.Downloads.Should().Be(1);
        updates.Ready.Should().BeTrue();
        updates.Status.Should().Contain("下次启动");
    }

    [Fact]
    public async Task CurrentVersion_DoesNotDownloadOrOfferRestart()
    {
        var manager = new Manager(_root) { HasUpdate = false };
        using var updates = new AppUpdates(manager);
        await updates.CheckAsync();
        manager.Downloads.Should().Be(0);
        updates.Ready.Should().BeFalse();
        updates.Status.Should().Contain("最新版本");
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
