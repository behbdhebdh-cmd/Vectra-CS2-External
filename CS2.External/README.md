# CS2.External

This folder contains the managed external CS2 client, its tests, release tooling, and official packages. The native/internal client is separate and is not required here.

> Use the client only where you have permission. Input-capable features remain locked until their explicit authorization is enabled for the current session. No anti-cheat bypass is included.

## Versions

- Source: `Vectra External 2.0.0`
- Latest official package: `v2.0.0-cs2.14171`
- Packaged CS2 build: `14171`
- Platform: Windows x64 / .NET 10

The source version can be newer than the latest official package. A source version becomes an official release only after the publishing script completes successfully.

Version 2.0.0 replaces the unreliable spotted-mask visibility gate with map-physics line-of-sight raycasts, removes Chicken ESP from the menu and runtime, and advances the native host contract to API version 2.

## Quick start

From the workspace root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\CS2.External\scripts\Build-NativeMenu.ps1 -Configuration Release
dotnet run --project .\CS2.External\Vectra.External\Vectra.External.csproj -c Release
```

Official packages should normally be started through the included Vectra Loader. The client checks the packaged offset metadata against the running CS2 build and will not attach on a mismatch.

## Folder layout

| Path | Contents |
| --- | --- |
| `Vectra.External/` | Managed runtime, external state reader, WPF overlay, native-menu host, and configuration. |
| `ui/native-imgui/` | Active Dear ImGui/DirectX 9 menu source and x64 project. |
| `ui/legacy-wpf/` | Documentation boundary for the retired WPF settings menu. |
| `Vectra.External.Tests/` | Geometry, UI, offset metadata, and configuration tests. |
| `Vectra.Loader/` | Animated Steam/CS2 handoff and verified External launcher. |
| `Vectra.Loader.Tests/` | Deterministic loader flow, manifest security, and UI tests. |
| `scripts/` | Release publishing and offset provenance checks. |
| `releases/Vectra.External/native-imgui/versions/` | Current verified packages from version 1.13.0 onward. |
| `releases/Vectra.External/legacy-wpf/versions/` | Read-only archive of WPF-menu packages through version 1.12.1. |
| `releases/Vectra.External/latest.txt` | Portable pointer to the newest official package. |
| `.release-staging/` | Temporary publishing output; safe to recreate. |
| `bin/`, `obj/`, `artifacts/` | Generated build/test output; not source code. |

Shared generated offsets remain in `../offsets` at the workspace root so the clients do not carry separate copies.

For setup, controls, safety gates, offsets, and release instructions, see [`Vectra.External/README.md`](./Vectra.External/README.md).

Version 1.7.0 introduces a restrained spectator-style ESP with crisp rounded boxes, integrated gradient health bars, muted Lavender/Glacier/Rose accents, compact glass labels and radar, and subtle 150 ms visibility transitions. Fresh configurations use a minimal core of box, name, health, and distance; optional detail remains available in the Visuals page.

Version 1.8.0 expands the aim assist with Head/Chest targeting, Crosshair/Closest/Most-visible priority, Smooth/Snap movement, freely captured hold keys, a stable target lock, a subtle overlay target marker, and a matching glass-style Aim page. Legacy profiles retain the earlier Always/Chest/Crosshair/Smooth behavior.

Version 1.8.1 fixes aim visibility evaluation by addressing the spotted mask with the local controller slot instead of the pawn entity index. The Aim page now includes a **Require visible target** switch and a compact live status that exposes the active gate, visibility capture, candidate rejection counts, target lock, and planned/sent mouse movement.

Version 1.9.0 adds optional Bomb ESP for carried, dropped, and planted C4. It shows objective position and distance, the planted site, validated explosion and defuse countdowns, and a compact off-screen marker. If the required game-time data is unavailable or invalid, the objective remains visible without an estimated timer.

Version 1.10.0 improves responsiveness and long-session efficiency. Startup work begins after the first UI frame; disconnected, background, visual, and input-active sessions use adaptive 2/15/60/120 Hz scheduling. Disabled weapon, skeleton, visibility, chicken, bomb, recoil, and radar reads are skipped, overlay work is change-aware, UI pages are reused, and long-lived caches are bounded.

Version 1.10.1 repairs Skeleton ESP for CS2 build 14171. It resolves the current Source 2 model hierarchy from the generated schemas, accepts helper bones between clean display joints, reads each player's validated bone cache in one batch, and keeps valid body sections visible when a separate limb is incomplete. The visual remains a thin full-body accent with a restrained soft outline and no joint dots or labels.

Version 1.11.0 adds the animated Vectra Loader as the normal release entry point. It verifies the packaged External SHA-256, opens CS2 through Steam when needed, waits for the real `cs2` process, performs a short stabilization handoff, starts External, and closes after a clear launch confirmation. Internal remains visible as a disabled future option.

Version 1.11.1 temporarily disables Skeleton ESP because the live model path remains under repair. The Visuals page shows **SKELETON ESP • CURRENTLY IN REPAIR**, old profiles cannot reactivate it, and capture planning skips all Skeleton ESP memory reads until the repair is complete.

Version 1.12.0 adds modern embedded CS2 item icons, optional ownerless Item ESP, stable incremental Chicken ESP discovery, and a local grenade predictor. Prediction is immediate in approximate mode; exact map bounces load asynchronously from local VPK physics meshes and are cached under `%LocalAppData%\Vectra External\map-cache`. The official package includes the verified Loader and External client for build 14171.

Version 1.12.1 reduces perceived ESP delay without increasing the 60 Hz foreground memory-scan rate. Snapshot publication now bypasses idle overlay maintenance, capture time participates in velocity prediction, player visuals appear immediately at useful opacity, and common reader/render allocations are reduced. Streamproof moves to Visuals and defaults to OFF so Discord monitor sharing can include the overlay.

Version 1.13.0 replaces the WPF settings shell with the native `ui/native-imgui` Dear ImGui/DirectX 9 menu. Four real areas—Aim, Visuals, Config, and System—expose the existing managed runtime through a versioned native host API; demo tabs, fake account content, and non-functional theme/density placeholders are removed. The Loader now verifies the native menu DLL as part of the release.

Version 1.13.1 repairs bounded world-entity discovery for Chicken and ownerless Item ESP, excludes active grenade projectiles, improves grenade priming detection, and reads current CS2 `world_physics` resources from local map VPKs. Native diagnostics expose discovery and collision-parser counts; map caches are rebuilt with cache format 2.

Version 2.0.0 casts exact visibility segments from the local eye position to the configured Head/Chest target through the active map's collision BVH. Visibility remains safely unavailable while map collision loads. Chicken ESP and its native state, configuration, capture, and overlay paths are removed; the native menu API is now version 2.

For future AI-assisted work, read [`AI-README.md`](./AI-README.md) before editing. It defines the runtime, overlay, Loader, active-menu, legacy-archive, and release boundaries.
