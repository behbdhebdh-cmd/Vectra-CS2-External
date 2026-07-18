# Vectra CS2 External

Vectra CS2 External is a Windows x64 companion client for Counter-Strike 2. It combines a managed state reader and overlay with a native Dear ImGui control panel, a verified launcher, automated offset validation, local map-physics parsing, tests, and reproducible release tooling.

> Use the software only on systems and in environments where you have permission to do so. You are responsible for complying with applicable platform rules and local law.

## Highlights

- Native Dear ImGui/DirectX 9 settings menu hosted by a managed .NET 10 runtime
- Verified launcher with Steam handoff and SHA-256 package validation
- Player, weapon, item, bomb, radar, and off-screen overlay components
- Configurable aim assistance with map-physics line-of-sight validation
- Local grenade trajectory prediction using CS2 `world_physics` collision meshes
- Automatic CS2 build and generated-offset compatibility checks
- Deterministic External and Loader test suites
- Versioned release manifests covering every shipped executable and native component

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
