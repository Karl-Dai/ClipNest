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
    private Action<HotkeySetting>? _recordHotkey;
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
        if (_appearance.LayoutVersion < 2)
        {
            _appearance.ShowPreview = true;
            _appearance.LayoutVersion = 2;
            try { _appearance.Save(_paths.Root); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Serilog.Log.Warning(ex, "Could not save preview layout preference"); }
        }
        Root.RequestedTheme = _appearance.Theme;
        _previewRequested = _appearance.ShowPreview;
        Title = "拾贴 ClipNest — 剪贴板历史";
        _desktop = new DesktopIntegration(WinRT.Interop.WindowNative.GetWindowHandle(this), _appearance.Hotkey);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        var floating = Microsoft.UI.Windowing.OverlappedPresenter.CreateForToolWindow();
        floating.SetBorderAndTitleBar(true, false);
        floating.IsResizable = false;
        floating.IsMinimizable = false;
        floating.IsMaximizable = false;
        floating.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(floating);
        AppWindow.IsShownInSwitchers = false;
        ResizePanel(_previewRequested ? 880 : 520, 480);
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
                DispatcherQueue.TryEnqueue(() =>
                {
                    // Owned flyouts and settings must not dismiss the panel.
                    if (!_busy && !DesktopIntegration.IsAppForeground && AppWindow.IsVisible) Hide();
                });
        };
        _desktop.ToggleRequested += (_, _) =>
        {
            // The registered combination is delivered as WM_HOTKEY rather than
            // a TextBox key event. Feed it into the focused recorder as well.
            if (_recordHotkey is not null) { _recordHotkey(_desktop.Hotkey); return; }
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
        if (Hosting.HostBuilderExtensions.StartupWarning is { } warning) ShowStatus(warning);
    }

    public void ShowHistory()
    {
        _desktop.RememberForeground();
        if (!AppWindow.IsVisible) PositionAtPointer();
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter
            && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
            presenter.Restore();
        AppWindow.Show();
        Activate();
        AppWindow.MoveInZOrderAtTop();
        _desktop.BringToForeground();
        _desktop.TryRegisterHotkey();
        UpdateHotkeyHint();
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
    private void UpdateHotkeyHint() => HotkeyHint.Text = _desktop.HotkeyRegistered
        ? _desktop.Hotkey.Label + " 唤出" : "快捷键被占用 · 通过托盘打开";
    private void OnDismissClicked(object sender, RoutedEventArgs e) { if (!_busy) Hide(); }

    private void PositionAtPointer()
    {
        var point = DesktopIntegration.PointerPosition;
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromPoint(point,
            Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        // Move to the target monitor before reading the window's DPI.
        AppWindow.Move(point);
        ResizePanel(_previewRequested ? 880 : 520, 480);
        var size = AppWindow.Size;
        AppWindow.Move(new Windows.Graphics.PointInt32(
            Math.Clamp(point.X + 12, area.X, area.X + Math.Max(0, area.Width - size.Width)),
            Math.Clamp(point.Y + 12, area.Y, area.Y + Math.Max(0, area.Height - size.Height))));
    }

    private void ResizePanel(int width, int height)
    {
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id,
            Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        var scale = _desktop.DpiScale;
        var w = Math.Min((int)(width * scale), area.Width);
        var h = Math.Min((int)(height * scale), area.Height);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
        AppWindow.Move(new Windows.Graphics.PointInt32(
            Math.Clamp(AppWindow.Position.X, area.X, area.X + area.Width - w),
            Math.Clamp(AppWindow.Position.Y, area.Y, area.Y + area.Height - h)));
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
        SearchArea.Margin = new Thickness(16, 4, show ? 360 : 16, 8);
        PreviewToggle.IsChecked = show;
    }

    private void OnPreviewClicked(object sender, RoutedEventArgs e)
    {
        _previewRequested = PreviewToggle.IsChecked == true;
        _appearance.ShowPreview = _previewRequested;
        try { _appearance.Save(_paths.Root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError(ex); }
        ResizePanel(_previewRequested ? 880 : 520, 480);
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
            var confirm = new ContentDialog { Title = "删除这条记录？", Content = "所选的" + HistoryRow.KindName(entry.Kind) + "记录将从历史中移除，此操作无法撤销。", PrimaryButtonText = "删除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme };
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
        if (e.Key is VirtualKey.Down or VirtualKey.Up && HistoryList.Items.Count > 0)
        {
            var next = HistoryList.SelectedIndex + (e.Key == VirtualKey.Down ? 1 : -1);
            HistoryList.SelectedIndex = Math.Clamp(next, 0, HistoryList.Items.Count - 1);
            HistoryList.ScrollIntoView(HistoryList.SelectedItem);
            e.Handled = true;
        }
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
            var startup = new CheckBox { Content = "登录 Windows 时启动", IsChecked = StartupRegistration.IsEnabled };
            var panel = new StackPanel { Spacing = 16, MinWidth = 300, MaxWidth = 440 };
            var theme = new ComboBox { Header = "外观", ItemsSource = new[] { "跟随系统", "浅色", "深色" }, SelectedIndex = (int)Root.RequestedTheme, HorizontalAlignment = HorizontalAlignment.Stretch };
            var candidate = _desktop.Hotkey;
            string? recordingError = null;
            const string recordingHelp = "点击输入框，直接按组合键。需包含 Ctrl 或 Alt，搭配字母、数字或 F1–F11；Esc 撤销录入。";
            var recorder = new TextBox { Header = "唤出快捷键", Text = candidate.Label, IsReadOnly = true, MinHeight = 40 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(recorder, "唤出快捷键，直接按组合键录入");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(recorder, recordingHelp);
            var recordingHint = new TextBlock { Text = recordingHelp, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            void Record(HotkeySetting value)
            {
                if (!value.IsValid)
                {
                    recordingError = "组合无效：请按 Ctrl 或 Alt，加字母、数字或 F1–F11。";
                    recordingHint.Text = recordingError;
                    return;
                }
                candidate = value;
                recordingError = null;
                recorder.Text = value.Label;
                recordingHint.Text = "已录入 " + value.Label + "，点击「保存」后生效。";
            }
            recorder.GotFocus += (_, _) => _recordHotkey = Record;
            recorder.LostFocus += (_, _) => _recordHotkey = null;
            recorder.PreviewKeyDown += (_, args) =>
            {
                if (args.Key == VirtualKey.Tab) return; // Preserve keyboard navigation.
                args.Handled = true; // No typing, paste, or dialog default action while recording.
                if (args.Key == VirtualKey.Escape)
                {
                    candidate = _desktop.Hotkey;
                    recorder.Text = candidate.Label;
                    recordingError = null;
                    recordingHint.Text = "已撤销录入，保留当前快捷键。";
                    return;
                }
                if (args.Key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
                    or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
                    or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
                    or VirtualKey.LeftWindows or VirtualKey.RightWindows) return;
                Record(new HotkeySetting(ReadHotkeyModifiers(), (uint)args.Key));
            };
            var shortcut = new StackPanel { Spacing = 8 };
            shortcut.Children.Add(recorder);
            shortcut.Children.Add(recordingHint);
            var resetHotkey = new HyperlinkButton { Content = "恢复 Ctrl + Shift + V", Padding = new Thickness(0) };
            resetHotkey.Click += (_, _) => Record(HotkeySetting.Default);
            shortcut.Children.Add(resetHotkey);
            var location = new TextBox { Header = "数据文件夹", Text = _paths.Root, TextWrapping = TextWrapping.Wrap, MaxHeight = 88 };
            var browse = new Button { Content = "选择文件夹…" };
            var locationHint = new TextBlock { Text = "选择空文件夹。保存后迁移历史、收藏、图片和设置并重启，原目录保留备份。", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            var error = new InfoBar { IsOpen = false, Severity = InfoBarSeverity.Error, IsClosable = false };
            browse.Click += async (_, _) =>
            {
                try
                {
                    var picker = new Windows.Storage.Pickers.FolderPicker();
                    picker.FileTypeFilter.Add("*");
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                    var folder = await picker.PickSingleFolderAsync();
                    if (folder is not null) location.Text = folder.Path;
                }
                catch (Exception ex) { error.Message = "无法打开文件夹选择器，也可以直接填写路径。" + ex.Message; error.IsOpen = true; }
            };
            panel.Children.Add(shortcut); panel.Children.Add(location); panel.Children.Add(browse); panel.Children.Add(locationHint);
            panel.Children.Add(theme); panel.Children.Add(limit); panel.Children.Add(startup);
            var scroll = new ScrollViewer { Content = panel, MaxHeight = Math.Max(180, Root.ActualHeight - 210), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(error); content.Children.Add(scroll);
            string? newLocation = null;
            var dialog = new ContentDialog { Title = "设置", Content = content, PrimaryButtonText = "保存", SecondaryButtonText = "清理历史", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary, XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                try
                {
                    var destination = DataDirectoryMigration.Normalize(location.Text);
                    newLocation = DataDirectoryMigration.SameLocation(destination, _paths.Root) ? null : destination;
                    if (newLocation is not null)
                    {
                        DataDirectoryMigration.ValidateTarget(_paths.Root, newLocation);
                        var configFolder = Path.GetDirectoryName(Hosting.HostBuilderExtensions.LocationFile)!;
                        if (DataDirectoryMigration.SameLocation(newLocation, configFolder)
                            || configFolder.StartsWith(newLocation + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                            || newLocation.StartsWith(configFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("请选择其他文件夹，不能使用程序的配置目录。");
                    }
                    if (!double.IsFinite(limit.Value) || limit.Value < 10 || limit.Value > 10000)
                        throw new InvalidOperationException("请输入 10–10000 之间的历史条数。");
                    if (recordingError is not null) throw new InvalidOperationException(recordingError);
                    _desktop.ChangeHotkey(candidate, () =>
                    {
                        var oldHotkey = _appearance.Hotkey;
                        var oldTheme = _appearance.Theme;
                        try
                        {
                            _appearance.Hotkey = candidate;
                            _appearance.Theme = (ElementTheme)theme.SelectedIndex;
                            _appearance.Save(_paths.Root);
                        }
                        catch { _appearance.Hotkey = oldHotkey; _appearance.Theme = oldTheme; throw; }
                    });
                    UpdateHotkeyHint();
                    _settings.SetHistoryLimit((int)limit.Value);
                    if (StartupRegistration.IsEnabled != (startup.IsChecked == true)) StartupRegistration.SetEnabled(startup.IsChecked == true);
                    Root.RequestedTheme = _appearance.Theme;
                }
                catch (Exception ex) { args.Cancel = true; error.Message = ex.Message; error.IsOpen = true; }
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (newLocation is not null)
                {
                    ShowStatus("正在迁移数据，完成后将自动重启…");
                    await ((App)Application.Current).MoveDataAndRestartAsync(newLocation);
                }
                else ShowStatus("设置已保存，快捷键已生效。");
            }
            else if (result == ContentDialogResult.Secondary)
            {
                var confirm = new ContentDialog { Title = "清理未收藏的历史？", Content = "收藏的内容会保留。此操作无法撤销。", PrimaryButtonText = "清理", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme };
                if (await confirm.ShowAsync() == ContentDialogResult.Primary)
                {
                    foreach (var blob in _store.ClearHistory()) _blobs.Delete(blob);
                    Refresh(true);
                }
            }
        }
        catch (Exception ex) { ShowError(ex); }
        finally { _recordHotkey = null; _busy = false; }
    }

    private static uint ReadHotkeyModifiers()
    {
        static bool Down(VirtualKey key) => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        return (Down(VirtualKey.Control) ? 2u : 0u) | (Down(VirtualKey.Shift) ? 4u : 0u)
            | (Down(VirtualKey.Menu) ? 1u : 0u)
            | (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows) ? 8u : 0u);
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
        ClipboardKind.Image => "图片",
        ClipboardKind.Rtf => "富文本",
        _ => (Entry.Content ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ') is var text && text.Length > 160 ? text[..160] + "…" : text,
    };
    public string Subtitle => $"{KindName(Entry.Kind)}  ·  {Entry.SourceApp ?? "未知来源"}  ·  {Entry.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.CurrentCulture)}";
    public static string KindName(ClipboardKind kind) => kind switch
    { ClipboardKind.Text => "文本", ClipboardKind.Image => "图片", ClipboardKind.File => "文件", ClipboardKind.Color => "颜色", ClipboardKind.Rtf => "富文本", _ => "内容" };
}
