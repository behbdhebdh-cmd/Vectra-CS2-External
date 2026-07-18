namespace Vectra.External;

public readonly record struct RadarPoint(float X, float Y, bool IsClamped);

public static class RadarGeometry
{
    public static bool TryProject(Vec3 localOrigin, Vec3 targetOrigin, float localYawDegrees, float rangeUnits, float radiusPixels, out RadarPoint point)
    {
        point = default;
        if (!Vec3.IsFinite(localOrigin) || !Vec3.IsFinite(targetOrigin) || !float.IsFinite(localYawDegrees) || !float.IsFinite(rangeUnits) || !float.IsFinite(radiusPixels) || rangeUnits <= 0 || radiusPixels <= 0) return false;

        var radians = localYawDegrees * MathF.PI / 180f;
        var deltaX = targetOrigin.X - localOrigin.X;
        var deltaY = targetOrigin.Y - localOrigin.Y;
        var right = -deltaX * MathF.Sin(radians) + deltaY * MathF.Cos(radians);
        var forward = deltaX * MathF.Cos(radians) + deltaY * MathF.Sin(radians);
        if (!float.IsFinite(right) || !float.IsFinite(forward)) return false;

        var scale = radiusPixels / rangeUnits;
        var x = right * scale;
        var y = -forward * scale;
        var distance = MathF.Sqrt(x * x + y * y);
        var isClamped = distance > radiusPixels;
        if (isClamped && distance > 0) {
            var factor = radiusPixels / distance;
            x *= factor;
            y *= factor;
        }
        point = new RadarPoint(x, y, isClamped);
        return true;
    }
}
