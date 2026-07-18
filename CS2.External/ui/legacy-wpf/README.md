# Legacy WPF menu

The WPF settings menu was retired after version 1.12.1. Its production packages are archived under `../../releases/Vectra.External/legacy-wpf/versions/`.

- Do not restore `MainWindow.cs` or `LuminControls.cs` into the current client.
- Do not add features or fixes here unless the user explicitly names a legacy release.
- `Shared/LuminPalette.cs` remains only because the separate Loader still uses it.
- The WPF game overlay is current runtime code, not legacy menu code.
