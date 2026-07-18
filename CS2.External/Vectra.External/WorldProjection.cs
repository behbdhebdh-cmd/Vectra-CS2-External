namespace Vectra.External;

public readonly record struct ScreenPoint(float X, float Y);

public static class WorldProjection
{
    public static bool TryProject(Vec3 point, float[] matrix, double width, double height, out ScreenPoint screen, bool requireVisible = false)
    {
        screen = default;
        if (matrix.Length != 16 || !Vec3.IsFinite(point) || !double.IsFinite(width) || !double.IsFinite(height) || width <= 1 || height <= 1) return false;
        var x = point.X * matrix[0] + point.Y * matrix[1] + point.Z * matrix[2] + matrix[3];
        var y = point.X * matrix[4] + point.Y * matrix[5] + point.Z * matrix[6] + matrix[7];
        var w = point.X * matrix[12] + point.Y * matrix[13] + point.Z * matrix[14] + matrix[15];
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(w) || w <= .001f) return false;
        var projectedX = width / 2 + x / w * width / 2;
        var projectedY = height / 2 - y / w * height / 2;
        if (!double.IsFinite(projectedX) || !double.IsFinite(projectedY)) return false;
        screen = new ScreenPoint((float)projectedX, (float)projectedY);
        return !requireVisible || (projectedX >= 0 && projectedX <= width && projectedY >= 0 && projectedY <= height);
    }
}
