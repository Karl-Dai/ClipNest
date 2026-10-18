// Included only with -p:ClipNestDiagnostics=true; excluded from shipped builds.
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ClipNest.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
using Clipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace ClipNest.App;

public sealed partial class MainWindow
{
    private static readonly JsonSerializerOptions DiagnosticJson = new() { WriteIndented = true };
    internal async Task RunDiagnosticsAsync()
    {
        var output = System.Environment.GetEnvironmentVariable("CLIPNEST_DIAGNOSTICS_OUTPUT")
            ?? throw new InvalidOperationException("Diagnostic output directory required.");
        if (!File.Exists(Path.Combine(_paths.Root, "diagnostic-fixture.json")))
            throw new InvalidOperationException("Refusing diagnostics without an explicit synthetic-data fixture marker.");
        Directory.CreateDirectory(output);
        var minutes = double.Parse(System.Environment.GetEnvironmentVariable("CLIPNEST_SOAK_MINUTES") ?? "60", CultureInfo.InvariantCulture);
        var checks = new List<object>();
        var samples = new List<object>();
        var timings = new List<double>();
        var failures = new List<string>();
        var started = DateTimeOffset.UtcNow;
        var status = "running";
        DataPackage? clipboardBackup = null;
        var clipboardWasEmpty = false;
        var clipboardStreams = new List<InMemoryRandomAccessStream>();
        var missingFormats = 0;
        var clipboardUsed = false;
        try
        {
            await Task.Delay(1000);
            await _refreshTask;
            Check(_totalEntries > 500, "Fixture must contain more than 500 entries");
            for (var attempt = 0; _rows.Count < _totalEntries && attempt < 200; attempt++) await LoadMoreAsync();
            Check(_rows.Count == _totalEntries && _rows.Select(row => row.Entry.Id).Distinct().Count() == _totalEntries, "All history pages accessible without gaps or duplicates");
            checks.Add(new { Name = "all-pages", Loaded = _rows.Count, Total = _totalEntries });

            // Preserve readable original formats in memory only, never in a test report or repository.
            var original = Clipboard.GetContent();
            clipboardWasEmpty = original.AvailableFormats.Count == 0;
            clipboardBackup = new DataPackage();
            foreach (var format in original.AvailableFormats)
            {
                try
                {
                    var data = await original.GetDataAsync(format);
                    if (data is RandomAccessStreamReference reference)
                    {
                        using var stream = await reference.OpenReadAsync();
                        var saved = new InMemoryRandomAccessStream();
                        await RandomAccessStream.CopyAsync(stream, saved);
                        saved.Seek(0);
                        clipboardStreams.Add(saved);
                        data = RandomAccessStreamReference.CreateFromStream(saved);
                    }
                    clipboardBackup.SetData(format, data);
                }
                catch (Exception) { missingFormats++; }
            }
            checks.Add(new { Name = "clipboard-backup", ReadableFormats = original.AvailableFormats.Count - missingFormats, UnsupportedFormats = missingFormats });

            SearchBox.Text = "TAIL-LONGTEXT-0000";
            await AwaitStateAsync(() => !_queryPending && _criteria?.Query == "TAIL-LONGTEXT-0000" && _previewTask.IsCompleted && Selected is not null, "long-text search and preview completed");
            Check(_totalEntries == 1 && Selected?.Content?.EndsWith("TAIL-LONGTEXT-0000", StringComparison.Ordinal) == true, "Search finds suffix beyond the list summary");
            var full = Selected!.Content;
            clipboardUsed = true;
            await CopyOrPasteAsync(false);
            Check(await Clipboard.GetContent().GetTextAsync() == full, "Copy preserves complete long text");
            checks.Add(new { Name = "full-content-copy", Characters = full!.Length });
            SearchBox.Text = "query-that-must-never-win";
            SearchBox.Text = "TAIL-LONGTEXT-0000";
            await AwaitStateAsync(() => !_queryPending && _criteria?.Query == "TAIL-LONGTEXT-0000", "latest query completed");
            Check(_totalEntries == 1 && SearchBox.Text == "TAIL-LONGTEXT-0000", "Latest query wins after cancellation");
            SearchBox.Text = string.Empty;
            await AwaitStateAsync(() => !_queryPending && _criteria?.Query == string.Empty, "unfiltered query completed");
            while (_rows.Count < _totalEntries) await LoadMoreAsync();
            var images = _rows.Where(row => row.Entry.Kind == ClipboardKind.Image).ToArray();
            Check(images.Length >= 20, "Fixture must include at least 20 large images");
            for (var index = 0; index < 240; index++)
            {
                var row = images[index % images.Length];
                var timer = Stopwatch.StartNew();
                HistoryList.SelectedItem = row;
                await AwaitStateAsync(() => Selected?.Id == row.Entry.Id && _previewTask.IsCompleted && PreviewImage.Source is WriteableBitmap, "image preview completed");
                Check(Selected?.Id == row.Entry.Id && PreviewImage.Source is WriteableBitmap, "Image preview matches selected row");
                var bitmap = (WriteableBitmap)PreviewImage.Source;
                Check(bitmap.PixelWidth <= 1024 && bitmap.PixelHeight <= 1024, "Image preview decode remains bounded");
                timings.Add(timer.Elapsed.TotalMilliseconds);
            }
            // Deliberately overlap pending previews; only the final selection may publish.
            for (var index = 0; index < 80; index++) HistoryList.SelectedItem = images[index % images.Length];
            await AwaitStateAsync(() => Selected?.Id == images[79 % images.Length].Entry.Id && _previewTask.IsCompleted && PreviewImage.Source is WriteableBitmap, "last overlapping preview completed");
            Check(Selected?.Id == images[79 % images.Length].Entry.Id && PreviewImage.Source is WriteableBitmap, "Rapid selection drops stale previews");
            checks.Add(new { Name = "large-image-switching", Sequential = 240, Overlapping = 80, CacheEntries = _previewCache.Count });

            var prefix = "ClipNest-fast-" + Guid.NewGuid().ToString("N") + "-";
            var rapid = Stopwatch.StartNew();
            for (var index = 0; index < 100; index++)
            {
                await SetClipboardTextAsync(prefix + index.ToString("D4", CultureInfo.InvariantCulture));
                await Task.Delay(20);
            }
            var sentMs = rapid.Elapsed.TotalMilliseconds;
            var drain = Stopwatch.StartNew();
            ClipboardHistoryPage rapidPage;
            do
            {
                await Task.Delay(100);
                rapidPage = await Task.Run(() => _store.GetPage(prefix, false));
            } while (rapidPage.TotalCount < 100 && drain.Elapsed < TimeSpan.FromSeconds(15));
            checks.Add(new { Name = "rapid-real-clipboard", Sent = 100, Captured = rapidPage.TotalCount, RequestedGapMs = 20, ActualSendMs = sentMs });
            Check(rapidPage.TotalCount == 100, $"Rapid copy preserved {rapidPage.TotalCount}/100 distinct values");

            await RestoreClipboardAsync();
            AppWindow.Hide();
            await WriteProgressAsync("soak", 0);

            var soak = Stopwatch.StartNew();
            var iteration = 0;
            while (soak.Elapsed < TimeSpan.FromMinutes(minutes))
            {
                SearchBox.Text = string.Empty;
                Refresh();
                await _refreshTask;
                var currentImages = _rows.Where(row => row.Entry.Kind == ClipboardKind.Image).ToArray();
                if (currentImages.Length == 0) { await LoadMoreAsync(); currentImages = _rows.Where(row => row.Entry.Kind == ClipboardKind.Image).ToArray(); }
                Check(currentImages.Length > 0, "Images remain reachable during ongoing capture");
                HistoryList.SelectedItem = currentImages[iteration % currentImages.Length];
                var expectedImage = currentImages[iteration % currentImages.Length].Entry.Id;
                await AwaitStateAsync(() => Selected?.Id == expectedImage && _previewTask.IsCompleted && PreviewImage.Source is WriteableBitmap, "soak preview completed");
                Check(PreviewImage.Source is WriteableBitmap && _previewCache.Count <= 4, "Soak preview remains available and cache stays bounded");
                if (iteration % 10 == 0)
                {
                    var text = "ClipNest-soak-" + iteration.ToString(CultureInfo.InvariantCulture) + new string('s', 4096);
                    await Task.Run(() => _store.Add(new ClipboardEntry(Guid.NewGuid(), DateTimeOffset.UtcNow,
                        ClipboardKind.Text, text, null, ClipNest.Core.Capture.ContentHasher.Hash(text), "SyntheticSoak"), 10000));
                }
                using var process = Process.GetCurrentProcess();
                process.Refresh();
                samples.Add(new { Seconds = Math.Round(soak.Elapsed.TotalSeconds, 2), WorkingMiB = process.WorkingSet64 / 1048576d, PrivateMiB = process.PrivateMemorySize64 / 1048576d,
                    ManagedMiB = GC.GetTotalMemory(false) / 1048576d, CpuSeconds = process.TotalProcessorTime.TotalSeconds, CacheEntries = _previewCache.Count, Loaded = _rows.Count, Total = _totalEntries });
                Check(process.PrivateMemorySize64 < 1536L * 1024 * 1024, "Diagnostic memory exceeded 1.5 GiB safety limit");
                iteration++;
                if (iteration % 12 == 0) await WriteProgressAsync("soak", soak.Elapsed.TotalMinutes);
                await Task.Delay(5000);
            }
            checks.Add(new { Name = "continuous-soak", Minutes = soak.Elapsed.TotalMinutes, Iterations = iteration });
            var favoriteCount = (await Task.Run(() => _store.GetPage("", true))).TotalCount;
            await Task.Run(() => { foreach (var blob in _store.SetHistoryLimitAndTrim(10)) _blobs.Delete(blob); });
            var remaining = await Task.Run(() => _store.GetPage("", false));
            var favorites = await Task.Run(() => _store.GetPage("", true));
            Check(remaining.TotalCount - favorites.TotalCount == 10 && favorites.TotalCount == favoriteCount, "Limit save immediately retains exactly ten non-favorites and preserves all favorites");
            var liveImages = _store.GetImageBlobReferences();
            Check(liveImages.All(reference => _blobs.Exists(reference.BlobPath)), "All retained image files still exist");
            checks.Add(new { Name = "immediate-retention", Total = remaining.TotalCount, Favorites = favorites.TotalCount });
            status = "passed";
        }
        catch (Exception ex) { status = "failed"; failures.Add(ex.ToString()); Serilog.Log.Error(ex, "Runtime diagnostics failed"); }
        finally
        {
            try { await RestoreClipboardAsync(); }
            catch (Exception ex) { failures.Add("Clipboard restore failed: " + ex.GetType().Name); status = "failed"; }
            foreach (var stream in clipboardStreams) stream.Dispose();
            timings.Sort();
            await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            {
                Status = status, StartedUtc = started, EndedUtc = DateTimeOffset.UtcNow, RequestedSoakMinutes = minutes,
                Checks = checks, Samples = samples, Failures = failures,
                ImageSwitchP95Ms = timings.Count == 0 ? 0 : timings[(int)Math.Ceiling(timings.Count * .95) - 1],
                ImageSwitchMaxMs = timings.Count == 0 ? 0 : timings[^1]
            }, DiagnosticJson));
            ((App)Application.Current).Shutdown();
        }

        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task RestoreClipboardAsync()
        {
            if (clipboardBackup is null || !clipboardUsed) return;
            var current = Clipboard.GetContent();
            var owned = current.AvailableFormats.Contains("ClipNest.Diagnostics.Marker");
            if (!owned && current.Contains(StandardDataFormats.Text))
                owned = (await current.GetTextAsync()).StartsWith("Synthetic long text 0000", StringComparison.Ordinal);
            // Never overwrite newer clipboard content supplied by the person using the computer.
            if (!owned) { clipboardUsed = false; return; }
            if (clipboardWasEmpty) Clipboard.Clear();
            else
            {
                for (var attempt = 0; ; attempt++)
                {
                    try { Clipboard.SetContent(clipboardBackup); Clipboard.Flush(); break; }
                    catch (System.Runtime.InteropServices.COMException) when (attempt < 15) { await Task.Delay(40); }
                }
            }
            clipboardUsed = false;
        }
        async Task AwaitStateAsync(Func<bool> predicate, string label)
        {
            var timer = Stopwatch.StartNew();
            while (!predicate())
            {
                if (timer.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException(label);
                await Task.Delay(10);
            }
        }
        async Task WriteProgressAsync(string phase, double completedMinutes)
            => await File.WriteAllTextAsync(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(new { Phase = phase, CompletedMinutes = completedMinutes, RequestedMinutes = minutes, Samples = samples.Count, Checks = checks }));
    }

    private static async Task SetClipboardTextAsync(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        package.SetData("ClipNest.Diagnostics.Marker", "ClipNest-runtime-test");
        for (var attempt = 0; ; attempt++)
        {
            try { Clipboard.SetContent(package); Clipboard.Flush(); return; }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 10) { await Task.Delay(10); }
        }
    }
}
