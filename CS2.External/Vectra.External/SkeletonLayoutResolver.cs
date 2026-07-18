namespace Vectra.External;

public static class SkeletonLayoutResolver
{
    private const int Build14170 = 14170;
    private static readonly int[] Build14170UpperBody =
    {
        6, 5, 4, 2, 0,
        8, 9, 10,
        13, 14, 15
    };

    public static bool TryResolve(IReadOnlyList<string> names, IReadOnlyList<short> parents, out int[] indices)
    {
        indices = Enumerable.Repeat(-1, (int)SkeletonJoint.Count).ToArray();
        if (names.Count == 0 || names.Count != parents.Count || names.Count > 256 || !HasValidParentTable(parents)) return false;
        for (var i = 0; i < names.Count; i++)
        {
            var joint = MatchJoint(names[i]);
            if (joint is SkeletonJoint matched && indices[(int)matched] < 0) indices[(int)matched] = i;
        }
        InvalidateChainIfInvalid(indices, parents, SkeletonJoint.Pelvis, SkeletonJoint.LeftUpperLeg, SkeletonJoint.LeftLowerLeg, SkeletonJoint.LeftAnkle);
        InvalidateChainIfInvalid(indices, parents, SkeletonJoint.Pelvis, SkeletonJoint.RightUpperLeg, SkeletonJoint.RightLowerLeg, SkeletonJoint.RightAnkle);
        InvalidateChainIfInvalid(indices, parents, SkeletonJoint.Neck, SkeletonJoint.LeftUpperArm, SkeletonJoint.LeftLowerArm, SkeletonJoint.LeftHand);
        InvalidateChainIfInvalid(indices, parents, SkeletonJoint.Neck, SkeletonJoint.RightUpperArm, SkeletonJoint.RightLowerArm, SkeletonJoint.RightHand);
        var resolvedIndices = indices;
        return SkeletonGeometry.Connections.Any(connection => resolvedIndices[(int)connection.From] >= 0 && resolvedIndices[(int)connection.To] >= 0);
    }

    public static bool TryCompose(IReadOnlyList<string> names, IReadOnlyList<short> parents, int build, out int[] indices)
    {
        var resolved = TryResolve(names, parents, out indices);
        if (build == Build14170 && !HasUsableUpperBody(indices) && names.Count >= Build14170UpperBody.Max() + 1) {
            foreach (var joint in new[] { SkeletonJoint.Head, SkeletonJoint.Neck, SkeletonJoint.SpineUpper, SkeletonJoint.SpineLower, SkeletonJoint.Pelvis, SkeletonJoint.LeftUpperArm, SkeletonJoint.LeftLowerArm, SkeletonJoint.LeftHand, SkeletonJoint.RightUpperArm, SkeletonJoint.RightLowerArm, SkeletonJoint.RightHand }) indices[(int)joint] = -1;
            resolved |= ApplyBuild14170Fallback(indices, names.Count);
        }
        return resolved;
    }

    private static bool HasUsableUpperBody(IReadOnlyList<int> indices) => new[] { SkeletonJoint.Head, SkeletonJoint.Neck, SkeletonJoint.SpineUpper, SkeletonJoint.SpineLower, SkeletonJoint.Pelvis, SkeletonJoint.LeftUpperArm, SkeletonJoint.LeftLowerArm, SkeletonJoint.LeftHand, SkeletonJoint.RightUpperArm, SkeletonJoint.RightLowerArm, SkeletonJoint.RightHand }.All(joint => indices[(int)joint] >= 0);

    public static bool ApplyBuild14170Fallback(int[] indices, int boneCount)
    {
        if (indices.Length != (int)SkeletonJoint.Count || boneCount < Build14170UpperBody.Max() + 1) return false;
        var applied = false;
        for (var i = 0; i < Build14170UpperBody.Length; i++)
        {
            var joint = i switch
            {
                0 => SkeletonJoint.Head,
                1 => SkeletonJoint.Neck,
                2 => SkeletonJoint.SpineUpper,
                3 => SkeletonJoint.SpineLower,
                4 => SkeletonJoint.Pelvis,
                5 => SkeletonJoint.LeftUpperArm,
                6 => SkeletonJoint.LeftLowerArm,
                7 => SkeletonJoint.LeftHand,
                8 => SkeletonJoint.RightUpperArm,
                9 => SkeletonJoint.RightLowerArm,
                _ => SkeletonJoint.RightHand
            };
            var slot = (int)joint;
            if (indices[slot] < 0) { indices[slot] = Build14170UpperBody[i]; applied = true; }
        }
        return applied;
    }

    public static bool HasValidParentTable(IReadOnlyList<short> parents)
    {
        if (parents.Count is < 1 or > 256) return false;
        var roots = 0;
        for (var i = 0; i < parents.Count; i++)
        {
            var parent = parents[i];
            if (parent == -1) { roots++; continue; }
            if (parent < 0 || parent >= parents.Count || parent == i) return false;
            var cursor = parent;
            for (var depth = 0; depth <= parents.Count; depth++)
            {
                if (cursor == -1) break;
                if (cursor == i || cursor < -1 || cursor >= parents.Count) return false;
                cursor = parents[cursor];
                if (depth == parents.Count) return false;
            }
        }
        return roots > 0;
    }

    private static void InvalidateChainIfInvalid(int[] indices, IReadOnlyList<short> parents, params SkeletonJoint[] chain)
    {
        for (var i = 1; i < chain.Length; i++)
        {
            var ancestor = indices[(int)chain[i - 1]];
            var descendant = indices[(int)chain[i]];
            if (ancestor >= 0 && descendant >= 0 && IsAncestor(descendant, ancestor, parents)) continue;
            for (var remaining = i; remaining < chain.Length; remaining++) indices[(int)chain[remaining]] = -1;
            return;
        }
    }

    private static bool IsAncestor(int descendant, int ancestor, IReadOnlyList<short> parents)
    {
        if (descendant < 0 || descendant >= parents.Count || ancestor < 0 || ancestor >= parents.Count) return false;
        var cursor = descendant;
        for (var depth = 0; depth < 12; depth++)
        {
            cursor = parents[cursor];
            if (cursor == ancestor) return true;
            if (cursor < 0 || cursor >= parents.Count) return false;
        }
        return false;
    }

    private static SkeletonJoint? MatchJoint(string value)
    {
        var name = Normalize(value);
        return name switch
        {
            "head" or "head_0" => SkeletonJoint.Head,
            "neck_0" or "neck" => SkeletonJoint.Neck,
            "spine_2" or "spine_3" or "spine_upper" => SkeletonJoint.SpineUpper,
            "spine_0" or "spine_1" or "spine_lower" => SkeletonJoint.SpineLower,
            "pelvis" or "hips" or "root_pelvis" => SkeletonJoint.Pelvis,
            "arm_upper_l" or "upper_arm_l" or "upperarm_l" or "left_upper_arm" or "l_upperarm" => SkeletonJoint.LeftUpperArm,
            "arm_lower_l" or "lower_arm_l" or "lowerarm_l" or "left_lower_arm" or "l_forearm" or "forearm_l" => SkeletonJoint.LeftLowerArm,
            "hand_l" or "left_hand" or "l_hand" => SkeletonJoint.LeftHand,
            "arm_upper_r" or "upper_arm_r" or "upperarm_r" or "right_upper_arm" or "r_upperarm" => SkeletonJoint.RightUpperArm,
            "arm_lower_r" or "lower_arm_r" or "lowerarm_r" or "right_lower_arm" or "r_forearm" or "forearm_r" => SkeletonJoint.RightLowerArm,
            "hand_r" or "right_hand" or "r_hand" => SkeletonJoint.RightHand,
            "leg_upper_l" or "upper_leg_l" or "thigh_l" or "left_thigh" => SkeletonJoint.LeftUpperLeg,
            "leg_lower_l" or "lower_leg_l" or "calf_l" or "shin_l" or "left_calf" => SkeletonJoint.LeftLowerLeg,
            "ankle_l" or "foot_l" or "left_foot" => SkeletonJoint.LeftAnkle,
            "leg_upper_r" or "upper_leg_r" or "thigh_r" or "right_thigh" => SkeletonJoint.RightUpperLeg,
            "leg_lower_r" or "lower_leg_r" or "calf_r" or "shin_r" or "right_calf" => SkeletonJoint.RightLowerLeg,
            "ankle_r" or "foot_r" or "right_foot" => SkeletonJoint.RightAnkle,
            _ => null
        };
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var name = value.Trim().ToLowerInvariant().Replace('-', '_').Replace('.', '_');
        var separator = Math.Max(name.LastIndexOf(':'), Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\')));
        if (separator >= 0 && separator + 1 < name.Length) name = name[(separator + 1)..];
        foreach (var prefix in new[] { "bip01_", "bip_", "bone_", "jnt_" })
            if (name.StartsWith(prefix, StringComparison.Ordinal)) { name = name[prefix.Length..]; break; }
        while (name.Contains("__", StringComparison.Ordinal)) name = name.Replace("__", "_", StringComparison.Ordinal);
        return name;
    }
}
