# Graphics and UI audit implementation

The subsequent [voxel performance work](VOXEL_PERFORMANCE.md) changes meshing,
scheduling, transparent scratch storage and geometry retention. Measurements and
test totals below describe the earlier hardening delivery, not that later change.

This report covers the September 2026 FishGfx and nested FishUI audit. The changes retain Windows x64, .NET 10, OpenGL 4.0 fallback, existing bitmap-font record bytes, voxel ownership, and the supported modern API. Legacy projects outside the modern solution were inspected but are not migrated wholesale. FishUI's upstream CI removal is preserved; no new CI or NuGet publication is claimed.

## Correctness and API changes

| Finding | Resolution and regression |
|---|---|
| Atlas insertion changed published positions on failure | Packing stages positions and pixels, then publishes dimensions, pixels, positions and version together. Exhaustion preserves existing positions and uses fallback. Construction rejects an atlas that cannot fit fallback. `AuditRegressionTests` exercises rollback and batch publication. |
| Binary Boolean writes and reads used different sizes | Both helpers use the managed unmanaged-value representation. Boolean, byte enum, packed struct, tuple and truncated-input round trips are covered. Existing BMFont bytes remain unchanged. This helper is native representation, not a portable endian-neutral wire format. |
| Collected resource deletion bypassed binding invalidation | Every owner-context deletion invalidates both binding and render caches before native deletion. `verify-graphics` collects bound buffers/VAOs, creates replacements and checks native bindings on 4.6 and exact 4.0 contexts. Native handle reuse itself is driver-dependent. |
| Cancellation could bypass process termination during stdin writes | The post-start lifetime now covers stdin, waiting and stream drainage. Cancellation kills the owned tree and awaits exit. Each stream retains at most 1 MiB of bytes while draining excess data; build/run results expose truncation flags. Build cancellation propagates to run results. Editor disposal awaits cleanup. Tests cover build, execution, blocked stdin, descendants, excess output and shutdown. |
| Supplementary input was truncated | Glyph lookup, kerning, input dispatch and filters use Unicode scalars. `GlyphMetrics.Character` is `Rune`; char convenience methods remain. Custom FishUI control overrides migrate to Rune. String storage and public cursor offsets remain UTF-16, with editing and wrapping constrained to grapheme boundaries. Invalid input becomes U+FFFD and unsupported scalars use one fallback glyph. |
| Equal tab indices were unstable; labels moved during drawing | FishUI preserves hierarchy order for tied indices and prepares checkbox/radio label positions before input. Regressions cover forward/reverse traversal and first-frame layout. |
| Numeric ranges could leave invalid values | Slider, numeric input and gauges share finite validation and atomic `SetRange`. YAML stages bounds together, then validates before attachment. Clamping raises at most one value event; slider stepping starts at the minimum. |
| Animation catch-up repeated completion or monopolized updates | Completion stops the current update even if callbacks restart playback. At most 256 transitions replay; remaining time advances arithmetically with a coalesced final frame event. Particle creation is bounded by capacity. Nonfinite rates/deltas are rejected. |
| Culling disabled still excluded resident chunks | The disabled path enumerates all resident GPU chunks. Toggling restores spatial filtering and invalidates transparent membership ordering. |
| Full voxel geometry pages reported capacity | The page search now returns zero when no range fits, rather than `int.MaxValue`. A unit regression and actual eight-page GPU fill/release probe cover the failure. |

The detailed FishUI migration contract is in [its hardening guide](thirdparty/FishUI/docs/HARDENING.md). Complex shaping, bidi layout, IME composition, and Unity runtime-specific Unicode behavior remain follow-ups.

## Performance implementation

`GraphicsFont.Measure` computes advances without constructing positioned glyphs or vertices. TrueType metric/kerning caches are bounded to 4,096 entries each. Warm measurement does not rasterize or mutate the atlas. `MeasureAdvances` provides UTF-16-indexed prefix metrics and leading pair adjustments to the FishUI wrapping cache.

Layout prepares all requested glyphs before capturing atlas coordinates. Requests that fit pack once; requests that exhaust capacity retry individually to admit the glyphs that still fit. Immediate text and Mesh2D reuse staging storage. Multiline layout caches grapheme boundaries and advances until text, dimensions, font metrics, size, spacing or scale changes.

Native context setup and capabilities are cached per window. Re-selecting the current context skips the native call; switching restores that context's API and capabilities. Context ownership checks remain in place.

### Release measurements

Same Windows machine, AMD Radeon RX 9070 XT, driver 26.1.1, .NET 10. The identical harness ran against baseline FishGfx `3a4a9ad` / FishUI `eeab58d` and the implementation. Five warmups precede 30 timed samples; percentiles are sampled batch timings divided by repetitions, not individual-frame latency. Managed allocations use the calling thread's allocation counter. Times are milliseconds per operation.

| Workload | Before median / p95 / p99 | After median / p95 / p99 | Bytes/op before → after |
|---|---:|---:|---:|
| Warm measure, 1,024 scalars | 0.143064 / 0.640953 / 0.660548 | 0.019404 / 0.256062 / 0.299130 | 106,576 → 0 |
| New font + 96-glyph batch | 132.9812 / 144.7027 / 149.7933 | 100.7018 / 115.3917 / 123.1407 | 25,765,808 → 1,467,850 |
| Changed 2,048/2,049-unit long line | 0.7361 / 1.115 / 1.2835 | 0.1418 / 0.1975 / 0.2024 | 8,495,552 → 70,464 |
| Select current context | 0.113466 / 0.196909 / 0.709259 | 0.000079 / 0.000095 / 0.000108 | 27,344 → 0 |
| Alternate two contexts | 0.159759 / 0.280880 / 0.843590 | 0.090286 / 0.108196 / 0.167603 | 54,672 → 0 |
| Sort 10,000 transparent faces | 0.829720 / 5.54746 / 6.11318 | 0.741180 / 4.79886 / 5.11390 | 168 → 168 |
| Submit/clear 1,000 queue items | 0.282240 / 0.53254 / 0.58660 | 0.243900 / 0.37410 / 0.43988 | 160,624 → 160,624 |

The wrap workload uses the test metrics backend to isolate layout CPU work; it is not a rendered-text benchmark. Queue and transparent algorithms are unchanged: their timing variation is not attributed to a production optimization. Context-switch tails varied between exploratory runs; these samples do not establish hardware-independent latency guarantees. CPU glyph batches perform no GPU upload. A separate real-context probe checks that 400 warm atlas requests create no replacement atlas.

### Follow-up experiments and implementation order

1. Reclaim completely empty geometry pages only after queued references release ownership. The probe fills eight pages, retains one queued allocation while releasing owners, then releases it: production retains eight nominal 1 MiB vertex pages with zero live allocations. Baseline filling exposed the capacity bug above, so no successful before/after timing is claimed. Future acceptance must preserve stable page identifiers and all queued allocation lifetimes.
2. Size sort scratch from visible faces and stop clearing arrays whose elements contain no managed references. Isolated rent/return experiments compare a cleared 10,000-entry buffer (0.008708 ms median) with an uncleared 1,000-entry buffer (0.000061 ms). This changes two variables and is not an end-to-end sort speedup. Benchmark each change separately with mixed visibility before changing production code.
3. Retain queue bucket lists and add explicit disposal for retained submissions. A standalone list prototype reduces 8,424 bytes/op to zero, while the actual queue still allocates 160,624 bytes per 1,000 submissions. Validate exception cleanup, queued resource retention and disposal before adopting it.

No renderer rewrite, new backend, general concurrency redesign, page reclamation, or queue ownership change is bundled here.

## Reproduction and validation

Run suites sequentially and then build the supported solutions:

```powershell
dotnet test thirdparty/FishUI/UnitTest -c Debug
dotnet test FishGfx.Tests -c Debug
dotnet test thirdparty/FishUI/UnitTest -c Release
dotnet test FishGfx.Tests -c Release
dotnet build thirdparty/FishUI/FishUI.sln -c Release
dotnet build FishGfx.Modern.sln -c Release
dotnet run --project FishGfx.Benchmarks -c Release -- measure
dotnet run --project FishGfx.Benchmarks -c Release -- context
dotnet run --project FishGfx.Benchmarks -c Release -- geometry
dotnet run --project FishGfx.Benchmarks -c Release -- verify-graphics
pwsh -NoProfile -File scripts/Test-Documentation.ps1
```

The benchmark first argument is a case-insensitive substring filter for CPU workload names; omit it to run all CPU workloads. `context`, `geometry` and `verify-graphics` open real windows. The harness references the test fixture for mock text metrics and is separate from the supported application solution.

Set `FISHGFX_CAPTURE_ROOT` to an absolute isolated directory before gallery `--auto` runs; the default still updates the repository's gallery pictures. FishUI provides `FISHUI_CAPTURE_ROOT` for sample screenshots. Keep logs, captures and runtime roots outside tracked content.

The initial reviewed baseline had 434 FishGfx and 230 FishUI passing tests. One initial allocation test failed then passed alone and on repeat. Its class now runs in a nonparallel collection to avoid shared-pool interference; the 4,096-byte threshold is unchanged. These counts and the older bug-history validation entries are historical, not current acceptance totals.

All five original first-party Markdown documents were reviewed. README, INFO, GRAPHICS_API and BUGS were reconciled; the asset PROVENANCE record remains an unchanged historical snapshot. The standalone documentation check verifies local Markdown/HTML links and includes the nested FishUI check. Package pins remain in project files and the nested gitlink is the dependency source of truth.

### Completed checks for this revision

- FishGfx: 446 tests passed in Debug and Release. FishUI: 245 tests passed in Debug and Release. Both supported solutions built in Release without warnings; the FishUI solution also built in Debug, including its linked Unity source target.
- Package audits for both solutions reported no vulnerable packages from the configured sources.
- Primitive galleries completed on OpenGL 4.6 and exact 4.0. Text and UI diagnostic captures were inspected. The windowing probe passed at 1.25 display scale; this is not a multi-monitor/high-DPI matrix.
- Node editor automatic startup/shutdown, voxel automatic convergence/underwater/UI checks, collected-resource probes, and both Raylib FishUI frame-ownership diagnostic modes passed.
- The geometry fill/release probe measured 1.4337 / 1.9808 / 2.0568 ms median/p95/p99 and 624 managed bytes/op; eight nominal MiB of vertex pages remained retained after release. No baseline comparison is available because the original capacity bug prevented the same workload from completing.
- Local documentation links and the current FishUI pin passed validation. Parent RaylibGame integration results are recorded in its hardening report after updating its dependency pointer.

Unity editor/player, Linux/macOS, another GPU vendor, actual old OpenGL hardware, and exhaustive manual Unicode/input acceptance are unavailable in this Windows run. Exact 4.0 context checks exercise the fallback code on a modern GPU. Driver handle reuse is nondeterministic; the resource probe verifies cache invalidation and replacement bindings without claiming that the driver reused a specific numeric handle. No full coverage percentage, end-to-end game frame speedup, or GPU memory profiler result is inferred from these measurements.

Publication also surfaced GitHub Dependabot alert 3 for Newtonsoft.Json 12.0.3 in the excluded .NET Framework `Test/packages.config`. This is tracked as BUG-038 in [BUGS.md](BUGS.md), with a package/assembly-reference update and JSON-loader validation planned for the separate legacy migration. The clean supported-solution package audits do not cover that legacy manifest.
