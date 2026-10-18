using ClipNest.Core.Capture;
using ClipNest.Core.Models;
using ClipNest.Core.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ClipNest.Core.Tests.Persistence;

public sealed class HistoryPagingTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "clipnest-pages-" + Guid.NewGuid() + ".db");
    private readonly ClipNestDatabase _database;
    private readonly SqliteClipboardStore _store;
    public HistoryPagingTests()
    {
        _database = new ClipNestDatabase(_file);
        _store = new SqliteClipboardStore(_database);
    }

    [Fact]
    public void Pages_AccessAllRowsWithoutDuplicates_WhenTimestampsMatch()
    {
        for (var index = 0; index < 601; index++) Add("content-" + index);
        var all = new List<Guid>();
        for (var offset = 0; offset < 601; offset += 100)
        {
            var page = _store.GetPage("", false, offset: offset);
            page.TotalCount.Should().Be(601);
            all.AddRange(page.Items.Select(entry => entry.Id));
        }
        all.Should().HaveCount(601).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Page_ContainsOnlyBoundedSummary_GetByIdReturnsExactCompleteContent()
    {
        var original = "first line\r\n" + new string('中', 100000) + "end marker";
        var id = Add(original);
        _store.GetPage("", false).Items.Single().PreviewText.Should().HaveLength(161);
        _store.GetById(id)!.Content.Should().Be(original);
        _store.GetPage("end marker", false).Items.Single().Id.Should().Be(id);
    }

    [Fact]
    public void LimitChange_ImmediatelyTrimsAndReturnsOnlyDeletedImagePaths_PreservesFavorites()
    {
        var favorite = Add("favorite", ClipboardKind.Image, "blobs/favorite.png", true);
        for (var index = 0; index < 20; index++) Add("image-" + index, ClipboardKind.Image, $"blobs/{index}.png");
        var previous = _store.Revision;
        var deleted = _store.SetHistoryLimitAndTrim(10);
        deleted.Should().HaveCount(10).And.NotContain("blobs/favorite.png");
        _store.GetRecent(100).Count(entry => !entry.IsFavorite).Should().Be(10);
        _store.GetById(favorite)!.IsFavorite.Should().BeTrue();
        new SqliteAppSettingsStore(_database).GetHistoryLimit().Should().Be(10);
        _store.Revision.Should().BeGreaterThan(previous);
        // Simulate a capture that read the previous cap before the settings transaction.
        Add("later-capture-with-stale-limit");
        _store.GetRecent(100).Count(entry => !entry.IsFavorite).Should().Be(10);
    }

    [Fact]
    public void CancelledPage_DoesNotRunQuery()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var query = () => _store.GetPage("", false, cancellationToken: cancel.Token);
        query.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void FiltersAndTotal_AgreeAcrossPages()
    {
        Add("needle", favorite: true);
        Add("needle second");
        Add("needle image", ClipboardKind.Image, "blobs/image.png", true);
        _store.GetPage("needle", true, "text").TotalCount.Should().Be(1);
        _store.GetPage("needle", false, count: 1).Items.Should().HaveCount(1);
        _store.GetPage("needle", false).TotalCount.Should().Be(2);
        _store.GetPage("needle", false, offset: 1, count: 1).Items.Should().HaveCount(1);
        _store.GetPage("absent", false).TotalCount.Should().Be(0);
        // Already loaded favorites can exceed the configurable ordinary-history cap.
        _store.GetPage("", false, count: 10001).TotalCount.Should().Be(3);
    }

    private Guid Add(string content, ClipboardKind kind = ClipboardKind.Text, string? blob = null, bool favorite = false)
    {
        var id = Guid.NewGuid();
        _store.Add(new ClipboardEntry(id, DateTimeOffset.FromUnixTimeMilliseconds(1700000000000), kind,
            kind == ClipboardKind.Image ? null : content, blob, ContentHasher.Hash(content), "Test", favorite), 10000);
        return id;
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_file);
    }
}
