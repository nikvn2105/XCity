# XCity roadmap

Goal: **the core of Cities: Skylines 2 on a phone**: curved roads, density zoning, real congestion,
public transport, budget and taxes, districts and policies. Built by Claude (advisor, review, merge, small
fixes) and Codex (heavy implementation), in parallel worktrees.

## Decisions (Anh NiK, 2026-11-27)
- **Curved roads: yes** (phase 4).
- **No App Store release planned for now.** Builds are unsigned IPAs from Codemagic, sideloaded with xsign.
- **Oldest supported phone: iPhone 11** (A13, 4 GB RAM). The test phone is an iPhone 14 Pro Max, so
  every phase must keep Low/Medium graphics comfortable on an A13: 30 FPS minimum, no long frame
  hitches, no thermal throttling in a 20-minute session.

## Later additions
- Android APK build workflow (Codex, 28/11): not yet part of the target devices; decide whether Android gets performance targets like iPhone 11.
- Post Processing branch `codex/post-processing` is ready but held for phase 7.

## Out of scope until further notice
Mods, multiplayer, asset editor, PC-size maps, App Store submission.

## Phases

| # | Phase | Main content | Size | Done when | Status (28/11) |
|---|---|---|---|---|---|
| 0 | Lock the foundation | Merge drag preview + camera gestures, test on iPhone, fix bugs | S | No crash, no major bug on device | Done |
| 1 | UI system | Compact info bar, tool icons, building cards (cost, upkeep, radius, lock), toasts separate from hints, one theme and type scale, 44 pt touch targets, **multiple save slots**, finished tutorial | M | A first-time player plays 15 minutes unaided | Done; logo, app icon and opening animation added. Needs a first-time play test |
| 2 | Gameplay depth | **Per-zone taxes**, budget panel and service funding, **land value**, **low/medium/high density zoning**, parks, garbage, economy balance (`docs/economy-report.md`) | L | A 10,000-person city has real choices and trade-offs | Done in simulation (a scripted city reaches 10,900 people solvent); needs play testing on device |
| 3 | Terrain and maps (next) | Height, hills, **rivers and ponds**, several maps to choose from for a new game | L | Each map plays differently |
| 4 | Roads, generation 2 | **Curved roads**, road types (small, large, highway, one-way), intersections. Roads move from the cell grid to a graph; the grid stays for zoning. Save migration | XL | Curved roads can be drawn and zoned along; old saves still load |
| 5 | Real traffic | Congestion slows trips, **buses** with lines and stops, then metro or train, cargo | L | Traffic jams are a problem the player solves |
| 6 | Districts and policies | Districts, per-district policies, statistics and graphs, citizen age and education | M | A 50,000+ city is manageable |
| 7 | Content and feel | Post Processing (prompt ready), weather, audio, **many more building types**, events and disasters | L, in batches | Long sessions stay interesting |
| 8 | Polish for sideloading | Performance on iPhone 11, crash logging, optional English | M | Stable on iPhone 11 |

## Why this order
- UI (1) before gameplay (2): every new feature needs screen space and a consistent pattern.
- Terrain (3) before curved roads (4): roads must follow height; the reverse means rewriting roads.
- Curved roads (4) before traffic (5) and content (7): zoning, bus routes and buildings all hang off roads.
  The later this lands, the more code must be rewritten. This is the riskiest phase.

## Working rules
- Ask Anh NiK before acting; push back when a request is wrong or too early. Short answers in Vietnamese.
- Stability before features; smallest complete change; say what was skipped.
- Every branch passes `dotnet test Tests/XCity.Simulation.Tests/...` and
  `dotnet build Tests/XCity.CompileCheck/...` before it is pushed.
- Test on the iPhone only when needed.
