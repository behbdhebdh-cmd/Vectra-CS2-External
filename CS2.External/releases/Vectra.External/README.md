# Vectra External release layout

Release manifests are separated by the menu generation they use. Complete binary packages are published as ZIP assets on the repository's GitHub Releases page so large generated binaries do not inflate Git history.

| Folder | Meaning |
| --- | --- |
| `legacy-wpf/versions/` | Manifests for archived releases through 1.12.1 with the retired WPF settings menu. |
| `native-imgui/versions/` | Manifests for current releases from 1.13.0 onward using `ui/native-imgui`. |
| `latest.txt` | Version tag and generation path of the current official native release. |

Each GitHub Release asset contains the full original package directory, including the matching `release.json`. Verify its SHA-256 entries before running a package.

Never place a native-ImGui release in `legacy-wpf`, and never modify an archived legacy package to use the new menu.
