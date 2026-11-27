# Task for Gemini (Antigravity): far view, overview LOD and service info view

You share this repo with Claude. You work in parallel, so stay inside the files listed under **You own**. Claude is changing the simulation at the same time.

## Branch
- Start from `claude/trusting-einstein-1p5whu` (pull it first).
- Work and push on your own branch: `gemini/graphics-view`.
- Do not push to `main` or to Claude's branch. Claude reviews your branch and merges it.

## The project in 60 seconds
- Unity 2022.3.47f1, iOS target (iPhone), built-in render pipeline. Everything is built from code: no prefabs, and the scene is empty. `XCityBootstrap` creates the camera, lights, HUD and renderers at startup.
- The world is a 512×512 tile grid (`CitySimulation.GridSize`), `CellSize = 4` m per tile, so the map is 2048 m wide.
- Rendering is chunked: 8×8 tiles per chunk (`ChunkRenderScheduler.Size`). Each chunk has a `ChunkDetail`: `Hidden`, `Overview` or `Full`. `CityChunkRenderer` (ground, roads, props) and `BuildingChunkRenderer` (buildings) build one mesh per chunk with `MeshBuilder` and vertex colours (`XCityVertexColor` shader).
- UI strings are Vietnamese. Use the same style as the existing labels.
- C# 9 only (Unity 2022.3): no collection expressions, no `required`, no primary constructors.
- Simulation tests: `dotnet test Tests/XCity.Simulation.Tests/XCity.Simulation.Tests.csproj`. These tests do not cover rendering, so check your work in the Unity editor (Game view at an iPhone resolution) or on a device.

## Problem
In Cities: Skylines 2 the player can zoom out and see the whole city. XCity stops far too early:
- `XCityBootstrap.cs`: `_camDistance` is clamped to `14f..110f` (mouse scroll and pinch), `farClipPlane = 320f`, linear fog from 120 to 265.
- `ChunkRenderScheduler`: `ViewDistance = 278f`. Chunks further away than that are hidden.

## Goals
1. **Zoom out to the whole city.** Raise the maximum camera distance so that the full 2048 m map fits on screen at maximum zoom. Scale far clip and fog with `_camDistance`, so close-up shots keep their atmosphere and the far view is not a fog wall. Keep pan speed proportional to distance (it already is).
2. **Cheap far view.** Add a third, cheaper detail level for distant chunks (for example `ChunkDetail.Far`): flat-coloured ground and road strips, and buildings as single boxes with no facade detail. Choose the level by camera distance and altitude, with hysteresis like the existing `Full` enter/exit thresholds. Keep the per-frame rebuild budget (`Refresh(_cam, budget)`, `RebuildDirtyChunks(1)`) so zooming out never freezes a frame. Vehicles, citizens, smoke and helicopters must stay hidden when zoomed out (`ShowEffects` already does this).
3. **Service coverage info view.** Add a toggle button "Dịch vụ" to the bottom bar. While it is on, tint the ground of each tile by its service coverage:
   - `CitySimulation.GetCoverage(x, z)` returns `ServiceCoverage` flags (`Education`, `Health`, `Police`, `Fire`).
   - `CitySimulation.GetHappiness(x, z)` returns 40–100.
   - Colour developed residential tiles by happiness (red, then yellow, then green); other tiles get a neutral grey.
   - Rebuild the tint only when the city ticks or a cell changes. Never rebuild it every frame.

## Performance budget (iPhone)
- 30 fps target (`Application.targetFrameRate = 30`).
- Zoomed all the way out: no more than about 300 draw calls, and no per-frame allocations.
- A mesh over 65k vertices needs `IndexFormat.UInt32`. `BuildingChunkRenderer` already warns about this.

## You own (edit freely)
- `Assets/Scripts/ChunkRenderScheduler.cs`
- `Assets/Scripts/CityChunkRenderer.cs`
- `Assets/Scripts/BuildingChunkRenderer.cs`
- `Assets/Scripts/MeshBuilder.cs`, but only additions. Do not change existing methods.
- `Assets/Resources/*.shader`
- New file `Assets/Scripts/XCityBootstrap.InfoView.cs` (a `partial class XCityBootstrap`) for the info view.

## You may touch in `XCityBootstrap.cs`, only these spots
- Camera: `SetupCamera`, `UpdateCameraTransform`, the zoom clamps in `HandleInput` / `HandleTouch`, and the fog values in `SetupEnvironment`.
- One line in `SetupHUD` that adds your bottom-bar button, calling into your partial file.

## Do not edit (Claude is changing these now)
- `CitySimulation*.cs`, `RoadGraph.cs`, `CitySave.cs`
- `BuildingMeshFactory.cs`, `IndustrialMeshFactory.cs`
- `SetupBuildCatalog`, `RefreshHUD` and `GetInspectionHint` in `XCityBootstrap.cs`
- Anything under `Tests/`

If you need a new simulation API, do not add it yourself. Write it under "Requests for Claude" in your PR description.

## Done means
- The whole map is visible at maximum zoom on an iPhone-sized Game view.
- Zooming in and out is smooth, with no frame hitches you can see.
- The info view toggles on and off, and it updates after a build.
- `dotnet test` still passes. The project compiles in Unity with no new warnings.
- Your branch is pushed, with a short note listing what you measured: draw calls and frame time when zoomed out, and the device or editor you used.
