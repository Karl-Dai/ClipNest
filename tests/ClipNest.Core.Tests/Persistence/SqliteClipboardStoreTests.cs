using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using ClipNest.Core.Models;
using ClipNest.Core.Persistence;
using Xunit;

namespace ClipNest.Core.Tests.Persistence;

public class SqliteClipboardStoreTests : IDisposable
{
    private readonly string _dbFile;
    private readonly SqliteClipboardStore _store;

    public SqliteClipboardStoreTests()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), "clipnest-store-" + Path.GetRandomFileName() + ".db");
        _store = new SqliteClipboardStore(new ClipNestDatabase(_dbFile));
    }

    [Fact]
    public void Favorites_SurviveHistoryTrimAndRecopy()
    {
        var now = DateTimeOffset.UtcNow;
        var favorite = TextEntry("keep", now);
        _store.Add(favorite, 1);
        _store.SetFavorite(favorite.Id, true);
        _store.Add(TextEntry("discard", now.AddSeconds(1)), 1);
        _store.Add(TextEntry("latest", now.AddSeconds(2)), 1);
        _store.Search("", false).Select(e => e.Content).Should().BeEquivalentTo("keep", "latest");
        _store.Add(TextEntry("keep", now.AddSeconds(10)), 1);
        _store.Search("", true).Should().ContainSingle().Which.Id.Should().Be(favorite.Id);
    }

    [Fact]
    public void Search_LiteralSymbolsChineseSourceAndType()
    {
        var now = DateTimeOffset.UtcNow;
        _store.Add(TextEntry("你好 100% _ quote'", now), 100);
        _store.Add(TextEntry("unrelated", now.AddSeconds(1)), 100);
        _store.Search("你好", false).Should().ContainSingle();
        _store.Search("%", false).Should().ContainSingle();
        _store.Search("'", false).Should().ContainSingle();
        _store.Search("NOTEPAD", false).Should().HaveCount(2);
        _store.Search("", false, "image").Should().BeEmpty();
    }

    [Fact]
    public void ClearHistory_PreservesFavoritesAndReturnsOnlyDeletedBlobs()
    {
        var now = DateTimeOffset.UtcNow;
        var kept = new ClipboardEntry(Guid.NewGuid(), now, ClipboardKind.Image, null, "blobs/keep.png", "keep", "test");
        var removed = new ClipboardEntry(Guid.NewGuid(), now, ClipboardKind.Image, null, "blobs/remove.png", "remove", "test");
        _store.Add(kept, 100);
        _store.SetFavorite(kept.Id, true);
        _store.Add(removed, 100);
        _store.ClearHistory().Should().BeEquivalentTo("blobs/remove.png");
        _store.GetRecent(100).Should().ContainSingle().Which.Id.Should().Be(kept.Id);
    }

    [Fact]
    public void Add_SamePayloadDifferentKindsKeepsBoth()
    {
        var now = DateTimeOffset.UtcNow;
        _store.Add(new ClipboardEntry(Guid.NewGuid(), now, ClipboardKind.Text, "C:/test.txt", null, "same-hash", "test"), 100);
        _store.Add(new ClipboardEntry(Guid.NewGuid(), now.AddSeconds(1), ClipboardKind.File, "C:/test.txt", null, "same-hash", "test"), 100);
        _store.GetRecent(100).Should().HaveCount(2);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbFile))
        {
            File.Delete(_dbFile);
        }

        GC.SuppressFinalize(this);
    }

    private static ClipboardEntry TextEntry(string text, DateTimeOffset createdAt)
        => new(Guid.NewGuid(), createdAt, ClipboardKind.Text, text, null, "hash-" + text, "notepad");

    [Fact]
    public void Add_ThenGetMostRecent_ReturnsTheEntry()
    {
        var entry = TextEntry("hello", DateTimeOffset.UtcNow);

        var result = _store.Add(entry, historyLimit: 100);

        result.WasDeduplicated.Should().BeFalse();
        var recent = _store.GetMostRecent();
        recent.Should().NotBeNull();
        recent!.Content.Should().Be("hello");
        recent.Kind.Should().Be(ClipboardKind.Text);
    }

    [Fact]
    public void GetRecent_ReturnsNewestFirst()
    {
        var t0 = new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero);
        _store.Add(TextEntry("oldest", t0), 100);
        _store.Add(TextEntry("middle", t0.AddSeconds(10)), 100);
        _store.Add(TextEntry("newest", t0.AddSeconds(20)), 100);

        var recent = _store.GetRecent(10);

        recent.Select(e => e.Content).Should().ContainInOrder("newest", "middle", "oldest");
    }

    [Fact]
    public void Add_SameHash_MovesExistingToTopWithoutNewRow()
    {
        var t0 = new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero);
        var first = new ClipboardEntry(Guid.NewGuid(), t0, ClipboardKind.Text, "A", null, "hash-A", "notepad");
        _store.Add(first, 100);
        _store.Add(TextEntry("B", t0.AddSeconds(10)), 100);

        // Re-copy "A": same hash, later timestamp.
        var reA = new ClipboardEntry(Guid.NewGuid(), t0.AddSeconds(20), ClipboardKind.Text, "A", null, "hash-A", "notepad");
        var result = _store.Add(reA, 100);

        result.WasDeduplicated.Should().BeTrue();
        var recent = _store.GetRecent(10);
        recent.Should().HaveCount(2);                          // not 3
        recent[0].Content.Should().Be("A");                    // A moved to top
        recent[0].Id.Should().Be(first.Id);                    // original row kept
    }

    [Fact]
    public void Add_DeduplicatedImage_ReportsIncomingBlobAsOrphan()
    {
        var t0 = new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero);
        var img1 = new ClipboardEntry(Guid.NewGuid(), t0, ClipboardKind.Image, null, "blobs/a.png", "hash-img", "snip");
        _store.Add(img1, 100);

        var img2 = new ClipboardEntry(Guid.NewGuid(), t0.AddSeconds(30), ClipboardKind.Image, null, "blobs/b.png", "hash-img", "snip");
        var result = _store.Add(img2, 100);

        result.WasDeduplicated.Should().BeTrue();
        result.OrphanedBlobPaths.Should().ContainSingle().Which.Should().Be("blobs/b.png");
    }

    [Fact]
    public void Add_BeyondHistoryLimit_TrimsOldestAndReportsTrimmedImageBlobs()
    {
        var t0 = new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero);
        // Oldest entry is an image; it should be trimmed and its blob reported.
        var oldImage = new ClipboardEntry(Guid.NewGuid(), t0, ClipboardKind.Image, null, "blobs/old.png", "hash-old", "snip");
        _store.Add(oldImage, historyLimit: 3);
        _store.Add(TextEntry("two", t0.AddSeconds(1)), 3);
        _store.Add(TextEntry("three", t0.AddSeconds(2)), 3);

        // Fourth insert exceeds the limit of 3 → oldest (the image) is trimmed.
        var result = _store.Add(TextEntry("four", t0.AddSeconds(3)), historyLimit: 3);

        result.OrphanedBlobPaths.Should().ContainSingle().Which.Should().Be("blobs/old.png");
        _store.GetRecent(10).Should().HaveCount(3);
        _store.GetRecent(10).Select(e => e.Content).Should().NotContain((string?)null!);
    }

    [Fact]
    public void GetImageBlobReferences_ReturnsOnlyImageRows()
    {
        var t0 = new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero);
        var img = new ClipboardEntry(Guid.NewGuid(), t0, ClipboardKind.Image, null, "blobs/x.png", "hash-x", "snip");
        _store.Add(img, 100);
        _store.Add(TextEntry("plain", t0.AddSeconds(1)), 100);

        var refs = _store.GetImageBlobReferences();

        refs.Should().ContainSingle();
        refs[0].Id.Should().Be(img.Id);
        refs[0].BlobPath.Should().Be("blobs/x.png");
    }

    [Fact]
    public void DeleteById_RemovesTheRow()
    {
        var entry = TextEntry("doomed", DateTimeOffset.UtcNow);
        _store.Add(entry, 100);

        _store.DeleteById(entry.Id);

        _store.GetMostRecent().Should().BeNull();
    }
}
