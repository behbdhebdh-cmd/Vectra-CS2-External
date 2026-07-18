# Release history

## v2.0.0-cs2.14171

Replaces the unreliable CS2 spotted-mask visibility gate with exact external line-of-sight raycasts through the BVH built from the active map's local `world_physics` collision meshes. The local eye position and selected Head/Chest aim point are tested without blocking the capture thread; visibility remains safely unavailable until map collision is ready. Removes Chicken ESP from the native menu, host contract, configuration, world capture, and overlay, and advances the native menu API to version 2.

## v1.13.1-cs2.14171

Fixes the exhausted world-entity discovery budget that prevented Chicken ESP and caused incomplete Item ESP. Adds current chicken/item classification, keeps active grenade projectiles out of Item ESP, improves grenade priming detection, and restores collision-aware grenade prediction by reading the current `world_physics` model resource from local CS2 map VPKs. Diagnostics now expose discovery and parser counts, and collision caches use format version 2.

## v1.13.0-cs2.14171

Replaces the WPF settings shell with the native `ui/native-imgui` Dear ImGui/DirectX 9 menu while retaining the managed runtime and WPF game overlay. Aim, Visuals, Config, and System contain only real modules; demo tabs, fake account content, Skeleton UI placeholders, and non-functional theme/density controls are removed. The Loader verifies the native menu DLL hash before launch.

## v1.12.1-cs2.14171

Reduces perceived ESP delay without increasing the 60 Hz foreground capture rate, and moves Streamproof to Visuals with capture-visible behavior as the default.

## v1.12.0-cs2.14171

Adds embedded item icons, ownerless Item ESP, incremental Chicken ESP discovery, and asynchronous local grenade prediction with optional map collision caching.

## v1.11.1-cs2.14171

Temporarily disables Skeleton ESP while its live model path remains under repair. The Visuals page clearly shows `SKELETON ESP • CURRENTLY IN REPAIR`, saved profiles cannot reactivate it, and the reader skips Skeleton ESP memory capture.

## v1.11.0-cs2.14171

Adds the animated Vectra Loader as the primary package entry point. The loader presents External alongside a disabled Internal preview, validates the packaged External SHA-256, opens CS2 through Steam when necessary, waits for `cs2.exe`, performs a stable three-second handoff, starts External, and closes after its launch confirmation. Release manifests now carry separate Loader and External executable hashes.

## v1.10.1-cs2.14171

Repairs the optional Skeleton ESP with validated Source 2 model-resource and parent-hierarchy resolution. Helper/twist bones are traversed safely, the bone cache is read once per eligible player, implausible joints are isolated instead of hiding the complete pose, and compact snapshot diagnostics expose resolution failures. The clean full-body visual keeps the existing thin accent and soft-outline style.

## v1.10.0-cs2.14171

Adds adaptive 2/15/60/120 Hz runtime scheduling, feature-aware memory capture, deferred startup work, change-aware overlay updates, reusable menu pages, reduced render allocations, and bounded long-session caches. Includes the optional carried/dropped/planted Bomb ESP introduced during the 1.9 source milestone.

## v1.6.1-cs2.14171

Adds explicit local save/load configuration for external-client settings. The saved profile does not retain session authorization, which must be reconfirmed for every session.

## v1.6.0-cs2.14171

Refreshes the external client for CS2 build 14171. Build metadata now comes from the packaged offset dump, the menu shows a prominent overlay/build status, and publishing rejects mixed or stale offset inputs. The build-specific 14170 skeleton fallback remains isolated; 14171 uses dynamic model resolution.

## v1.3.1-cs2.14170

Fixes self-player ESP by identifying and excluding the local pawn from ESP, radar, aim-assist, and trigger target lists. Aim assist is disabled by default; no anti-cheat bypass is included.

## v1.3.0-cs2.14170

Adds a tightly bounded aim assist with soft upper-body correction. Optimizes memory-region reuse and movement-aware ESP projection so moving players remain tracked smoothly at viewport edges and during short capture delays.

## v1.2.2-cs2.14170

Stabilizes the external overlay through short bounds/snapshot gaps and preserves trigger context during transient stale snapshots while retaining the strict fresh-data firing gate.

## v1.2.1-cs2.14170

Adds lightweight runtime obfuscation for selected external UI and diagnostic literals. No gameplay or input behavior changed.

## v1.2.0-cs2.14170

Adds the compact rotating radar overlay, with local facing fixed upward, range-clamped player markers, and Teamcheck-consistent filtering. Refreshes the external ESP and menu presentation with unified theme accents, clearer labels, health colors, and status cards.

## v1.1.2-cs2.14170

Fixes controlled-trigger rearming by using stable recoil changes rather than requiring the absolute view-punch to return to zero. The trigger focus gate now accepts any foreground CS2 window, and the UI shows trigger gate diagnostics.

## v1.1.1-cs2.14170

Fixes controlled-trigger rearming after a confirmed tap and adds the capture-exclusion toggle for the external overlay.

## v1.1.0-cs2.14170

First managed external release. Adds Dark Tactical UI, Tactical 2D ESP, controlled recoil-aware trigger taps, trigger diagnostics, shared-offset build validation, and formal SemVer packaging.
