namespace AVAIkariWarriors.Engine;

/// <summary>
/// The computer at a commando's controls, for the demonstration (and the tests). It aims as a rotary stick lets a
/// player aim, apart from where it runs: anything lying along one of the eight ways from it, in range and in the
/// open, it turns the gun on and fires at, while it goes about its business. Its business: to climb into an empty
/// tank when there's one about (and out again before it goes up), to pick up what's lying around, to stand a
/// grenade's throw from what bullets won't touch (pillboxes, armour, the fortress gate) and lob one, to line up on
/// whatever's near, and otherwise to make its way up the map. Before anything, it looks at where the bullets,
/// bombs and shells will be, and if that would get it killed, it takes whichever way of moving keeps it clearest.
/// </summary>
public sealed class WarriorPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    private const double Lined = 11, Close = 90, Engage = 230;

    private int _tick, _stuck, _wander, _lining, _giveUp;
    private double _lastX, _lastY;
    private (int Dx, int Dy) _wanderWay;

    public Controls Decide(GameEngine g, int index)
    {
        _tick++;
        var p = g.Players[index];
        if (g.Phase is not (Phase.Play or Phase.Start) || !p.Fighting) { Doing = "waiting"; return default; }
        var want = p.Tank is { } t ? Drive(g, p, t) : Plan(g, p);
        // Moving nowhere for a while when it means to: a few steps some other way.
        if ((want.Dx != 0 || want.Dy != 0) && Field.Distance(p.X, p.Y, _lastX, _lastY) < 0.4) { if (++_stuck > 25) { _stuck = 0; _wander = 20; _wanderWay = Field.Ways[(_tick / 7 + index * 3) % 8]; } }
        else _stuck = 0;
        _lastX = p.X;
        _lastY = p.Y;
        if (_wander > 0) { _wander--; want = want with { Dx = _wanderWay.Dx, Dy = _wanderWay.Dy }; Doing = "unsticking"; }
        return p.Tank != null ? want : Safe(g, p, want);
    }

    // ───────────────────────────── on foot ─────────────────────────────

    /// <summary>What it would like to do on foot, if nothing were coming at it.</summary>
    private Controls Plan(GameEngine g, Warrior p)
    {
        var move = Move(g, p, out string doing);
        Doing = doing;
        // A grenade: on whatever bullets won't do for, from where it stands.
        if (p.Grenades > 0 && !p.Wading && p.ThrowIn <= 0 && ThrowWay(g, p) is { } tw)
        {
            Doing = "grenade";
            var (ax, ay) = Field.Ways[tw];
            return move with { Grenade = true, AimX = ax, AimY = ay, Fire = false };
        }
        // Anything lined up: the gun on it, still on the move.
        if (p.Ammo > 0 && FireWay(g, p) is { } fw)
        {
            var (ax, ay) = Field.Ways[fw];
            if (doing == "advancing") Doing = "firing";
            return move with { Fire = true, AimX = ax, AimY = ay };
        }
        return move;
    }

    /// <summary>Where to go, on foot.</summary>
    private Controls Move(GameEngine g, Warrior p, out string doing)
    {
        // A burning tank nearby: away from it.
        if (g.Tanks.FirstOrDefault(t => !t.Dead && t.Doomed && t.Driver != p && Field.Distance(t.X, t.Y, p.X, p.Y) < GameEngine.TankBlast * 1.6) is { } fire)
        {
            doing = "running from the tank";
            return new Controls(Math.Sign(p.X - fire.X), p.Y - fire.Y >= 0 || p.Y - g.Camera < 60 ? 1 : -1);
        }
        // An empty tank about: in it.
        var tank = g.Tanks.Where(t => !t.Dead && t.Driver == null && !t.Doomed && t.Fuel > 25 && t.Y > g.Camera + 30 && t.Y < g.ViewTop - 20)
            .OrderBy(t => Field.Distance(t.X, t.Y, p.X, p.Y)).FirstOrDefault();
        if (tank != null && !g.Players.Any(o => o != p && o.Fighting && o.Tank == null && Field.Distance(o.X, o.Y, tank.X, tank.Y) < Field.Distance(p.X, p.Y, tank.X, tank.Y) - 40))
        {
            doing = "to the tank";
            return Towards(g, p, tank.X, tank.Y);
        }
        // Something only a grenade will do for: to a throw's distance below it.
        if (p.Grenades > 0 && HardTarget(g, p) is { } hard)
        {
            doing = "to throw";
            return Towards(g, p, hard.X + (p.Index == 0 ? -6 : 6), hard.Y - GameEngine.ThrowRange);
        }
        var targets = Targets(g, p).ToList();
        // Only the near ones are worth stopping for; the rest it shoots on the way (not ones behind cover, and not
        // one it's been trying to line up on for too long).
        var near = _giveUp > 0 ? [] : targets.Where(t => Field.Distance(p.X, p.Y, t.X, t.Y) < Engage && g.Stage.Clear(p.X, p.Y, t.X, t.Y)).ToList();
        if (_giveUp > 0) _giveUp--;
        if (near.Count > 0 && ++_lining > 150) { _lining = 0; _giveUp = 90; }
        if (near.Count == 0) _lining = 0;
        if (near.Count > 0 && _giveUp == 0 && p.Ammo > 0)
        {
            var (x, y, _) = near[0];
            double dx = x - p.X, dy = y - p.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < Close)
            {
                doing = "backing off";
                return new Controls(-Math.Sign(dx), dy > -40 ? -1 : 1);
            }
            // Line it up the nearest way: the same column, the same row, or a diagonal.
            double col = Math.Abs(dx), row = Math.Abs(dy), diag = Math.Abs(col - row) / 1.4;
            doing = "lining up";
            if (col <= row && col <= diag) return new Controls(Math.Sign(dx), 0);
            if (row <= diag) return new Controls(0, Math.Sign(dy));
            return col > row ? new Controls(Math.Sign(dx), 0) : new Controls(0, Math.Sign(dy));
        }
        // Something to pick up close by.
        if (g.Pickups.Where(k => k.Y > g.Camera + 30 && k.Y < g.ViewTop - 20 && Field.Distance(k.X, k.Y, p.X, p.Y) < 320)
                .OrderBy(k => Field.Distance(k.X, k.Y, p.X, p.Y)).FirstOrDefault() is { } item)
        {
            doing = "to a pickup";
            return Towards(g, p, item.X, item.Y);
        }
        double lane = Lane(g, p);
        if (g.AtEnd)
        {
            doing = "holding";
            return Towards(g, p, lane + Math.Sin(_tick / 90.0 + p.Index * 2) * 110, g.Camera + 170);
        }
        doing = "advancing";
        return Towards(g, p, lane, g.ViewTop - 40);
    }

    /// <summary>Its own side of the screen with two playing; the middle alone.</summary>
    private static double Lane(GameEngine g, Warrior p) =>
        g.Fighting.Count() > 1 ? Field.Width / 2 + (p.Index == 0 ? -130 : 130) : Field.Width / 2;

    /// <summary>The nearest of what bullets won't touch (or, out of bullets, anything), on the screen.</summary>
    private static (double X, double Y)? HardTarget(GameEngine g, Warrior p)
    {
        var all = new List<(double X, double Y)>();
        foreach (var b in g.Pillboxes) if (!b.Dead) all.Add((b.X, b.Y));
        foreach (var e in g.EnemyTanks) if (!e.Dead) all.Add((e.X, e.Y));
        if (g.Stage.HasGate && g.AtEnd && !g.Stage.GateOpen) all.Add((g.Stage.GateX, g.Stage.GateY + 16));
        if (p.Ammo == 0)
        {
            foreach (var s in g.Soldiers) if (!s.Dead) all.Add((s.X, s.Y));
            foreach (var gun in g.Guns) if (!gun.Dead) all.Add((gun.X, gun.Y));
        }
        return all.Where(t => t.Y > g.Camera + 40 && t.Y < g.ViewTop + 60 && t.Y - GameEngine.ThrowRange > g.Camera + 20)
            .OrderBy(t => Field.Distance(p.X, p.Y, t.X, t.Y)).Select(t => ((double, double)?)t).FirstOrDefault();
    }

    /// <summary>A way to throw from here that lands on something worth it (pillboxes, armour, the gate; soldiers in cover or in a trench).</summary>
    private static int? ThrowWay(GameEngine g, Warrior p)
    {
        var aims = new List<(double X, double Y, double Within)>();
        foreach (var b in g.Pillboxes) if (!b.Dead) aims.Add((b.X, b.Y, 34));
        foreach (var e in g.EnemyTanks) if (!e.Dead) aims.Add((e.X, e.Y, 26));
        foreach (var gun in g.Guns) if (!gun.Dead) aims.Add((gun.X, gun.Y, 36));
        foreach (var s in g.Soldiers)
            if (!s.Dead && (s.Kind == SoldierKind.Trench || p.Ammo == 0 || !g.Stage.Clear(p.X, p.Y, s.X, s.Y))) aims.Add((s.X, s.Y, 30));
        if (g.Stage.HasGate && g.AtEnd && !g.Stage.GateOpen) aims.Add((g.Stage.GateX, g.Stage.GateY + 16, 70));
        for (int w = 0; w < 8; w++)
        {
            var (ux, uy) = Field.Unit(w);
            double lx = p.X + ux * GameEngine.ThrowRange, ly = p.Y + uy * GameEngine.ThrowRange;
            foreach (var (x, y, within) in aims)
                if (Field.Distance(lx, ly, x, y) < within && y > g.Camera && y < g.ViewTop + 40) return w;
        }
        return null;
    }

    /// <summary>Everything there is to shoot, nearest first.</summary>
    private static IEnumerable<(double X, double Y, double Cost)> Targets(GameEngine g, Warrior p)
    {
        var all = new List<(double X, double Y, double Cost)>();
        foreach (var s in g.Soldiers)
        {
            if (s.Dead || (s.Kind == SoldierKind.Trench && !s.Up) || s.Y > g.ViewTop - 8 || s.Y < g.Camera + 4 || s.X < 4 || s.X > Field.Width - 4) continue;
            all.Add((s.X, s.Y, Field.Distance(p.X, p.Y, s.X, s.Y)));
        }
        foreach (var gun in g.Guns)
            if (!gun.Dead && gun.Y < g.ViewTop && gun.Y > g.Camera) all.Add((gun.X, gun.Y, Field.Distance(p.X, p.Y, gun.X, gun.Y) * 0.9));
        foreach (var h in g.Helicopters)
            if (!h.Dead && h.Y < g.ViewTop) all.Add((h.X, h.Y, Field.Distance(p.X, p.Y, h.X, h.Y) * 0.6));
        return all.OrderBy(t => t.Cost);
    }

    /// <summary>Which way a shot now would hit something, if any: near enough on the line, in range, nothing in the way.</summary>
    private static int? FireWay(GameEngine g, Warrior p)
    {
        double range = GameEngine.BulletSpeed * (p.Far ? GameEngine.FarLife : GameEngine.BulletLife) * 0.95;
        foreach (var (x, y, _) in Targets(g, p))
        {
            bool big = g.Helicopters.Any(h => !h.Dead && h.X == x && h.Y == y);
            if (Along(p.X, p.Y, x, y, range, big ? 24 : Lined) is { } w && g.Stage.Clear(p.X, p.Y, x, y)) return w;
        }
        return null;
    }

    /// <summary>The way from one point to another, if the other lies near enough along it and in range.</summary>
    private static int? Along(double px, double py, double x, double y, double range, double tolerance)
    {
        int w = Field.WayTowards(px, py, x, y);
        var (ux, uy) = Field.Unit(w);
        double dx = x - px, dy = y - py, along = dx * ux + dy * uy, off = Math.Abs(dx * uy - dy * ux);
        return along > 8 && along < range && off < tolerance ? w : null;
    }

    // ───────────────────────────── in a tank ─────────────────────────────

    /// <summary>At the controls of a tank: out before it goes up; otherwise shells on anything along a way, and on up the map.</summary>
    private Controls Drive(GameEngine g, Warrior p, Tank t)
    {
        if (t.Doomed || t.Fuel < 5 || t.Damage > 75)
        {
            Doing = "getting out";
            // Both buttons together, on a fresh press.
            return _tick % 2 == 0 ? new Controls(Fire: true, Grenade: true) : default;
        }
        Controls move;
        if (t.Fuel < 60 && g.Pickups.FirstOrDefault(k => k.Kind == PickupKind.Fuel && k.Y > g.Camera + 30 && k.Y < g.ViewTop - 20) is { } fuel)
        {
            Doing = "to the fuel";
            move = Towards(g, p, fuel.X, fuel.Y, tank: true);
        }
        else if (g.AtEnd)
        {
            Doing = "tank holding";
            double hx = g.Stage.HasGate ? g.Stage.GateX : Lane(g, p) + Math.Sin(_tick / 100.0) * 140;
            double hy = g.Stage.HasGate ? g.Stage.GateY - GameEngine.ShellRange + 40 : g.Camera + 160;
            move = Towards(g, p, hx, hy, tank: true);
        }
        else
        {
            Doing = "tank advancing";
            var near = Targets(g, p).FirstOrDefault(s => Field.Distance(s.X, s.Y, p.X, p.Y) < 200);
            move = near != default && near.Y < p.Y + 120
                ? Towards(g, p, near.X, near.Y, tank: true)
                : Towards(g, p, Lane(g, p), g.ViewTop - 60, tank: true);
        }
        // Shells on anything along a way.
        var aims = Targets(g, p).Select(s => (s.X, s.Y)).ToList();
        foreach (var b in g.Pillboxes) if (!b.Dead) aims.Add((b.X, b.Y - 24));
        foreach (var e in g.EnemyTanks) if (!e.Dead) aims.Add((e.X, e.Y));
        if (g.Stage.HasGate && g.AtEnd && !g.Stage.GateOpen) aims.Add((g.Stage.GateX, g.Stage.GateY - 4));
        foreach (var (x, y) in aims.OrderBy(a => Field.Distance(a.X, a.Y, p.X, p.Y)))
        {
            if (Along(p.X, p.Y, x, y, GameEngine.ShellRange + 20, 18) is not { } w) continue;
            var (ux, uy) = Field.Unit(w);
            double d = Field.Distance(p.X, p.Y, x, y);
            if (!g.Stage.Clear(p.X + ux * 30, p.Y + uy * 30, x - ux * 20, y - uy * 20) && d > 60) continue;
            var (ax, ay) = Field.Ways[w];
            Doing = "shelling";
            return move with { Fire = true, AimX = ax, AimY = ay };
        }
        return move;
    }

    // ───────────────────────────── finding the way ─────────────────────────────

    /// <summary>The way to run (or drive) towards a point, round what's in the way, over the cells on the screen.</summary>
    private static Controls Towards(GameEngine g, Warrior p, double tx, double ty, bool tank = false)
    {
        var stage = g.Stage;
        bool Blocked(int c, int r) => tank
            ? stage.BlocksTank(c, r) || stage.BlocksTank(c + 1, r) || stage.BlocksTank(c, r + 1) || stage.BlocksTank(c - 1, r) || stage.BlocksTank(c, r - 1)
            : stage.BlocksFoot(c, r);
        int r0 = Field.RowOf(g.Camera) + 1, r1 = Field.RowOf(g.ViewTop) - 1;
        int sc = Field.ColOf(p.X), sr = Field.RowOf(p.Y), gc = Math.Clamp(Field.ColOf(tx), 0, Field.Cols - 1), gr = Math.Clamp(Field.RowOf(ty), r0, r1);
        var from = new Dictionary<(int, int), (int, int)>();
        var queue = new Queue<(int, int)>();
        from[(sc, sr)] = (sc, sr);
        queue.Enqueue((sc, sr));
        (int C, int R) best = (sc, sr);
        double bestD = double.MaxValue;
        while (queue.Count > 0)
        {
            var (c, r) = queue.Dequeue();
            double d = Math.Abs(c - gc) + Math.Abs(r - gr);
            if (d < bestD) { bestD = d; best = (c, r); }
            foreach (var (dc, dr) in Field.Ways)
            {
                int nc = c + dc, nr = r + dr;
                if (nc < 0 || nc >= Field.Cols || nr < r0 || nr > r1 || from.ContainsKey((nc, nr)) || Blocked(nc, nr)) continue;
                if (dc != 0 && dr != 0 && (Blocked(c + dc, r) || Blocked(c, r + dr))) continue;
                from[(nc, nr)] = (c, r);
                queue.Enqueue((nc, nr));
            }
        }
        var path = new List<(int C, int R)>();
        for (var at = best; at != (sc, sr); at = from[at]) path.Add(at);
        path.Reverse();
        double wx = best == (sc, sr) ? tx : Field.CellX(path[0].C), wy = best == (sc, sr) ? ty : Field.CellY(path[0].R);
        double radius = tank ? Tank.Radius : Warrior.Radius;
        foreach (var (c, r) in path.Take(5))
        {
            double x = Field.CellX(c), y = Field.CellY(r);
            if (!Open(stage, p.X, p.Y, x, y, radius, tank)) break;
            (wx, wy) = (x, y);
        }
        if (best == (sc, sr) && Field.Distance(p.X, p.Y, tx, ty) < 6) return default;
        double vx = wx - p.X, vy = wy - p.Y, len = Math.Max(1, Math.Sqrt(vx * vx + vy * vy));
        return new Controls(Math.Abs(vx) > len * 0.38 ? Math.Sign(vx) : 0, Math.Abs(vy) > len * 0.38 ? Math.Sign(vy) : 0);
    }

    private static bool Open(Stage stage, double ax, double ay, double bx, double by, double radius, bool tank)
    {
        double d = Field.Distance(ax, ay, bx, by);
        int n = Math.Max(1, (int)(d / 6));
        double a = Math.Atan2(by - ay, bx - ax), px = -Math.Sin(a) * (radius + 1), py = Math.Cos(a) * (radius + 1);
        bool Blocks(double x, double y) => tank ? stage.BlocksTank(Field.ColOf(x), Field.RowOf(y)) : stage.BlocksFoot(Field.ColOf(x), Field.RowOf(y));
        for (int i = 1; i <= n; i++)
        {
            double t = (double)i / n, x = ax + (bx - ax) * t, y = ay + (by - ay) * t;
            if (Blocks(x + px, y + py) || Blocks(x - px, y - py)) return false;
        }
        return true;
    }

    // ───────────────────────────── keeping alive ─────────────────────────────

    /// <summary>
    /// What it wanted, unless that walks into something: then of the nine ways to move (counting standing still),
    /// the one that keeps it clearest, still aiming and firing as it was.
    /// </summary>
    private Controls Safe(GameEngine g, Warrior p, Controls want)
    {
        double here = Clearance(g, p, want);
        if (here > 14) return want;
        Controls best = want;
        double most = here;
        foreach (var (dx, dy) in Field.Ways.Append((0, 0)))
        {
            var c = want with { Dx = dx, Dy = dy };
            double clear = Clearance(g, p, c);
            if (clear > most + 2) { most = clear; best = c; }
        }
        if (best == want) return want;
        Doing = "dodging";
        // Its aim doesn't follow its feet while it dodges: it keeps the aim it had.
        return best.AimX == 0 && best.AimY == 0 ? best with { AimX = Field.Ways[p.Facing].Dx, AimY = Field.Ways[p.Facing].Dy } : best;
    }

    /// <summary>How close the nearest danger comes to it over the next half second or so, moving like this.</summary>
    private static double Clearance(GameEngine g, Warrior p, Controls c)
    {
        var me = new Warrior(p.Index) { X = p.X, Y = p.Y, Fast = p.Fast };
        double least = double.MaxValue;
        var shots = g.Bullets.Where(b => b.Hostile && Field.Distance(b.X, b.Y, p.X, p.Y) < 320).Select(b => (b.X, b.Y, b.Vx, b.Vy, R: 0.0))
            .Concat(g.Shells.Where(s => s.Hostile && Field.Distance(s.X, s.Y, p.X, p.Y) < 360).Select(s => (s.X, s.Y, s.Vx, s.Vy, R: GameEngine.EnemyBlast)))
            .ToArray();
        var bombs = g.Grenades.Where(n => n.Hostile).ToList();
        var armour = g.EnemyTanks.Where(e => !e.Dead && Field.Distance(e.X, e.Y, p.X, p.Y) < 160).ToList();
        var fires = g.Tanks.Where(t => !t.Dead && t.Doomed).ToList();
        if (shots.Length == 0 && bombs.Count == 0 && armour.Count == 0 && fires.Count == 0) return double.MaxValue;
        for (int t = 1; t <= 36; t++)
        {
            double dt = t * Field.Step;
            GameEngine.Step(g.Stage, me, c, g.Camera, Field.Step);
            for (int i = 0; i < shots.Length; i++)
            {
                var (x, y, vx, vy, r) = shots[i];
                if (double.IsNaN(x)) continue;
                double sx = x + vx * dt, sy = y + vy * dt;
                if (Ground.Stops(g.Stage.AtPoint(sx, sy))) { shots[i] = (double.NaN, 0, 0, 0, 0); continue; }
                least = Math.Min(least, Field.Distance(sx, sy, me.X, me.Y) - Warrior.Radius - r * 0.5);
            }
            foreach (var n in bombs)
                if (Math.Abs(n.Time - n.T - dt) < Field.Step * 1.5) least = Math.Min(least, Field.Distance(n.ToX, n.ToY, me.X, me.Y) - GameEngine.EnemyBlast);
            foreach (var e in armour) least = Math.Min(least, Field.Distance(e.X, e.Y, me.X, me.Y) - EnemyTank.Radius - Warrior.Radius);
            foreach (var f in fires)
                if (f.Fuse - dt < 0.2) least = Math.Min(least, Field.Distance(f.X, f.Y, me.X, me.Y) - GameEngine.TankBlast * 0.8);
        }
        return least;
    }
}
