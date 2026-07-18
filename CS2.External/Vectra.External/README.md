# Vectra.External

`Vectra.External` is the external C# / WPF client in this workspace. It reads CS2 state from a separate desktop process and does not inject a DLL into the game.

> **Scope: private matches only.** Use it only in a locally hosted or otherwise trusted private match. It provides no anti-cheat bypass, and all input features require explicit private-match authorization for every session.

Current client version: **2.0.0**  
Current packaged CS2 build: **14171**

## Requirements

- Windows x64
- .NET 10 Desktop Runtime
- CS2 build matching `../../offsets/info.json`

## Run and controls

From the workspace root, build and start with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\CS2.External\scripts\Build-NativeMenu.ps1 -Configuration Release
dotnet build .\CS2.External\Vectra.External\Vectra.External.csproj -c Release
dotnet run --project .\CS2.External\Vectra.External\Vectra.External.csproj -c Release
```

The visible settings menu is the native x64 Dear ImGui/DirectX 9 UI from `../ui/native-imgui`. It runs on its own UI thread and updates the existing managed `ClientSettings` through a versioned C interface. The WPF runtime remains only for the click-through game overlay. There is no WPF menu fallback.

Version 2.0.0 uses the parsed map BVH for exact aim line-of-sight raycasts, removes Chicken ESP, and advances the native menu contract to API version 2. The underlying 1.13.1 map parser continues to read CS2's `world_physics` `vmdl_c` entry, exclude vanity archives, report parser counts/errors, and invalidate old map caches.

Official packages should normally be opened through `Vectra.Loader-v<version>-cs2.<build>.exe`. Select **External** and press **Start**. The loader validates the adjacent release manifest and External hash, opens `steam://rungameid/730` only when CS2 is not already running, waits up to 90 seconds for `cs2.exe`, stabilizes for three seconds, starts External, shows **You can go into a training match now**, and closes after two seconds. The disabled Internal card is informational only. Direct External startup remains available for development and recovery.

The menu requires both **Master enable** and **Private-match authorization** before input actions are allowed. Authorization is session-only and must be confirmed again after restarting the client or loading a configuration. ESP has independent visual switches. Teamcheck controls whether teammates are drawn and eligible for the trigger.

The rotating radar is enabled by default. It keeps the local facing direction at the top, clamps distant player markers to its edge, and follows the ESP Teamcheck setting.

Bomb ESP is optional and disabled by default. When enabled, it distinguishes carried, dropped, and planted C4 independently of Teamcheck. Carried and dropped states include position and distance; a planted bomb also shows site A/B, remaining explosion time, active defuse status, and defuse time when the underlying game-time values are valid. Off-screen C4 uses a compact objective marker. Invalid or incomplete timer data is omitted rather than estimated.

Weapon labels use the embedded `IconsForWeapons/obs_icons.ttf` font and show icon plus ammunition. Bomb ESP uses the current C4 glyph (`U+E031`). MP5-SD, Defuse Kit, and unsupported knife variants can fall back to SVG paths parsed from the embedded local archive; Healthshot uses a code-native medical cross. Unknown future definitions fall back to a short text name.

Item ESP is optional and disabled by default. It shows ownerless weapons, unthrown grenades, Defuse Kits, Healthshots, and dropped C4 with distance and overlap-stable placement. Held equipment is excluded, and C4 is not drawn twice when Bomb ESP already owns the objective marker.

The local Grenade Predictor is optional and disabled by default. Pulling a supported grenade immediately produces a dashed 64 Hz approximation using Source 2 pitch correction, throw strength, player velocity, and 800 u/s² gravity. A latest-only background queue prevents snapshot and render waits. In parallel, ValveResourceFormat 19.2.6339 reads only local VPK physics meshes for the selected map, builds collision acceleration data, and stores a fingerprinted cache under `%LocalAppData%\Vectra External\map-cache`. Exact mode draws a solid path, bounce points, and a grenade-specific endpoint; current door and moving-prop bounds are combined with map collision. Auto selection uses Windows Restart Manager file-use detection, with a manual Visuals-page map selector when detection is unavailable or ambiguous. Parser and map failures leave the approximate path active.

The optional aim assist is disabled by default. Fresh profiles use Hold Mouse 4, Head targeting, Crosshair priority, Smooth movement, and require a visible target. Head and Chest points, Crosshair/Closest/Most-visible priority, Smooth/Snap movement, a freely captured keyboard or mouse hold key, an optional FOV circle, and the visibility requirement are configurable on the glass-style Aim page. Visibility casts an exact segment from the local eye position to the selected target point through the BVH built from the active map's local `world_physics` collision meshes. Until map collision is ready, visibility data remains unavailable and no target passes the enabled visibility gate. Disabling **Require visible target** permits otherwise eligible in-FOV candidates without a raycast. A valid target remains locked until it becomes ineligible or the hold key is released; Snap applies once per acquired lock, while Smooth follows the target continuously. Foreground-CS2, master-enable, private-match, snapshot, team, projection, and FOV gates remain active, and aim assist never sends firing input. No anti-cheat bypass is provided.

The Aim page's compact live status identifies the current gate and activation state, visibility-reader slot/mask counts, candidate rejection reasons, the current lock, and both the calculated and successfully sent mouse correction. This diagnostic state remains in memory and is not written to a log file.

The controlled trigger fires one tap only after a target has been stable for two fresh snapshots. It then waits until a shot is observed and recoil is neutral before it can tap again. The live diagnostics identify missing recoil data, reload, stale snapshots, target stabilization, and recoil waiting.

The native menu under `../ui/native-imgui` keeps the original vectraNewUi layout, animation, sidebar, subtabs, group boxes, and widgets. Its four real areas are Aim, Visuals, Config, and System; all original demo modules and fake account content are removed. **Streamproof** defaults to OFF so Discord screen/monitor sharing can include the overlay, while ON requests Windows capture exclusion. Application-only CS2 capture may still omit a separate external overlay window.

Runtime scheduling is adaptive: process discovery runs at 2 Hz while disconnected, attached background capture at 15 Hz, foreground ESP at 60 Hz, and active Aim/Trigger capture at 120 Hz. New snapshot sequences bypass idle overlay maintenance and are presented on the next available monitor frame without raising the memory-scan rate. Optional reads follow their feature switches; local, dead, and dormant pawns skip unused detail reads. Map parsing never runs on the snapshot or render thread, drawing resources are reused, and long-lived caches remain bounded.

The Config tab can save and load external-client preferences in `%LocalAppData%\Vectra External\client-config.json`. Saving is explicit; startup does not auto-load a profile. Private-match authorization is session-only and is always reset when loading a configuration.

Visuals use a restrained esports-spectator style: crisp one-DIP rounded boxes, integrated gradient health bars, compact glass-backed labels, three muted accent themes (Lavender, Glacier, and Rose), immediate 55%-visible player appearance completed within 55 ms, and separate 150 ms health transitions. Fresh configurations enable only the minimal core—box, name, health, and distance—while weapon labels, Item ESP, Grenade Predictor, Streamproof, head markers, radar, off-screen arrows, and snaplines remain optional.

Skeleton ESP remains disabled and is not exposed as a placeholder module. Old profiles cannot reactivate it, and capture planning skips all Skeleton ESP memory reads.

## Offsets

The project links the generated offset files, including `../../offsets/client_dll.cs` and `../../offsets/animationsystem_dll.cs`, and copies `../../offsets/info.json` into the application output. Runtime attachment, menu build labels, and release tags all read that packaged metadata. Run the dumper, replace the complete generated set in `offsets/`, then rebuild. The client refuses a game build that does not match `info.json`, and the release script rejects offset sources from mixed dump timestamps.

## Official releases

The version is defined only in `Version.props`. Use semantic versioning:

- MAJOR for incompatible changes
- MINOR for features
- PATCH for fixes

Run `..\scripts\Publish-ExternalRelease.ps1` after bumping `Version.props`. It builds the native menu, runs the External and Loader tests in isolated artifacts, verifies the packaged offset metadata, publishes both applications, creates `../releases/Vectra.External/native-imgui/versions/v<version>-cs2.<build>/`, writes `release.json` with Loader, External, native-menu, and offset hashes, and updates the portable `latest.txt` pointer. Generated `bin/`, `obj/`, `artifacts/`, and `verify*` outputs are not releases.

The authoritative current package pointer is `../releases/Vectra.External/latest.txt`. A version change is not an official release until the publish script completes successfully.

## External test suite

Run `dotnet run --project .\CS2.External\Vectra.External.Tests\Vectra.External.Tests.csproj -c Release` to verify the native host contract, settings/command mapping, configuration roundtrips, radar rotation, aim-assist limits, line-of-sight raycasts, icon/SVG fallbacks, Item/Bomb ESP rendering, grenade math, collision/cache behavior, ESP bounds/clipping, and invalid-data handling.
