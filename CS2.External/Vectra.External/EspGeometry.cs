namespace Vectra.External;

public readonly record struct ScreenRect(float Left, float Top, float Right, float Bottom)
{
    public float Width => Right - Left;
    public float Height => Bottom - Top;
}

public static class EspGeometry
{
    private static readonly Vec3 DefaultMins = new(-16, -16, 0);
    private static readonly Vec3 DefaultMaxs = new(16, 16, 72);

    public static bool TryGetBounds(PlayerSnapshot player, Vec3 origin, float[] matrix, double width, double height, out ScreenRect result)
    {
        return TryGetBounds(origin, player.CollisionMins, player.CollisionMaxs, player.HasCollisionBounds, matrix, width, height, out result);
    }

    private static bool TryGetBounds(Vec3 origin, Vec3 collisionMins, Vec3 collisionMaxs, bool hasCollisionBounds, float[] matrix, double width, double height, out ScreenRect result)
    {
        result = default;
        if (matrix.Length != 16 || !Vec3.IsFinite(origin) || width <= 1 || height <= 1) return false;
        var mins = hasCollisionBounds ? collisionMins : DefaultMins;
        var maxs = hasCollisionBounds ? collisionMaxs : DefaultMaxs;
        var minX = double.MaxValue; var minY = double.MaxValue; var maxX = double.MinValue; var maxY = double.MinValue; var projectedCount = 0;
        for (var corner = 0; corner < 8; corner++) {
            var point = new Vec3(origin.X + ((corner & 1) != 0 ? maxs.X : mins.X), origin.Y + ((corner & 2) != 0 ? maxs.Y : mins.Y), origin.Z + ((corner & 4) != 0 ? maxs.Z : mins.Z));
            if (!WorldProjection.TryProject(point, matrix, width, height, out var projected)) continue;
            projectedCount++;
            minX = Math.Min(minX, projected.X); minY = Math.Min(minY, projected.Y); maxX = Math.Max(maxX, projected.X); maxY = Math.Max(maxY, projected.Y);
        }
        if (projectedCount < 4 || maxX < 0 || minX > width || maxY < 0 || minY > height) return false;
        var left = Math.Clamp(minX, 0, width); var top = Math.Clamp(minY, 0, height); var right = Math.Clamp(maxX, 0, width); var bottom = Math.Clamp(maxY, 0, height);
        result = new ScreenRect((float)left, (float)top, (float)right, (float)bottom);
        return result.Width >= 2 && result.Width <= 5000 && result.Height >= 8 && result.Height <= 5000;
    }
}
