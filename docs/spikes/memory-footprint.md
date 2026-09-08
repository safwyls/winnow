# Spike: where Winnow's memory goes

> **Evidence, not a rule.** This document records how something was measured and is
> never the place to look up what to do. The work it motivates is TASK-152 and its
> subtasks; any rule that comes out of that work belongs in the document that owns it.

**Question:** Winnow reports around 500 MB in Task Manager on the author's library while
Playnite sits near 175 MB. What is the memory, and which parts are ours to reduce?

**Measured 2026-09-07** on the author's machine: Windows 11 Pro 26200, 24 logical CPUs,
AMD GPU (the `amdxx64.dll` user-mode driver), .NET 10.0.11, Avalonia 11.3.20, SkiaSharp
2.88.9. Framework-dependent **Release** build of commit `0d7090a` plus the uncommitted
working tree, run from a scratch output directory. Nothing in the repository was changed
to take these numbers.

## 1. Method

- The real library (`%LOCALAPPDATA%\Winnow`: `winnow.db` 14 MB, 1,039 works, 1,039
  releases, 2,950 cover files / 304 MB) was **copied** to a throwaway directory and every
  run used `--data-dir` against a copy. The real library was never opened.
- Each configuration was launched, left alone at its startup view with no clicks, and
  sampled at 20 s and 60 s (or 30 s and 90 s). Working set, private bytes, thread and
  handle counts come from `System.Diagnostics.Process`; GC figures from
  `dotnet-counters` (`System.Runtime` provider); the region breakdown from a
  `VirtualQueryEx` + `QueryWorkingSetEx` walk; managed types and loader heaps from a
  `dotnet-dump` capture analysed with `eeheap` and `dumpheap -stat`.
- `docs/spikes/memory-footprint.ps1` is the cleaned-up version of the scripts used and
  reproduces the per-run numbers and the region breakdown.
- A bare Avalonia 11.3.20 window (the `avalonia.app` template, Fluent theme, dark
  variant, no Inter font, no diagnostics) was built the same way as a floor for
  "what does the framework cost on this machine".

Terms: **private bytes** is memory only this process can see (heaps, stacks, bitmaps);
**working set** adds the resident pages of DLL images and shared sections, which the OS
shares between processes. Task Manager's default "Memory" column is private working set,
which sits between the two.

Limitations, stated up front:

- No scrolling, no details view, no lightbox. The runs measure the startup state only,
  which is why nothing here reaches 500 MB. Driving the UI needed desktop automation
  that this session was not set up to do. Section 2.5 says why scrolling was expected
  to add 100–180 MB on top of the figures below; §10, added later, drives the window
  from Win32 messages and measures what it actually added.
- Framework-dependent Release, not the self-contained package `packaging/` ships. The
  published build is larger on disk and may JIT differently; TASK-152.4 measures it.
- One machine, one library, single samples. Repeat runs of the same configuration
  agreed within about 15 MB; treat differences smaller than that as noise.
- Playnite was not measured here; the 175 MB figure is the author's observation.

## 2. Results

### 2.1 Launch matrix

Private bytes and working set in MB. "sync" means the normal startup pipeline in
`Program.cs` ran (local scans, remote backfill, enrichment, facets, maturity, reception,
soft match, update poll, merge queue, storefront sync). `--no-sync` skips all of it.

| Configuration | t | Private | Working set | Threads | GC committed |
|---|---|---|---|---|---|
| Bare Avalonia window | 15 s | 119–137 | 122–135 | 62 | 3.0 |
| Winnow, empty library, `--no-sync` | 30 s / 90 s | 143.6 / 142.1 | 191.7 / 189.9 | 69 / 66 | 27.0 / 24.0 |
| Winnow, empty library, sync | 20 s / 60 s | 242.6 / 234.7 | 307.2 / 310.3 | 91 / 80 | 67.8 / 70.5 |
| Winnow, real library, `--no-sync` | 30 s / 90 s | 263.0 / 241.9 | 312.8 / 299.7 | 70 / 67 | 78.8 / 59.3 |
| Winnow, real library, sync | 20 s / 60 s | 288.9 / 275.2 | 361.2 / 354.7 | 83 / 73 | 97.2 / 89.9 |
| Real library, covers directory absent | 20 s / 60 s | 292.8 / 261.7 | 369.6 / 345.4 | 93 / 80 | 93.8 / 69.4 |
| Real, `DOTNET_GCgen0size=0x1000000` | 20 s / 60 s | 289.6 / 264.3 | 363.7 / 344.0 | 82 / 71 | 92.9 / 76.0 |
| Real, `DOTNET_gcConcurrent=0` | 20 s / 60 s | **409.2** / 310.0 | 488.2 / 388.4 | 84 / 73 | 213.5 / 117.5 |
| Real, `DOTNET_GCConserveMemory=9` | 20 s / 60 s | 272.6 / 261.5 | 348.8 / 341.1 | 84 / 73 | 72.2 / 73.1 |
| Real, `DOTNET_GCHeapHardLimit=0x8000000` | 20 s / 60 s | 289.2 / 268.8 | 361.6 / 349.5 | 84 / 73 | 98.3 / 85.7 |

Reading the table as a stack, private bytes at rest:

| Layer | Private bytes | Evidence |
|---|---|---|
| Framework floor: .NET runtime, Avalonia, Skia, ANGLE/D3D11, WinUI composition | ~120–137 | bare window |
| Winnow's shell on top of that (host, DI, every pane realized, theme) | ~5–20 | empty `--no-sync` minus bare |
| **The library the pipeline imports, plus its own working set** | **~95–100** | empty sync minus empty `--no-sync`. Not residue: §6.1 shows a pristine directory reaches 707 works within a second of launch, and the same library relaunched with `--no-sync` costs more than the sync run that built it |
| Real library at rest (data, view models, first screen of covers, JIT for those paths) | ~100 | real `--no-sync` minus empty `--no-sync` |
| Everything above, on the real library with sync | ~275 | measured |

GC knobs are not the lever: gen0 size and a hard limit changed nothing, conserve-memory
saved about 14 MB, and turning off concurrent GC made the startup peak 120 MB worse.

### 2.2 Region breakdown (resident MB)

`Image` = DLL code and data (shared with other processes). `Mapped` = pagefile-backed
and file-backed sections; for a .NET process the unnamed part is mostly the runtime's
double-mapped JIT code and executable loader heaps. `Private` = everything only this
process owns.

| Run | Image | Mapped | Private | of which RW data (prot 4) | NT heap segments (multi-region `4,1` blocks) |
|---|---|---|---|---|---|
| Bare Avalonia window | 51.6 | 17.0 | 48.2 | 33.9 | ~3 |
| Empty library, `--no-sync` | 70.7 | 38.4 | 82.7 | 68.4 | ~16 |
| Empty library, sync | 79.5 | 60.8 | 182.5 | 168.1 | ~51 |
| Real library, `--no-sync` | 73.0 | 50.0 | 180.8 | 164.4 | (not fully captured) |
| Real library, sync | 80.5 | 65.2 | 204.9 | 188.5 | ~74 |

Three things stand out.

- **Native heap growth, and there are bitmaps after all.** The empty library with sync
  carries about 51 MB of NT heap segments against 16 MB for the same library without sync
  and 3 MB for the bare window. §6.1 corrects the reading: that run imports 707 works and
  fetches their covers, so it does have bitmaps. §6.2 measured the candidates — pooled
  `Microsoft.Data.Sqlite` page caches are not the cost, and decoded cover pixels track it.
- **JIT code and loader heaps.** Mapped resident goes from 17 MB (bare) to 38 MB (shell)
  to 61–65 MB (pipeline ran). In the real-library dump the loader heaps total 44.4 MB
  (low-frequency 16.9, high-frequency 22.9, fixup precode 4.0) and the JIT code heaps
  16.7 MB. Nothing is ReadyToRun-compiled except the framework itself; Avalonia,
  SkiaSharp, the toolkit and Winnow's own 3.3 MB assembly are all jitted on every launch.
- **The GC keeps what the pipeline allocated.** GC committed is 24 MB for the empty
  library without sync and 70 MB with it; the real-library dump shows 58.8 MB allocated
  against 63.1 MB committed at 30 s, while the counters read 90–97 MB committed at 20 s.
  The pipeline's allocation burst, not the library, sets the committed size.

Image-backed memory is not ours: `amdxx64.dll` maps 44.8 MB (4.5 resident),
`System.Private.CoreLib` 15.3 (9.6 resident), `libSkiaSharp` 9.0 (3.5), `av_libglesv2`
5.2 (3.5), `D3DCompiler_47` 4.5 (1.9). Loading is lazy and correct: `AngleSharp`,
`Microsoft.Web.WebView2.Core` and `WebView2Loader` are not in the module list at startup,
and no `msedgewebview2.exe` child exists until a sign-in window opens.

Threads: 62 in the bare window, 66–69 for the shell, 73–97 during and after the
pipeline. Only 12 have managed stacks (UI, render, compositor, timer, thread-pool
gate/wait/IO); the rest belong to the AMD driver, D3D, DirectWrite and WinRT composition.
At roughly 100 KB touched per stack the thread count is worth about 8–10 MB.

### 2.3 Managed heap (real library, 30 s after launch)

58.8 MB allocated, 657,455 objects. Top types by retained bytes:

| MB | Count | Type |
|---|---|---|
| 4.32 | 40,473 | `Avalonia.Styling.StyleInstance` |
| 4.25 | 40,764 | `AvaloniaPropertyDictionary<IValueEntry>+Entry[]` |
| 3.50 | 3,989 | `System.Byte[]` |
| 3.47 | 26,734 | `Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExpression` |
| 3.09 | 36,858 | `System.String` |
| 2.06 | 44,898 | `Avalonia.Styling.Activators.StyleClassActivator` |
| 1.83 | 314 | Free (LOH fragmentation, 3.3 MB by the counters) |
| 1.80 | 24,422 | `IValueEntry[]` |
| 1.37 | 1,519 | `ServerCompositionDrawListVisual` |
| 1.01 | 2,731 | `HashSet<IClassesChangedListener>+Entry[]` |
| 0.72 | 31,342 | `WeakReference<AvaloniaObject>` |
| 0.72 | 13,525 | `System.Diagnostics.ThreadInfo` |
| 0.40 | 965 | `Winnow.App.ViewModels.GameTileViewModel` |

- **Avalonia styling and property storage dominate**, not Winnow's data. 5,603 value
  stores means about 5,600 live `AvaloniaObject`s; every pane in `MainWindow.axaml` is
  realized at startup behind `IsVisible`, including the 1,864-line details view, and
  `controls.axaml` binds every token through `DynamicResource` by design. Together these
  Avalonia types are roughly 20 MB of the 59 MB heap. (TASK-152.3 changed the first half
  of that: see §9.)
- **Winnow's own projection is small.** 965 tile view models cost 0.40 MB; the whole
  `Work`/`ReleaseFacets`/`Facet` dictionaries are a few hundred KB. The comment at the top
  of `LibraryViewModel.cs` ("a few hundred kilobytes of projection") is about right.
- **238 `WriteableBitmap`s** were alive: 119 cover pairs (vivid plus floor) for the
  realized wall tiles and feed cards. Their pixels live on the native heap, not here.
- **648 `Process` and 13,525 `ThreadInfo` objects** are the last `Process.GetProcesses()`
  snapshot from `SystemProcessSource` (5 s poll). They are garbage, not a leak, but the
  poll materializes every thread of every process on the machine each tick.
- The 8,146 `Dictionary<long, ...>` entries and 6,246 `List<long>` are facet ids kept
  once in `TileFacets` and once merged in `FilterableRow`; small, noted for completeness.

### 2.4 The startup pipeline, from the diagnostic log

One launch on the real library logged, inside the first five seconds: the local library
sync (Steam, Epic and GOG scans plus resolution) **four times**, the feed scored **four
times**, the expansion scan twice, and a second full snapshot for the merge queue while
its pane was hidden. `LocalLibrarySyncService` is also triggered by
`SnapshotSchedulerService`, the two install-refresh services and the startup task, and
each `RefreshLibraryAsync` re-runs the recommender's full read. None of it is retained;
it is the allocation burst that decides the GC's committed size (section 2.2) and it is
what made the non-concurrent GC run peak at 409 MB.

### 2.5 The cover pipeline, from the code

What is on disk (sampled with `file`): Steam sources 600×900 JPEG, IGDB covers 528×704,
IGDB screenshots 1280×720 with a 640×360 floor variant; 2,950 files, 304 MB, average
103 KB. Disk is not a memory concern.

What is in memory, per displayed cover: two `WriteableBitmap`s (vivid and dormancy
floor), decoded at the width bucket ≥ `displayWidth × RenderScaling` from
`[160, 240, 320, 480, 640, 1280]`. A 148-DIP tile at 100 % DPI lands in the 240 bucket
(240×360×4×2 = 691 KB); at 200 % DPI in the 480 bucket (2.76 MB); a lightbox screenshot
at 1280 costs 7.4 MB. The wall realizes about 55–65 tiles plus one buffer row; the feed
holds 30 cards with their own leases; both keep their leases while hidden.

`DecodedLru` is byte-bounded at 128 MiB and reports pressure to the GC, but evicted
bitmaps are dropped, not disposed, so their native pixels return only when a gen-2 GC
finalizes them. The floor layer is decoded and cached for every consumer, including
those that draw only the vivid layer (details, screenshots, lightbox, IGDB rows, editor
previews) and even when the display ramp does not dim. The details cover, screenshot
thumbnails, merge sides, IGDB rows and editor previews hold raw `Bitmap` references
outside the lease pool. The merge queue requests covers for every row of a
non-virtualized `ItemsControl`. Decode concurrency is unbounded (`Task.Run` per slot);
only fetch concurrency is capped. The RECOMMENDATION section of
`docs/spikes/avalonia-dormancy-rendering.md` already estimated 13–30 MB for 100 visible
tiles and asked for the two-layer approach to be revisited if profiling showed the
doubled bitmap memory mattered; the real cost is higher because a 148-DIP tile decodes
at the 240 bucket, not at 148.

Expected effect of scrolling, which this spike did not measure: the LRU fills to
128 MiB (about 185 covers at the 240 bucket), the leased screenful sits on top of it,
evicted bitmaps linger until the next gen-2, and at 200 % DPI one screenful alone
exceeds the budget. That is the 100–180 MB between the 350 MB measured here and the
500 MB observed in use.

§10 measured it, and this section describes the pipeline as it was before that work:
every cost named above still reads correctly as the "before" column.

## 3. What is already done well

- The wall is a real virtualizing panel with one-row overscan, container recycling that
  provably releases art and leases, and a trimmed spare pool.
- Covers decode at display resolution with the JPEG codec's n/8 scaling, never upscale,
  dispose every transient `SKBitmap`, de-duplicate in-flight loads, and share one lease
  per surface. Downloads stream with a 16 MiB cap; negatives are cached.
- One tile instance is shared by the grid, list, feed and details; filtering and sorting
  reuse instances. Explicit column lists everywhere; the snapshot is one connection, one
  transaction, seven result sets. Details, screenshots, history and update events load
  on demand and the details view model is dropped on close.
- Event subscriptions are paired; the ramp has one subscription instead of one per tile.
- WebView2 is created per sign-in window and destroyed with it; the Chromium processes
  never exist at rest. AngleSharp documents are disposed and the assembly does not load
  until a store page is parsed. Enrichment caches are SQLite-backed; the in-memory
  variants are test-only. Ingest keeps no parsed VDF or JSON trees.
- Serilog is a single unbuffered rolling file sink at Information; fonts are six files,
  780 KB.

## 4. Ranked opportunities

Each row is a subtask of TASK-152. Savings are order-of-magnitude estimates from the
numbers above, not promises.

| # | Opportunity | Where the number comes from | Estimated saving | Task |
|---|---|---|---|---|
| 1 | Return the GC regions the pipeline's allocation burst commits, on an interval while it runs and once when it ends | §6.3, measured before and after | ~19 MB private while the pipeline runs, 91 MB at the moment it ends, and nothing durable until rows 2 and 5 land | TASK-152.1 (done) |
| 2 | Cover cache: vivid-only requests, dispose on eviction, smaller or DPI-derived budget, bounded decode concurrency, no eager merge-queue decodes | §2.5; 238 bitmaps at rest; 128 MiB budget when scrolled | 60–120 MB when scrolled, 20–40 at rest | TASK-152.2 |
| 3 | Construct hidden panes lazily; reduce per-control styling cost | 40k `StyleInstance`, 27k `DynamicResourceExpression`, ~5,600 live controls | 10–25 MB managed plus composition visuals; faster startup | TASK-152.3 |
| 4 | ReadyToRun publish; `System.GC.ConserveMemory` | 44 MB loader heaps + 17 MB JIT code; conserve trial saved 14 MB | 15–40 MB | TASK-152.4 |
| 5 | Do each startup read once: one local scan, one feed pass, merge queue on demand, cheaper hidden-count queries, pid-only process polling | §2.4; 13.5k `ThreadInfo` per poll | Lowers the peak that sizes the GC heap; 10–30 MB committed | TASK-152.5 |
| 6 | Small, safe: `PRAGMA cache_size`, `SqliteConnection.ClearPool` after the burst; drop `summary` / `background_url` from the snapshot works read; skip the redundant floor-file existence read in `CoverPipeline.TryDecodeFromDisk` | code reading, then §6.2 for the two SQLite items | the two SQLite items measured at no detectable saving; the two cover items are still unmeasured | folded into 152.1 / 152.2 |

What is not ours: about 120–137 MB is the price of Avalonia + Skia + ANGLE/D3D11 + the
runtime on this machine, and Playnite (WPF on .NET, sharing the OS's own rendering
stack) does not pay most of it. A realistic floor for Winnow with this stack is roughly
170–200 MB at rest; below that means changing the rendering backend, which nothing
here recommends.

## 5. Reproducing

```powershell
# Build to a scratch path so a running app does not lock the output.
dotnet build src/Winnow.App/Winnow.App.csproj -c Release -p:BaseOutputPath=C:\Temp\winnow-mem\build\

# Copy the library first; never point a measurement at the real one.
Copy-Item "$env:LOCALAPPDATA\Winnow\winnow.db" C:\Temp\winnow-mem\data\
Copy-Item "$env:LOCALAPPDATA\Winnow\covers" C:\Temp\winnow-mem\data\covers -Recurse

./docs/spikes/memory-footprint.ps1 -Exe C:\Temp\winnow-mem\build\Release\net10.0\Winnow.exe -DataDir C:\Temp\winnow-mem\data -Label real
./docs/spikes/memory-footprint.ps1 -Exe C:\Temp\winnow-mem\build\Release\net10.0\Winnow.exe -DataDir C:\Temp\winnow-mem\data -NoSync -Label real-nosync
```

`dotnet-counters`, `dotnet-gcdump`, `dotnet-dump` and `dotnet-stack` were installed with
`dotnet tool install --tool-path <scratch> <tool>`; pass that path as `-ToolPath`. For
the managed breakdown: `dotnet-dump collect -p <pid>` then
`dotnet-dump analyze <dmp> -c "eeheap -gc" -c "eeheap -loader" -c "dumpheap -stat"`.

## 6. Follow-up: TASK-152.1

**Measured 2026-09-07**, same machine and method, framework-dependent **Release** build of
branch `mem/152-1`, sampled at 30 s and 90 s with `memory-footprint.ps1`. Every run used a
throwaway `--data-dir`; the real library was never opened. Two corrections to §2 come
first, because they change what the remaining work is.

### 6.1 An "empty library with sync" is not an empty library

The startup pipeline imports whatever the launchers hold. A **pristine** data directory on
this machine reaches **707 works** (626 Steam, 67 Epic, 14 GOG) 0.6 s after launch, and
enrichment, reception and update polling are all still running at 90 s. So §2.1's row did
not measure what stays behind after the pipeline: it measured a 707-work library, with the
covers the run had fetched so far, against an empty one.

This also means the earlier runs are not repeatable in place. A directory that has had one
sync run is a populated library, and measuring "empty with sync" twice in the same
directory measures two different things. Every run below resets the directory first.

Pricing that library: relaunch the directory the sync run left, with `--no-sync`, so the
same rows and cover files are loaded and no pipeline runs.

One figure per run, all runs shown, so the spread is visible. Private, GC committed and
NT heap are committed MB; mapped resident is resident MB, as in §2.2.

| Configuration (90 s) | Private | GC committed | NT heap | Mapped resident |
|---|---|---|---|---|
| Empty library, `--no-sync` | 143.9 / 149.0 | 24.0 / 27.5 | 12.7 / 14.9 | 38.2 / 38.2 |
| Empty library, sync | 239.1 / 246.4 / 246.8 | 74.5 / 79.3 / 78.0 | 43.4 / 44.5 / 39.4 | 68.6 / 68.6 / 67.6 |
| The library that run built, `--no-sync` | 255.5 / 306.0 | 65.5 / 97.2 | 69.0 / 84.4 | 51.4 / 57.2 |

The pipeline run is **9–67 MB lower** in private bytes than the same library at rest,
because at 90 s it has decoded fewer covers than a relaunch finds already on disk. There
is no ~95 MB of residue to return. Comparing the sync run against the same library at
rest, the only difference that survives the noise is **+14 MB of mapped resident** — the
JIT and loader heaps for the pipeline's own code paths, which is TASK-152.4's row. Its GC
committed (74–79 against 66–97) and its native heap (39–45 against 69–84) are not higher
at all.

### 6.2 Where the sync run's extra private bytes are

`memory-footprint.ps1`'s region walk separates them. The largest single `prot=4` private
block is the GC heap — it matches `dotnet-counters`' committed figure to 0.1 MB in every
run, which is what makes the split trustworthy. Multi-region `1,4` blocks are NT heap
segments. Committed MB:

| Bucket | Empty, `--no-sync` | Empty, sync | Attributed to |
|---|---|---|---|
| GC heap block | 24–28 | 74–79 | the pipeline's allocation burst; §6.3 returns part of it |
| NT heap segments | 13–15 | 39–45 | **not** SQLite: see below |
| Mapped resident (JIT code, loader heaps) | 38 | 68 | TASK-152.4 |
| Everything else private (stacks, other) | 96 | 108–117 | +10–17 threads, and the rest unattributed |

Three hypotheses were measured rather than argued:

- **Pooled SQLite page caches are not the native heap.** `PRAGMA cache_size` was set to
  200 KiB per connection, against SQLite's 2 MiB default, for a whole sync run: NT heap
  came out at 45.1 MB against 39.4–44.5 MB unchanged — no effect. Instrumenting the
  factory explains why: the pipeline opens **14,063 connections in 90 s** but never more
  than **10 at once**, so at most ~20 MB of page cache can ever be live, and in practice
  these queries never fill it. Stage timings were unchanged (local sync 0.48–0.63 s
  against 0.31–0.61 s; feed 15–85 ms against 22–118 ms).
- **Decoded cover pixels track the native heap.** Not proven here, but it is the only
  candidate that moves with it: the same library at rest, with every cover already on
  disk, carries 69–84 MB of NT heap against the sync run's 39–45 MB and the empty
  library's 13–15 MB. SkiaSharp's pixels are native. That is TASK-152.2's row.
- **The session watcher's process poll is part of the gap, not part of the pipeline.**
  `--no-sync` disables `SessionWatcherService` outright, so its 5-second
  `Process.GetProcesses()` — 648 `Process` and 13,525 `ThreadInfo` objects per tick,
  §2.3 — never runs in the baseline it is being compared against. Disabling it in a sync
  run, changing nothing else, gave 229.2 MB private and 62.4 MB GC committed against
  246.8 / 78.0: about **17 MB private and 16 MB GC committed**. That is TASK-152.5's
  pid-only polling, and it is the largest single item left in this gap.

How much to trust a single run: GC committed in the sync configuration read 55–79 MB
across the nine runs behind this section, and private bytes 224–252, with no change to the
build in six of them. The region-walk buckets are much steadier than the totals — NT heap
stayed inside 39–45 MB in every unmodified sync run — so a bucket that does not move is
solid evidence, while a total that moves by less than about 15 MB in one run is not. The
one-run figures for the session watcher and for `cache_size` were both taken late in the
sequence, when the whole set was drifting low; the watcher's 17 MB should be re-measured
interleaved before TASK-152.5 leans on it.

One more thing the traces show, for TASK-152.5: `--no-sync` does not stop the local scans.
A `--no-sync` run still logged three full local library syncs and five feed scorings, from
the install-refresh services rather than the startup task.

### 6.3 What changed, and what it returns

`StartupMemoryTrim` releases the pooled SQLite connections and forces one blocking,
compacting, `Aggressive` gen-2 collection — every 20 s for as long as the startup pipeline
runs, and once when it ends. `SqliteConnectionFactory` also bounds each connection's page
cache at 256 KiB. No pipeline stage, query or fetch changed.

Three interleaved pairs on a pristine library, base build then changed build, so a drift in
machine state hits both arms:

| Pair | Private, base → changed | GC committed | GC heap block | NT heap | Mapped resident |
|---|---|---|---|---|---|
| 1 | 251.7 → 222.5 | 79.2 → 60.2 | 79.2 → 61.3 | 55.3 → 41.8 | 66.1 → 68.5 |
| 2 | 245.1 → 217.6 | 75.8 → 51.9 | 75.8 → 51.9 | 45.7 → 44.4 | 69.2 → 69.1 |
| 3 | 231.8 → 232.7 | 66.7 → 70.8 | 66.7 → 70.8 | 47.5 → 40.8 | 68.4 → 67.4 |
| mean | **242.9 → 224.3** | **73.9 → 61.0** | 73.9 → 61.3 | **49.5 → 42.3** | 67.9 → 68.3 |

By bucket, at 90 s: **GC committed slack −12.9 MB**, **native heap −7.2 MB** (the direction
held in all three pairs), **JIT code unchanged**, which is right — that bucket is
TASK-152.4's and nothing here touches it. Private bytes −18.6 MB.

Pair 3 returned nothing, and the trim log says why. Each trim is effective and the pipeline
undoes it: across the three runs every trim took GC committed to 35–40 MB, from 50–76 MB,
and gave back 16–41 MB of private bytes —

```
Startup memory trim: private 249->208 MB, GC committed 76->35 MB.
Startup memory trim: private 242->213 MB, GC committed 64->35 MB.
Startup memory trim: private 225->206 MB, GC committed 53->35 MB.
Startup memory trim: private 239->204 MB, GC committed 70->36 MB.
```

— and then enrichment re-commits over the next 20 s. A sample lands somewhere on that
sawtooth: pair 3's landed at the top of it. So the honest claim for a **first-run** library
is that the peak between trims is unchanged and the average is ~19 MB lower, not that the
number at any given second is.

Where the trim sticks is when the pipeline actually finishes, which a first run does not do
inside 90 s: 707 works of enrichment, reception and update polling take minutes. A second
sync run over the library the first one built is cache-warm and finishes in under 20 s, so
the end-of-pipeline trim is the only one that fires, and it is the largest single number in
this section:

```
Startup memory trim, pipeline finished: private 362->271 MB, GC committed 143->49 MB.
```

That is the moment the pipeline stops: 91 MB of private bytes and 94 MB of GC committed
handed back at once, on a library of 707 works with covers on disk.

**It does not stay handed back**, and this is the result that qualifies the rest of the
section. Two interleaved warm pairs, sampled 180 s in — three minutes after a pipeline that
finished in under twenty seconds:

| Pair | Private, base → changed | GC committed |
|---|---|---|
| 1 | 289.3 → 279.0 | 83.4 → 88.6 |
| 2 | 259.8 → 262.1 | 67.7 → 70.6 |

No durable saving: −10.3 MB and +2.3 MB, inside the noise. The GC re-commits what the trim
returned within those three minutes, and it is not the pipeline doing it — the pipeline is
finished. It is the wall still decoding covers (TASK-152.2) and the session watcher
allocating 648 `Process` and 13,525 `ThreadInfo` objects every five seconds (TASK-152.5).

So what this change does, measured: it lowers the process's memory **while the startup
pipeline runs**, by about 19 MB on average on a first run, and hands back 91 MB at the
moment the pipeline ends. What it does not do is lower where the app settles, because what
holds that level up is the at-rest allocation those other two subtasks own. Once they land,
this trim is what stops the pipeline's peak from becoming the resting level; on its own it
does not.

### 6.4 Limits, and what TASK-152.1 did not do

- **The task's first acceptance criterion is not met and cannot be, as written.** "Empty
  library with sync within 20 MB of the same library with `--no-sync`" compares a process
  holding 707 imported works against one holding none. Measured with the same build,
  interleaved: 224.3 MB against 149.1 MB, a 75 MB gap, down from 96 MB. Of what is left,
  §6.1 and §6.2 put ~50 MB on the library itself, ~14 MB on JIT for the pipeline's paths
  (TASK-152.4) and ~17 MB on the session watcher's poll (TASK-152.5). The criterion worth
  keeping is the one in §6.3: the sync run against the same library at rest.
- **Bounding the page cache is a ceiling, not a saving.** Kept because it bounds a
  per-connection cost that scales with concurrency, but it returned nothing measurable here
  (§6.2) and should not be counted as part of the −18.6 MB.
- **The trim has no durable effect on its own**, for the reason §6.3 ends with. If the
  reviewer's bar is "the number a user sees at rest goes down", this change does not clear
  it by itself and TASK-152.2 and TASK-152.5 have to land first. The evidence for keeping it
  is that it removes the pipeline's contribution at the transition; the evidence against is
  two warm pairs that show no lasting difference. Both are above.
- **Not attributed:** the ~12 MB of "everything else private" the sync run adds beyond
  threads, and whether the −7.2 MB of native heap comes from the pool release or from the
  finalizer pass returning cover pixels. Splitting those needs a heap-by-allocation-site
  tool this session did not have.
- **Noise is large and the machine was shared.** Three other agents were running their own
  Winnow measurements throughout; one run was killed by another agent's cleanup and is
  reported as such rather than dropped silently. Interleaved pairs are the only comparisons
  here worth trusting; the single-knob runs in §6.2 are indicative.
- **14,063 connection opens in 90 s**, peak 10 concurrent, is a repository-per-call read
  pattern rather than a memory problem, but it is a number TASK-152.5 will want.

## 7. Follow-up: TASK-152.4

**Question:** row 4 of the ranked-opportunities table estimated 15–40 MB from
`PublishReadyToRun` and `System.GC.ConserveMemory`, against the 44 MB of loader heaps and
17 MB of JIT code heaps §2.2 found in a framework-dependent build. Does that hold for the
self-contained package `packaging/Publish.ps1` actually ships, and does
`PublishReadyToRunComposite` or `System.Runtime.TieredPGO=false` add anything on top?

**Measured 2026-09-07**, same machine and same copied real library as §1, against
`packaging/Publish.ps1` self-contained win-x64 publishes of commit `0d7090a` (this task's
own packaging and csproj edits carried into the publish, application code otherwise
unchanged). Two runs per configuration, `--no-sync`, sampled at 30 s and 90 s, exactly as
in §1; one run of the adopted configuration with the normal startup pipeline (no
`--no-sync`) to show the real launch, sampled at 20 s and 60 s to match §2.1's sync rows.
`GCConserveMemory` and `TieredPGO` trials used the environment-variable overrides the
runtime already respects (`docs/spikes/memory-footprint.ps1 -Env @{ ... }`), so those two
knobs did not need a separate publish per value; `PublishReadyToRun` is a publish-time
decision and needed one publish per variant.

### 7.1 Memory

| Configuration | t | Private | Working set | GC committed | Mapped resident |
|---|---|---|---|---|---|
| Baseline (current shipped settings) | 30s/90s | 277.5 / 270.8 | 328.5 / 328.6 | 98.0 / 96.9 | 47.7 |
| Baseline, run 2 | 30s/90s | 259.5 / 252.8 | 310.5 / 310.8 | 74.9 / 74.9 | 47.8 |
| `PublishReadyToRun=true` | 30s/90s | 283.0 / 268.3 | 330.4 / 322.7 | 97.0 / 89.3 | 35.7 |
| R2R, run 2 | 30s/90s | 265.5 / 265.3 | 320.5 / 320.7 | 86.4 / 86.4 | 36.5 |
| R2R + `PublishReadyToRunComposite=true` | 30s/90s | 266.5 / 267.4 | 324.0 / 326.1 | 85.7 / 86.1 | 34.0 |
| R2R + Composite, run 2 | 30s/90s | 278.6 / 243.5 | 336.5 / 302.1 | 93.2 / 62.0 | 33.6 |
| R2R + `DOTNET_GCConserveMemory=5` | 30s/90s | 268.5 / 261.8 | 317.7 / 318.0 | 85.1 / 85.1 | 36.6 |
| R2R + ConserveMemory=5, run 2 | 30s/90s | 265.6 / 255.2 | 316.8 / 311.5 | 75.7 / 72.4 | 37.6 |
| R2R + `DOTNET_GCConserveMemory=9` | 30s/90s | 246.3 / 242.1 | 303.5 / 299.8 | 65.2 / 61.2 | 38.2 |
| R2R + ConserveMemory=9, run 2 | 30s/90s | 252.9 / 248.2 | 304.0 / 306.3 | 63.1 / 64.2 | 39.7 |
| R2R + `DOTNET_TieredPGO=0` | 30s/90s | 275.4 / 269.2 | 320.9 / 320.5 | 88.7 / 88.7 | 33.8 |
| R2R + TieredPGO off, run 2 | 30s/90s | 261.9 / 255.1 | 308.6 / 308.4 | 74.4 / 74.4 | 34.8 |
| **Adopted: R2R + ConserveMemory=9 baked in**, `--no-sync` | 30s/90s | 260.1 / 245.6 | 315.6 / 301.2 | 78.7 / 62.9 | 37.9 |
| Adopted, run 2 | 30s/90s | 270.7 / 285.3 | 328.0 / 330.4 | 89.1 / 88.0 | 39.0 |
| **Adopted, real launch** (sync, matches §2.1's sync rows) | 20s/60s | 281.4 / 270.3 | 353.4 / 347.2 | 71.5 / 75.0 | 51.7 |

"Mapped resident" is the same region-breakdown line §2.2 used to place the runtime's
double-mapped JIT code and loader heaps: it drops from **~47.7–47.8 MB at baseline to
~35.7–36.5 MB under plain R2R**, the clearest and most consistent effect of any setting
tried here — every R2R run, regardless of GC knob, lands in the 33.6–39.7 MB band, against
a baseline that never goes below 47.7. `PublishReadyToRunComposite` shaves another ~2 MB
off that (33.6–34.0 MB) but is noise-sized next to the +28 MB it costs in package size
(§7.2) and was not adopted.

Private bytes and GC committed are noisier — run-to-run agreement is closer to 15–20 MB
than the ~15 MB §1 found, plausibly because these are self-contained publishes (more
assemblies to fault in) rather than the framework-dependent build §1 used throughout.
Within that noise, `ConserveMemory=9` on top of R2R is the one setting that consistently
separates from plain R2R: private bytes at 242–253 MB against R2R's 265–283 MB (roughly
20–30 MB), and GC committed at 61–65 MB against R2R's 86–97 MB. `ConserveMemory=5` lands
in between (255–269 MB private) but close enough to plain R2R that it is not worth a
separate knob when 9 does better. `TieredPGO=0` (275.4/269.2 and 261.9/255.1 MB private)
sits inside the same band as plain R2R and was not adopted — disabling PGO trades steady-
state JIT quality for no measured memory gain here.

The adopted configuration's own `--no-sync` runs (260.1/245.6 and 270.7/285.3 MB private)
land inside the same range as the `DOTNET_GCConserveMemory=9` trial that motivated it,
confirming the runtimeconfig setting behaves the same as the environment-variable
override used to find it. The real-launch run is highest across the board (281.4/270.3 MB
private, 51.7 MB Mapped resident) because the startup pipeline itself allocates and JITs
its own code paths, exactly as §2.1 and §2.2 describe for the framework-dependent build;
R2R does not precompile that pipeline's diagnostics- and enrichment-specific code any
more effectively than everything else already covered.

### 7.2 Startup time and package size

Startup is wall-clock from `Start-Process` to `Process.MainWindowTitle` first reporting a
value, polled every 100 ms, `--no-sync`. The first run after a fresh publish is
consistently slower — most likely Windows Defender scanning the newly written
executable — so both runs are reported rather than picking one as "the" number.

| Configuration | Startup, run 1 | Startup, run 2 | Published size | `Winnow.dll` |
|---|---|---|---|---|
| Baseline | 3,397 ms | 1,992 ms | 115.8 MB | 3.35 MB |
| R2R | 2,362 ms | 990 ms | 139.3 MB | 7.40 MB |
| R2R + Composite | 2,953 ms | 739 ms | 167.3 MB | 3.37 MB |
| Adopted (R2R + ConserveMemory=9) | 1,972 ms | 1,669 ms | 139.3 MB | 7.40 MB |

R2R's second-run startup is about half the baseline's (990 ms against 1,992 ms), the
second clearest effect measured here — plausible, since a plain build JITs Avalonia,
SkiaSharp, the toolkit and Winnow's own assembly on every launch (§2.2) and R2R ships
that code precompiled. Composite goes further still (739 ms) at a real package-size cost.
The adopted configuration's own runs (1,972 / 1,669 ms) do not reproduce R2R's full
second-run improvement; nothing distinguishes its build from plain R2R's other than
`ConserveMemory`, which should not affect JIT at all, so this is read as machine noise
from running startup timing after the memory trials rather than a property of the
setting — a single pair of runs is not enough to separate that from a real effect, and a
future pass with more repetitions should treat this number as unconfirmed.

`Winnow.dll` size is unaffected by `ConserveMemory` (a runtime config value, not a
compilation setting) and by Composite (which moves code into a combined R2R image rather
than growing the app's own assembly); it grows under plain, non-composite R2R because
that mode embeds native code for the app's own IL directly in `Winnow.dll` alongside the
managed metadata.

### 7.3 Decision

**Adopted: `-p:PublishReadyToRun=true` for the win-x64 publish, and
`System.GC.ConserveMemory=9`.** Both changes are in `packaging/Publish.ps1` and
`src/Winnow.App/Winnow.App.csproj` respectively, each commented with the numbers above.
R2R is scoped to win-x64 only — this task did not try it against linux-x64, which keeps
its prior behavior — because CI publishes win-x64 and linux-x64 on their native OS in
separate jobs and only the Windows side was measured here.

**Not adopted:**

- `PublishReadyToRunComposite=true` — the extra ~2 MB of Mapped-resident saving and
  faster second-run startup over plain R2R do not pay for +28 MB of package size (167.3
  MB against R2R's 139.3 MB, +20%) on a local-first app users download and update
  repeatedly.
- `System.Runtime.TieredPGO=false` — no measured memory gain over plain R2R, and turning
  off PGO trades away steady-state code quality this spike has no way to price.
- `DOTNET_GCConserveMemory=5` — real but smaller effect than level 9, not worth carrying
  two supported values.

Trimming remains out of scope per this task's charter.

### 7.4 Commands used

```powershell
# One publish per build-time variant (ReadyToRun and Composite are publish-time only).
./packaging/Publish.ps1 -Runtime win-x64 -Version 0.1.0-mem -Commit <40-char sha> -OutputDirectory <out>\baseline
./packaging/Publish.ps1 -Runtime win-x64 -Version 0.1.0-mem -Commit <40-char sha> -OutputDirectory <out>\r2r -ExtraProperties @('-p:PublishReadyToRun=true')
./packaging/Publish.ps1 -Runtime win-x64 -Version 0.1.0-mem -Commit <40-char sha> -OutputDirectory <out>\r2r-composite -ExtraProperties @('-p:PublishReadyToRun=true','-p:PublishReadyToRunComposite=true')
# Adopted variant: PublishReadyToRun is now Publish.ps1's default for win-x64, and
# ConserveMemory=9 is now in Winnow.App.csproj, so this is an ordinary publish.
./packaging/Publish.ps1 -Runtime win-x64 -Version 0.1.0-mem -Commit <40-char sha> -OutputDirectory <out>\adopted

# Memory: two runs per configuration, --no-sync, against the copied real library.
./docs/spikes/memory-footprint.ps1 -Exe <out>\baseline\Winnow.exe -DataDir <data> -NoSync -Label baseline -SampleAtSeconds 30,90 -ToolPath <tools>
./docs/spikes/memory-footprint.ps1 -Exe <out>\r2r\Winnow.exe -DataDir <data> -NoSync -Label r2r -SampleAtSeconds 30,90 -ToolPath <tools>
./docs/spikes/memory-footprint.ps1 -Exe <out>\r2r-composite\Winnow.exe -DataDir <data> -NoSync -Label r2rc -SampleAtSeconds 30,90 -ToolPath <tools>
./docs/spikes/memory-footprint.ps1 -Exe <out>\r2r\Winnow.exe -DataDir <data> -NoSync -Label conserve9 -SampleAtSeconds 30,90 -Env @{ DOTNET_GCConserveMemory = '9' } -ToolPath <tools>
./docs/spikes/memory-footprint.ps1 -Exe <out>\r2r\Winnow.exe -DataDir <data> -NoSync -Label conserve5 -SampleAtSeconds 30,90 -Env @{ DOTNET_GCConserveMemory = '5' } -ToolPath <tools>
./docs/spikes/memory-footprint.ps1 -Exe <out>\r2r\Winnow.exe -DataDir <data> -NoSync -Label nopgo -SampleAtSeconds 30,90 -Env @{ DOTNET_TieredPGO = '0' } -ToolPath <tools>
./docs/spikes/memory-footprint.ps1 -Exe <out>\adopted\Winnow.exe -DataDir <data> -NoSync -Label adopted -SampleAtSeconds 30,90 -ToolPath <tools>
./docs/spikes/memory-footprint.ps1 -Exe <out>\adopted\Winnow.exe -DataDir <data> -Label adopted-sync -SampleAtSeconds 20,60 -ToolPath <tools>

# Startup: wall-clock from process start to a window title, --no-sync, two runs each.
# (one-off helper, not part of the committed spike tooling)
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process <exe> -ArgumentList '--data-dir', <data>, '--no-sync' -PassThru
do { Start-Sleep -Milliseconds 100; $p.Refresh() } until ($p.MainWindowTitle)
$sw.ElapsedMilliseconds
```

Limitations, stated up front:

- Two runs per configuration, one machine, one library — the same limitations §1 states,
  and private-bytes agreement between runs was looser here (up to ~30 MB) than §1's
  ~15 MB, plausibly from self-contained publishes faulting in more assemblies per run.
- Startup timing is a new, ad hoc measurement for this follow-up, not the tooling §1 used;
  two runs per configuration is thin, and §7.2 already flags the adopted configuration's
  startup numbers as unconfirmed against noise.
- `PublishReadyToRunComposite` and the `ConserveMemory`/`TieredPGO` trials were measured
  against the plain-R2R publish rather than each other in every combination; the adopted
  configuration (R2R + ConserveMemory=9 baked in, not via environment variable) was
  published and measured once on its own to confirm the combination, not exhaustively
  cross-measured against every other pairing.
- Local Windows PowerShell here is 5.1, not the `pwsh` (PowerShell 7+) CI runs
  `packaging/Publish.ps1` under; `Set-Content -Encoding utf8NoBOM` on the script's last
  line is a `pwsh`-only enumerator value and fails on 5.1. This did not require changing
  the committed script — 5.1 was worked around locally for these publishes — but anyone
  reproducing this table on a machine with only Windows PowerShell should expect the same
  failure at that line and know it is a local-tooling gap, not a script bug.

## 8. Follow-up: TASK-152.5

**Measured 2026-09-07**, same machine and same method as §1, on a copy of the real
library (1,039 works, 2,950 covers). Framework-dependent Release, `--data-dir` against
the copy, no clicks, sampled at 30 s and 90 s. Before and after are the same tree apart
from this task's changes.

Startup work, counted as lines in `<data>\logs\diagnostic.log` over the first 60 s:

| Log line | Before | After |
|---|---|---|
| `Local library sync` | 3 | **1** |
| `Steam scan` | 4 | **1** |
| `Epic scan` | 4 | **1** |
| `GOG scan` | 2 | **1** |
| `Resolved N candidates` | 4 | **2** |
| `Expansion scan` (the merge screen being built) | 2 | **0** |
| `Feed scored` | 7 | **3** |

The four scan-and-resolve passes were the startup pipeline's local sync, the remote
backfill's install-state re-read on the way back from HTTP, and the first stable manifest
read of each install watcher — which treated whatever it found at launch as a change,
because it had published nothing yet. `LibraryScanBaseline` records the launcher
fingerprints each completed pass covered, read before the pass rather than after, so the
other three adopt that answer and a manifest that genuinely moved still publishes. The
two remaining resolves are the local pass and the remote union, which is a different
candidate set (707 against 1,557). One `Resolved` line and one scan of each store is the
floor, not a coincidence of timing: fifteen minutes later the snapshot scheduler ticked
and produced exactly one more of each, plus one feed pass.

The merge screen is no longer built at all on a launch that never opens its pane. Its
rail row carries no count — `MainWindow.axaml` says so, and the pending count lives on
the screen's own header — so nothing outside the pane read it, while building it cost a
full library snapshot with non-game entries plus an expansion scan over 974 works. The
startup pipeline now answers "has the queue moved?" with a `COUNT`.

Three feed passes rather than one. Two triggers went: the window's open sequence no
longer scores the feed itself (every completed `LibraryViewModel` load already raises
`TilesChanged`, so asking again scored the library twice over), and the two install
watchers no longer refresh at launch. What remains is one pass per surviving refresh
call, and `LibraryViewModel` raises `TilesChanged` on every reload whether or not the
rows changed — on this warm library nothing was written, so all three passes were
avoidable. Removing them needs a change in `LibraryViewModel`, which this task did not
touch.

Process memory, private bytes and working set in MB:

| | Private 30 s / 90 s | Working set 30 s / 90 s | GC committed 30 s / 90 s | Threads |
|---|---|---|---|---|
| Before | 280.0 / 266.4 | 351.2 / 344.6 | 83.3 / 76.1 | 77 / 73 |
| After | 260.7 / 241.5 | 327.7 / 319.0 | 93.1 / 85.0 | 77 / 72 |

About 19 MB of private bytes at 30 s and 25 MB at 90 s, and the private RW-data figure
(prot=4, the native heaps of §2.2) fell from 195.3 to 170.9 MB committed. GC committed
moved the other way by 9 MB, which is inside the ±15 MB §1 calls noise on a single
sample; these are single runs, and the burst that sizes the heap is now shorter rather
than absent.

`SystemProcessSource.List()` no longer materialises a `Process`, a `ProcessInfo` and a
`ThreadInfo` per thread of every process on the machine. On Windows it walks the
`NtQuerySystemInformation(SystemProcessInformation)` snapshot for pids and image names
only — the two facts `ProcessListing` carries — into one reusable pinned buffer.
Allocation per five-second poll, measured with `GC.GetAllocatedBytesForCurrentThread()`
around the call (three warm-up iterations, then the minimum of five) at **702 processes
and roughly 14,100 threads**:

| | Bytes per tick | Bytes per process |
|---|---|---|
| Before (`Process.GetProcesses()` plus the old loop) | 1,319,760 | 1,880 |
| After (snapshot walk) | **84,320** | **120** |

15.6× less: about 16 MB a minute of allocation pressure removed while the app sits idle.
The trade is a pinned buffer that settles near 1.7 MB and stays for the process's
lifetime.

## 9. Follow-up: TASK-152.3, hidden panes built on first show

**Measured 2026-09-07**, same machine and the same copied library, Release, `--no-sync`,
sampled at 30 s at the startup view with no clicks. Before is this branch's parent plus
its working tree; after is the same tree with the shell's hidden screens behind
`Views/LazyPane.cs`: the detail modal, the screenshot lightbox, the merge queue, STATS,
the three settings sections and the filter panel. The dump of the after-state confirms
what is no longer there: eight `LazyPane`s and not one instance of any of those eight
views.

Two runs of each, because §1's noise floor is wider than some of these figures. The
object counts came back identical run to run; the byte figures did not.

| At rest, 30 s | Before | After | Change |
|---|---|---|---|
| Live objects (`gcdump`, which collects first) | 49.8 / 50.4 MB, 612k objects | 41.3 / 41.4 MB, 499k | **−9 MB, −113k objects** |
| GC allocated heap (`eeheap -gc`, garbage included) | 54.7 / 54.5 MB | 44.0 / 45.8 MB | −9 to −11 MB |
| `StyleInstance` | 40,473 | 30,336 | **−25.0 %** |
| `StyleClassActivator` | 44,898 | 33,506 | −25.4 % |
| `DynamicResourceExpression` | 26,734 | 21,065 | −21.2 % |
| `ValueStore` | 5,603 | 4,320 | −22.9 % |
| `Controls.Classes` (≈ live controls) | 2,771 | 2,000 | −27.8 % |
| Private bytes | 258.9 / 285.7 MB | 222.5 / 223.7 MB | −35 MB or better |
| Working set | 305.2 / 328.4 MB | 270.8 / 272.5 MB | −33 MB or better |

**The managed heap is the smaller half of the saving.** Nine megabytes of it are
managed objects; the process gives back 35 MB or more of private bytes, because each
control that is no longer attached also had a composition visual, render resources
behind it and code jitted to build it. The two after-runs agreed within 1.2 MB of
private bytes where the two before-runs differed by 27, so the direction is safe even
though the exact figure is not.

**The styling count fell by a quarter, not by a third, and the reason is measurable.**
A control pays for styles when it is *attached*, not when it is visible, and the panes
that were attached-but-hidden were 771 of the 2,771 attached controls — 28 %, which is
what came off. What remains is mostly the screen the window opens on: one `FeedCardView`
is 51 controls and the feed realizes about 29 of them, so the feed alone is roughly
1,550 of the 2,000 that are left. Reaching a third would mean recycling the feed's
off-screen cards, which is not this task's change; the shelves are ordinary panels
today.

Two things that were expected to pay and did not:

- **The hidden library island cost almost nothing.** Its 536 lines of markup are 32
  attached controls, because the command bar's flyout contents are never attached until
  opened and the wall, the list rows and the sort menu are all templated per item.
- **Neither did the filter panel** — 15 controls at rest for the same reason, so its
  `LazyPane` saved about 150 `StyleInstance`s. It is behind one anyway, so the rule is
  "a screen the shell can open is a `LazyPane`" with no exception to remember.

`controls.axaml`'s `DynamicResource` per token stands as it is: the views themselves
already use `StaticResource` (eight `DynamicResource` references across all of `Views/`,
and one of them is the tile's badge glow), so the 26,734 expressions are the shared
sheets and Fluent's own templates applied per control, and they fall with the control
count rather than with any rewriting of the sheets.

What a first show costs, from a headless probe of the same panes (Release, the
eight-game preview library, materialize plus layout plus the pane's own load):

| First show | Controls added | Elapsed |
|---|---|---|
| Merge queue | +119 | 78 ms |
| STATS | +255 | 104 ms |
| Platforms | +273 | 567 ms |
| Library settings | +110 | 81 ms |
| Appearance | +350 | 270 ms |
| Filter panel | +628 | 536 ms |
| Detail modal | +197 | 964 ms |

Construction is the small part of those figures: `new GameDetailsView()` is 9.5 ms in
Release and `new MergeQueueView()` 3.9 ms, so what the columns mostly show is the layout
pass and each pane's own first read — work that was already paid on first open before
this change. The panes stay built once shown.

## 10. Follow-up: TASK-152.2, the cover cache

**Measured 2026-09-07**, same machine, same copied library (965 titles visible in the
grid; 1,013 of the 1,039 works have art on disk), same framework-dependent Release
build and `--no-sync` runs, window 1280×820 at 100 % DPI, the dormancy ramp on unless
a row says otherwise. Section 1's rules held: the real library was never opened.

### 10.1 How the UI was driven

Section 1 could not scroll, and §2 therefore measured the startup state only. This
follow-up drives the window without desktop automation, from PowerShell:

- `WM_MOUSEWHEEL` posted to the window handle scrolls whatever is under a stated client
  point; `WM_LBUTTONDOWN`/`WM_LBUTTONUP` at a stated point clicks the rail, a tile's
  Details fold and a screenshot thumbnail; `PrintWindow` with `PW_RENDERFULLCONTENT`
  captures the window to a PNG so the state can be confirmed rather than assumed.
- Nothing else on the desktop is touched: the messages go to one window of one process
  this session started, and no cursor is moved. A `Popup` is its own window, so a
  flyout does not appear in the capture — the one preference these runs needed
  (`display.dim_dormant_covers`) was written straight into the throwaway library's
  `settings` table instead of through the menu.
- **The landing screen is the feed, not the wall.** The first attempt scrolled for a
  minute and moved nothing but the feed's own shelves; ALL GAMES has to be clicked
  first. Any repeat of this needs that click.

A pass is: sample at t=30 s on the feed, click ALL GAMES, sample, post 1,200 wheel
notches down (the last of them at the bottom, confirmed by capture), sample, 1,200
back up, sample. Live objects come from `dotnet-gcdump collect` plus its `report`,
which forces a gen-2 collection — so a census is what survives a full collection, and
it is taken after the private-bytes samples, never before.

### 10.2 Private bytes, before and after

Before is the tree at `0d7090a` plus the working tree, one pass. After is the same
protocol on the change, three passes, because run-to-run scatter turned out to be
larger than §1 assumed.

| Point in the pass | Before | After (three runs) |
|---|---|---|
| t=30 s, feed (post-startup) | 257.0 | 247.0 / 250.4 / 277.3 |
| ALL GAMES realized | 290.0 | 276.7 / — / 300.9 |
| scrolled to the bottom of 965 | **506.9** | **352.7 / 327.5 / 368.0** |
| scrolled back to the top | 490.4 | 344.0 / — / 341.8 |
| growth over post-startup | **+249.9** | **+105.7 / +77.1 / +90.7** |
| growth over the realized wall | +216.9 | +76.0 / — / +67.1 |

The 500 MB the author reported reproduces exactly, and it is the scroll that produces
it. After the change the same pass ends 140–180 MB lower.

**TASK-152.2's exit criterion was 60 MB of growth over the post-startup figure and it
is not met**: the growth is 77–106 MB. Section 6.4 accounts for it.

### 10.3 Live decoded art, before and after

Counts are live `Avalonia.Skia.WriteableBitmapImpl` objects and live `CoverArt`
records; LRU entries are the decoded-memory cache's own count. One two-layer cover at
the 160 bucket (a 148-DIP tile at 100 % DPI) is 160×240×4×2 = 300 KB, which is what
turns a count into bytes.

| State | Before | After |
|---|---|---|
| at rest on the feed | 238 bitmaps, 119 LRU entries (≈36 MB) | 60 bitmaps, 30 entries (≈9 MB) |
| after the full scroll | **1,226 bitmaps, 455 entries (≈133 MB cached, ≈184 MB alive)** | **286 bitmaps, 143 art, 114 entries (≈34 MB cached, ≈43 MB alive)** |
| same, ramp switched off | not applicable | 249 bitmaps for 249 art — **one bitmap per cover** |
| detail modal and lightbox open | not measured | 263 bitmaps for 135 art: the modal's cover, its five screenshot thumbnails and the 1280 px lightbox shot are one bitmap each |

Three separate causes, and the counts separate them:

- **At rest, 89 of the 119 cached pairs were the merge queue's.** Its screen is not
  virtualized and its view asked for every row's cover on attach, while the pane was
  still behind the library — including the rows of answered cards, whose thumbnails
  are inside a subtree hidden by `IsPending` and are never drawn. The queue now asks
  per visible section, for cards that are still asking a question, and only once the
  pane is on screen.
- **After the scroll, 455 cached pairs was the 128 MiB budget doing what it was told.**
  It is 32 MiB now, sized from the measured screenful (`CoverCacheOptions` carries the
  arithmetic), and 143 live art records against 114 cached entries is the other half of
  the change working: the 29 the cache has evicted are alive only because a wall tile
  still holds a lease on them, and everything else was freed at eviction instead of at
  the next gen-2 finalization.
- **The floor layer is decoded only where it is drawn.** With the ramp off the wall
  holds one bitmap per cover, and the single-layer surfaces hold one each whatever the
  ramp says — a 1280 px lightbox screenshot is 3.7 MB rather than 7.4 MB.

### 10.4 What the remaining growth is, and what it is not

Of the 77–106 MB a full scroll still adds:

- **≈34 MB is decoded cover art** — the 32 MiB budget plus the leased screenful, which
  is what the cache is for. Computed from the census, not measured separately.
- **±40 MB is GC committed size, and it is the noisiest term here.** At t=30 s the same
  build reported 92.4, 92.5 and 120.0 MB committed across the three runs, against 77.7
  before; a full scroll then moved it by only 4.4 MB (92.5 → 96.9, measured with
  `dotnet-counters`). The managed heap after a scroll is 72 MB in 881,000 objects with
  no single large retainer, which is §2.3's styling and composition cost, not art. The
  scatter is why the after column is three runs; it is also why the smaller cache reads
  as a smaller win on private bytes than it is on pixels — less declared memory
  pressure means fewer gen-2 collections.
- **The rest is native and per-frame memory that the scroll causes but the cache does
  not hold**: 965 covers decoded through Skia, each uploaded as a texture and each
  freeing a transient bitmap, plus the wall's own realized containers. Committed
  private RW pages (`prot=4`) are 212 MB after a scroll against 184 MB at rest in §2.2,
  and the largest single block, 80 MB at one allocation base, is already there at rest.
  Nothing in the cover cache can return that. Sizing Skia's GPU resource cache is the
  obvious next question and it is an `AppBuilder` option, so it belongs with
  TASK-152.3/152.4 rather than here; `Avalonia.Skia.SkiaOptions` was not reachable from
  this build's package set when tried.

### 10.5 Limitations

- One machine, one library, one window size, one DPI. The 200 % DPI case is computed
  from the bucket arithmetic, not measured.
- Three after-runs against one before-run. The before figures are the same single pass
  §2 reports, and the ±40 MB GC scatter measured afterwards applies to it too.
- The wheel-message harness scrolls, but it cannot verify that every row in between
  was rendered rather than skipped; the captures confirm the top and the bottom, and
  the LRU count confirms that a full budget's worth of distinct covers was decoded.
- The census forces a collection, so it reports what is retained, not the peak.

## 11. Combined result

**Measured 2026-09-07** after TASK-152.1, 152.2, 152.3 and 152.5 were integrated into one
working tree (152.4's ReadyToRun and `ConserveMemory` settings apply only to the published
win-x64 build and are not in this number). Same machine, same copied real library, same
method as §1: framework-dependent Release build, `--data-dir` against the copy, the normal
startup pipeline running, no clicks, one run.

| | Before (§2.1, real library, sync) | After | Change |
|---|---|---|---|
| Private bytes, 30 s / 90 s | 288.9 / 275.2 | 217.1 / 205.7 | −72 / −70 MB |
| Working set, 30 s / 90 s | 361.2 / 354.7 | 332.8 / 323.7 | −28 / −31 MB |
| GC committed, 30 s / 90 s | 97.2 / 89.9 | 59.2 / 57.9 | −38 / −32 MB |
| Private RW data resident (prot 4) | 188.5 | 129.7 | −59 MB |
| Threads | 83 / 73 | 76 / 73 | |

Working set falls less than private bytes because the image-backed part (the driver,
CoreLib, Skia, ANGLE) is untouched by any of this work, as §2.2 said it would be. The
scrolled state, which is where the 500 MB figure came from, is §10's measurement:
about 330–370 MB after a full pass over the grid, against 507 MB before.

Build: `dotnet build` with zero warnings; `dotnet test`: 4,005 passed, 2 skipped
(Linux-only), on the integrated tree.

