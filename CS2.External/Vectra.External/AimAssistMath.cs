namespace Vectra.External;

public readonly record struct AimCorrection(float X, float Y);

public static class AimAssistMath
{
    public static Vec3 TargetPoint(PlayerSnapshot player, AimTargetPoint target) => target == AimTargetPoint.Head ? HeadPoint(player) : ChestPoint(player);

    public static Vec3 HeadPoint(PlayerSnapshot player)
    {
        var head = (int)SkeletonJoint.Head;
        if (player.HasSkeletonJoint.Length > head && player.HasSkeletonJoint[head] && player.SkeletonJoints.Length > head && Vec3.IsFinite(player.SkeletonJoints[head])) return player.SkeletonJoints[head];
        return BoundsPoint(player, .88f);
    }

    public static Vec3 ChestPoint(PlayerSnapshot player)
    {
        var neck = (int)SkeletonJoint.Neck; var spine = (int)SkeletonJoint.SpineUpper;
        if (player.HasSkeletonJoint.Length > spine && player.SkeletonJoints.Length > spine && player.HasSkeletonJoint[neck] && player.HasSkeletonJoint[spine] && Vec3.IsFinite(player.SkeletonJoints[neck]) && Vec3.IsFinite(player.SkeletonJoints[spine]))
        {
            var first = player.SkeletonJoints[neck]; var second = player.SkeletonJoints[spine];
            return new Vec3((first.X + second.X) * .5f, (first.Y + second.Y) * .5f, (first.Z + second.Z) * .5f);
        }
        return BoundsPoint(player, .70f);
    }

    public static Vec3 UpperBodyPoint(PlayerSnapshot player) => ChestPoint(player);

    private static Vec3 BoundsPoint(PlayerSnapshot player, float heightRatio)
    {
        var mins = player.HasCollisionBounds ? player.CollisionMins : new Vec3(-16, -16, 0);
        var maxs = player.HasCollisionBounds ? player.CollisionMaxs : new Vec3(16, 16, 72);
        var height = Math.Clamp(maxs.Z - mins.Z, 32, 128);
        return new Vec3(player.Origin.X, player.Origin.Y, player.Origin.Z + mins.Z + height * heightRatio);
    }

    public static bool TryGetCorrection(ScreenPoint target, double width, double height, float fovPixels, float strength, float maxStepPixels, out AimCorrection correction)
    {
        correction = default;
        if (!float.IsFinite(target.X) || !float.IsFinite(target.Y) || !float.IsFinite(fovPixels) || !float.IsFinite(strength) || !float.IsFinite(maxStepPixels) || fovPixels <= 0 || maxStepPixels <= 0) return false;
        var dx = target.X - width / 2d; var dy = target.Y - height / 2d; var distance = Math.Sqrt(dx * dx + dy * dy);
        if (!double.IsFinite(distance) || distance > fovPixels) return false;
        var scale = Math.Clamp(strength, 0, 1);
        var moveX = dx * scale; var moveY = dy * scale; var magnitude = Math.Sqrt(moveX * moveX + moveY * moveY);
        if (magnitude > maxStepPixels && magnitude > 0) { var clamp = maxStepPixels / magnitude; moveX *= clamp; moveY *= clamp; }
        correction = new AimCorrection((float)moveX, (float)moveY);
        return MathF.Abs(correction.X) >= .01f || MathF.Abs(correction.Y) >= .01f;
    }

    public static bool TryGetSnapCorrection(ScreenPoint target, double width, double height, float fovPixels, out AimCorrection correction) =>
        TryGetCorrection(target, width, height, fovPixels, 1, fovPixels, out correction);
}
