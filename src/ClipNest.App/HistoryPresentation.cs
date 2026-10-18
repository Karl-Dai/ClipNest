using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ClipNest.Core.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Microsoft.UI.Text;

namespace ClipNest.App;

public sealed partial class MainWindow
{
    private const int PageSize = 100;
    private readonly ObservableCollection<HistoryRow> _rows = new();
    private readonly SemaphoreSlim _queryGate = new(1);
    private readonly SemaphoreSlim _previewGate = new(1);
    private readonly BoundedCache<Guid, WriteableBitmap> _previewCache = new(4);
    private CancellationTokenSource? _queryCancellation;
    private CancellationTokenSource? _previewCancellation;
    private Task _refreshTask = Task.CompletedTask;
    private Task _previewTask = Task.CompletedTask;
    private ClipboardEntry? _selectedEntry;
    private HistoryCriteria? _criteria;
    private long _lastRevision = -1;
    private int _queryGeneration;
    private int _totalEntries;
    private bool _queryPending;
    private bool _paging;
    private bool _applyingRows;
    private bool _disposed;
    private ScrollViewer? _listScroll;

    private sealed record HistoryCriteria(string Query, bool Favorites, string? Kind);

    private void Refresh(bool force = false) => _refreshTask = RefreshAsync(force);

    private async Task RefreshAsync(bool force, bool debounce = false)
    {
        if (!_ready || _disposed) return;
        var kind = (KindFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        var criteria = new HistoryCriteria(SearchBox.Text.Trim(), HistoryFilter.SelectedItem == SavedFilter,
            string.IsNullOrEmpty(kind) ? null : kind);
        if (!force && criteria == _criteria && _lastRevision == _store.Revision) return;
        _queryCancellation?.Cancel();
        _queryCancellation?.Dispose();
        _queryCancellation = new CancellationTokenSource();
        var token = _queryCancellation.Token;
        var generation = ++_queryGeneration;
        var count = criteria == _criteria ? Math.Max(_rows.Count, PageSize) : PageSize;
        _queryPending = true;
        UpdateHistoryStatus();
        try
        {
            if (debounce) await Task.Delay(180, token);
            var page = await ReadPageAsync(criteria, 0, count, token);
            if (_disposed || token.IsCancellationRequested || generation != _queryGeneration) return;
            var changed = criteria != _criteria || !_rows.Select(row => row.Entry).SequenceEqual(page.Items);
            _criteria = criteria;
            _lastRevision = page.Revision;
            _totalEntries = page.TotalCount;
            if (changed)
            {
                var selected = (HistoryList.SelectedItem as HistoryRow)?.Entry.Id;
                _applyingRows = true;
                try
                {
                    _rows.Clear();
                    foreach (var entry in page.Items) _rows.Add(new HistoryRow(entry, _paths.Blobs));
                    HistoryList.SelectedItem = _rows.FirstOrDefault(row => row.Entry.Id == selected) ?? _rows.FirstOrDefault();
                }
                finally { _applyingRows = false; }
                UpdatePreview();
            }
            EmptyState.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var unfiltered = criteria.Query.Length == 0 && !criteria.Favorites && criteria.Kind is null;
            EmptyTitle.Text = unfiltered ? "从一次复制开始" : "没有找到匹配的内容";
            EmptyHint.Text = unfiltered ? "复制文字、图片或文件，拾贴会替你记住。" : "试试其他关键词，或切换到「最近 / 所有类型」。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed && generation == _queryGeneration) ShowError(ex); }
        finally
        {
            if (generation == _queryGeneration)
            {
                _queryPending = false;
                if (!_disposed) UpdateHistoryStatus();
            }
        }
    }

    private async Task<ClipboardHistoryPage> ReadPageAsync(HistoryCriteria criteria, int offset, int count, CancellationToken token)
    {
        await _queryGate.WaitAsync(token);
        try { return await Task.Run(() => _store.GetPage(criteria.Query, criteria.Favorites, criteria.Kind, offset, count, token), token); }
        finally { _queryGate.Release(); }
    }

    private async Task LoadMoreAsync()
    {
        if (_disposed || _queryPending || _paging || _criteria is null || _rows.Count >= _totalEntries) return;
        if (_lastRevision != _store.Revision) { await RefreshAsync(true); return; }
        _paging = true;
        var generation = _queryGeneration;
        var token = _queryCancellation?.Token ?? CancellationToken.None;
        UpdateHistoryStatus();
        try
        {
            var page = await ReadPageAsync(_criteria, _rows.Count, PageSize, token);
            if (_disposed || token.IsCancellationRequested || generation != _queryGeneration) return;
            // A concurrent capture shifts offsets. Re-read before appending to avoid gaps/duplicates.
            if (page.Revision != _lastRevision || _store.Revision != _lastRevision) { await RefreshAsync(true); return; }
            foreach (var entry in page.Items) _rows.Add(new HistoryRow(entry, _paths.Blobs));
            _totalEntries = page.TotalCount;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed && generation == _queryGeneration) ShowError(ex); }
        finally { _paging = false; if (!_disposed) UpdateHistoryStatus(); }
    }

    private async void OnLoadMoreClicked(object sender, RoutedEventArgs e) => await LoadMoreAsync();

    private void OnHistoryListLoaded(object sender, RoutedEventArgs e)
    {
        if (_listScroll is not null) return;
        _listScroll = FindChild<ScrollViewer>(HistoryList);
        if (_listScroll is null) return;
        _listScroll.ViewChanged += async (_, args) =>
        {
            if (!args.IsIntermediate && _listScroll.ScrollableHeight > 0
                && _listScroll.ScrollableHeight - _listScroll.VerticalOffset < 160) await LoadMoreAsync();
        };
    }

    private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } descendant) return descendant;
        }
        return null;
    }

    private void UpdateHistoryStatus()
    {
        var count = _rows.Count < _totalEntries ? $"已显示 {_rows.Count} / 共 {_totalEntries} 条" : $"{_totalEntries} 条";
        StatusText.Text = $"{(_capture.IsPaused ? "已暂停" : "正在记录")} · {(_queryPending ? "搜索中…" : count)} · 仅本机";
        LoadMoreButton.Visibility = _rows.Count < _totalEntries ? Visibility.Visible : Visibility.Collapsed;
        LoadMoreButton.Content = _paging ? "正在加载…" : $"加载更多（{_rows.Count} / {_totalEntries}）";
        LoadMoreButton.IsEnabled = !_paging && !_queryPending;
        foreach (var button in ActionButtons.Children.OfType<Button>()) button.IsEnabled = !_queryPending && Selected is not null;
    }

    private void UpdatePreview() => _previewTask = UpdatePreviewAsync();

    private async Task UpdatePreviewAsync()
    {
        if (!_ready || _disposed) return;
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        var token = _previewCancellation.Token;
        var summary = (HistoryList.SelectedItem as HistoryRow)?.Entry;
        _selectedEntry = null;
        PreviewImage.Source = null;
        PreviewText.Text = summary is null ? string.Empty : "正在加载…";
        ImageScroll.Visibility = Visibility.Collapsed;
        RtfPreview.Visibility = Visibility.Collapsed;
        SetRtfPreview(string.Empty, TextSetOptions.None);
        TextScroll.Visibility = Visibility.Visible;
        PreviewTitle.Text = summary is null ? "内容预览" : HistoryRow.KindName(summary.Kind);
        PreviewMeta.Text = summary is null ? "选择左侧记录查看详情" : $"{summary.SourceApp ?? "未知应用"}  ·  {summary.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        FavoriteIcon.Glyph = summary?.IsFavorite == true ? "\uE735" : "\uE734";
        var favoriteLabel = summary?.IsFavorite == true ? "取消收藏" : "收藏";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(FavoriteButton, favoriteLabel);
        ToolTipService.SetToolTip(FavoriteButton, favoriteLabel);
        UpdateHistoryStatus();
        if (summary is null) return;
        var entered = false;
        try
        {
            // Only one full image decode may be in flight. Obsolete selections never publish results.
            await _previewGate.WaitAsync(token);
            entered = true;
            var entry = await Task.Run(() => _store.GetById(summary.Id), token);
            token.ThrowIfCancellationRequested();
            if (_disposed || entry is null) return;
            _selectedEntry = entry;
            PreviewText.Text = entry.Content ?? string.Empty;
            if (entry.Kind == ClipboardKind.Image && entry.BlobPath is not null)
            {
                if (!_previewCache.TryGet(entry.Id, out var image))
                {
                    var file = await StorageFile.GetFileFromPathAsync(BlobFile(entry));
                    using var stream = await file.OpenReadAsync();
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    token.ThrowIfCancellationRequested();
                    var size = ImagePreviewSize.Fit(decoder.PixelWidth, decoder.PixelHeight, Math.Clamp((int)(400 * _desktop.DpiScale), 400, 1024));
                    var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                        new BitmapTransform { ScaledWidth = (uint)size.Width, ScaledHeight = (uint)size.Height },
                        ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
                    token.ThrowIfCancellationRequested();
                    image = new WriteableBitmap(size.Width, size.Height);
                    using (var output = image.PixelBuffer.AsStream()) await output.WriteAsync(pixels.DetachPixelData());
                    image.Invalidate();
                    token.ThrowIfCancellationRequested();
                    _previewCache.Add(entry.Id, image);
                }
                token.ThrowIfCancellationRequested();
                PreviewImage.Source = image;
                TextScroll.Visibility = Visibility.Collapsed;
                ImageScroll.Visibility = Visibility.Visible;
            }
            else if (entry.Kind == ClipboardKind.Rtf)
            {
                SetRtfPreview(entry.Content ?? string.Empty, TextSetOptions.FormatRtf);
                TextScroll.Visibility = Visibility.Collapsed;
                RtfPreview.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed && !token.IsCancellationRequested) { PreviewText.Text = "内容暂不可用，原始记录仍保留。"; ShowError(ex); } }
        finally
        {
            if (entered) _previewGate.Release();
            if (!_disposed && !token.IsCancellationRequested) UpdateHistoryStatus();
        }
    }

    private void SetRtfPreview(string text, TextSetOptions options)
    {
        RtfPreview.IsReadOnly = false;
        try { RtfPreview.Document.SetText(options, text); }
        finally { RtfPreview.IsReadOnly = true; }
    }
}
