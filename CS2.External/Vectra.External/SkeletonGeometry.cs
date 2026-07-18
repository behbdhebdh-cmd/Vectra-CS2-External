namespace Vectra.External;

public readonly record struct SkeletonConnection(SkeletonJoint From, SkeletonJoint To);
public readonly record struct HeadMarker(ScreenPoint Center, float Radius);

public static class SkeletonGeometry
{
    public static IReadOnlyList<SkeletonConnection> Connections { get; } = new[]
    {
        new SkeletonConnection(SkeletonJoint.Head, SkeletonJoint.Neck),
        new SkeletonConnection(SkeletonJoint.Neck, SkeletonJoint.SpineUpper),
        new SkeletonConnection(SkeletonJoint.SpineUpper, SkeletonJoint.SpineLower),
        new SkeletonConnection(SkeletonJoint.SpineLower, SkeletonJoint.Pelvis),
        new SkeletonConnection(SkeletonJoint.Neck, SkeletonJoint.LeftUpperArm),
        new SkeletonConnection(SkeletonJoint.LeftUpperArm, SkeletonJoint.LeftLowerArm),
        new SkeletonConnection(SkeletonJoint.LeftLowerArm, SkeletonJoint.LeftHand),
        new SkeletonConnection(SkeletonJoint.Neck, SkeletonJoint.RightUpperArm),
        new SkeletonConnection(SkeletonJoint.RightUpperArm, SkeletonJoint.RightLowerArm),
        new SkeletonConnection(SkeletonJoint.RightLowerArm, SkeletonJoint.RightHand),
        new SkeletonConnection(SkeletonJoint.Pelvis, SkeletonJoint.LeftUpperLeg),
        new SkeletonConnection(SkeletonJoint.LeftUpperLeg, SkeletonJoint.LeftLowerLeg),
        new SkeletonConnection(SkeletonJoint.LeftLowerLeg, SkeletonJoint.LeftAnkle),
        new SkeletonConnection(SkeletonJoint.Pelvis, SkeletonJoint.RightUpperLeg),
        new SkeletonConnection(SkeletonJoint.RightUpperLeg, SkeletonJoint.RightLowerLeg),
        new SkeletonConnection(SkeletonJoint.RightLowerLeg, SkeletonJoint.RightAnkle)
    };

    public static bool TryGetHeadMarker(PlayerSnapshot player, Vec3 predictionOffset, float[] matrix, double width, double height, float fallbackBoxWidth, out HeadMarker marker)
    {
        marker = default;
        var headIndex = (int)SkeletonJoint.Head;
        var neckIndex = (int)SkeletonJoint.Neck;
        var hasHead = player.HasSkeletonJoint.Length > headIndex && player.HasSkeletonJoint[headIndex] && Vec3.IsFinite(player.SkeletonJoints[headIndex]);
        var hasNeck = player.HasSkeletonJoint.Length > neckIndex && player.HasSkeletonJoint[neckIndex] && Vec3.IsFinite(player.SkeletonJoints[neckIndex]);
        Vec3 head;
        Vec3? neck = null;
        if (hasHead)
        {
            head = Add(player.SkeletonJoints[headIndex], predictionOffset);
            if (hasNeck) neck = Add(player.SkeletonJoints[neckIndex], predictionOffset);
        }
        else if (player.HasCollisionBounds)
        {
            var centerX = (player.CollisionMins.X + player.CollisionMaxs.X) * .5f;
            var centerY = (player.CollisionMins.Y + player.CollisionMaxs.Y) * .5f;
            var headZ = player.CollisionMins.Z + (player.CollisionMaxs.Z - player.CollisionMins.Z) * .94f;
            head = Add(new Vec3(player.Origin.X + centerX, player.Origin.Y + centerY, player.Origin.Z + headZ), predictionOffset);
        }
        else return false;

        if (!WorldProjection.TryProject(head, matrix, width, height, out var projected)) return false;
        var radius = Math.Clamp(fallbackBoxWidth * .16f, 3f, 16f);
        if (neck is Vec3 neckPoint && WorldProjection.TryProject(neckPoint, matrix, width, height, out var projectedNeck))
            radius = Math.Clamp(MathF.Abs(projectedNeck.Y - projected.Y) * .55f, 3f, 16f);
        if (projected.X < -radius || projected.X > width + radius || projected.Y < -radius || projected.Y > height + radius) return false;
        marker = new HeadMarker(projected, radius);
        return true;
    }

    private static Vec3 Add(Vec3 first, Vec3 second) => new(first.X + second.X, first.Y + second.Y, first.Z + second.Z);
}
