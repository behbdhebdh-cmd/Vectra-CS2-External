using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vectra.Loader;

public sealed class ReleasePackageResolver(string releaseDirectory) : IReleasePackageResolver
{
    private readonly string _releaseDirectory = Path.GetFullPath(releaseDirectory);

    public async Task<LoaderPackage> ResolveAsync(CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(_releaseDirectory, "release.json");
        if (!File.Exists(manifestPath)) throw new FileNotFoundException("The release manifest is missing.", manifestPath);

        ReleaseManifest? manifest;
        try
        {
            await using var manifestStream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<ReleaseManifest>(manifestStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("The release manifest is invalid.", error);
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.ExternalExecutable) || string.IsNullOrWhiteSpace(manifest.ExternalSha256)
            || string.IsNullOrWhiteSpace(manifest.NativeMenu) || string.IsNullOrWhiteSpace(manifest.NativeMenuSha256))
            throw new InvalidDataException("The release manifest does not identify the External client and native menu.");
        if (!IsSha256(manifest.ExternalSha256)) throw new InvalidDataException("The External SHA-256 in the release manifest is invalid.");
        if (!IsSha256(manifest.NativeMenuSha256)) throw new InvalidDataException("The native menu SHA-256 in the release manifest is invalid.");

        var executable = ResolveContainedPath(manifest.ExternalExecutable);
        if (!File.Exists(executable)) throw new FileNotFoundException("The packaged External executable is missing.", executable);
        var actualHash = await ComputeSha256Async(executable, cancellationToken).ConfigureAwait(false);
        if (!actualHash.Equals(manifest.ExternalSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The packaged External executable failed SHA-256 validation.");

        var nativeMenu = ResolveContainedPath(manifest.NativeMenu);
        if (!File.Exists(nativeMenu)) throw new FileNotFoundException("The packaged native menu is missing.", nativeMenu);
        var actualNativeHash = await ComputeSha256Async(nativeMenu, cancellationToken).ConfigureAwait(false);
        if (!actualNativeHash.Equals(manifest.NativeMenuSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The packaged native menu failed SHA-256 validation.");

        return new LoaderPackage(_releaseDirectory, executable, actualHash, nativeMenu, actualNativeHash);
    }

    private string ResolveContainedPath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new InvalidDataException("The External path in the release manifest must be relative.");
        var resolved = Path.GetFullPath(Path.Combine(_releaseDirectory, relativePath));
        var prefix = _releaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The External path in the release manifest leaves the release folder.");
        return resolved;
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private sealed class ReleaseManifest
    {
        [JsonPropertyName("executable")]
        public string ExternalExecutable { get; init; } = string.Empty;

        [JsonPropertyName("executable_sha256")]
        public string ExternalSha256 { get; init; } = string.Empty;

        [JsonPropertyName("native_menu")]
        public string NativeMenu { get; init; } = string.Empty;

        [JsonPropertyName("native_menu_sha256")]
        public string NativeMenuSha256 { get; init; } = string.Empty;
    }
}
