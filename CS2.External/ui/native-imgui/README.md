# Active native ImGui menu

This is the current settings UI for Vectra External 2.0.0. The product project is `neverlose/examples/example_win32_directx9/example_win32_directx9.vcxproj`; it builds `Vectra.Menu.Native.dll`.

Keep the original vectraNewUi layout, sidebar, subtabs, group boxes, animations, and DirectX 9 renderer. Product controls communicate with the managed runtime only through `Vectra.External/NativeMenuHost.cs` and `native_api.h`.

Build from the workspace root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\CS2.External\scripts\Build-NativeMenu.ps1 -Configuration Release
```

Demo tabs, fake account content, wallpapers, and non-functional placeholder modules must not be reintroduced.

The menu contains the four product areas Aim, Visuals, Config, and System. Gameplay logic remains in `Vectra.External`; changes to settings must update both `native_api.h` and `Vectra.External/NativeMenuHost.cs`, together with their mapping tests.
