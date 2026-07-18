# UI generations

The UI is deliberately separated into an archived generation and the active generation.

| Folder | Status |
| --- | --- |
| `legacy-wpf/` | Documentation for the retired settings UI. No active source or new features belong here. |
| `native-imgui/` | Active Dear ImGui/DirectX 9 menu source used by Vectra External 1.13.0 and newer. |

The game overlay is not part of either menu folder. It remains in `../Vectra.External/OverlayWindow.cs` because it is a runtime renderer shared with the current client.

Build the active menu from the workspace root with `CS2.External/scripts/Build-NativeMenu.ps1`. Runtime settings cross the versioned native API in `Vectra.External/NativeMenuHost.cs`; gameplay behavior does not belong in either UI folder.
