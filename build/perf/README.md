# Local performance audit

Run from the repository root with .NET 8:

```powershell
dotnet run --project build/perf -c Release -- dist/perf-new-run
```

The output directory must not exist. The runner creates synthetic SQLite databases there and never reads
the user's clipboard, application data or environment-based data-location setting. It does not change
the installed app. Output and databases are ignored by Git; no automatic deletion is performed.

Scenarios cover 100, 1,000 and 10,000 short text entries, a 50,000-entry stress case, and 1,000 entries
with 64 KiB text. Each operation is warmed up three times and measured 25 times against the actual
`SqliteClipboardStore`. Timings include the store's connection acquisition, SQL and result materialization,
but not WinUI rendering, keyboard delivery, disk-cache-cold reads or clipboard conversion.

Managed allocation is process-wide `GC.GetTotalAllocatedBytes` delta divided by repetitions. It is an
allocation rate, not retained memory or proof of a leak. Insertion tests run after the search tests and
may add rows within the requested non-favorite limit; favorites remain excluded from trimming.

The final lock scenario deliberately holds an exclusive SQLite transaction for 1.2 seconds and reports
the query's own duration. With local WAL enabled the reader can finish during that transaction.
This is fault injection, not a normal clipboard write. The retention probe uses the same immediate
limit-and-trim operation as the settings UI. Results are observations, not CI timing assertions.

## Actual Windows runtime tests

Build a separate diagnostic executable; its test-only code is excluded from normal packages:

```powershell
dotnet publish src/ClipNest.App -c Release -r win-x64 --self-contained true -p:ClipNestDiagnostics=true -o dist/diagnostics
python build/perf/seed_runtime.py C:/Test/clipnest-fresh-fixture
./build/perf/run-runtime.ps1 -AppPath dist/diagnostics/ClipNest.exe -DataRoot C:/Test/clipnest-fresh-fixture -OutputDirectory dist/runtime-fresh -Minutes 60 -PauseInstalledApp
```

Fixture generation needs Pillow. Both directories must be new. The runner refuses to test without a
synthetic-fixture marker. It briefly pauses the installed application for the real clipboard phase,
keeps a memory-only backup of readable clipboard formats, and restores it unless newer user content
has replaced the test payload. It then restores the normal application and runs the isolated soak
instance in the background using a separate instance key and a different hotkey.

Tests load all 1,280 fixture rows, find and copy a suffix of a 65,579-character entry, switch 240 large
images plus 80 overlapping selections, and send 100 real clipboard values with a requested 20 ms gap.
Actual send duration and captured count are reported; OS scheduling may make the gap longer.
The soak cycles previews, polls history, writes synthetic records and samples process memory/CPU
every five seconds. It does not periodically overwrite the user's clipboard during the soak.
At the end it trims the isolated history to ten ordinary entries, verifies favorites and image files,
and writes `result.json`. The isolated monitor can also see normal clipboard changes during the soak,
so never publish the fixture database, blobs or application logs; the result report contains no payloads.

The `--diagnostics-ui` flag in diagnostic builds allows a separate manual UI check without starting
the automated workload. Neither diagnostic flag nor its instance-key behavior exists in release builds.

For the separate Windows app measurements and limitations see
[the 2026-10-18 report](../../docs/performance-2026-10-18.md).
