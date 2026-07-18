using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Vectra.External;

public sealed class MapCollisionProvider : IDisposable
{
    internal const int CacheVersion = 2;
    internal const string ParserVersion = "ValveResourceFormat-19.2.6339";
    private readonly object _gate = new();
    private CancellationTokenSource? _loadCancellation;
    private string _requestKey = string.Empty;
    private ICollisionWorld? _world;
    private GrenadePredictionReport _report = GrenadePredictionReport.Unavailable;
    private IReadOnlyList<string> _availableMaps = Array.Empty<string>();

    public ICollisionWorld? CollisionWorld { get { lock (_gate) return _world; } }
    public GrenadePredictionReport Report { get { lock (_gate) return _report; } }
    public IReadOnlyList<string> AvailableMaps { get { lock (_gate) return _availableMaps; } }

    public void Update(bool enabled, string requestedMap, GameProcessSession session)
    {
        if (!enabled || session.ProcessId == 0 || string.IsNullOrWhiteSpace(session.ExecutablePath)) { Disable(); return; }
        var mapsDirectory = FindMapsDirectory(session.ExecutablePath);
        if (mapsDirectory is null)
        {
            SetReport(new(MapCollisionStatus.Unavailable, requestedMap, "CS2 maps directory was not found")); return;
        }

        RefreshAvailableMaps(mapsDirectory);
        var safeRequest = SanitizeMap(requestedMap);
        var key = $"{session.ProcessId}|{mapsDirectory}|{safeRequest}";
        lock (_gate) if (string.Equals(_requestKey, key, StringComparison.OrdinalIgnoreCase)) return;
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            _loadCancellation?.Cancel(); _loadCancellation?.Dispose();
            _loadCancellation = cancellation = new CancellationTokenSource(); _requestKey = key; _world = null;
            _report = new(MapCollisionStatus.Loading, safeRequest, "Loading map collision in the background");
        }
        _ = Task.Run(() => LoadAsync(mapsDirectory, safeRequest, session.ProcessId, key, cancellation.Token));
    }

    private async Task LoadAsync(string mapsDirectory, string requestedMap, int processId, string key, CancellationToken cancellationToken)
    {
        try
        {
            var mapPath = !string.Equals(requestedMap, "Auto", StringComparison.OrdinalIgnoreCase)
                ? FindMapPath(mapsDirectory, requestedMap)
                : await Task.Run(() => RestartManagerMapResolver.FindActiveMap(mapsDirectory, processId, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (mapPath is null)
            {
                Complete(key, null, new(MapCollisionStatus.Approximate, requestedMap, "Map could not be selected uniquely; choose it in Visuals")); return;
            }

            var info = new FileInfo(mapPath); var mapName = MapBaseName(mapPath);
            var cached = TryReadCache(mapName, info, cancellationToken);
            var parsed = cached is null ? ParseMap(mapPath, cancellationToken) : new MapParseResult(cached, 0, 0, 0, string.Empty);
            var triangles = parsed.Triangles;
            cancellationToken.ThrowIfCancellationRequested();
            if (triangles.Count == 0)
            {
                var detail = parsed.FirstError.Length > 0 ? parsed.FirstError : "No compatible physics meshes found";
                throw new InvalidDataException($"{detail}; resources {parsed.PhysicsResources}, meshes {parsed.PhysicsMeshes}, errors {parsed.ParserErrors}");
            }
            if (!CacheExists(mapName, info)) WriteCache(mapName, info, triangles, cancellationToken);
            var world = new TriangleCollisionWorld(triangles);
            if (world.TriangleCount == 0) throw new InvalidDataException("Physics resources contained no valid collision triangles");
            var source = cached is null ? $"{parsed.PhysicsResources} resources · {parsed.PhysicsMeshes} meshes" : "cache";
            Complete(key, world, new(MapCollisionStatus.Ready, mapName, $"Collision ready · {world.TriangleCount:N0} triangles · {source}", parsed.PhysicsResources, parsed.PhysicsMeshes, world.TriangleCount, parsed.ParserErrors));
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            var detail = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : $"{error.GetType().Name}: {error.Message}";
            Complete(key, null, new(MapCollisionStatus.Failed, requestedMap, $"Approximate only · {Truncate(detail, 180)}"));
        }
    }

    private void Complete(string key, ICollisionWorld? world, GrenadePredictionReport report)
    {
        lock (_gate) { if (!string.Equals(_requestKey, key, StringComparison.Ordinal)) return; _world = world; _report = report; }
    }

    private void Disable()
    {
        lock (_gate)
        {
            if (_requestKey.Length == 0 && _world is null) return;
            _loadCancellation?.Cancel(); _loadCancellation?.Dispose(); _loadCancellation = null;
            _requestKey = string.Empty; _world = null; _report = GrenadePredictionReport.Unavailable;
        }
    }

    private void SetReport(GrenadePredictionReport report) { lock (_gate) _report = report; }

    private void RefreshAvailableMaps(string mapsDirectory)
    {
        lock (_gate) if (_availableMaps.Count > 0) return;
        try
        {
            var names = Directory.EnumerateFiles(mapsDirectory, "*.vpk", SearchOption.TopDirectoryOnly)
                .Where(IsPlayableMapArchive).Select(MapBaseName)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
            lock (_gate) _availableMaps = names;
        }
        catch { }
    }

    internal static string? FindMapsDirectory(string executablePath)
    {
        try
        {
            var directory = new FileInfo(executablePath).Directory;
            while (directory is not null && !directory.Name.Equals("game", StringComparison.OrdinalIgnoreCase)) directory = directory.Parent;
            var maps = directory is null ? null : Path.Combine(directory.FullName, "csgo", "maps");
            return maps is not null && Directory.Exists(maps) ? maps : null;
        }
        catch { return null; }
    }

    internal static string SanitizeMap(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("Auto", StringComparison.OrdinalIgnoreCase)) return "Auto";
        var trimmed = value.Trim();
        if (trimmed.Contains('/') || trimmed.Contains('\\') || trimmed.Contains("..", StringComparison.Ordinal)) return "Auto";
        var name = Path.GetFileNameWithoutExtension(trimmed);
        return name.Length is > 0 and <= 80 && name.All(character => char.IsLetterOrDigit(character) || character is '_' or '-') ? name : "Auto";
    }

    private static string? FindMapPath(string directory, string map)
        => Directory.EnumerateFiles(directory, "*.vpk", SearchOption.TopDirectoryOnly).Where(IsPlayableMapArchive).FirstOrDefault(path => MapBaseName(path).Equals(map, StringComparison.OrdinalIgnoreCase));

    internal static bool IsPlayableMapArchive(string path)
    {
        var name = MapBaseName(path);
        return !name.EndsWith("_vanity", StringComparison.OrdinalIgnoreCase) &&
            (name.StartsWith("de_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("cs_", StringComparison.OrdinalIgnoreCase));
    }

    private static string MapBaseName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.EndsWith("_dir", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private static MapParseResult ParseMap(string mapPath, CancellationToken cancellationToken)
    {
        var triangles = new List<CollisionTriangle>(250_000);
        var physicsResources = 0; var physicsMeshes = 0; var parserErrors = 0; var firstError = string.Empty;
        using var package = new Package(); package.Read(mapPath);
        if (package.Entries is null) return new(triangles, 0, 0, 0, "VPK contains no entries");
        foreach (var entry in package.Entries.SelectMany(pair => pair.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullName = entry.DirectoryName + "/" + entry.FileName;
            if (!IsPhysicsResourceEntry(entry.TypeName, fullName)) continue;
            physicsResources++;
            try
            {
                using var stream = package.GetMemoryMappedStreamIfPossible(entry);
                using var resource = new Resource(); resource.FileName = fullName; resource.Read(stream, false, true);
                if ((resource.GetBlockByType(BlockType.PHYS) ?? resource.DataBlock) is not PhysAggregateData physics) continue;
                foreach (var part in physics.Parts)
                foreach (var descriptor in part.Shape.Meshes)
                {
                    physicsMeshes++;
                    var vertices = descriptor.Shape.GetVertices(); var indices = descriptor.Shape.GetTriangles();
                    foreach (var triangle in indices)
                    {
                        var x = checked((int)triangle.X); var y = checked((int)triangle.Y); var z = checked((int)triangle.Z);
                        if ((uint)x >= vertices.Length || (uint)y >= vertices.Length || (uint)z >= vertices.Length) continue;
                        var a = new Vec3(vertices[x].X, vertices[x].Y, vertices[x].Z); var b = new Vec3(vertices[y].X, vertices[y].Y, vertices[y].Z); var c = new Vec3(vertices[z].X, vertices[z].Y, vertices[z].Z);
                        if (Vec3.IsFinite(a) && Vec3.IsFinite(b) && Vec3.IsFinite(c)) triangles.Add(new(a, b, c));
                        if (triangles.Count >= 2_000_000) return new(triangles, physicsResources, physicsMeshes, parserErrors, firstError);
                    }
                }
            }
            catch (Exception error)
            {
                parserErrors++;
                if (firstError.Length == 0) firstError = $"{fullName}: {error.GetType().Name}: {error.Message}";
            }
        }
        return new(triangles, physicsResources, physicsMeshes, parserErrors, firstError);
    }

    internal static bool IsPhysicsResourceEntry(string typeName, string fullName)
        => (typeName.Equals("vphys_c", StringComparison.OrdinalIgnoreCase) &&
            (fullName.Contains("world", StringComparison.OrdinalIgnoreCase) || fullName.Contains("physics", StringComparison.OrdinalIgnoreCase))) ||
           (typeName.Equals("vmdl_c", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(fullName).Equals("world_physics", StringComparison.OrdinalIgnoreCase));

    internal static MapCollisionInspection InspectMap(string mapPath, CancellationToken cancellationToken = default)
    {
        var result = ParseMap(mapPath, cancellationToken);
        var world = new TriangleCollisionWorld(result.Triangles);
        return new(result.PhysicsResources, result.PhysicsMeshes, world.TriangleCount, result.ParserErrors, result.FirstError);
    }

    internal static IReadOnlyList<string> InspectPackageEntries(string mapPath)
    {
        using var package = new Package(); package.Read(mapPath);
        if (package.Entries is null) return Array.Empty<string>();
        var entries = package.Entries.SelectMany(pair => pair.Value).ToArray();
        var matching = entries.Select(entry => $"{entry.TypeName}|{entry.DirectoryName}/{entry.FileName}")
            .Where(value => value.Contains("phys", StringComparison.OrdinalIgnoreCase) || value.Contains("world", StringComparison.OrdinalIgnoreCase))
            .Take(200);
        var types = entries.GroupBy(entry => entry.TypeName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count()).Take(30).Select(group => $"TYPE {group.Key}={group.Count()}");
        return types.Concat(matching).ToArray();
    }

    internal static string CachePath(string mapName, FileInfo source)
    {
        var fingerprint = $"{mapName}|{source.Length}|{source.LastWriteTimeUtc.Ticks}|{ParserVersion}|{CacheVersion}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))[..20];
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vectra External", "map-cache");
        return Path.Combine(root, $"{mapName}-{hash}.vmbvh");
    }

    private static bool CacheExists(string mapName, FileInfo source) => File.Exists(CachePath(mapName, source));
    private static IReadOnlyList<CollisionTriangle>? TryReadCache(string mapName, FileInfo source, CancellationToken cancellationToken)
    {
        var path = CachePath(mapName, source); if (!File.Exists(path)) return null;
        try
        {
            using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8, false);
            if (reader.ReadUInt32() != 0x48564256 || reader.ReadInt32() != CacheVersion || reader.ReadInt64() != source.Length || reader.ReadInt64() != source.LastWriteTimeUtc.Ticks || reader.ReadString() != ParserVersion) return null;
            var count = reader.ReadInt32(); if (count <= 0 || count > 2_000_000) return null;
            var result = new CollisionTriangle[count];
            for (var i = 0; i < count; i++) { cancellationToken.ThrowIfCancellationRequested(); result[i] = new(ReadVec(reader), ReadVec(reader), ReadVec(reader)); }
            return result;
        }
        catch { return null; }
    }

    private static void WriteCache(string mapName, FileInfo source, IReadOnlyList<CollisionTriangle> triangles, CancellationToken cancellationToken)
    {
        var path = CachePath(mapName, source); Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var writer = new BinaryWriter(File.Create(temporary), Encoding.UTF8, false))
            {
                writer.Write(0x48564256u); writer.Write(CacheVersion); writer.Write(source.Length); writer.Write(source.LastWriteTimeUtc.Ticks); writer.Write(ParserVersion); writer.Write(triangles.Count);
                foreach (var triangle in triangles) { cancellationToken.ThrowIfCancellationRequested(); WriteVec(writer, triangle.A); WriteVec(writer, triangle.B); WriteVec(writer, triangle.C); }
            }
            File.Move(temporary, path, true);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }

    private static Vec3 ReadVec(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    private static void WriteVec(BinaryWriter writer, Vec3 value) { writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); }

    private static string Truncate(string value, int maximumLength) => value.Length <= maximumLength ? value : value[..maximumLength];

    private sealed record MapParseResult(IReadOnlyList<CollisionTriangle> Triangles, int PhysicsResources, int PhysicsMeshes, int ParserErrors, string FirstError);

    public void Dispose() => Disable();
}

internal sealed record MapCollisionInspection(int PhysicsResources, int PhysicsMeshes, int Triangles, int ParserErrors, string FirstError);

internal static class RestartManagerMapResolver
{
    private const int ErrorMoreData = 234;
    public static string? FindActiveMap(string mapsDirectory, int processId, CancellationToken cancellationToken)
    {
        var matches = new List<string>();
        foreach (var path in Directory.EnumerateFiles(mapsDirectory, "*.vpk", SearchOption.TopDirectoryOnly).Where(MapCollisionProvider.IsPlayableMapArchive))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (UsesFile(path, processId)) { matches.Add(path); if (matches.Count > 1) return null; }
        }
        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool UsesFile(string path, int processId)
    {
        var key = Guid.NewGuid().ToString("N"); if (RmStartSession(out var session, 0, key) != 0) return false;
        try
        {
            if (RmRegisterResources(session, 1, new[] { path }, 0, null, 0, null) != 0) return false;
            uint needed = 0, count = 0, reason = 0; var result = RmGetList(session, out needed, ref count, null, ref reason);
            if (result != ErrorMoreData || needed == 0) return false;
            var processes = new RmProcessInfo[needed]; count = needed; result = RmGetList(session, out needed, ref count, processes, ref reason);
            return result == 0 && processes.Take((int)count).Any(item => item.Process.dwProcessId == processId);
        }
        finally { RmEndSession(session); }
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmStartSession(out uint sessionHandle, int flags, string sessionKey);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmRegisterResources(uint sessionHandle, uint fileCount, string[] fileNames, uint applicationCount, RmUniqueProcess[]? applications, uint serviceCount, string[]? serviceNames);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmGetList(uint sessionHandle, out uint needed, ref uint count, [In, Out] RmProcessInfo[]? processes, ref uint rebootReasons);
    [DllImport("rstrtmgr.dll")] private static extern int RmEndSession(uint sessionHandle);

    [StructLayout(LayoutKind.Sequential)] private struct RmUniqueProcess { public int dwProcessId; public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct RmProcessInfo
    {
        public RmUniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ApplicationName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ServiceShortName;
        public uint ApplicationType; public uint AppStatus; public uint TsSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }
}
