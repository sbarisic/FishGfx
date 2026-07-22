# FishGfx Parametric Manifold CAD

`FishGfx.ManifoldCad` is the Windows x64/.NET 10 CAD application for constructing one exact exhaust runner attached to imported STEP geometry. FishGfx and FishUI own interaction and rendering; Open CASCADE 8.0.0 owns the exact model.

## Build

From a Visual Studio 2022 developer environment:

```powershell
.\tools\bootstrap-manifold-cad.ps1 -Configuration Release
```

The script bootstraps an ignored local vcpkg checkout, restores the manifest-pinned `opencascade` 8.0.0 port revision 1, builds and tests the C++17 bridge, runs the managed CAD tests, and builds `FishGfx.Modern.sln`. The app build copies the native bridge, its dynamically linked OCCT DLLs, and the OCCT/LGPL notice into its output directory.

Run the application with:

```powershell
dotnet run --project .\FishGfx.ManifoldCad\FishGfx.ManifoldCad.csproj -c Release -p:Platform=x64
```

## Project format

`.fgcad` is an atomic, versioned ZIP archive:

- `manifest.json`: project, imported component, placement, and logical mate metadata.
- `graph.json`: stable GUID-based typed runner graph.
- `view.json`: viewport and graph view state.
- `model.xbf`: exact binary XCAF document containing the imported and generated B-reps.

Original STEP paths are informational. Reopening a project uses `model.xbf` and does not require the source files.

## V1 workflow

1. Import one or more `.step` parts.
2. Click a cyan sphere on a detected planar opening to create or move the runner's start mate immediately. Circular edges and cylindrical faces can still be selected manually and bound with **Create / Rebind Mate**.
3. Edit straight, bend, and circular-pipe values in the graph inspector.
4. Move or rotate a selected part with the viewport gizmo or exact millimetre/degree fields. Use **Gizmo** in the toolbar to switch handle modes. Its local mate frame is recomposed and the runner regenerates.
5. Save `.fgcad` or export the complete placed assembly and valid runner as STEP AP242.

Replacing a part deliberately invalidates its logical mates. Select compatible replacement topology and use **Create / Rebind Mate**; the mate ID and name remain unchanged.

## Boundaries

V1 supports one start-mate-attached runner with straight and circular-arc centreline edges, a hollow circular profile, exact G1 sweep, exact length, automatic planar opening candidates, CPU face BVH picking, edge preview picking, rigid placements, and STEP/XCAF persistence. End constraints, transitions, multiple runners, collectors, booleans, collision/clearance analysis, equal-length tooling, automatic routing, and assembly solving are deferred.

Preview vertices are immutable float tessellations for rendering and picking only. All editable, persisted, validated, and exported geometry remains double-precision OCCT B-rep.

## Native boundary

`FishGfx.CadKernel.Native` exposes a versioned UTF-8 C ABI. Every XCAF document is owned by one managed background worker, native exceptions are converted into status plus thread-local diagnostic text, native resources use `SafeHandle`, and mutation revisions prevent stale previews from replacing newer results. No OCCT type or allocator-owned buffer crosses the ABI.
