using System.Globalization;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using ClipNest.Core.Abstractions;
using ClipNest.Core.Capture;
using ClipNest.Core.Environment;
using ClipNest.Core.Models;
using ClipNest.Core.Persistence;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Microsoft.UI.Text;

namespace ClipNest.App;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly SqliteClipboardStore _store;
    private readonly IAppPaths _paths;
    private readonly IBlobStore _blobs;
    private readonly IAppSettingsStore _settings;
    private readonly ClipboardCaptureService _capture;
    private readonly DesktopIntegration _desktop;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
    private bool _ready;
    private bool _busy;
    private DateTimeOffset _statusUntil;
    private IReadOnlyList<ClipboardEntry> _lastEntries = Array.Empty<ClipboardEntry>();
    private ClipboardEntry? Selected => (HistoryList.SelectedItem as HistoryRow)?.Entry;

    public MainWindow()
    {
        _store = App.Services.GetRequiredService<SqliteClipboardStore>();
        _paths = App.Services.GetRequiredService<IAppPaths>();
        _blobs = App.Services.GetRequiredService<IBlobStore>();
        _settings = App.Services.GetRequiredService<IAppSettingsStore>();
        _capture = App.Services.GetRequiredService<ClipboardCaptureService>();
        InitializeComponent();
        Title = "拾贴 ClipNest — 剪贴板历史";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1120, 740));
        var display = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
        var area = display.WorkArea;
        AppWindow.Move(new Windows.Graphics.PointInt32(area.X + Math.Max(0, (area.Width - 1120) / 2), area.Y + Math.Max(0, (area.Height - 740) / 2)));
        _desktop = new DesktopIntegration(WinRT.Interop.WindowNative.GetWindowHandle(this));
        _desktop.ToggleRequested += (_, _) => { if (_desktop.IsForeground) Hide(); else ShowHistory(); };
        AppWindow.Closing += (_, args) => { args.Cancel = true; Hide(); };
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => { if (AppWindow.IsVisible && !_busy) Refresh(); };
        _timer.Start();
        _ready = true;
        Refresh(true);
        if (!_desktop.HotkeyRegistered)
            HotkeyHint.Text = "快捷键已被占用 · 请通过托盘打开";
    }

    public void ShowHistory()
    {
        _desktop.RememberForeground();
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter
            && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
            presenter.Restore();
        AppWindow.Show();
        Activate();
        Refresh(true);
        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectAll();
    }

    public void Dispose()
    {
        _timer.Stop();
        _desktop.Dispose();
    }

    private void Hide() => AppWindow.Hide();

    private void Refresh(bool force = false)
    {
        if (!_ready) return;
        try
        {
            var kind = (KindFilter.SelectedItem as ComboBoxItem)?.Tag as string;
            var entries = _store.Search(SearchBox.Text, FavoritesButton.IsChecked == true, string.IsNullOrEmpty(kind) ? null : kind);
            if (!force && _lastEntries.SequenceEqual(entries)) return;
            var selected = Selected?.Id;
            _lastEntries = entries;
            var rows = entries.Select(e => new HistoryRow(e)).ToList();
            HistoryList.ItemsSource = rows;
            HistoryList.SelectedItem = rows.FirstOrDefault(r => r.Entry.Id == selected) ?? rows.FirstOrDefault();
            EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyHint.Text = string.IsNullOrWhiteSpace(SearchBox.Text) && FavoritesButton.IsChecked != true
                ? "复制一段文字或一张图片，即可开始。" : "没有匹配的记录，试试其他关键词或筛选条件。";
            foreach (var button in ActionButtons.Children.OfType<Button>()) button.IsEnabled = rows.Count > 0;
            if (rows.Count == 0) UpdatePreview();
            if (DateTimeOffset.UtcNow >= _statusUntil)
                StatusText.Text = $"{(_capture.IsPaused ? "已暂停记录" : "正在记录")} · 显示 {rows.Count} 条（最多 500 条）· 数据仅保存在本机";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void UpdatePreview()
    {
        var entry = Selected;
        PreviewImage.Source = null;
        ImageScroll.Visibility = Visibility.Collapsed;
        RtfPreview.Visibility = Visibility.Collapsed;
        TextScroll.Visibility = Visibility.Visible;
        PreviewTitle.Text = entry is null ? "内容预览" : HistoryRow.KindName(entry.Kind);
        PreviewMeta.Text = entry is null ? "选择左侧记录查看详情" : $"{entry.SourceApp ?? "未知应用"}  ·  {entry.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        PreviewText.Text = entry?.Content ?? string.Empty;
        FavoriteButton.Content = entry?.IsFavorite == true ? "★ 已收藏" : "☆ 收藏";
        if (entry is null) return;
        try
        {
            if (entry.Kind == ClipboardKind.Image && entry.BlobPath is not null)
            {
                var path = BlobFile(entry);
                if (!File.Exists(path)) { PreviewText.Text = "图片文件已丢失，可删除这条记录。"; return; }
                PreviewImage.Source = new BitmapImage(new Uri(path));
                TextScroll.Visibility = Visibility.Collapsed;
                ImageScroll.Visibility = Visibility.Visible;
            }
            else if (entry.Kind == ClipboardKind.Rtf)
            {
                RtfPreview.Document.SetText(TextSetOptions.FormatRtf, entry.Content ?? string.Empty);
                TextScroll.Visibility = Visibility.Collapsed;
                RtfPreview.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private string BlobFile(ClipboardEntry entry) => Path.Combine(_paths.Blobs, Path.GetFileName(entry.BlobPath!));
    private void ShowStatus(string message)
    {
        _statusUntil = DateTimeOffset.UtcNow.AddSeconds(8);
        StatusText.Text = message;
    }
    private void ShowError(Exception ex)
    {
        Serilog.Log.Error(ex, "User operation failed");
        ShowStatus("操作未完成：" + (string.IsNullOrWhiteSpace(ex.Message) ? "剪贴板或窗口暂时不可用，请重试。" : ex.Message));
    }

    private async Task CopyOrPasteAsync(bool paste)
    {
        if (_busy || Selected is not { } entry) return;
        _busy = true;
        try
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            switch (entry.Kind)
            {
                case ClipboardKind.Image:
                    var image = await StorageFile.GetFileFromPathAsync(BlobFile(entry));
                    package.SetBitmap(RandomAccessStreamReference.CreateFromFile(image));
                    break;
                case ClipboardKind.File:
                    var items = new List<IStorageItem>();
                    foreach (var path in (entry.Content ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (Directory.Exists(path)) items.Add(await StorageFolder.GetFolderFromPathAsync(path));
                        else items.Add(await StorageFile.GetFileFromPathAsync(path));
                    }
                    package.SetStorageItems(items);
                    break;
                case ClipboardKind.Rtf:
                    package.SetRtf(entry.Content ?? string.Empty);
                    RtfPreview.Document.GetText(TextGetOptions.None, out var plain);
                    package.SetText(plain.TrimEnd('\r'));
                    break;
                default: package.SetText(entry.Content ?? string.Empty); break;
            }
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            // Clipboard listeners can briefly open the clipboard immediately after
            // SetContent. Retry Flush instead of abandoning an otherwise valid copy.
            for (var attempt = 0; ; attempt++)
            {
                try { Windows.ApplicationModel.DataTransfer.Clipboard.Flush(); break; }
                catch (System.Runtime.InteropServices.COMException) when (attempt < 5)
                { await Task.Delay(40); }
            }
            if (!paste) { ShowStatus("已复制到剪贴板。"); return; }
            if (!_desktop.HasPasteTarget) { ShowStatus("已复制。请切换到目标应用，按 Ctrl + V 粘贴。"); return; }
            Hide();
            if (!await _desktop.PasteAsync())
            {
                AppWindow.Show();
                Activate();
                ShowStatus("已复制，但未能自动粘贴。请切换到目标应用按 Ctrl + V（管理员窗口可能限制自动粘贴）。");
            }
        }
        catch (Exception ex) { ShowError(ex); }
        finally { _busy = false; }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Refresh();
    private void OnFilterClicked(object sender, RoutedEventArgs e) => Refresh(true);
    private void OnKindChanged(object sender, SelectionChangedEventArgs e) => Refresh(true);
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();
    private async void OnPasteClicked(object sender, RoutedEventArgs e) => await CopyOrPasteAsync(true);
    private async void OnCopyClicked(object sender, RoutedEventArgs e) => await CopyOrPasteAsync(false);
    private async void OnItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null && source != HistoryList)
        {
            if (source is ListViewItem) { await CopyOrPasteAsync(true); return; }
            source = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(source);
        }
    }
    private void OnFavoriteClicked(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) return;
        try { _store.SetFavorite(entry.Id, !entry.IsFavorite); Refresh(true); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) return;
        try
        {
            _store.DeleteById(entry.Id);
            if (entry.BlobPath is not null) _blobs.Delete(entry.BlobPath);
            Refresh(true);
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void OnPauseClicked(object sender, RoutedEventArgs e)
    {
        _capture.IsPaused = PauseButton.IsChecked == true;
        PauseButton.Content = _capture.IsPaused ? "继续记录" : "暂停记录";
        Refresh(true);
    }
    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { Hide(); e.Handled = true; }
    }
    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Down && HistoryList.Items.Count > 0)
        { HistoryList.Focus(FocusState.Programmatic); e.Handled = true; }
        else if (e.Key == VirtualKey.Enter) { e.Handled = true; await CopyOrPasteAsync(true); }
    }
    private async void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { e.Handled = true; await CopyOrPasteAsync(true); }
    }
    private async void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var limit = new NumberBox { Header = "历史记录上限（收藏不计入）", Minimum = 10, Maximum = 10000, Value = _settings.GetHistoryLimit(), SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            var info = new TextBlock { Text = "关闭窗口后仍在托盘运行；右键托盘可退出。\n快捷键：Ctrl + Shift + V。\n数据保存在：" + _paths.Root, TextWrapping = TextWrapping.Wrap };
            var startup = new CheckBox { Content = "登录 Windows 时启动", IsChecked = StartupRegistration.IsEnabled };
            var panel = new StackPanel { Spacing = 18, MaxWidth = 480 };
            panel.Children.Add(limit); panel.Children.Add(startup); panel.Children.Add(info);
            var dialog = new ContentDialog { Title = "设置", Content = panel, PrimaryButtonText = "保存", SecondaryButtonText = "清理历史", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary, XamlRoot = Root.XamlRoot };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (!double.IsFinite(limit.Value)) throw new InvalidOperationException("请输入 10–10000 之间的历史条数。");
                _settings.SetHistoryLimit((int)Math.Clamp(limit.Value, 10, 10000));
                StartupRegistration.SetEnabled(startup.IsChecked == true);
                StatusText.Text = "设置已保存；历史上限在下次复制时应用。";
            }
            else if (result == ContentDialogResult.Secondary)
            {
                var confirm = new ContentDialog { Title = "清理未收藏的历史？", Content = "收藏的内容会保留。此操作无法撤销。", PrimaryButtonText = "清理", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot };
                if (await confirm.ShowAsync() == ContentDialogResult.Primary)
                {
                    foreach (var blob in _store.ClearHistory()) _blobs.Delete(blob);
                    Refresh(true);
                }
            }
        }
        catch (Exception ex) { ShowError(ex); }
        finally { _busy = false; }
    }
}

public sealed class HistoryRow
{
    public ClipboardEntry Entry { get; }
    public HistoryRow(ClipboardEntry entry) => Entry = entry;
    public string Title => (Entry.IsFavorite ? "★  " : string.Empty) + (Entry.Kind switch
    {
        ClipboardKind.Image => "图片 · 点击预览",
        ClipboardKind.Rtf => "富文本 · 点击预览",
        _ => (Entry.Content ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ') is var text && text.Length > 160 ? text[..160] + "…" : text,
    });
    public string Subtitle => $"{KindName(Entry.Kind)}  ·  {Entry.SourceApp ?? "未知来源"}  ·  {Entry.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.CurrentCulture)}";
    public static string KindName(ClipboardKind kind) => kind switch
    { ClipboardKind.Text => "文本", ClipboardKind.Image => "图片", ClipboardKind.File => "文件", ClipboardKind.Color => "颜色", ClipboardKind.Rtf => "富文本", _ => "内容" };
}
