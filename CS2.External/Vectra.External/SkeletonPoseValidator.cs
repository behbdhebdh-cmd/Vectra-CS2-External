namespace Vectra.External;

public static class SkeletonPoseValidator
{
    private static readonly (SkeletonJoint Parent, SkeletonJoint Child, float Minimum, float Maximum)[] Segments =
    {
        (SkeletonJoint.Pelvis, SkeletonJoint.SpineLower, 1, 50), (SkeletonJoint.SpineLower, SkeletonJoint.SpineUpper, 1, 45),
        (SkeletonJoint.SpineUpper, SkeletonJoint.Neck, 1, 40), (SkeletonJoint.Neck, SkeletonJoint.Head, 1, 35),
        (SkeletonJoint.Neck, SkeletonJoint.LeftUpperArm, 1, 50), (SkeletonJoint.LeftUpperArm, SkeletonJoint.LeftLowerArm, 1, 60),
        (SkeletonJoint.LeftLowerArm, SkeletonJoint.LeftHand, .5f, 55), (SkeletonJoint.Neck, SkeletonJoint.RightUpperArm, 1, 50),
        (SkeletonJoint.RightUpperArm, SkeletonJoint.RightLowerArm, 1, 60), (SkeletonJoint.RightLowerArm, SkeletonJoint.RightHand, .5f, 55),
        (SkeletonJoint.Pelvis, SkeletonJoint.LeftUpperLeg, 1, 60), (SkeletonJoint.LeftUpperLeg, SkeletonJoint.LeftLowerLeg, 1, 70),
        (SkeletonJoint.LeftLowerLeg, SkeletonJoint.LeftAnkle, .5f, 65), (SkeletonJoint.Pelvis, SkeletonJoint.RightUpperLeg, 1, 60),
        (SkeletonJoint.RightUpperLeg, SkeletonJoint.RightLowerLeg, 1, 70), (SkeletonJoint.RightLowerLeg, SkeletonJoint.RightAnkle, .5f, 65)
    };
    private static readonly (SkeletonJoint Joint, int Bone)[] Build14170UpperBody =
    {
        (SkeletonJoint.Head, 6), (SkeletonJoint.Neck, 5), (SkeletonJoint.SpineUpper, 4),
        (SkeletonJoint.SpineLower, 2), (SkeletonJoint.Pelvis, 0),
        (SkeletonJoint.LeftUpperArm, 8), (SkeletonJoint.LeftLowerArm, 9), (SkeletonJoint.LeftHand, 10),
        (SkeletonJoint.RightUpperArm, 13), (SkeletonJoint.RightLowerArm, 14), (SkeletonJoint.RightHand, 15)
    };

    public static bool TryApplyBuild14170UpperBody(IReadOnlyList<Vec3> cache, Vec3 origin, Vec3 mins, Vec3 maxs, bool hasBounds, Vec3[] joints, bool[] valid)
    {
        if (!hasBounds || joints.Length != (int)SkeletonJoint.Count || valid.Length != (int)SkeletonJoint.Count || cache.Count <= 15) return false;
        var candidateJoints = PlayerSnapshotBones.EmptyJoints;
        var candidateValid = PlayerSnapshotBones.EmptyValidity;
        foreach (var (joint, bone) in Build14170UpperBody)
        {
            var position = cache[bone];
            if (!Vec3.IsFinite(position) || !InsideBounds(position, origin, mins, maxs)) return false;
            candidateJoints[(int)joint] = position;
            candidateValid[(int)joint] = true;
        }
        if (!HasPlausibleUpperBody(candidateJoints, candidateValid)) return false;
        Array.Copy(candidateJoints, joints, joints.Length);
        Array.Copy(candidateValid, valid, valid.Length);
        return true;
    }

    public static bool HasRenderableSkeleton(IReadOnlyList<bool> valid) => SkeletonGeometry.Connections.Any(connection => valid.Count > Math.Max((int)connection.From, (int)connection.To) && valid[(int)connection.From] && valid[(int)connection.To]);

    public static bool ValidatePose(Vec3[] joints, bool[] valid, Vec3 origin, Vec3 mins, Vec3 maxs, bool hasBounds)
    {
        if (joints.Length != (int)SkeletonJoint.Count || valid.Length != joints.Length) return false;
        for (var i = 0; i < joints.Length; i++)
            if (valid[i] && (!Vec3.IsFinite(joints[i]) || !InsideAnimationEnvelope(joints[i], origin, mins, maxs, hasBounds))) valid[i] = false;

        foreach (var segment in Segments)
        {
            var parent = (int)segment.Parent; var child = (int)segment.Child;
            if (!valid[parent] || !valid[child]) continue;
            var distance = Distance(joints[parent], joints[child]);
            if (distance < segment.Minimum || distance > segment.Maximum) valid[child] = false;
        }
        return HasRenderableSkeleton(valid);
    }

    private static bool HasPlausibleUpperBody(IReadOnlyList<Vec3> joints, IReadOnlyList<bool> valid)
    {
        var segments = new[]
        {
            (SkeletonJoint.Head, SkeletonJoint.Neck, 2f, 35f), (SkeletonJoint.Neck, SkeletonJoint.SpineUpper, 2f, 35f),
            (SkeletonJoint.SpineUpper, SkeletonJoint.SpineLower, 2f, 40f), (SkeletonJoint.SpineLower, SkeletonJoint.Pelvis, 2f, 45f),
            (SkeletonJoint.Neck, SkeletonJoint.LeftUpperArm, 2f, 45f), (SkeletonJoint.LeftUpperArm, SkeletonJoint.LeftLowerArm, 2f, 55f),
            (SkeletonJoint.LeftLowerArm, SkeletonJoint.LeftHand, 1f, 45f), (SkeletonJoint.Neck, SkeletonJoint.RightUpperArm, 2f, 45f),
            (SkeletonJoint.RightUpperArm, SkeletonJoint.RightLowerArm, 2f, 55f), (SkeletonJoint.RightLowerArm, SkeletonJoint.RightHand, 1f, 45f)
        };
        return segments.All(segment =>
        {
            var distance = Distance(joints[(int)segment.Item1], joints[(int)segment.Item2]);
            return valid[(int)segment.Item1] && valid[(int)segment.Item2] && distance >= segment.Item3 && distance <= segment.Item4;
        });
    }

    private static bool InsideBounds(Vec3 point, Vec3 origin, Vec3 mins, Vec3 maxs)
    {
        const float margin = 16;
        return point.X >= origin.X + mins.X - margin && point.X <= origin.X + maxs.X + margin && point.Y >= origin.Y + mins.Y - margin && point.Y <= origin.Y + maxs.Y + margin && point.Z >= origin.Z + mins.Z - margin && point.Z <= origin.Z + maxs.Z + margin;
    }

    private static bool InsideAnimationEnvelope(Vec3 point, Vec3 origin, Vec3 mins, Vec3 maxs, bool hasBounds)
    {
        if (!hasBounds) return Distance(point, origin) <= 160;
        const float horizontalMargin = 48, verticalMargin = 24;
        return point.X >= origin.X + mins.X - horizontalMargin && point.X <= origin.X + maxs.X + horizontalMargin &&
               point.Y >= origin.Y + mins.Y - horizontalMargin && point.Y <= origin.Y + maxs.Y + horizontalMargin &&
               point.Z >= origin.Z + mins.Z - verticalMargin && point.Z <= origin.Z + maxs.Z + verticalMargin;
    }

    private static float Distance(Vec3 first, Vec3 second)
    {
        var x = second.X - first.X; var y = second.Y - first.Y; var z = second.Z - first.Z;
        return MathF.Sqrt(x * x + y * y + z * z);
    }
}
