namespace Vectra.External;

public static class VisibilityRaycaster
{
    public static GameSnapshot Apply(GameSnapshot snapshot, ICollisionWorld? collisionWorld, AimTargetPoint targetPoint)
    {
        if (!snapshot.Valid || collisionWorld is null || !Vec3.IsFinite(snapshot.LocalEyePosition))
            return snapshot with { Visibility = VisibilityCaptureReport.Unavailable };

        var players = snapshot.Players.ToArray();
        var tested = 0; var visible = 0; var occluded = 0;
        for (var index = 0; index < players.Length; index++)
        {
            var player = players[index];
            if (player.IsLocal)
            {
                players[index] = player with { HasVisibilityData = true, IsVisible = true };
                continue;
            }
            if (!player.Alive || player.Dormant) continue;
            var target = AimAssistMath.TargetPoint(player, targetPoint);
            if (!Vec3.IsFinite(target)) continue;
            tested++;
            var isVisible = !collisionWorld.IntersectsSegment(snapshot.LocalEyePosition, target, out _);
            if (isVisible) visible++; else occluded++;
            players[index] = player with { HasVisibilityData = true, IsVisible = isVisible };
        }
        return snapshot with { Players = players, Visibility = new(true, tested, visible, occluded) };
    }
}
