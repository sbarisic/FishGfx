# Voxel performance and ownership

The voxel renderer continues to use immutable world/light snapshots, background
workers, bounded ready queues and incremental graphics-thread uploads. Global
`IsIdle` includes distant deferred jobs. Use `GetPendingWork(camera).IsSettled` for
camera-specific benchmark settlement; inspect its work counters when recovery
does not finish.

`VoxelMeshingOptions.CubeMeshingMode` defaults to `Culled`. `GreedyOpaque` is an
opt-in deterministic rectangle pass over uniformly lit matching opaque cube
faces. Custom models, cutout, transparent, animated and double-sided geometry
retain their existing treatment. Rectangles stay within a chunk. The vertex ABI
is unchanged: opaque merged cubes reserve internal `WaveParameters.W = -1` for
tiled sampling, with zero wave amplitude. Cube shaders calculate derivatives from
continuous UVs before wrapping; unmerged UVs keep their original edge sampling.
Applications using custom voxel shaders must implement this convention before
enabling greedy meshing.

Merged pooled outputs are compacted when their old capacity exceeds twice the
remaining vertex count, preserving normal pool rounding in ready-queue retention.

Meshing caches visibility/model choices, computes four unique corner samples per
face and reuses invariant tangents. A horizontal dirty index narrows focused
scheduling while retaining deferred chunks and deterministic priority.

`VoxelLightingOptions.TimeBudgetMilliseconds` defaults to infinity for existing
consumers. Finite positive values add a soft deadline checked every 64 consumed
operations; a call still makes progress, and commit/setup work remains atomic.
Count budgets continue to apply. Invalid, zero and negative values are rejected.

Transparent sorting rents scratch space for visible faces and preserves its exact
stable ordering. Opaque/shadow submission grouping is stable and linear in entry
count. Completely empty geometry pages are reclaimed on the graphics thread after
two seconds; retain one spare per pool and delete at most one page per renderer
update. Upload preparation and queued allocation ownership prevent reclamation.
Page identifiers are monotonic and surviving pages are never renumbered.

`RenderQueue` now implements `IDisposable`; owners should dispose it before their
renderer/context during shutdown. Disposal attempts every retained release and
then reports aggregate failures. It is idempotent even after a release failure.
Bucket reuse was benchmarked and rejected because it slowed submission; normal
frame clearing still releases bucket storage. Public sorted snapshots remain owned
by the caller.

## Reproduction

```powershell
dotnet run --project FishGfx.Benchmarks -c Release -- mesh
dotnet run --project FishGfx.Benchmarks -c Release -- schedule
dotnet run --project FishGfx.Benchmarks -c Release -- group
dotnet run --project FishGfx.Benchmarks -c Release -- sort-
dotnet run --project FishGfx.Benchmarks -c Release -- geometry
dotnet run --project FishGfx.VoxelTest -c Release -- --streaming-benchmark
dotnet run --project FishGfx.VoxelTest -c Release -- --auto --greedy
dotnet run --project FishGfx.SmokeTest -c Release -- --auto --gl40
```

CPU filters select workload names; measurements use at least 300 ms warm-up and
30 samples. Do not run timed comparisons alongside builds or tests. `geometry`
opens a real context and checks retained allocation ownership during trimming.
Voxel rendering retains its existing OpenGL 4.3 minimum. `VoxelTest --gl40`
verifies rejection on an exact 4.0 context; the primitive gallery exercises the
general graphics 4.0 fallback. For fixed-time world framebuffer
comparison, add `--capture-world <absolute-path.rgba>` to a streaming benchmark;
the sidecar JSON records dimensions and renderer. Keep captures outside tracked
files. Do not combine capture mode with the automatic acceptance state machine.
`--capture-fixture` installs a fixed two-material platform for checking merged
texture repetition. Benchmark input is disabled and captures use shader time zero;
the sidecar also records the camera transform.

The September 2026 comparison measured 21–30% faster culled meshing on solid,
slab and checkerboard fixtures, unchanged 160-byte pooled-mesh allocations, and
87.5% fewer retained empty vertex pages in the eight-page probe. Uniform solid
greedy geometry falls from 9,216 vertices to 36, with extra CPU cost for merging.
These figures are component results, not a whole-game frame-time claim. Greedy
remains opt-in; full manual lighting, normal-map, shadow, fog and seam acceptance
must precede a default change.

## Validation record

456 FishGfx tests and 245 nested FishUI tests pass in both Debug and Release.
The supported Modern solution builds in Release with zero warnings and errors.
NuGet vulnerability audits report no vulnerable packages from the configured
sources for the supported solutions. The excluded legacy `Test` project still
has the Newtonsoft.Json issue recorded as BUG-038 in [Bug history](BUGS.md).
The greedy voxel automatic scenario passes on GL 4.6, including stale
edits, underwater rendering and cleanup. The primitive gallery passes on exact
GL 4.0; mixed-context resource checks also pass. Voxel rendering rejects exact
4.0 because its pre-existing minimum is 4.3.

Paired fixed-time showcase captures have identical candidate culled/greedy pixels;
the candidate differs from the original culled build in 1,445 channels by one
byte. A merged grass/brick platform differs in 899 pixels out of 2,073,600, mainly
at tile edges. These are focused comparisons, not the full five-by-60-second
camera-path acceptance matrix. The latter, other GPU vendors and other platforms
remain unverified. No greedy default change is claimed.
