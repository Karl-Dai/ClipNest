using System.Diagnostics;
using System.Text.Json;
using ClipNest.Core.Capture;
using ClipNest.Core.Models;
using ClipNest.Core.Persistence;

// Synthetic data only. Each run gets a new directory; no user data is read or removed.
var output = Path.GetFullPath(args.Length > 0 ? args[0] : "dist/perf-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
if (Directory.Exists(output)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(output);
var report = new List<object>();
foreach (var scenario in new[] { ("normal-100", 100, 512), ("normal-1000", 1000, 512), ("normal-10000", 10000, 512), ("stress-50000", 50000, 512), ("large-text-1000", 1000, 65536) })
{
    var (label, count, characters) = scenario;
    var database = new ClipNestDatabase(Path.Combine(output, label + ".db"));
    var store = new SqliteClipboardStore(database);
    using (var connection = database.Open())
    using (var transaction = connection.BeginTransaction())
    using (var command = connection.CreateCommand())
    {
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO clipboard_entries VALUES ($id,$time,'text',$content,NULL,$hash,'Notepad',$favorite)";
        command.Parameters.AddWithValue("$id", "");
        command.Parameters.AddWithValue("$time", 0L);
        command.Parameters.AddWithValue("$content", "");
        command.Parameters.AddWithValue("$hash", "");
        command.Parameters.AddWithValue("$favorite", 0);
        var padding = new string('x', characters);
        for (var index = 0; index < count; index++)
        {
            command.Parameters["$id"].Value = Guid.NewGuid().ToString();
            command.Parameters["$time"].Value = 1700000000000L + index;
            command.Parameters["$content"].Value = $"synthetic-{index:D6} 中文内容 {padding}";
            command.Parameters["$hash"].Value = "synthetic-hash-" + index;
            command.Parameters["$favorite"].Value = index % 10 == 0 ? 1 : 0;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }
    foreach (var (name, query, favorites) in new[] { ("recent", "", false), ("no-match", "definitely-absent", false), ("oldest-match", "synthetic-000000", false), ("favorites", "", true) })
    {
        var measurement = Measure(label, name, 25, () => store.Search(query, favorites).Count);
        report.Add(measurement);
        Console.WriteLine(JsonSerializer.Serialize(measurement));
        var paged = Measure(label, "page-" + name, 25, () => store.GetPage(query, favorites).Items.Count);
        report.Add(paged);
        Console.WriteLine(JsonSerializer.Serialize(paged));
    }
    var write = Measure(label, "insert-and-trim", 25, () =>
    {
        var content = Guid.NewGuid().ToString();
        var result = store.Add(new ClipboardEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, ClipboardKind.Text, content, null, ContentHasher.Hash(content), "Perf"), count);
        return result.OrphanedBlobPaths.Count;
    });
    report.Add(write);
    Console.WriteLine(JsonSerializer.Serialize(write));
    var limit = new SqliteAppSettingsStore(database);
    store.SetHistoryLimitAndTrim(10);
    var afterLimitChange = store.GetRecent(count + 100);
    report.Add(new { Scenario = label, Operation = "retention-change-to-10", RowsImmediatelyAfterSetting = afterLimitChange.Count, NonFavoritesImmediatelyAfterSetting = afterLimitChange.Count(entry => !entry.IsFavorite) });
}

// Reproduce UI-side wait under an explicitly held external write lock; not a normal-use timing.
var lockedDb = new ClipNestDatabase(Path.Combine(output, "forced-lock.db"));
using (var blocker = lockedDb.Open())
{
    using var begin = blocker.CreateCommand();
    begin.CommandText = "BEGIN EXCLUSIVE";
    begin.ExecuteNonQuery();
    var query = Task.Run(() =>
    {
        var stopwatch = Stopwatch.StartNew();
        new SqliteClipboardStore(lockedDb).GetPage("", false);
        return stopwatch.Elapsed.TotalMilliseconds;
    });
    await Task.Delay(1200);
    begin.CommandText = "ROLLBACK";
    begin.ExecuteNonQuery();
    var elapsedMs = await query;
    report.Add(new { Scenario = "forced-lock", Operation = "page-search-during-1200ms-write-lock", QueryElapsedMs = elapsedMs });
}
await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { TimestampUtc = DateTimeOffset.UtcNow, Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, Results = report }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Results: " + Path.Combine(output, "results.json"));

static object Measure(string scenario, string name, int repetitions, Func<int> operation)
{
    for (var warmup = 0; warmup < 3; warmup++) operation();
    var times = new double[repetitions];
    var allocated = GC.GetTotalAllocatedBytes(true);
    var lastResult = 0;
    for (var index = 0; index < repetitions; index++)
    {
        var stopwatch = Stopwatch.StartNew();
        lastResult = operation();
        times[index] = stopwatch.Elapsed.TotalMilliseconds;
    }
    var bytes = GC.GetTotalAllocatedBytes(true) - allocated;
    Array.Sort(times);
    return new { Scenario = scenario, Operation = name, Iterations = repetitions, ResultCount = lastResult, MedianMs = Math.Round(times[repetitions / 2], 3), P95Ms = Math.Round(times[(int)Math.Ceiling(repetitions * .95) - 1], 3), MaxMs = Math.Round(times[^1], 3), AllocatedBytesPerCall = bytes / repetitions };
}
