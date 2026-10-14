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
    private bool _previewRequested = true;
    private readonly UiPreferences _appearance;
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
        _appearance = UiPreferences.Load(_paths.Root);
        Root.RequestedTheme = _appearance.Theme;
        _previewRequested = _appearance.ShowPreview;
        Title = "拾贴 ClipNest — 剪贴板历史";
        _desktop = new DesktopIntegration(WinRT.Interop.WindowNative.GetWindowHandle(this));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        Root.ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateCaptionColors);
        Root.Loaded += (_, _) => UpdateCaptionColors();
        ResizePanel(_previewRequested ? 940 : 560, 660);
        _desktop.ToggleRequested += (_, _) =>
        {
            if (_busy) return;
            if (AppWindow.IsVisible && _desktop.IsForeground) Hide(); else ShowHistory();
        };
        AppWindow.Closing += (_, args) => { args.Cancel = true; Hide(); };
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => { if (AppWindow.IsVisible && !_busy) Refresh(); };
        _timer.Start();
        _ready = true;
        ApplyLayout();
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
        AppWindow.MoveInZOrderAtTop();
        _desktop.BringToForeground();
        _desktop.TryRegisterHotkey();
        HotkeyHint.Text = _desktop.HotkeyRegistered ? "Ctrl + Shift + V 唤出" : "快捷键被占用 · 通过托盘打开";
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

    private void UpdateCaptionColors()
    {
        if (CaptionPalette.BorderBrush is Microsoft.UI.Xaml.Media.SolidColorBrush foreground)
        {
            AppWindow.TitleBar.ButtonForegroundColor = foreground.Color;
            AppWindow.TitleBar.ButtonInactiveForegroundColor = foreground.Color;
            AppWindow.TitleBar.ButtonHoverForegroundColor = foreground.Color;
            AppWindow.TitleBar.ButtonPressedForegroundColor = foreground.Color;
        }
        if (CaptionPalette.Background is Microsoft.UI.Xaml.Media.SolidColorBrush background)
        {
            AppWindow.TitleBar.ButtonHoverBackgroundColor = background.Color;
            AppWindow.TitleBar.ButtonPressedBackgroundColor = background.Color;
        }
    }

    private void ResizePanel(int width, int height)
    {
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id,
            Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        var scale = _desktop.DpiScale;
        var w = Math.Min((int)(width * scale), area.Width);
        var h = Math.Min((int)(height * scale), area.Height);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
        AppWindow.Move(new Windows.Graphics.PointInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2));
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_ready) ApplyLayout();
    }

    private void ApplyLayout()
    {
        var show = _previewRequested && Root.ActualWidth >= 780;
        PreviewPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PreviewColumn.Width = new GridLength(show ? 344 : 0);
        PreviewToggle.IsChecked = show;
    }

    private void OnPreviewClicked(object sender, RoutedEventArgs e)
    {
        _previewRequested = PreviewToggle.IsChecked == true;
        _appearance.ShowPreview = _previewRequested;
        try { _appearance.Save(_paths.Root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError(ex); }
        ResizePanel(_previewRequested ? 940 : 560, 660);
        ApplyLayout();
    }

    private void Refresh(bool force = false)
    {
        if (!_ready) return;
        try
        {
            var kind = (KindFilter.SelectedItem as ComboBoxItem)?.Tag as string;
            var entries = _store.Search(SearchBox.Text, HistoryFilter.SelectedItem == SavedFilter, string.IsNullOrEmpty(kind) ? null : kind);
            if (!force && _lastEntries.SequenceEqual(entries)) return;
            var selected = Selected?.Id;
            _lastEntries = entries;
            var rows = entries.Select(e => new HistoryRow(e, _paths.Blobs)).ToList();
            HistoryList.ItemsSource = rows;
            HistoryList.SelectedItem = rows.FirstOrDefault(r => r.Entry.Id == selected) ?? rows.FirstOrDefault();
            EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var unfiltered = string.IsNullOrWhiteSpace(SearchBox.Text) && HistoryFilter.SelectedItem != SavedFilter && string.IsNullOrEmpty(kind);
            EmptyTitle.Text = unfiltered ? "从一次复制开始" : "没有找到匹配的内容";
            EmptyHint.Text = unfiltered ? "复制文字、图片或文件，拾贴会替你记住。" : "试试其他关键词，或切换到「最近 / 所有类型」。";
            foreach (var button in ActionButtons.Children.OfType<Button>()) button.IsEnabled = rows.Count > 0;
            if (rows.Count == 0) UpdatePreview();
            StatusText.Text = $"{(_capture.IsPaused ? "已暂停" : "正在记录")} · {rows.Count} 条 · 仅本机";
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
        FavoriteIcon.Glyph = entry?.IsFavorite == true ? "\uE735" : "\uE734";
        var favoriteLabel = entry?.IsFavorite == true ? "取消收藏" : "收藏";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(FavoriteButton, favoriteLabel);
        ToolTipService.SetToolTip(FavoriteButton, favoriteLabel);
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
        FeedbackBar.Severity = InfoBarSeverity.Informational;
        FeedbackBar.Message = message;
        FeedbackBar.IsOpen = true;
    }
    private void ShowError(Exception ex)
    {
        Serilog.Log.Error(ex, "User operation failed");
        ShowStatus("操作未完成：" + (string.IsNullOrWhiteSpace(ex.Message) ? "剪贴板或窗口暂时不可用，请重试。" : ex.Message));
        FeedbackBar.Severity = InfoBarSeverity.Error;
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
                ShowHistory();
                ShowStatus("已复制，但未能自动粘贴。请切换到目标应用按 Ctrl + V（管理员窗口可能限制自动粘贴）。");
            }
        }
        catch (Exception ex) { ShowError(ex); }
        finally { _busy = false; }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Refresh();
    private void OnHistoryFilterChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) => Refresh(true);
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
    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (_busy || Selected is not { } entry) return;
        _busy = true;
        try
        {
            var confirm = new ContentDialog { Title = "删除这条记录？", Content = "所选的" + HistoryRow.KindName(entry.Kind) + "记录将从历史中移除，此操作无法撤销。", PrimaryButtonText = "删除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            _store.DeleteById(entry.Id);
            if (entry.BlobPath is not null) _blobs.Delete(entry.BlobPath);
            Refresh(true);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { _busy = false; }
    }
    private void OnPauseClicked(object sender, RoutedEventArgs e)
    {
        _capture.IsPaused = PauseButton.IsChecked == true;
        PauseIcon.Glyph = _capture.IsPaused ? "\uE768" : "\uE769";
        var label = _capture.IsPaused ? "继续记录" : "暂停记录";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PauseButton, label);
        ToolTipService.SetToolTip(PauseButton, label);
        Refresh(true);
    }
    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && !_busy) { Hide(); e.Handled = true; }
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
            var theme = new ComboBox { Header = "外观", ItemsSource = new[] { "跟随系统", "浅色", "深色" }, SelectedIndex = (int)Root.RequestedTheme, HorizontalAlignment = HorizontalAlignment.Stretch };
            panel.Children.Add(theme); panel.Children.Add(limit); panel.Children.Add(startup); panel.Children.Add(info);
            var dialog = new ContentDialog { Title = "设置", Content = panel, PrimaryButtonText = "保存", SecondaryButtonText = "清理历史", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary, XamlRoot = Root.XamlRoot };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (!double.IsFinite(limit.Value)) throw new InvalidOperationException("请输入 10–10000 之间的历史条数。");
                _settings.SetHistoryLimit((int)Math.Clamp(limit.Value, 10, 10000));
                StartupRegistration.SetEnabled(startup.IsChecked == true);
                _appearance.Theme = (ElementTheme)theme.SelectedIndex;
                _appearance.Save(_paths.Root);
                Root.RequestedTheme = _appearance.Theme;
                ShowStatus("设置已保存；历史上限在下次复制时应用。");
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
    public BitmapImage? Thumbnail { get; }
    public Visibility ThumbnailVisibility => Thumbnail is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FavoriteVisibility => Entry.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
    public string Glyph => Entry.Kind switch
    { ClipboardKind.Image => "\uEB9F", ClipboardKind.File => "\uE8A5", ClipboardKind.Color => "\uE790", ClipboardKind.Rtf => "\uE8D2", _ => "\uE8A4" };
    public HistoryRow(ClipboardEntry entry, string blobs)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
        if (entry.Kind == ClipboardKind.Image && entry.BlobPath is not null)
        {
            var path = Path.Combine(blobs, Path.GetFileName(entry.BlobPath));
            if (File.Exists(path)) Thumbnail = new BitmapImage { DecodePixelWidth = 88, UriSource = new Uri(path) };
        }
    }
    public string Title => Entry.Kind switch
    {
        ClipboardKind.Image => "图片 · 点击预览",
        ClipboardKind.Rtf => "富文本 · 点击预览",
        _ => (Entry.Content ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ') is var text && text.Length > 160 ? text[..160] + "…" : text,
    };
    public string Subtitle => $"{KindName(Entry.Kind)}  ·  {Entry.SourceApp ?? "未知来源"}  ·  {Entry.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.CurrentCulture)}";
    public static string KindName(ClipboardKind kind) => kind switch
    { ClipboardKind.Text => "文本", ClipboardKind.Image => "图片", ClipboardKind.File => "文件", ClipboardKind.Color => "颜色", ClipboardKind.Rtf => "富文本", _ => "内容" };
}
