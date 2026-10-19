namespace AVAIkariWarriors.Engine;

public sealed partial class GameEngine
{
    public const double EnemyTankSpeed = 55, HelicopterSpeed = 120, GunRange = 420, EnemyShellSpeed = 300;

    /// <summary>The commando nearest a point (on foot or in a tank), if anyone's fighting.</summary>
    public Warrior? Nearest(double x, double y) =>
        Fighting.OrderBy(p => Field.Distance(x, y, p.X, p.Y)).FirstOrDefault();

    private bool OnScreen(double x, double y, double margin = 0) =>
        x > -margin && x < Field.Width + margin && y > Camera - margin && y < ViewTop + margin;

    /// <summary>
    /// The enemy waiting in the map comes on as the screen reaches it; more come in from the top and the sides all the
    /// while; and when the screen stops at the end of the stage, its guard comes on (or, at the fortress, the gate's
    /// defenders keep coming round the ends of the wall).
    /// </summary>
    private void Reinforce(double dt)
    {
        while (_spawnIndex < _spawns.Count && _spawns[_spawnIndex].Y <= ViewTop + 40)
        {
            var s = _spawns[_spawnIndex++];
            if (!Calm || s.Kind is SpawnKind.Tank or SpawnKind.Pickup) Bring(s);
        }
        if (Calm) return;
        if (!AtEnd && Camera >= Stage.CameraEnd - 0.5)
        {
            AtEnd = true;
            if (Stage.HasGate)
            {
                Say("THE FORTRESS", "BLAST THE GATE OPEN", 2.4);
                Play(Sfx.Warning);
            }
            else
            {
                _guardOut = true;
                int i = 0;
                foreach (var guard in Stage.Guard)
                {
                    double x = Stage.Guard.Count == 1 ? Field.Width / 2 : Field.Width * (0.3 + 0.4 * i++);
                    Bring(new Spawn(guard, x, ViewTop + 30));
                }
                Say(Stage.Guard.Contains(SpawnKind.Helicopter) ? "GUNSHIP!" : "ENEMY ARMOUR!", "DESTROY IT TO GO ON", 2.2);
            }
        }
        bool due = (_ambientIn -= dt) <= 0;
        if (_scrolled >= SpawnSpacing) { _scrolled -= SpawnSpacing; due = true; }
        if (!due) return;
        _ambientIn = AmbientEvery * (0.7 + _rng.NextDouble() * 0.6) * (AtEnd ? 1.2 : 1);
        if (Soldiers.Count(s => s.Kind is SoldierKind.Rifleman or SoldierKind.Grenadier) >= MaxSoldiers) return;
        var kind = _rng.Next(5) == 0 ? SpawnKind.Grenadier : SpawnKind.Rifleman;
        for (int i = 0; i < 10; i++)
        {
            double x, y;
            if (AtEnd && Stage.HasGate)
            {
                // Round the ends of the fortress wall.
                x = _rng.Next(2) == 0 ? 40 + _rng.NextDouble() * 80 : Field.Width - 40 - _rng.NextDouble() * 80;
                y = Stage.GateY - 20;
            }
            else if (_rng.Next(3) == 0)
            {
                // From a side, in the top half.
                x = _rng.Next(2) == 0 ? -14 : Field.Width + 14;
                y = Camera + Field.View * (0.5 + _rng.NextDouble() * 0.45);
            }
            else
            {
                x = 30 + _rng.NextDouble() * (Field.Width - 60);
                y = ViewTop + 24;
            }
            char at = Stage.AtPoint(Math.Clamp(x, 1, Field.Width - 1), y);
            if (Ground.BlocksFoot(at) || at == Ground.Water) continue;
            Bring(new Spawn(kind, x, y));
            break;
        }
    }

    private void Bring(Spawn s)
    {
        switch (s.Kind)
        {
            case SpawnKind.Rifleman or SpawnKind.Grenadier:
            {
                var kind = s.Kind == SpawnKind.Grenadier ? SoldierKind.Grenadier : SoldierKind.Rifleman;
                var o = new Soldier { Kind = kind, X = s.X, Y = s.Y, Up = true, Facing = -Math.PI / 2, FireIn = SoldierFireGap * (0.5 + _rng.NextDouble()) };
                Choose(o);
                Soldiers.Add(o);
                break;
            }
            case SpawnKind.Trench:
                Soldiers.Add(new Soldier { Kind = SoldierKind.Trench, X = s.X, Y = s.Y, ToX = s.X, ToY = s.Y, Facing = -Math.PI / 2, Wait = 0.5 + _rng.NextDouble() * 2 });
                break;
            case SpawnKind.Tank:
                Tanks.Add(new Tank { X = s.X, Y = s.Y });
                break;
            case SpawnKind.EnemyTank:
                EnemyTanks.Add(new EnemyTank { X = s.X, Y = s.Y, ToY = Math.Max(Camera + Field.View * 0.55, s.Y - 180 - _rng.NextDouble() * 120), Hp = EnemyTankHp, FireIn = 2 + _rng.NextDouble() });
                break;
            case SpawnKind.Helicopter:
                Helicopters.Add(new Helicopter
                {
                    X = s.X, Y = ViewTop + 80, ToX = s.X, ToY = Camera + Field.View * 0.72, Hp = HelicopterHp, MaxHp = HelicopterHp,
                    FireIn = 2.2, BombIn = 3.5, T = _rng.NextDouble() * 3,
                });
                Play(Sfx.Helicopter);
                break;
            case SpawnKind.Pickup:
                Pickups.Add(new Pickup { Kind = s.Item, X = s.X, Y = s.Y });
                break;
        }
    }

    /// <summary>Where a soldier runs to next: somewhere above his man and to one side, that he can stand on.</summary>
    private void Choose(Soldier s)
    {
        var p = Nearest(s.X, s.Y);
        double px = p?.X ?? Field.Width / 2, py = p?.Y ?? Camera + 200;
        double above = s.Kind == SoldierKind.Grenadier ? 220 + _rng.NextDouble() * 120 : 130 + _rng.NextDouble() * 170;
        for (int i = 0; i < 10; i++)
        {
            double x = Math.Clamp(px + (_rng.NextDouble() - 0.5) * 460, 20, Field.Width - 20);
            double y = Math.Clamp(py + above, Camera + 60, ViewTop - 20);
            if (Stage.BlocksFoot(Field.ColOf(x), Field.RowOf(y))) continue;
            s.ToX = x;
            s.ToY = y;
            s.T = 0;
            return;
        }
        s.ToX = s.X;
        s.ToY = s.Y;
    }

    private void MoveSoldiers(double dt)
    {
        foreach (var s in Soldiers.ToList())
        {
            if (Calm || s.Dead) continue;
            s.T += dt;
            s.FireIn -= dt;
            var p = Nearest(s.X, s.Y);
            if (s.Kind == SoldierKind.Trench)
            {
                // Down in the trench, then up for a moment to fire (or lob one), then down again.
                if (!s.Up)
                {
                    if (s.T < s.Wait || !OnScreen(s.X, s.Y)) continue;
                    s.Up = s.Aiming = true;
                    s.T = 0;
                    continue;
                }
                if (p != null) s.Facing = Math.Atan2(p.Y - s.Y, p.X - s.X);
                if (s.Aiming && s.T > 0.6)
                {
                    s.Aiming = false;
                    if (p != null && _rng.Next(3) == 0 && CanLob(s, p)) Lob(s, p);
                    else if (p != null && Sees(s, p)) Shoot(s, p);
                }
                if (s.T > 1.4)
                {
                    s.Up = false;
                    s.T = 0;
                    s.Wait = Math.Max(0.8, SoldierFireGap) * (0.8 + _rng.NextDouble());
                }
                continue;
            }
            if (s.Aiming)
            {
                if (p != null) s.Facing = Math.Atan2(p.Y - s.Y, p.X - s.X);
                if ((s.Wait -= dt) > 0) continue;
                s.Aiming = false;
                // A rifleman needs a clear shot; a grenadier throws over whatever's between.
                if (p != null && (s.Kind == SoldierKind.Grenadier ? CanLob(s, p) : Sees(s, p)))
                {
                    if (s.Kind == SoldierKind.Grenadier) Lob(s, p);
                    else Shoot(s, p);
                    s.FireIn = SoldierFireGap * (s.Kind == SoldierKind.Grenadier ? 1.6 : 1);
                }
                Choose(s);
                continue;
            }
            double before = Field.Distance(s.X, s.Y, s.ToX, s.ToY);
            Walk(s, s.ToX, s.ToY, SoldierSpeed * Ground.Pace(Stage.AtPoint(s.X, s.Y)), dt);
            double after = Field.Distance(s.X, s.Y, s.ToX, s.ToY);
            s.Stuck = before - after < SoldierSpeed * dt * 0.2 ? s.Stuck + dt : 0;
            if (after < 6 || s.T > 2.6 || s.Stuck > 0.3)
            {
                if (s.FireIn <= 0 && p != null && Phase == Phase.Play) { s.Aiming = true; s.Wait = 0.45; }
                else Choose(s);
            }
        }
    }

    private void Walk(Soldier s, double tx, double ty, double speed, double dt)
    {
        double dx = tx - s.X, dy = ty - s.Y, d = Math.Sqrt(dx * dx + dy * dy);
        if (d < 1) return;
        double step = Math.Min(d, speed * dt);
        double x = s.X + dx / d * step, y = s.Y + dy / d * step;
        Stage.Push(ref x, ref y, Soldier.Radius);
        s.X = x;
        s.Y = y;
        s.Facing = Math.Atan2(dy, dx);
        s.Walk += dt;
    }

    /// <summary>Whether a soldier (on the screen) has a clear shot at a commando.</summary>
    private bool Sees(Soldier s, Warrior p) =>
        p.Safe <= 0 && Phase == Phase.Play && OnScreen(s.X, s.Y, -2) && Stage.Clear(s.X, s.Y, p.X, p.Y);

    /// <summary>Whether a soldier (on the screen) could lob a grenade onto a commando: near enough, cover or no cover.</summary>
    private bool CanLob(Soldier s, Warrior p) =>
        p.Safe <= 0 && Phase == Phase.Play && OnScreen(s.X, s.Y, -2) && Field.Distance(s.X, s.Y, p.X, p.Y) < 420;

    private void Shoot(Soldier s, Warrior p)
    {
        double a = Math.Atan2(p.Y - s.Y, p.X - s.X) + (_rng.NextDouble() - 0.5) * 2 * AimError;
        s.Facing = a;
        EnemyShot(s.X + Math.Cos(a) * 12, s.Y + Math.Sin(a) * 12, a);
    }

    private void EnemyShot(double x, double y, double a, double life = 2.2)
    {
        Bullets.Add(new Bullet { X = x, Y = y, Vx = Math.Cos(a) * EnemyShotSpeed, Vy = Math.Sin(a) * EnemyShotSpeed, Life = life, Hostile = true, Owner = -1 });
        Play(Sfx.EnemyFire);
    }

    /// <summary>A grenade lobbed at where a commando is, give or take, over any cover between.</summary>
    private void Lob(Soldier s, Warrior p)
    {
        double tx = p.X + (_rng.NextDouble() - 0.5) * 48, ty = p.Y + (_rng.NextDouble() - 0.5) * 48;
        Grenades.Add(new Grenade { FromX = s.X, FromY = s.Y, ToX = tx, ToY = ty, Time = Math.Clamp(Field.Distance(s.X, s.Y, tx, ty) / 300, 0.6, 1.2), Hostile = true, Owner = -1 });
        Play(Sfx.Throw);
    }

    /// <summary>Enemy tanks roll down to a stopping place, swing the gun round on the nearest commando, and shell him; then on again.</summary>
    private void MoveEnemyTanks(double dt)
    {
        foreach (var e in EnemyTanks)
        {
            if (Calm || e.Dead) continue;
            e.FireIn -= dt;
            e.Flash = Math.Max(0, e.Flash - dt);
            var p = Nearest(e.X, e.Y);
            if (e.Y > e.ToY + 1)
            {
                double x = e.X, y = e.Y - EnemyTankSpeed * dt;
                if (Stage.Push(ref x, ref y, EnemyTank.Radius - 2, tank: true) && Math.Abs(y - (e.Y - EnemyTankSpeed * dt)) > 0.5) e.ToY = e.Y;
                e.X = Math.Clamp(x, EnemyTank.Radius, Field.Width - EnemyTank.Radius);
                e.Y = y;
                e.Tread += dt;
            }
            if (p == null) continue;
            double want = Math.Atan2(p.Y - e.Y, p.X - e.X), diff = Math.IEEERemainder(want - e.Turret, Math.PI * 2);
            e.Turret += Math.Clamp(diff, -1.6 * dt, 1.6 * dt);
            if (e.FireIn > 0 || Math.Abs(diff) > 0.12 || !OnScreen(e.X, e.Y, -10) || p.Safe > 0 || Phase != Phase.Play) continue;
            double d = Field.Distance(e.X, e.Y, p.X, p.Y);
            if (d > 560) continue;
            e.FireIn = EnemyTankGap * (0.85 + _rng.NextDouble() * 0.3);
            e.Flash = 0;
            double ux = Math.Cos(e.Turret), uy = Math.Sin(e.Turret);
            Shells.Add(new Shell { X = e.X + ux * 32, Y = e.Y + uy * 32, Vx = ux * EnemyShellSpeed, Vy = uy * EnemyShellSpeed, Range = Math.Min(d + 30, 520), Hostile = true, Owner = -1 });
            Effects.Add(new Effect { Kind = EffectKind.Flash, X = e.X + ux * 34, Y = e.Y + uy * 34, Life = 0.12 });
            Play(Sfx.Cannon);
            // Now and then, after a shot, on down the map a little.
            if (_rng.Next(3) == 0) e.ToY = Math.Max(Camera + Field.View * 0.4, e.Y - 90);
        }
    }

    /// <summary>Gunships hover over the field, drifting over the nearest commando, firing bursts of three and dropping bombs.</summary>
    private void MoveHelicopters(double dt)
    {
        bool chop = false;
        foreach (var h in Helicopters)
        {
            if (Calm || h.Dead) continue;
            chop = true;
            h.T += dt;
            h.Flash = Math.Max(0, h.Flash - dt);
            h.FireIn -= dt;
            h.BombIn -= dt;
            var p = Nearest(h.X, h.Y);
            double dx = h.ToX - h.X, dy = h.ToY - h.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d > 2)
            {
                double step = Math.Min(d, HelicopterSpeed * dt);
                h.X += dx / d * step;
                h.Y += dy / d * step;
            }
            h.Tilt += (Math.Clamp(dx / 120, -1, 1) - h.Tilt) * Math.Min(1, dt * 4);
            if (d < 8 || (h.T % 3.2) < dt)
            {
                double px = p?.X ?? Field.Width / 2;
                h.ToX = Math.Clamp(px + (_rng.NextDouble() - 0.5) * 360, 70, Field.Width - 70);
                h.ToY = Camera + Field.View * (0.58 + _rng.NextDouble() * 0.27);
            }
            if (p == null || p.Safe > 0 || Phase != Phase.Play || !OnScreen(h.X, h.Y)) continue;
            if (h.FireIn <= 0)
            {
                double a = Math.Atan2(p.Y - h.Y, p.X - h.X) + (_rng.NextDouble() - 0.5) * AimError;
                EnemyShot(h.X + Math.Cos(a) * 26, h.Y + Math.Sin(a) * 26, a);
                h.Burst++;
                h.FireIn = h.Burst % 3 == 0 ? Math.Max(1.4, 2.4 - 0.1 * Level) : 0.13;
            }
            if (h.BombIn <= 0)
            {
                h.BombIn = Math.Max(2.4, 3.8 - 0.15 * Level);
                double tx = p.X + (_rng.NextDouble() - 0.5) * 60, ty = p.Y + (_rng.NextDouble() - 0.5) * 60;
                Grenades.Add(new Grenade { FromX = h.X, FromY = h.Y, ToX = tx, ToY = ty, Time = 1.0, Hostile = true, Owner = -1 });
            }
        }
        if (chop && (_rotorIn -= dt) <= 0)
        {
            _rotorIn = 0.9;
            Play(Sfx.Helicopter);
        }
    }

    /// <summary>Gun emplacements swing round on the nearest commando and fire when they have a clear shot.</summary>
    private void FireGuns(double dt)
    {
        foreach (var g in Guns)
        {
            g.Flash = Math.Max(0, g.Flash - dt);
            if (g.Dead || Calm || !OnScreen(g.X, g.Y)) continue;
            var p = Nearest(g.X, g.Y);
            if (p == null) continue;
            double want = Math.Atan2(p.Y - g.Y, p.X - g.X), diff = Math.IEEERemainder(want - g.Angle, Math.PI * 2);
            g.Angle += Math.Clamp(diff, -2.2 * dt, 2.2 * dt);
            if ((g.FireIn -= dt) > 0 || p.Safe > 0 || Math.Abs(diff) > 0.2) continue;
            double sx = g.X + Math.Cos(g.Angle) * 24, sy = g.Y + Math.Sin(g.Angle) * 24;
            if (Field.Distance(g.X, g.Y, p.X, p.Y) > GunRange || !Stage.Clear(sx, sy, p.X, p.Y)) continue;
            g.FireIn = GunGap;
            EnemyShot(sx, sy, g.Angle + (_rng.NextDouble() - 0.5) * AimError);
        }
    }

    /// <summary>Pillboxes on the screen fire a spread of three at the nearest commando when they can see him.</summary>
    private void FirePillboxes(double dt)
    {
        foreach (var b in Pillboxes)
        {
            if (b.Dead || Calm || !OnScreen(b.X, b.Y)) continue;
            var p = Nearest(b.X, b.Y);
            if ((b.FireIn -= dt) > 0 || p == null || p.Safe > 0) continue;
            double a = Math.Atan2(p.Y - b.Y, p.X - b.X), sx = b.X + Math.Cos(a) * 38, sy = b.Y + Math.Sin(a) * 38;
            if (Field.Distance(b.X, b.Y, p.X, p.Y) > 420 || !Stage.Clear(sx, sy, p.X, p.Y)) continue;
            b.FireIn = PillboxGap;
            foreach (double off in new[] { -0.2, 0, 0.2 }) EnemyShot(sx, sy, a + off, 2);
        }
    }

    // ───────────────────────────── the enemy falling ─────────────────────────────

    private static readonly PickupKind[] Drops =
        [PickupKind.Bullets, PickupKind.Bullets, PickupKind.Bullets, PickupKind.Grenades, PickupKind.Grenades, PickupKind.Fuel, PickupKind.Speed, PickupKind.Range, PickupKind.Big, PickupKind.KillAll];

    /// <summary>A soldier falls, with his cry; now and then he drops something worth having.</summary>
    private void Down(Soldier s, int owner)
    {
        if (s.Dead) return;
        s.Dead = true;
        Award(owner, PointsFor(s.Kind), s.X, s.Y);
        Sparks(s.X, s.Y, 0xFFB0A080, 6, 0.5);
        Play(Sfx.EnemyDown);
        if (++_kills % DropEvery == 0 && !Ground.BlocksFoot(Stage.AtPoint(s.X, s.Y)))
            Pickups.Add(new Pickup { Kind = Drops[_rng.Next(Drops.Length)], X = s.X, Y = s.Y, Life = 12 });
    }

    private void Wreck(Gun g, int owner)
    {
        g.Dead = true;
        Stage.Set(g.Col, g.Row, Ground.Rubble);
        Award(owner, GunPoints, g.X, g.Y);
        Blast(g.X, g.Y, 0.9);
        Play(Sfx.Blast);
    }

    private void Wreck(Pillbox b, int owner)
    {
        // Its walls come down: the cells it stood on are open ground again.
        b.Dead = true;
        for (int dc = 0; dc < 2; dc++) for (int dr = 0; dr < 2; dr++) Stage.Set(b.Col + dc, b.Row + dr, Ground.Rubble);
        Award(owner, PillboxPoints, b.X, b.Y);
        Sparks(b.X, b.Y, 0xFFA0A0A0, 14, 1);
        Play(Sfx.TankBoom);
    }

    private void Wreck(EnemyTank e, int owner)
    {
        e.Dead = true;
        Award(owner, EnemyTankPoints, e.X, e.Y);
        Blast(e.X, e.Y, 1.6);
        Sparks(e.X, e.Y, 0xFF6A6050, 16, 1.2);
        Play(Sfx.TankBoom);
    }

    private void Wreck(Helicopter h, int owner)
    {
        h.Dead = true;
        Award(owner, HelicopterPoints, h.X, h.Y);
        Blast(h.X, h.Y, 1.9);
        Sparks(h.X, h.Y, 0xFF5A6A5A, 20, 1.4);
        Play(Sfx.TankBoom);
    }
}
