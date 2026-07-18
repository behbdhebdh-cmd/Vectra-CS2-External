Vectra CS2 External – Windows x64 External Framework

Engineered as a high-performance companion client for Counter-Strike 2, Vectra combines a low-latency external overlay with a fully native Dear ImGui control center, backed by a verified secure launcher, dynamic memory resolution, real-time map-physics parsing, and a comprehensive release pipeline built for stability and precision.

Disclaimer: This software is intended exclusively for use in authorized testing environments. The end-user assumes full responsibility for compliance with all applicable platform policies and local legislation.

Key Features

    Sleek Native UI: Fully interactive settings panel powered by Dear ImGui and DirectX 9, running seamlessly on the .NET 10 runtime for responsive, flicker-free control.

    Secure Bootstrapping: Verified launcher with Steam process handoff and SHA-256 integrity checks on every loaded module – no unexpected binaries, no surprises.

    Complete Visual Suite: Full-spectrum overlay components including Player ESP, Weapon/Icons, Item pickups, Bomb status, Radar, and off-screen target markers – keep every threat in your peripheral vision.

    Intelligent Aim Assistance: Adaptive targeting logic enhanced by line-of-sight verification using in-engine map-physics data – surgical precision without the noise.

    Grenade Trajectory Prediction: Real-time grenade path preview leveraging CS2’s native world_physics collision meshes – land perfect smokes and molotovs every time.

    Automatic Offset Management: Built-in compatibility checks against current CS2 builds with generated-offset fallback, ensuring functionality persists through game updates.

    Stability & Validation: Rigorous external and loader test suites guarantee deterministic behavior under load, while versioned release manifests provide full transparency over every shipped executable and native component.


## Repository layout

| Path | Purpose |
| --- | --- |
| `CS2.External/Vectra.External/` | Managed runtime, state reader, overlay, configuration, and native menu host |
| `CS2.External/ui/native-imgui/` | Active Dear ImGui/DirectX 9 menu source |
| `CS2.External/Vectra.Loader/` | Verified Steam/CS2 handoff application |
| `CS2.External/Vectra.External.Tests/` | Runtime, geometry, collision, configuration, and API tests |
| `CS2.External/Vectra.Loader.Tests/` | Loader coordinator, manifest security, and UI tests |
| `CS2.External/cphys-extractor/` | Standalone local CS2 physics extraction utility |
| `CS2.External/scripts/` | Native build, publishing, and provenance checks |
| `offsets/` | Pinned cs2-dumper output used by the source and release pipeline |
| `CS2.External/releases/` | Release layout, current pointer, and published manifests |

Full binary packages are published as assets on the repository's [Releases page](https://github.com/behbdhebdh-cmd/Vectra-CS2-External/releases). They are intentionally kept out of Git history.

## Requirements

- Windows x64
- .NET 10 SDK and Desktop Runtime
- Visual Studio with the Desktop development with C++ workload
- Counter-Strike 2 build matching `offsets/info.json`

## Build

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\CS2.External\scripts\Build-NativeMenu.ps1 -Configuration Release
dotnet build .\CS2.External\Vectra.External\Vectra.External.csproj -c Release
dotnet build .\CS2.External\Vectra.Loader\Vectra.Loader.csproj -c Release
```

Run the validation suites:

```powershell
dotnet run --project .\CS2.External\Vectra.External.Tests\Vectra.External.Tests.csproj -c Release
dotnet run --project .\CS2.External\Vectra.Loader.Tests\Vectra.Loader.Tests.csproj -c Release
```

For runtime details, controls, configuration, offsets, and publishing, see [CS2.External/README.md](CS2.External/README.md) and [CS2.External/Vectra.External/README.md](CS2.External/Vectra.External/README.md).

## Releases and integrity

Official packages use the tag format `v<version>-cs2.<build>`. Every package contains `release.json` with the exact CS2 build, offset provenance, file names, and SHA-256 hashes for the Loader, External executable, and native menu DLL. The newest published package is identified by `CS2.External/releases/Vectra.External/latest.txt`.

## License

This project is released under the [MIT License](LICENSE). Commercial use, modification, redistribution, and resale are permitted subject to the license terms. Third-party components remain covered by their respective licenses and notices.

## Disclaimer

This is an independent open-source project and is not affiliated with, endorsed by, or sponsored by Valve Corporation or Steam. Counter-Strike, Counter-Strike 2, Steam, and related marks belong to their respective owners.
