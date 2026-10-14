using ClipNest.Core.Environment;
using ClipNest.Core.Models;
using ClipNest.Core.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ClipNest.Core.Tests.Environment;

public sealed class CustomSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clipnest-settings-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(6u, 0x56u, true)]
    [InlineData(3u, 0x70u, true)]
    [InlineData(1u, 0x39u, true)]
    [InlineData(0u, 0x56u, false)]
    [InlineData(4u, 0x56u, false)]
    [InlineData(10u, 0x56u, false)]
    [InlineData(2u, 0x7Bu, false)]
    [InlineData(2u, 0x0Du, false)]
    public void Hotkeys_RequireModifiersAndSupportedPrimaryKey(uint modifiers, uint key, bool valid)
        => new HotkeySetting(modifiers, key).IsValid.Should().Be(valid);

    [Fact]
    public void HotkeyLabel_ReflectsAllConfiguredKeys()
    {
        new HotkeySetting(7, 0x72).Label.Should().Be("Ctrl + Shift + Alt + F3");
        HotkeySetting.Default.Label.Should().Be("Ctrl + Shift + V");
    }

    [Fact]
    public void Location_RoundTripsAnExactUnicodePathOutsideDataFolder()
    {
        var root = Path.Combine(_root, "我的剪贴板");
        var file = Path.Combine(_root, "config", "location.json");
        new DataLocationSettings(root).Save(file);
        DataLocationSettings.Load(file)!.DataRoot.Should().Be(root);
        AppPaths.FromRoot(root).Root.Should().Be(root);
        Directory.Exists(root).Should().BeFalse();
    }

    [Fact]
    public void Migration_PreservesWalRecordsFavoritesImagesAndSettings_AndLeavesSourceIntact()
    {
        var source = AppPaths.FromRoot(Path.Combine(_root, "original"));
        AppEnvironmentBootstrapper.Ensure(source);
        var database = new ClipNestDatabase(source);
        using var connection = database.Open();
        using (var command = connection.CreateCommand())
        { command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;"; command.ExecuteNonQuery(); }
        var store = new SqliteClipboardStore(database);
        var image = new ClipboardEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, ClipboardKind.Image, null, "blobs/test.png", "image-hash", "test");
        store.Add(image, 100);
        store.SetFavorite(image.Id, true);
        var imageBytes = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(Path.Combine(source.Blobs, "test.png"), imageBytes);
        var settings = new SqliteAppSettingsStore(database);
        settings.SetHistoryLimit(333);
        File.WriteAllText(Path.Combine(source.Root, "appearance.json"), "{\"Hotkey\":{\"Modifiers\":3,\"Key\":66}}");

        var moved = DataDirectoryMigration.Copy(source, Path.Combine(_root, "新的数据"), database);
        var copiedDatabase = new ClipNestDatabase(moved);
        new SqliteClipboardStore(copiedDatabase).Search("", true).Should().ContainSingle().Which.Id.Should().Be(image.Id);
        new SqliteAppSettingsStore(copiedDatabase).GetHistoryLimit().Should().Be(333);
        File.ReadAllBytes(Path.Combine(moved.Blobs, "test.png")).Should().Equal(imageBytes);
        File.ReadAllText(Path.Combine(moved.Root, "appearance.json")).Should().Be(File.ReadAllText(Path.Combine(source.Root, "appearance.json")));
        store.Search("", true).Should().ContainSingle();
        File.Exists(Path.Combine(source.Blobs, "test.png")).Should().BeTrue();
        File.Exists(Path.Combine(moved.Root, "migration-incomplete.txt")).Should().BeFalse();
    }

    [Fact]
    public void Migration_RejectsExistingDataWithoutOverwritingIt()
    {
        var source = Path.Combine(_root, "source");
        var target = Path.Combine(_root, "occupied");
        Directory.CreateDirectory(target);
        var file = Path.Combine(target, "important.txt");
        File.WriteAllText(file, "keep");
        var action = () => DataDirectoryMigration.ValidateTarget(source, target);
        action.Should().Throw<IOException>();
        File.ReadAllText(file).Should().Be("keep");
    }

    [Fact]
    public void Migration_RejectsSameFolderParentsChildrenAndRelativePaths()
    {
        var source = Path.Combine(_root, "source");
        foreach (var target in new[] { source, _root, Path.Combine(source, "nested") })
        {
            var action = () => DataDirectoryMigration.ValidateTarget(source, target);
            action.Should().Throw<IOException>();
        }
        var relative = () => DataDirectoryMigration.Normalize("relative/path");
        relative.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Migration_FailedCopyKeepsSourceAndMarksIncompleteDestination()
    {
        var source = AppPaths.FromRoot(Path.Combine(_root, "source"));
        AppEnvironmentBootstrapper.Ensure(source);
        var database = new ClipNestDatabase(source);
        var blob = Path.Combine(source.Blobs, "locked.png");
        File.WriteAllText(blob, "original");
        var target = Path.Combine(_root, "target");
        using (File.Open(blob, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var action = () => DataDirectoryMigration.Copy(source, target, database);
            action.Should().Throw<IOException>();
        }
        File.ReadAllText(blob).Should().Be("original");
        File.Exists(Path.Combine(target, "migration-incomplete.txt")).Should().BeTrue();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
