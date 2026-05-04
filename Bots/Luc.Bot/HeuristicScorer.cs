using TankDestroyer.API;
using TankDestroyer.Engine;

namespace Luc.Bot;

public static class HeuristicScorer
{
    public static void ExecuteBestAction(PlayerTurnContext ctx, Genome g)
    {
        var me      = (Tank)ctx.Tank;
        var world   = (World)ctx.World;
        var tanks   = ctx.GetTanks().Cast<Tank>().ToArray();
        var bullets = ctx.GetBullets().Cast<Bullet>().ToArray();
        var enemies = tanks.Where(t => t.OwnerId != me.OwnerId && !t.Destroyed).ToArray();

        if (enemies.Length == 0) return;

        var   target   = NearestEnemy(me, enemies);
        var   targetTd = NearestTurretDir(target.X - me.X, target.Y - me.Y);
        float distNow  = Dist(me.X, me.Y, target.X, target.Y);
        bool  onTree   = world.GetTile(me.X, me.Y).TileType == TileType.Tree;

        float  bestScore  = float.MinValue;
        Action bestAction = () => { };

        void Candidate(float score, Action action)
        {
            if (score > bestScore) { bestScore = score; bestAction = action; }
        }

        // Move candidates
        foreach (Direction dir in Enum.GetValues<Direction>())
        {
            var (dx, dy) = MoveDelta(dir);
            int nx = me.X + dx, ny = me.Y + dy;
            if (nx < 0 || nx >= world.Width || ny < 0 || ny >= world.Height) continue;

            var tile = (Tile)world.GetTile(nx, ny);
            if (!IsPassable(tile.TileType)) continue;
            if (tanks.Any(t => !t.Destroyed && t.X == nx && t.Y == ny)) continue;

            float score = g.ApproachEnemy * (distNow - Dist(nx, ny, target.X, target.Y)) / 30f
                        + g.SeekCover     * CoverScore(tile.TileType)
                        + g.EvadeBullets  * -BulletThreat(bullets, nx, ny)
                        + g.PreferMove;

            var d = dir;
            Candidate(score, () => ctx.MoveTank(d));
        }

        // Rotate candidates — skip current direction to avoid wasting a turn
        foreach (TurretDirection td in Enum.GetValues<TurretDirection>())
        {
            if (td == me.TurretDirection) continue;
            float score = g.AimAtEnemy * AimQuality(td, targetTd) + g.PreferRotate;
            var t = td;
            Candidate(score, () => ctx.RotateTurret(t));
        }

        // Fire candidate
        if (!onTree)
        {
            float score = g.FireWhenAimed * AimQuality(me.TurretDirection, targetTd) + g.FireBase;
            Candidate(score, () => ctx.Fire());
        }

        bestAction();
    }

    // --- helpers ---

    private static Tank NearestEnemy(Tank me, Tank[] enemies)
    {
        Tank best = enemies[0];
        float bestDist = float.MaxValue;
        foreach (var e in enemies)
        {
            float d = Dist(me.X, me.Y, e.X, e.Y);
            if (d < bestDist) { bestDist = d; best = e; }
        }
        return best;
    }

    private static TurretDirection NearestTurretDir(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return TurretDirection.North;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        float nx = dx / len, ny = dy / len;
        TurretDirection best = TurretDirection.North;
        float bestDot = float.MinValue;
        foreach (TurretDirection td in Enum.GetValues<TurretDirection>())
        {
            var (tdx, tdy) = TurretDelta(td);
            float tlen = MathF.Sqrt(tdx * tdx + tdy * tdy);
            float dot = tdx / tlen * nx + tdy / tlen * ny;
            if (dot > bestDot) { bestDot = dot; best = td; }
        }
        return best;
    }

    private static float AimQuality(TurretDirection current, TurretDirection target)
    {
        var (cdx, cdy) = TurretDelta(current);
        var (tdx, tdy) = TurretDelta(target);
        float clen = MathF.Sqrt(cdx * cdx + cdy * cdy);
        float tlen = MathF.Sqrt(tdx * tdx + tdy * tdy);
        float dot = cdx / clen * (tdx / tlen) + cdy / clen * (tdy / tlen);
        return (dot + 1f) / 2f;
    }

    private static float BulletThreat(Bullet[] bullets, int cx, int cy)
    {
        float threat = 0f;
        foreach (var b in bullets)
        {
            var (bdx, bdy) = TurretDelta(b.Direction);
            for (int step = 1; step <= 6; step++)
                if (b.X + bdx * step == cx && b.Y + bdy * step == cy) { threat += 1f; break; }
        }
        return threat;
    }

    private static float CoverScore(TileType t) => t switch
    {
        TileType.Tree     => 1.0f,
        TileType.Building => 0.5f,
        _                 => 0.0f,
    };

    private static bool IsPassable(TileType t) => t is not TileType.Water and not TileType.Building;

    private static float Dist(int x1, int y1, int x2, int y2)
    {
        int dx = x2 - x1, dy = y2 - y1;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    // API coordinate conventions: North=Y+1, East=X-1, South=Y-1, West=X+1
    private static (int dx, int dy) MoveDelta(Direction d) => d switch
    {
        Direction.North => ( 0, +1),
        Direction.East  => (-1,  0),
        Direction.South => ( 0, -1),
        Direction.West  => (+1,  0),
        _               => ( 0,  0),
    };

    private static (int dx, int dy) TurretDelta(TurretDirection d) => d switch
    {
        TurretDirection.North     => ( 0, +1),
        TurretDirection.NorthEast => (-1, +1),
        TurretDirection.East      => (-1,  0),
        TurretDirection.SouthEast => (-1, -1),
        TurretDirection.South     => ( 0, -1),
        TurretDirection.SouthWest => (+1, -1),
        TurretDirection.West      => (+1,  0),
        TurretDirection.NorthWest => (+1, +1),
        _                         => ( 0,  0),
    };
}
