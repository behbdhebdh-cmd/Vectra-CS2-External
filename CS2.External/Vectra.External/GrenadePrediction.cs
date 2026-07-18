namespace Vectra.External;

public readonly record struct CollisionTriangle(Vec3 A, Vec3 B, Vec3 C)
{
    public Vec3 Min => new(MathF.Min(A.X, MathF.Min(B.X, C.X)), MathF.Min(A.Y, MathF.Min(B.Y, C.Y)), MathF.Min(A.Z, MathF.Min(B.Z, C.Z)));
    public Vec3 Max => new(MathF.Max(A.X, MathF.Max(B.X, C.X)), MathF.Max(A.Y, MathF.Max(B.Y, C.Y)), MathF.Max(A.Z, MathF.Max(B.Z, C.Z)));
    public Vec3 Center => Scale(Add(Add(A, B), C), 1f / 3f);
    public Vec3 Normal
    {
        get
        {
            var normal = Cross(Subtract(B, A), Subtract(C, A));
            var length = Length(normal); return length > .0001f ? Scale(normal, 1 / length) : default;
        }
    }

    internal static Vec3 Add(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    internal static Vec3 Subtract(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    internal static Vec3 Scale(Vec3 value, float scale) => new(value.X * scale, value.Y * scale, value.Z * scale);
    internal static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    internal static Vec3 Cross(Vec3 a, Vec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    internal static float Length(Vec3 value) => MathF.Sqrt(Dot(value, value));
}

public interface ICollisionWorld
{
    bool SweepSphere(Vec3 from, Vec3 to, float radius, out CollisionHit hit);
    bool IntersectsSegment(Vec3 from, Vec3 to, out CollisionHit hit);
}

public readonly record struct CollisionHit(Vec3 Point, Vec3 Normal, float Fraction);

public sealed class CompositeCollisionWorld(params ICollisionWorld?[] worlds) : ICollisionWorld
{
    private readonly ICollisionWorld[] _worlds = worlds.Where(world => world is not null).Cast<ICollisionWorld>().ToArray();
    public bool SweepSphere(Vec3 from, Vec3 to, float radius, out CollisionHit hit)
    {
        hit = default; var found = false; var best = float.MaxValue;
        foreach (var world in _worlds)
        {
            if (!world.SweepSphere(from, to, radius, out var candidate) || candidate.Fraction >= best) continue;
            found = true; best = candidate.Fraction; hit = candidate;
        }
        return found;
    }

    public bool IntersectsSegment(Vec3 from, Vec3 to, out CollisionHit hit)
    {
        hit = default; var found = false; var best = float.MaxValue;
        foreach (var world in _worlds)
        {
            if (!world.IntersectsSegment(from, to, out var candidate) || candidate.Fraction >= best) continue;
            found = true; best = candidate.Fraction; hit = candidate;
        }
        return found;
    }
}

public sealed class DynamicBoundsCollisionWorld(IEnumerable<DynamicCollisionSnapshot> bounds) : ICollisionWorld
{
    private readonly DynamicCollisionSnapshot[] _bounds = bounds.ToArray();
    public bool SweepSphere(Vec3 from, Vec3 to, float radius, out CollisionHit hit)
    {
        hit = default; var direction = CollisionTriangle.Subtract(to, from); var found = false; var best = float.MaxValue;
        foreach (var bounds in _bounds)
        {
            var mins = new Vec3(bounds.Mins.X - radius, bounds.Mins.Y - radius, bounds.Mins.Z - radius);
            var maxs = new Vec3(bounds.Maxs.X + radius, bounds.Maxs.Y + radius, bounds.Maxs.Z + radius);
            if (!RayBox(from, direction, mins, maxs, out var fraction, out var normal) || fraction >= best) continue;
            best = fraction; found = true; hit = new(CollisionTriangle.Add(from, CollisionTriangle.Scale(direction, fraction)), normal, fraction);
        }
        return found;
    }

    public bool IntersectsSegment(Vec3 from, Vec3 to, out CollisionHit hit)
    {
        hit = default; var direction = CollisionTriangle.Subtract(to, from); var found = false; var best = float.MaxValue;
        foreach (var bounds in _bounds)
        {
            if (!RayBox(from, direction, bounds.Mins, bounds.Maxs, out var fraction, out var normal) || fraction <= 1e-4f || fraction >= best) continue;
            best = fraction; found = true; hit = new(CollisionTriangle.Add(from, CollisionTriangle.Scale(direction, fraction)), normal, fraction);
        }
        return found;
    }

    private static bool RayBox(Vec3 origin, Vec3 direction, Vec3 mins, Vec3 maxs, out float fraction, out Vec3 normal)
    {
        var near = 0f; var far = 1f; normal = default;
        for (var axis = 0; axis < 3; axis++)
        {
            var o = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z; var d = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
            var min = axis == 0 ? mins.X : axis == 1 ? mins.Y : mins.Z; var max = axis == 0 ? maxs.X : axis == 1 ? maxs.Y : maxs.Z;
            if (MathF.Abs(d) < 1e-6f) { if (o < min || o > max) { fraction = 0; return false; } continue; }
            var first = (min - o) / d; var second = (max - o) / d; var sign = -MathF.Sign(d);
            if (first > second) (first, second) = (second, first);
            if (first > near) { near = first; normal = axis == 0 ? new Vec3(sign, 0, 0) : axis == 1 ? new Vec3(0, sign, 0) : new Vec3(0, 0, sign); }
            far = MathF.Min(far, second); if (near > far) { fraction = 0; return false; }
        }
        fraction = near; return near is >= 0 and <= 1;
    }
}

public sealed class TriangleCollisionWorld : ICollisionWorld
{
    private const int LeafSize = 16;
    private readonly CollisionTriangle[] _triangles;
    private readonly int[] _indices;
    private readonly List<Node> _nodes = new();

    public TriangleCollisionWorld(IEnumerable<CollisionTriangle> triangles)
    {
        _triangles = triangles.Where(ValidTriangle).ToArray();
        _indices = Enumerable.Range(0, _triangles.Length).ToArray();
        if (_triangles.Length > 0) Build(0, _triangles.Length);
    }

    public int TriangleCount => _triangles.Length;

    public bool SweepSphere(Vec3 from, Vec3 to, float radius, out CollisionHit hit)
    {
        hit = default;
        if (_nodes.Count == 0 || !Vec3.IsFinite(from) || !Vec3.IsFinite(to) || !float.IsFinite(radius) || radius <= 0) return false;
        var best = float.MaxValue; var found = false;
        var stack = new Stack<int>(); stack.Push(0);
        while (stack.Count > 0)
        {
            var node = _nodes[stack.Pop()];
            if (!SegmentIntersectsExpandedBounds(from, to, node.Min, node.Max, radius)) continue;
            if (!node.Leaf) { stack.Push(node.Left); stack.Push(node.Right); continue; }
            for (var i = node.Start; i < node.Start + node.Count; i++)
            {
                var triangle = _triangles[_indices[i]];
                if (!SweepTriangle(from, to, radius, triangle, out var candidate) || candidate.Fraction >= best) continue;
                best = candidate.Fraction; hit = candidate; found = true;
            }
        }
        return found;
    }

    public bool IntersectsSegment(Vec3 from, Vec3 to, out CollisionHit hit)
    {
        hit = default;
        if (_nodes.Count == 0 || !Vec3.IsFinite(from) || !Vec3.IsFinite(to)) return false;
        var direction = CollisionTriangle.Subtract(to, from); var best = 1f; var found = false;
        var stack = new Stack<int>(); stack.Push(0);
        while (stack.Count > 0)
        {
            var node = _nodes[stack.Pop()];
            if (!SegmentIntersectsExpandedBounds(from, to, node.Min, node.Max, 0)) continue;
            if (!node.Leaf) { stack.Push(node.Left); stack.Push(node.Right); continue; }
            for (var i = node.Start; i < node.Start + node.Count; i++)
            {
                var triangle = _triangles[_indices[i]];
                if (!SegmentTriangle(from, direction, triangle, out var fraction) || fraction >= best) continue;
                var normal = triangle.Normal;
                if (CollisionTriangle.Dot(normal, direction) > 0) normal = CollisionTriangle.Scale(normal, -1);
                best = fraction; found = true; hit = new(CollisionTriangle.Add(from, CollisionTriangle.Scale(direction, fraction)), normal, fraction);
            }
        }
        return found;
    }

    private static bool SegmentTriangle(Vec3 origin, Vec3 direction, CollisionTriangle triangle, out float fraction)
    {
        fraction = 0; const float epsilon = 1e-5f;
        var edge1 = CollisionTriangle.Subtract(triangle.B, triangle.A); var edge2 = CollisionTriangle.Subtract(triangle.C, triangle.A);
        var p = CollisionTriangle.Cross(direction, edge2); var determinant = CollisionTriangle.Dot(edge1, p);
        if (MathF.Abs(determinant) < epsilon) return false;
        var inverse = 1f / determinant; var s = CollisionTriangle.Subtract(origin, triangle.A);
        var u = inverse * CollisionTriangle.Dot(s, p); if (u < -epsilon || u > 1 + epsilon) return false;
        var q = CollisionTriangle.Cross(s, edge1); var v = inverse * CollisionTriangle.Dot(direction, q);
        if (v < -epsilon || u + v > 1 + epsilon) return false;
        fraction = inverse * CollisionTriangle.Dot(edge2, q);
        return fraction > 1e-4f && fraction < 1f - 1e-4f;
    }

    private int Build(int start, int count)
    {
        var min = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue); var max = new Vec3(float.MinValue, float.MinValue, float.MinValue);
        for (var i = start; i < start + count; i++) { var triangle = _triangles[_indices[i]]; min = Min(min, triangle.Min); max = Max(max, triangle.Max); }
        var index = _nodes.Count; _nodes.Add(default);
        if (count <= LeafSize) { _nodes[index] = new(min, max, start, count, -1, -1); return index; }
        var extent = CollisionTriangle.Subtract(max, min); var axis = extent.Y > extent.X ? 1 : 0; if ((axis == 0 ? extent.X : extent.Y) < extent.Z) axis = 2;
        Array.Sort(_indices, start, count, Comparer<int>.Create((left, right) => Axis(_triangles[left].Center, axis).CompareTo(Axis(_triangles[right].Center, axis))));
        var leftCount = count / 2; var left = Build(start, leftCount); var right = Build(start + leftCount, count - leftCount);
        _nodes[index] = new(min, max, start, count, left, right); return index;
    }

    private static bool SweepTriangle(Vec3 from, Vec3 to, float radius, CollisionTriangle triangle, out CollisionHit hit)
    {
        hit = default; const int subdivisions = 12;
        for (var step = 0; step <= subdivisions; step++)
        {
            var fraction = step / (float)subdivisions;
            var point = Lerp(from, to, fraction); var closest = ClosestPoint(point, triangle);
            var delta = CollisionTriangle.Subtract(point, closest);
            if (CollisionTriangle.Dot(delta, delta) > radius * radius) continue;
            var normal = triangle.Normal;
            if (CollisionTriangle.Length(normal) < .5f) return false;
            var movement = CollisionTriangle.Subtract(to, from); if (CollisionTriangle.Dot(normal, movement) > 0) normal = CollisionTriangle.Scale(normal, -1);
            hit = new(point, normal, fraction); return true;
        }
        return false;
    }

    internal static Vec3 ClosestPoint(Vec3 point, CollisionTriangle triangle)
    {
        var ab = CollisionTriangle.Subtract(triangle.B, triangle.A); var ac = CollisionTriangle.Subtract(triangle.C, triangle.A); var ap = CollisionTriangle.Subtract(point, triangle.A);
        var d1 = CollisionTriangle.Dot(ab, ap); var d2 = CollisionTriangle.Dot(ac, ap); if (d1 <= 0 && d2 <= 0) return triangle.A;
        var bp = CollisionTriangle.Subtract(point, triangle.B); var d3 = CollisionTriangle.Dot(ab, bp); var d4 = CollisionTriangle.Dot(ac, bp); if (d3 >= 0 && d4 <= d3) return triangle.B;
        var vc = d1 * d4 - d3 * d2; if (vc <= 0 && d1 >= 0 && d3 <= 0) return CollisionTriangle.Add(triangle.A, CollisionTriangle.Scale(ab, d1 / (d1 - d3)));
        var cp = CollisionTriangle.Subtract(point, triangle.C); var d5 = CollisionTriangle.Dot(ab, cp); var d6 = CollisionTriangle.Dot(ac, cp); if (d6 >= 0 && d5 <= d6) return triangle.C;
        var vb = d5 * d2 - d1 * d6; if (vb <= 0 && d2 >= 0 && d6 <= 0) return CollisionTriangle.Add(triangle.A, CollisionTriangle.Scale(ac, d2 / (d2 - d6)));
        var va = d3 * d6 - d5 * d4; if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return CollisionTriangle.Add(triangle.B, CollisionTriangle.Scale(CollisionTriangle.Subtract(triangle.C, triangle.B), (d4 - d3) / ((d4 - d3) + (d5 - d6))));
        var denominator = 1f / (va + vb + vc); return CollisionTriangle.Add(triangle.A, CollisionTriangle.Add(CollisionTriangle.Scale(ab, vb * denominator), CollisionTriangle.Scale(ac, vc * denominator)));
    }

    private static bool SegmentIntersectsExpandedBounds(Vec3 from, Vec3 to, Vec3 min, Vec3 max, float radius)
    {
        min = new(min.X - radius, min.Y - radius, min.Z - radius); max = new(max.X + radius, max.Y + radius, max.Z + radius);
        var tMin = 0f; var tMax = 1f;
        return Slab(from.X, to.X - from.X, min.X, max.X, ref tMin, ref tMax) && Slab(from.Y, to.Y - from.Y, min.Y, max.Y, ref tMin, ref tMax) && Slab(from.Z, to.Z - from.Z, min.Z, max.Z, ref tMin, ref tMax);
    }

    private static bool Slab(float origin, float direction, float min, float max, ref float tMin, ref float tMax)
    {
        if (MathF.Abs(direction) < .00001f) return origin >= min && origin <= max;
        var inverse = 1 / direction; var first = (min - origin) * inverse; var second = (max - origin) * inverse; if (first > second) (first, second) = (second, first);
        tMin = MathF.Max(tMin, first); tMax = MathF.Min(tMax, second); return tMin <= tMax;
    }

    private static bool ValidTriangle(CollisionTriangle triangle) => Vec3.IsFinite(triangle.A) && Vec3.IsFinite(triangle.B) && Vec3.IsFinite(triangle.C) && CollisionTriangle.Length(triangle.Normal) > .5f;
    private static Vec3 Min(Vec3 a, Vec3 b) => new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Min(a.Z, b.Z));
    private static Vec3 Max(Vec3 a, Vec3 b) => new(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y), MathF.Max(a.Z, b.Z));
    private static float Axis(Vec3 value, int axis) => axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;
    private static Vec3 Lerp(Vec3 a, Vec3 b, float amount) => new(a.X + (b.X - a.X) * amount, a.Y + (b.Y - a.Y) * amount, a.Z + (b.Z - a.Z) * amount);
    private readonly record struct Node(Vec3 Min, Vec3 Max, int Start, int Count, int Left, int Right) { public bool Leaf => Left < 0; }
}

public static class GrenadeTrajectoryMath
{
    public const float Gravity = 800f;
    public const float TickInterval = 1f / 64f;
    public const int MaximumSteps = 320;
    public const int MaximumBounces = 10;

    public static Vec3 Direction(Angles viewAngles)
    {
        var pitch = viewAngles.Pitch;
        if (pitch < -89) pitch += 360; else if (pitch > 89) pitch -= 360;
        pitch -= (90 - MathF.Abs(pitch)) * 10 / 90;
        var pitchRadians = pitch * MathF.PI / 180; var yawRadians = viewAngles.Yaw * MathF.PI / 180; var cosine = MathF.Cos(pitchRadians);
        return new(MathF.Cos(yawRadians) * cosine, MathF.Sin(yawRadians) * cosine, -MathF.Sin(pitchRadians));
    }

    public static Vec3 InitialVelocity(GrenadeThrowState state)
    {
        var direction = Direction(state.ViewAngles); var speed = (Math.Clamp(state.ThrowStrength, 0, 1) * .7f + .3f) * 1115f;
        var movement = new Vec3(Math.Clamp(state.PlayerVelocity.X, -4000, 4000), Math.Clamp(state.PlayerVelocity.Y, -4000, 4000), Math.Clamp(state.PlayerVelocity.Z, -4000, 4000));
        return new(direction.X * speed + movement.X * 1.25f, direction.Y * speed + movement.Y * 1.25f, direction.Z * speed + movement.Z * 1.25f);
    }

    public static GrenadeTrajectory Simulate(GrenadeThrowState state, ICollisionWorld? collisionWorld = null)
    {
        if (!state.Available || !Vec3.IsFinite(state.StartPosition) || !Vec3.IsFinite(state.PlayerVelocity) || !Angles.IsFinite(state.ViewAngles) || !float.IsFinite(state.ThrowStrength) || !float.IsFinite(state.ProjectileRadius)) return GrenadeTrajectory.Unavailable;
        var direction = Direction(state.ViewAngles); var position = CollisionTriangle.Add(state.StartPosition, CollisionTriangle.Scale(direction, 16)); var velocity = InitialVelocity(state);
        var points = new List<Vec3>(MaximumSteps / 2 + 2) { position }; var bounces = new List<int>(); var bounceCount = 0;
        for (var step = 0; step < MaximumSteps && bounceCount < MaximumBounces; step++)
        {
            velocity = new(velocity.X, velocity.Y, velocity.Z - Gravity * TickInterval);
            var next = CollisionTriangle.Add(position, CollisionTriangle.Scale(velocity, TickInterval));
            if (collisionWorld is not null && collisionWorld.SweepSphere(position, next, Math.Clamp(state.ProjectileRadius, 2, 8), out var hit))
            {
                position = CollisionTriangle.Add(hit.Point, CollisionTriangle.Scale(hit.Normal, Math.Clamp(state.ProjectileRadius, 2, 8) * .12f));
                var normalVelocity = CollisionTriangle.Dot(velocity, hit.Normal); var normal = CollisionTriangle.Scale(hit.Normal, normalVelocity); var tangent = CollisionTriangle.Subtract(velocity, normal);
                velocity = CollisionTriangle.Add(CollisionTriangle.Scale(tangent, .4f), CollisionTriangle.Scale(normal, -.45f));
                bounceCount++; points.Add(position); bounces.Add(points.Count - 1);
            }
            else position = next;
            if ((step & 1) == 1) points.Add(position);
            if (CollisionTriangle.Length(velocity) < 20 && bounceCount > 0) break;
        }
        if (!points[^1].Equals(position)) points.Add(position);
        return new(points, bounces, state.Kind, collisionWorld is null ? TrajectoryQuality.Approximate : TrajectoryQuality.CollisionAware, DateTimeOffset.UtcNow);
    }
}

public sealed class GrenadePredictionService : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly Task _worker;
    private GrenadeThrowState _pending = GrenadeThrowState.Unavailable;
    private ICollisionWorld? _world;
    private GrenadeTrajectory _latest = GrenadeTrajectory.Unavailable;

    public GrenadePredictionService() => _worker = Task.Run(WorkerLoop);
    public GrenadeTrajectory Latest => Volatile.Read(ref _latest);

    public void SetCollisionWorld(ICollisionWorld? world) => Volatile.Write(ref _world, world);

    public void Submit(GrenadeThrowState state)
    {
        Volatile.Write(ref _pending, state);
        if (!state.Available) Volatile.Write(ref _latest, GrenadeTrajectory.Unavailable);
        try { _signal.Release(); } catch (SemaphoreFullException) { }
    }

    private async Task WorkerLoop()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try { await _signal.WaitAsync(_cancellation.Token); } catch (OperationCanceledException) { break; }
            var request = Volatile.Read(ref _pending); if (!request.Available) continue;
            var world = Volatile.Read(ref _world);
            var result = GrenadeTrajectoryMath.Simulate(request, world);
            if (ReferenceEquals(request, Volatile.Read(ref _pending))) Volatile.Write(ref _latest, result);
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel(); try { _worker.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _signal.Dispose(); _cancellation.Dispose();
    }
}
