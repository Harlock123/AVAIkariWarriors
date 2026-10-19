namespace AVAIkariWarriors.Engine;

public sealed partial class GameEngine
{
    // On foot.
    public const double RunSpeed = 150, FastSpeed = 205, BulletSpeed = 560, BulletLife = 0.42, FarLife = 0.72, FireGap = 0.12, TurnGap = 0.15;
    public const int MaxBullets = 4, StartAmmo = 150, StartGrenades = 30, BulletBox = 60, GrenadeBox = 10, MaxAmmo = 999;
    public const double ThrowRange = 210, ThrowTime = 0.75, ThrowGap = 0.4, BlastRadius = 52, BigBlastRadius = 84, EnemyBlast = 40;
    // In a tank.
    public const double TankSpeed = 120, ShellSpeed = 470, ShellRange = 300, ShellGap = 0.55, ShellBlast = 58, TankBlast = 76, FuseSeconds = 3;
    public const double FuelDriving = 2.4, FuelIdling = 0.5, FuelLow = 20, BulletDamage = 4, BlastDamage = 30, ShellDamage = 34;
    // The flow of the game.
    public const double StartSeconds = 2.4, DyingSeconds = 2.4, SafeSeconds = 2.5, ClearSeconds = 4, FinaleSeconds = 9;
    public const int ExtraLifeEvery = 30000, GunPoints = 500, PillboxPoints = 1000, EnemyTankPoints = 2000, HelicopterPoints = 3000,
        GatePoints = 5000, FuelPoints = 500, DropEvery = 6;

    public Stage Stage { get; private set; } = Stage.Build(1);
    /// <summary>The world y at the bottom of the screen.</summary>
    public double Camera { get; private set; }
    public List<Soldier> Soldiers { get; } = [];
    public List<Tank> Tanks { get; } = [];
    public List<EnemyTank> EnemyTanks { get; } = [];
    public List<Helicopter> Helicopters { get; } = [];
    public List<Gun> Guns { get; } = [];
    public List<Pillbox> Pillboxes { get; } = [];
    public List<Bullet> Bullets { get; } = [];
    public List<Grenade> Grenades { get; } = [];
    public List<Shell> Shells { get; } = [];
    public List<Pickup> Pickups { get; } = [];
    public List<Debris> Wreckage { get; } = [];
    public int Bonus { get; private set; }
    /// <summary>The screen has stopped at the end of the stage: the guard is out, or the gate's ahead.</summary>
    public bool AtEnd { get; private set; }
    public int GateHp { get; private set; }
    public int GateMaxHp { get; private set; }
    /// <summary>How far the fortress gate has swung open, 0 to 1.</summary>
    public double GateSwing => Stage.GateOpen ? Math.Min(1, (Phase == Phase.Finale ? PhaseTime : 9) / 1.8) : 0;
    /// <summary>For tests: the enemy stands still and holds its fire, and no more come.</summary>
    internal bool Calm { get; set; }

    private int _spawnIndex, _kills;
    private double _ambientIn, _scrolled, _engineIn, _rotorIn, _warnIn;
    private bool _guardOut;
    private List<Spawn> _spawns = [];

    /// <summary>How hard it is: 0 for the first stage, one more each stage, round after round.</summary>
    public int Level => Stage.Number - 1;
    public double EnemyShotSpeed => Math.Min(360, 220 + 18 * Level);
    public double SoldierSpeed => Math.Min(135, 80 + 8 * Level);
    public double SoldierFireGap => Math.Max(0.85, 2.1 - 0.18 * Level);
    public double AimError => Math.Max(0.03, 0.14 - 0.02 * Level);
    /// <summary>A soldier comes on every so far up the map...</summary>
    public double SpawnSpacing => Math.Max(90, 170 - 12 * Level);
    /// <summary>...and, slowly, every so often anyway, so standing still is no answer.</summary>
    public double AmbientEvery => Math.Max(2, 4.5 - 0.3 * Level);
    public int MaxSoldiers => Math.Min(12, 5 + Level + (Fighting.Count() > 1 ? 2 : 0));
    public double GunGap => Math.Max(1.0, 1.9 - 0.1 * Level);
    public double PillboxGap => Math.Max(1.3, 2.6 - 0.15 * Level);
    public double EnemyTankGap => Math.Max(1.6, 2.9 - 0.15 * Level);
    public int EnemyTankHp => 4 + (Stage.Round - 1) * 2;
    public int HelicopterHp => 24 + (Stage.Round - 1) * 8;
    public int GunHp => 6 + (Stage.Round - 1) * 2;
    /// <summary>The top of the play area on the screen, in world y.</summary>
    public double ViewTop => Camera + Field.View;

    public static int PointsFor(SoldierKind kind) => kind switch { SoldierKind.Grenadier => 150, SoldierKind.Trench => 200, _ => 100 };

    private void NewGame()
    {
        _rng = new Random(_seed);
        _kills = 0;
        foreach (var p in Players)
        {
            p.Joined = p.Alive = false;
            p.Score = p.Lives = p.Continues = 0;
            p.Continue = p.Down = 0;
            p.Tank = null;
        }
        Wreckage.Clear();
        Stage = Stage.Build(1);
        Camera = 0;
        ClearField();
        SetPhase(Phase.Start);
    }

    private void ClearField()
    {
        Soldiers.Clear();
        Tanks.Clear();
        EnemyTanks.Clear();
        Helicopters.Clear();
        Guns.Clear();
        Pillboxes.Clear();
        Bullets.Clear();
        Grenades.Clear();
        Shells.Clear();
        Pickups.Clear();
    }

    /// <summary>A full kit: bullets, grenades, and none of the letters.</summary>
    private static void Kit(Warrior p)
    {
        p.Ammo = StartAmmo;
        p.Grenades = StartGrenades;
        p.Fast = p.Far = p.Big = false;
        p.Tank = null;
    }

    /// <summary>In at the bottom of the screen: beside the other commando if he's there, safe for a moment.</summary>
    private void PlaceAtBottom(Warrior p)
    {
        var mate = Players.FirstOrDefault(o => o != p && o.Fighting);
        double side = p.Index == 0 ? -1 : 1;
        double x = mate != null ? Math.Clamp(mate.X + side * 60, 40, Field.Width - 40) : Field.Width / 2 + side * 50;
        double y = Camera + 120;
        // Somewhere dry and open, near there.
        for (int i = 0; i < 24; i++)
        {
            double tx = Math.Clamp(x + (i % 2 == 0 ? 1 : -1) * (i / 2) * 26, 30, Field.Width - 30), ty = y + (i / 8) * 30;
            if (Stage.BlocksFoot(Field.ColOf(tx), Field.RowOf(ty)) || Stage.AtPoint(tx, ty) == Ground.Water) continue;
            x = tx;
            y = ty;
            break;
        }
        p.X = x;
        p.Y = y;
        Stage.Push(ref p.X, ref p.Y, Warrior.Radius);
        p.Facing = 0;
        p.Alive = true;
        p.Down = 0;
        p.Tank = null;
        p.Safe = SafeSeconds;
        p.FireIn = p.ThrowIn = p.TurnIn = 0;
    }

    /// <summary>A stage: its ground built, the commandos at the bottom, the enemy waiting in it.</summary>
    private void BeginStage(int number)
    {
        Stage = Stage.Build(number);
        Camera = 0;
        ClearField();
        foreach (var (c, r) in Stage.Bunkers) Pillboxes.Add(new Pillbox { Col = c, Row = r, FireIn = 1 + _rng.NextDouble() });
        foreach (var (c, r) in Stage.Guns) Guns.Add(new Gun { Col = c, Row = r, Hp = GunHp, FireIn = 1 + _rng.NextDouble() });
        _spawns = Stage.Spawns.OrderBy(s => s.Y).ToList();
        _spawnIndex = 0;
        _ambientIn = 2;
        _scrolled = 0;
        AtEnd = _guardOut = false;
        GateHp = GateMaxHp = Stage.HasGate ? 12 + 4 * (Stage.Round - 1) : 0;
        foreach (var p in Players.Where(p => p.Joined && (p.Alive || p.Lives > 0)))
        {
            p.Tank = null;
            PlaceAtBottom(p);
            p.Y = 110;
            p.X = Field.Width / 2 + (p.Index == 0 ? -50 : 50);
        }
        SetPhase(Phase.Start);
        int stage = Stage.Theme + 1;
        if (Stage.Round > 1 && Stage.Theme == 0) Say($"ROUND {Stage.Round}", "THE WAR GOES ON - " + Stage.Name, StartSeconds + 0.6);
        else Say($"STAGE {stage}", Stage.Name, StartSeconds + 0.3);
        Play(Sfx.StageStart);
        Music(true);
    }

    private void Simulate(double dt, Controls c1, Controls c2)
    {
        MoveDebris(dt);
        Controls[] cs = [c1, c2];
        switch (Phase)
        {
            case Phase.Start:
                foreach (var p in Fighting) Live(p, cs[p.Index] with { Fire = false, Grenade = false }, dt);
                if (PhaseTime >= StartSeconds) SetPhase(Phase.Play);
                break;
            case Phase.Play:
                foreach (var p in Players.Where(p => p.Fighting).ToList()) Live(p, cs[p.Index], dt);
                Scroll();
                Reinforce(dt);
                MoveSoldiers(dt);
                MoveEnemyTanks(dt);
                MoveHelicopters(dt);
                FireGuns(dt);
                FirePillboxes(dt);
                MoveTanks(dt);
                MoveBullets(dt);
                MoveShells(dt);
                MoveGrenades(dt);
                Contact();
                Tidy();
                if (Phase == Phase.Play) CheckEnd();
                break;
            case Phase.Clear:
                foreach (var p in Fighting) Live(p, cs[p.Index] with { Fire = false, Grenade = false }, dt);
                MoveTanks(dt);
                MoveBullets(dt);
                MoveGrenades(dt);
                if (PhaseTime >= 1.2 && PhaseTime - dt < 1.2 && Bonus > 0)
                    foreach (var p in Fighting) Award(p.Index, Bonus, p.X, p.Y + 30);
                if (PhaseTime >= ClearSeconds) BeginStage(Stage.Number + 1);
                break;
            case Phase.Finale:
                MarchIn(dt);
                if (PhaseTime >= 3.2 && PhaseTime - dt < 3.2)
                {
                    foreach (var p in Fighting) Award(p.Index, Bonus, p.X, p.Y);
                    Say("MISSION COMPLETE", $"BONUS {Bonus}", 3.5);
                }
                if (PhaseTime >= FinaleSeconds) BeginStage(Stage.Number + 1);
                break;
        }
        PhaseTime += dt;
        Downs(dt);
    }

    // ───────────────────────────── the commandos ─────────────────────────────

    /// <summary>One step for a commando in the fight: on foot or at the controls of a tank.</summary>
    private void Live(Warrior p, Controls c, double dt)
    {
        p.Safe = Math.Max(0, p.Safe - dt);
        p.FireIn -= dt;
        p.ThrowIn -= dt;
        p.TurnIn -= dt;
        if (p.Tank is { } t)
        {
            Drive(p, t, c, dt);
            return;
        }
        p.BothPrev = c.Fire && c.Grenade;
        Aim(p, c);
        bool wading = p.Wading;
        Step(Stage, p, c, Camera, dt);
        if (p.Wading && (c.Dx != 0 || c.Dy != 0) && (p.StepIn -= dt) <= 0)
        {
            p.StepIn = 0.34;
            Play(Sfx.Wade);
            Effects.Add(new Effect { Kind = EffectKind.Splash, X = p.X, Y = p.Y - 8, Life = 0.4 });
        }
        else if (p.Wading && !wading) Play(Sfx.Wade);
        Fire(p, c);
        Throw(p, c);
        Board(p);
    }

    /// <summary>
    /// The aim, apart from the running: a right stick points it; turning swings it a step (and steps on while
    /// held); otherwise he aims the way he runs, unless he's firing, which holds the aim where it is.
    /// </summary>
    public static void Aim(Warrior p, Controls c)
    {
        if (c.AimX != 0 || c.AimY != 0) p.Facing = Field.WayOf(c.AimX, c.AimY);
        else if (c.Turn != 0)
        {
            if (p.TurnIn <= 0)
            {
                p.Facing = (p.Facing + Math.Sign(c.Turn) + 8) % 8;
                p.TurnIn = TurnGap;
            }
            return;
        }
        else if (!c.Fire && (c.Dx != 0 || c.Dy != 0)) p.Facing = Field.WayOf(c.Dx, c.Dy);
        p.TurnIn = 0;
    }

    /// <summary>One step of running: eight ways, slowly through water and trenches, out of anything solid, on the screen.</summary>
    public static void Step(Stage stage, Warrior p, Controls c, double camera, double dt)
    {
        if (c.Dx != 0 || c.Dy != 0)
        {
            double pace = Ground.Pace(stage.AtPoint(p.X, p.Y)), speed = (p.Fast ? FastSpeed : RunSpeed) * pace;
            double k = c.Dx != 0 && c.Dy != 0 ? Math.Sqrt(0.5) : 1;
            double x = p.X + Math.Sign(c.Dx) * speed * k * dt, y = p.Y + Math.Sign(c.Dy) * speed * k * dt;
            stage.Push(ref x, ref y, Warrior.Radius);
            p.X = Math.Clamp(x, Warrior.Radius, Field.Width - Warrior.Radius);
            p.Y = Math.Clamp(y, camera + Warrior.Radius + 4, camera + Field.View - Warrior.Radius);
            p.Walk += dt * pace;
        }
        char at = stage.AtPoint(p.X, p.Y);
        p.Wading = at == Ground.Water;
        p.Trench = at == Ground.Trench;
    }

    private void Fire(Warrior p, Controls c)
    {
        if (!c.Fire || p.FireIn > 0) return;
        if (Bullets.Count(b => !b.Hostile && b.Owner == p.Index) >= MaxBullets) return;
        if (p.Ammo <= 0)
        {
            p.FireIn = 0.35;
            Play(Sfx.Empty);
            return;
        }
        p.Ammo--;
        p.FireIn = FireGap;
        var (ux, uy) = Field.Unit(p.Facing);
        Bullets.Add(new Bullet { X = p.X + ux * 14, Y = p.Y + uy * 14 + 2, Vx = ux * BulletSpeed, Vy = uy * BulletSpeed, Life = p.Far ? FarLife : BulletLife, Owner = p.Index });
        Play(Sfx.Fire);
    }

    /// <summary>A grenade, thrown the way he aims, a fixed distance, over whatever's in the way. Not from the water.</summary>
    private void Throw(Warrior p, Controls c)
    {
        if (!c.Grenade || p.ThrowIn > 0 || p.Grenades <= 0 || p.Wading) return;
        p.ThrowIn = ThrowGap;
        p.Grenades--;
        var (ux, uy) = Field.Unit(p.Facing);
        Grenades.Add(new Grenade
        {
            FromX = p.X, FromY = p.Y + 6, ToX = Math.Clamp(p.X + ux * ThrowRange, 8, Field.Width - 8), ToY = p.Y + uy * ThrowRange,
            Time = ThrowTime, Radius = p.Big ? BigBlastRadius : BlastRadius, Big = p.Big, Owner = p.Index,
        });
        Play(Sfx.Throw);
    }

    /// <summary>Walking into an empty tank (with fuel in it, and not burning) is climbing in.</summary>
    private void Board(Warrior p)
    {
        foreach (var t in Tanks)
        {
            if (t.Dead || t.Driver != null || t.Doomed || t.Fuel <= 0 || t.EnterIn > 0) continue;
            if (Field.Distance(p.X, p.Y, t.X, t.Y) > Tank.Radius + Warrior.Radius) continue;
            p.Tank = t;
            t.Driver = p;
            t.Turret = p.Facing;
            p.X = t.X;
            p.Y = t.Y;
            p.Wading = p.Trench = false;
            Effects.Add(new Effect { Kind = EffectKind.Word, X = t.X, Y = t.Y + 40, Text = "TANK!", Life = 1, Colour = 0xFF8AFF8A });
            Play(Sfx.TankEngine);
            return;
        }
    }

    /// <summary>At the controls: driving, the gun on the aim, shells on either button, and both buttons together to get out.</summary>
    private void Drive(Warrior p, Tank t, Controls c, double dt)
    {
        bool both = c.Fire && c.Grenade;
        if (both && !p.BothPrev)
        {
            p.BothPrev = true;
            GetOut(p);
            return;
        }
        p.BothPrev = both;
        Aim(p, c with { Fire = c.Fire || c.Grenade });
        t.Turret = p.Facing;
        t.Moving = c.Dx != 0 || c.Dy != 0;
        if (t.Moving)
        {
            t.Body = Field.WayOf(c.Dx, c.Dy);
            var (ux, uy) = Field.Unit(t.Body);
            double x = t.X + ux * TankSpeed * dt, y = t.Y + uy * TankSpeed * dt;
            Flatten(x, y);
            Stage.Push(ref x, ref y, Tank.Radius, tank: true);
            foreach (var e in EnemyTanks.Where(e => !e.Dead))
            {
                // Tanks don't drive through one another.
                double d = Field.Distance(x, y, e.X, e.Y), min = Tank.Radius + EnemyTank.Radius;
                if (d < min && d > 0.01) { x = e.X + (x - e.X) / d * min; y = e.Y + (y - e.Y) / d * min; }
            }
            t.X = Math.Clamp(x, Tank.Radius, Field.Width - Tank.Radius);
            t.Y = Math.Clamp(y, Camera + Tank.Radius + 4, Camera + Field.View - Tank.Radius);
            t.Tread += dt;
        }
        p.X = t.X;
        p.Y = t.Y;
        t.Fuel = Math.Max(0, t.Fuel - (t.Moving ? FuelDriving : FuelIdling) * dt);
        if ((c.Fire || c.Grenade) && !both && t.FireIn <= 0)
        {
            t.FireIn = ShellGap;
            var (ux, uy) = Field.Unit(t.Turret);
            Shells.Add(new Shell { X = t.X + ux * 30, Y = t.Y + uy * 30, Vx = ux * ShellSpeed, Vy = uy * ShellSpeed, Range = ShellRange, Owner = p.Index });
            Effects.Add(new Effect { Kind = EffectKind.Flash, X = t.X + ux * 32, Y = t.Y + uy * 32, Life = 0.12 });
            Play(Sfx.Cannon);
        }
    }

    /// <summary>A tank rolls sandbags and wire flat.</summary>
    private void Flatten(double x, double y)
    {
        double r = Tank.Radius - 2;
        for (int row = Field.RowOf(y - r); row <= Field.RowOf(y + r); row++)
        for (int col = Field.ColOf(x - r); col <= Field.ColOf(x + r); col++)
        {
            if (!Ground.Crushable(Stage.At(col, row))) continue;
            double nx = Math.Clamp(x, col * Field.Cell, (col + 1) * Field.Cell), ny = Math.Clamp(y, row * Field.Cell, (row + 1) * Field.Cell);
            if (Field.Distance(x, y, nx, ny) >= r) continue;
            uint colour = Stage.At(col, row) == Ground.Fence ? 0xFF9A9A9Au : 0xFFC8B880u;
            Stage.Set(col, row, Ground.Road);
            Sparks(Field.CellX(col), Field.CellY(row), colour, 5, 0.5);
        }
    }

    /// <summary>Out of the tank, beside it: wherever there's room, on the screen.</summary>
    private void GetOut(Warrior p)
    {
        if (p.Tank is not { } t) return;
        double d = Tank.Radius + Warrior.Radius + 8;
        (double X, double Y)[] spots = [(-1, 0), (1, 0), (0, -1), (-1, -1), (1, -1), (0, 1), (-1, 1), (1, 1)];
        double x = t.X, y = t.Y;
        foreach (var (sx, sy) in spots)
        {
            double tx = t.X + sx * d, ty = t.Y + sy * d;
            if (tx < 12 || tx > Field.Width - 12 || ty < Camera + 16 || ty > ViewTop - 12) continue;
            double px = tx, py = ty;
            if (Stage.Push(ref px, ref py, Warrior.Radius)) continue;
            (x, y) = (tx, ty);
            break;
        }
        p.Tank = null;
        p.X = x;
        p.Y = y;
        t.Driver = null;
        t.Moving = false;
        t.EnterIn = 0.8;
        p.FireIn = p.ThrowIn = 0.2;
        Stage.Push(ref p.X, ref p.Y, Warrior.Radius);
    }

    /// <summary>Tanks: running dry or too badly hit, one catches fire; a few seconds later it goes up, and whoever's still in it.</summary>
    private void MoveTanks(double dt)
    {
        bool rumble = false;
        foreach (var t in Tanks)
        {
            if (t.Dead) continue;
            t.EnterIn -= dt;
            t.FireIn -= dt;
            if (!t.Doomed && (t.Damage >= 100 || (t.Driver != null && t.Fuel <= 0)))
            {
                t.Fuse = FuseSeconds;
                Effects.Add(new Effect { Kind = EffectKind.Word, X = t.X, Y = t.Y + 44, Text = t.Driver != null ? "GET OUT!" : "BURNING", Life = 1.6, Colour = 0xFFFF6A4A });
                Play(Sfx.Warning);
            }
            if (t.Doomed && (t.Fuse -= dt) <= 0)
            {
                Explode(t);
                continue;
            }
            if (t.Driver != null && !t.Doomed && t.Fuel < FuelLow && (_warnIn -= dt) <= 0)
            {
                _warnIn = 1;
                Play(Sfx.Warning);
            }
            if (t.Driver != null && t.Moving) rumble = true;
        }
        if (rumble && (_engineIn -= dt) <= 0)
        {
            _engineIn = 0.5;
            Play(Sfx.TankEngine);
        }
    }

    /// <summary>A tank goes up: whoever's in it, and anyone on foot close by, with it; and the enemy round it.</summary>
    private void Explode(Tank t)
    {
        t.Dead = true;
        int owner = t.Driver?.Index ?? -1;
        if (t.Driver is { } d)
        {
            d.Tank = null;
            t.Driver = null;
            Kill(d, "went up with the tank");
        }
        foreach (var p in Fighting.Where(p => p.Tank == null && p.Safe <= 0 && Field.Distance(p.X, p.Y, t.X, t.Y) < TankBlast * 0.8).ToList())
            Kill(p, "caught in the blast");
        Blast(t.X, t.Y, TankBlast, owner, hostile: false, power: 3, heli: 12, sound: Sfx.TankBoom);
        Sparks(t.X, t.Y, 0xFF5A6A3A, 20, 1.4);
    }

    /// <summary>Back in, the lives allowing; out of lives, the offer to carry on; everybody out, the end.</summary>
    private void Downs(double dt)
    {
        if (State is not (GameState.Playing or GameState.Attract)) return;
        foreach (var p in Players.Where(p => p.Joined && !p.Alive))
        {
            if (p.Down > 0)
            {
                p.Down -= dt;
                if (p.Down > 0) continue;
                p.Down = 0;
                if (p.Lives > 0)
                {
                    Respawn(p);
                    continue;
                }
                p.Continue = State == GameState.Playing ? ContinueSeconds : 0;
                continue;
            }
            if (p.Continue > 0 && (p.Continue -= dt) <= 0 && Players.Any(o => o.Fighting))
                Say(p.Name, "GAME OVER", 2);
        }
        var joined = Players.Where(p => p.Joined).ToList();
        if (joined.Count > 0 && joined.All(p => p.Out && p.Continue <= 0)) GameOver();
    }

    /// <summary>Back in at the bottom of the screen, a fresh supply in hand, safe for a moment.</summary>
    private void Respawn(Warrior p)
    {
        PlaceAtBottom(p);
        p.Ammo = Math.Max(p.Ammo, StartAmmo / 2);
        p.Grenades = Math.Max(p.Grenades, StartGrenades / 2);
        Bullets.RemoveAll(b => b.Hostile && Field.Distance(b.X, b.Y, p.X, p.Y) < 300);
    }

    private void Kill(Warrior p, string cause)
    {
        if (!p.Alive) return;
        p.Cause = cause;
        p.Alive = false;
        p.Lives--;
        p.Down = DyingSeconds;
        p.Fast = p.Far = p.Big = false;
        if (p.Tank is { } t)
        {
            t.Driver = null;
            p.Tank = null;
        }
        Sparks(p.X, p.Y, 0xFFE0A070, 14, 1.2);
        Effects.Add(new Effect { Kind = EffectKind.Blast, X = p.X, Y = p.Y, Life = 0.5, Size = 0.7 });
        Play(Sfx.Death);
        if (p.Lives <= 0 && State == GameState.Playing) Say(p.Name, "CONTINUE? PRESS " + (p.Index + 1), 2.4);
    }

    /// <summary>
    /// The screen follows the leading commando up the map (never back down), as far as the end of the stage, but
    /// never so far that the other is left behind off the bottom of it.
    /// </summary>
    private void Scroll()
    {
        var on = Fighting.ToList();
        if (on.Count == 0) return;
        double want = on.Max(p => p.Y) - Field.View * 0.5;
        double limit = on.Min(p => p.Y - (p.Tank != null ? Tank.Radius : Warrior.Radius) - 4);
        want = Math.Min(want, limit);
        if (want <= Camera) return;
        double was = Camera;
        Camera = Math.Min(Stage.CameraEnd, want);
        _scrolled += Camera - was;
    }

    // ───────────────────────────── bullets, grenades, shells, and what they hit ─────────────────────────────

    private void MoveBullets(double dt)
    {
        foreach (var b in Bullets)
        {
            for (int k = 0; k < 2 && b.Life > 0; k++)
            {
                b.X += b.Vx * dt / 2;
                b.Y += b.Vy * dt / 2;
                if (b.X < -10 || b.X > Field.Width + 10 || b.Y < Camera - 10 || b.Y > ViewTop + 10) { b.Life = 0; break; }
                if (b.Hostile ? HitPlayer(b) : HitEnemy(b)) { b.Life = 0; break; }
                if (Ground.Stops(Stage.AtPoint(b.X, b.Y)) && !(Stage.GateOpen && Stage.AtPoint(b.X, b.Y) == Ground.Gate)) { b.Life = 0; Puff(b.X, b.Y); break; }
            }
            b.Life -= dt;
        }
        Bullets.RemoveAll(b => b.Life <= 0);
    }

    /// <summary>A commando's bullet: a soldier (in a trench only when he's up), a gun, a gunship; it glances off armour and concrete.</summary>
    private bool HitEnemy(Bullet b)
    {
        foreach (var s in Soldiers)
        {
            if (s.Dead || (s.Kind == SoldierKind.Trench && !s.Up) || Field.Distance(b.X, b.Y, s.X, s.Y) > Soldier.Radius + 2) continue;
            Down(s, b.Owner);
            return true;
        }
        foreach (var g in Guns)
        {
            if (g.Dead || Math.Abs(b.X - g.X) > 17 || Math.Abs(b.Y - g.Y) > 17) continue;
            g.Flash = 0.1;
            if (--g.Hp <= 0) Wreck(g, b.Owner);
            else Play(Sfx.Clank);
            return true;
        }
        foreach (var h in Helicopters)
        {
            if (h.Dead || Field.Distance(b.X, b.Y, h.X, h.Y) > Helicopter.Radius) continue;
            h.Flash = 0.08;
            if (--h.Hp <= 0) Wreck(h, b.Owner);
            return true;
        }
        foreach (var e in EnemyTanks)
        {
            if (e.Dead || Field.Distance(b.X, b.Y, e.X, e.Y) > EnemyTank.Radius) continue;
            Spark(b.X, b.Y);
            return true;
        }
        foreach (var x in Pillboxes)
        {
            if (x.Dead || Math.Abs(b.X - x.X) > 30 || Math.Abs(b.Y - x.Y) > 30) continue;
            Spark(b.X, b.Y);
            return true;
        }
        return false;
    }

    /// <summary>An enemy bullet: a commando on foot falls; a tank takes the knock.</summary>
    private bool HitPlayer(Bullet b)
    {
        foreach (var p in Players)
        {
            if (!p.Fighting) continue;
            if (p.Tank is { } t)
            {
                if (Field.Distance(b.X, b.Y, t.X, t.Y) > Tank.Radius) continue;
                t.Damage += BulletDamage;
                Spark(b.X, b.Y);
                return true;
            }
            if (Field.Distance(b.X, b.Y, p.X, p.Y) > Warrior.Radius) continue;
            if (p.Safe <= 0) Kill(p, "shot");
            return true;
        }
        foreach (var t in Tanks.Where(t => !t.Dead && t.Driver == null))
            if (Field.Distance(b.X, b.Y, t.X, t.Y) < Tank.Radius) { Spark(b.X, b.Y); return true; }
        return false;
    }

    private void MoveShells(double dt)
    {
        foreach (var s in Shells)
        {
            for (int k = 0; k < 3 && !s.Done; k++)
            {
                double step = dt / 3;
                s.X += s.Vx * step;
                s.Y += s.Vy * step;
                s.Range -= Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy) * step;
                if (s.X < -20 || s.X > Field.Width + 20 || s.Y < Camera - 20 || s.Y > ViewTop + 20) { s.Done = true; break; }
                char at = Stage.AtPoint(s.X, s.Y);
                bool hit = s.Range <= 0 || (Ground.Stops(at) && !(Stage.GateOpen && at == Ground.Gate)) || (s.Hostile ? ShellHitsPlayer(s) : ShellHitsEnemy(s));
                if (!hit) continue;
                s.Done = true;
                if (s.Hostile) Blast(s.X, s.Y, EnemyBlast + 6, -1, hostile: true, power: 1, heli: 0, sound: Sfx.Blast, damage: ShellDamage);
                else Blast(s.X, s.Y, ShellBlast, s.Owner, hostile: false, power: 2, heli: 8);
            }
        }
        Shells.RemoveAll(s => s.Done);
    }

    private bool ShellHitsEnemy(Shell s) =>
        Soldiers.Any(o => !o.Dead && Field.Distance(s.X, s.Y, o.X, o.Y) < Soldier.Radius + 4)
        || EnemyTanks.Any(e => !e.Dead && Field.Distance(s.X, s.Y, e.X, e.Y) < EnemyTank.Radius)
        || Helicopters.Any(h => !h.Dead && Field.Distance(s.X, s.Y, h.X, h.Y) < Helicopter.Radius)
        || Guns.Any(g => !g.Dead && Math.Abs(s.X - g.X) < 16 && Math.Abs(s.Y - g.Y) < 16);

    private bool ShellHitsPlayer(Shell s) =>
        Fighting.Any(p => Field.Distance(s.X, s.Y, p.X, p.Y) < (p.Tank != null ? Tank.Radius : Warrior.Radius + 2))
        || Tanks.Any(t => !t.Dead && Field.Distance(s.X, s.Y, t.X, t.Y) < Tank.Radius);

    private void MoveGrenades(double dt)
    {
        foreach (var g in Grenades)
        {
            g.T += dt;
            if (g.T < g.Time) continue;
            if (g.Hostile) Blast(g.ToX, g.ToY, EnemyBlast, -1, hostile: true, power: 1, heli: 0);
            else Blast(g.ToX, g.ToY, g.Radius, g.Owner, hostile: false, power: g.Big ? 2 : 1, heli: g.Big ? 9 : 6);
        }
        Grenades.RemoveAll(g => g.T >= g.Time);
    }

    /// <summary>
    /// Something goes off. The commandos' grenades and shells take out soldiers (in trenches or not), guns, pillboxes
    /// and wire, wear down armour, gunships and the gate; the enemy's take out commandos on foot and knock tanks about.
    /// </summary>
    private void Blast(double x, double y, double radius, int owner, bool hostile, int power, int heli, Sfx sound = Sfx.Blast, double damage = BlastDamage)
    {
        bool wet = Stage.AtPoint(x, y) == Ground.Water;
        Effects.Add(new Effect { Kind = wet ? EffectKind.Splash : EffectKind.Blast, X = x, Y = y, Life = wet ? 0.7 : 0.6, Size = radius / 50 });
        Sparks(x, y, wet ? 0xFFA0D0FF : 0xFFFFB04A, 8, 0.6);
        Play(sound);
        if (hostile)
        {
            foreach (var p in Fighting.ToList())
            {
                if (p.Tank is { } t)
                {
                    if (Field.Distance(x, y, t.X, t.Y) < radius + Tank.Radius * 0.6) t.Damage += damage;
                }
                else if (p.Safe <= 0 && Field.Distance(x, y, p.X, p.Y) < radius) Kill(p, "blown up");
            }
            foreach (var t in Tanks.Where(t => !t.Dead && t.Driver == null && Field.Distance(x, y, t.X, t.Y) < radius + Tank.Radius * 0.6)) t.Damage += damage;
            return;
        }
        foreach (var s in Soldiers.Where(s => !s.Dead && Field.Distance(s.X, s.Y, x, y) < radius).ToList()) Down(s, owner);
        foreach (var g in Guns.Where(g => !g.Dead && Field.Distance(g.X, g.Y, x, y) < radius + 14).ToList()) Wreck(g, owner);
        foreach (var b in Pillboxes.Where(b => !b.Dead && Math.Abs(b.X - x) < radius + 14 && Math.Abs(b.Y - y) < radius + 14).ToList()) Wreck(b, owner);
        foreach (var e in EnemyTanks.Where(e => !e.Dead && Field.Distance(e.X, e.Y, x, y) < radius + EnemyTank.Radius * 0.6).ToList())
        {
            e.Flash = 0.15;
            if ((e.Hp -= power) <= 0) Wreck(e, owner);
        }
        foreach (var h in Helicopters.Where(h => !h.Dead && Field.Distance(h.X, h.Y, x, y) < radius + 22).ToList())
        {
            h.Flash = 0.15;
            if ((h.Hp -= heli) <= 0) Wreck(h, owner);
        }
        if (Stage.HasGate && !Stage.GateOpen && GateHp > 0)
        {
            double gx0 = Stage.GateCol * Field.Cell, gx1 = (Stage.GateCol + Stage.GateWidth) * Field.Cell;
            if (x > gx0 - radius && x < gx1 + radius && y > Stage.GateY - radius && y < Stage.GateY + 2 * Field.Cell + radius)
            {
                GateHp = Math.Max(0, GateHp - power);
                Sparks(x, Math.Max(y, Stage.GateY), 0xFF8A5A2A, 10, 0.9);
                if (GateHp == 0) GateBlown(owner);
            }
        }
        // Wire within the blast is blown away.
        for (int row = Field.RowOf(y - radius); row <= Field.RowOf(y + radius); row++)
        for (int col = Field.ColOf(x - radius); col <= Field.ColOf(x + radius); col++)
            if (Stage.At(col, row) == Ground.Fence && Field.Distance(Field.CellX(col), Field.CellY(row), x, y) < radius) Stage.Set(col, row, Ground.Road);
    }

    // ───────────────────────────── pickups, and running into things ─────────────────────────────

    /// <summary>Pickups are picked up (by a tank too); a driven tank runs soldiers down; an enemy tank runs down a man on foot.</summary>
    private void Contact()
    {
        foreach (var p in Fighting.ToList())
        {
            double reach = p.Tank != null ? Tank.Radius + 6 : 22;
            foreach (var k in Pickups.Where(k => Field.Distance(k.X, k.Y, p.X, p.Y) < reach).ToList())
            {
                Pickups.Remove(k);
                Collect(p, k);
            }
            if (p.Tank is { } t)
            {
                foreach (var s in Soldiers.Where(s => !s.Dead && Field.Distance(s.X, s.Y, t.X, t.Y) < Tank.Radius + Soldier.Radius - 4).ToList())
                {
                    Down(s, p.Index);
                    Effects.Add(new Effect { Kind = EffectKind.Word, X = s.X, Y = s.Y + 34, Text = "CRUSHED", Life = 0.8, Colour = 0xFFFFB04A });
                }
                continue;
            }
            if (p.Safe > 0) continue;
            if (EnemyTanks.Any(e => !e.Dead && Field.Distance(e.X, e.Y, p.X, p.Y) < EnemyTank.Radius + Warrior.Radius - 4)) Kill(p, "run over");
        }
    }

    private static readonly string[] PickupWords = ["+60 BULLETS", "+10 GRENADES", "SPEED", "LONG RANGE", "BIG GRENADES", "FUEL", "KILL ALL"];

    private void Collect(Warrior p, Pickup k)
    {
        string word = PickupWords[(int)k.Kind];
        switch (k.Kind)
        {
            case PickupKind.Bullets: p.Ammo = Math.Min(MaxAmmo, p.Ammo + BulletBox); break;
            case PickupKind.Grenades: p.Grenades = Math.Min(MaxAmmo, p.Grenades + GrenadeBox); break;
            case PickupKind.Speed: p.Fast = true; break;
            case PickupKind.Range: p.Far = true; break;
            case PickupKind.Big: p.Big = true; break;
            case PickupKind.Fuel:
                if (p.Tank is { } t)
                {
                    t.Fuel = Tank.MaxFuel;
                    word = "FULL TANK";
                }
                else
                {
                    Award(p.Index, FuelPoints, k.X, k.Y);
                    word = "";
                }
                break;
            case PickupKind.KillAll:
                foreach (var s in Soldiers.Where(s => !s.Dead && s.Y > Camera && s.Y < ViewTop && s.X > 0 && s.X < Field.Width).ToList()) Down(s, p.Index);
                Effects.Add(new Effect { Kind = EffectKind.Flash, X = Field.Width / 2, Y = Camera + Field.View / 2, Life = 0.35, Size = 40 });
                break;
        }
        if (word != "") Effects.Add(new Effect { Kind = EffectKind.Word, X = k.X, Y = k.Y + 26, Text = word, Life = 1.1, Colour = 0xFFFFE14A });
        Play(Sfx.Pickup);
    }

    /// <summary>The dead and whatever has fallen off the bottom of the screen go.</summary>
    private void Tidy()
    {
        Soldiers.RemoveAll(s => s.Dead || s.Y < Camera - 40);
        EnemyTanks.RemoveAll(e => e.Dead || e.Y < Camera - 60);
        Helicopters.RemoveAll(h => h.Dead);
        Tanks.RemoveAll(t => t.Dead || (t.Driver == null && t.Y < Camera - 60));
        foreach (var k in Pickups) k.Age += Field.Step;
        Pickups.RemoveAll(k => k.Age > k.Life || k.Y < Camera - 30);
    }

    // ───────────────────────────── the end of a stage ─────────────────────────────

    /// <summary>The guard at the end of a stage beaten: the stage is taken.</summary>
    private void CheckEnd()
    {
        if (!AtEnd || Stage.HasGate || !_guardOut) return;
        if (EnemyTanks.Any(e => !e.Dead) || Helicopters.Any(h => !h.Dead)) return;
        Bonus = 1000 * Stage.Number;
        Bullets.RemoveAll(b => b.Hostile);
        Grenades.RemoveAll(g => g.Hostile);
        Shells.RemoveAll(s => s.Hostile);
        Music(false);
        Play(Sfx.StageClear);
        Say($"STAGE {Stage.Theme + 1} CLEAR", $"BONUS {Bonus}", ClearSeconds);
        SetPhase(Phase.Clear);
    }

    /// <summary>The gate's blown in: it swings open, the defenders give up, and the commandos march in.</summary>
    private void GateBlown(int owner)
    {
        Stage.GateOpen = true;
        Award(owner, GatePoints, Stage.GateX, Stage.GateY + 40);
        foreach (var s in Soldiers) s.Dead = true;
        foreach (var g in Guns.Where(g => !g.Dead).ToList()) Wreck(g, -1);
        foreach (var b in Pillboxes.Where(b => !b.Dead).ToList()) Wreck(b, -1);
        Bullets.RemoveAll(b => b.Hostile);
        Grenades.RemoveAll(g => g.Hostile);
        Shells.RemoveAll(s => s.Hostile);
        Bonus = 10000 * Stage.Round;
        Music(false);
        Play(Sfx.GateOpen);
        Say("THE GATE OPENS", "VICTORY", 3);
        SetPhase(Phase.Finale);
    }

    /// <summary>The finale: the commandos march up through the open gate (out of their tanks first).</summary>
    private void MarchIn(double dt)
    {
        if (PhaseTime < 1.4) return;
        foreach (var p in Fighting)
        {
            if (p.Tank != null) GetOut(p);
            double tx = Stage.GateX + (p.Index == 0 ? -40 : 40), ty = Stage.GateY + 5 * Field.Cell;
            double dx = tx - p.X, dy = ty - p.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 2) continue;
            double step = Math.Min(d, RunSpeed * dt);
            p.X += dx / d * step;
            p.Y += dy / d * step;
            p.Facing = 0;
            p.Walk += dt;
        }
    }

    // ───────────────────────────── scoring, and the bits flying ─────────────────────────────

    private void Award(int owner, int points, double x, double y)
    {
        if (owner is < 0 or > 1) return;
        var p = Players[owner];
        p.Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y + 16, Text = points.ToString(), Life = 0.7, Colour = owner == 0 ? 0xFFFFB0A0 : 0xFFA0D0FF });
        while (p.Score >= p.NextExtra)
        {
            p.NextExtra += ExtraLifeEvery;
            p.Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    private void Blast(double x, double y, double size)
    {
        Effects.Add(new Effect { Kind = EffectKind.Blast, X = x, Y = y, Life = 0.7, Size = size });
        Sparks(x, y, 0xFFFFB04A, 14, 1);
    }

    private void Puff(double x, double y) => Effects.Add(new Effect { Kind = EffectKind.Puff, X = x, Y = y, Life = 0.3 });

    private void Spark(double x, double y)
    {
        Effects.Add(new Effect { Kind = EffectKind.Puff, X = x, Y = y, Life = 0.2, Colour = 0xFFFFF0A0 });
        Play(Sfx.Clank);
    }

    private void Sparks(double x, double y, uint colour, int n, double life)
    {
        for (int i = 0; i < n; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = 30 + _rng.NextDouble() * 150;
            Wreckage.Add(new Debris { X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Life = life * (0.5 + _rng.NextDouble() * 0.5), Colour = colour });
        }
    }

    private void MoveDebris(double dt)
    {
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
            d.Vx *= 1 - 2 * dt;
            d.Vy *= 1 - 2 * dt;
        }
        Wreckage.RemoveAll(d => d.Age >= d.Life);
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls p1, Controls p2 = default)
    {
        for (int i = 0; i < ticks; i++) Tick(p1, p2);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into a stage, past its name, with one commando or both.</summary>
    internal void Go(int stage = 1, int players = 1)
    {
        StartGame(players);
        if (stage != 1) BeginStage(stage);
        SetPhase(Phase.Play);
        foreach (var p in Fighting) p.Safe = 0;
        Message = SubMessage = null;
    }

    internal Soldier PutSoldier(SoldierKind kind, double x, double y)
    {
        var s = new Soldier { Kind = kind, X = x, Y = y, ToX = x, ToY = y, FireIn = 0, Up = kind != SoldierKind.Trench };
        Soldiers.Add(s);
        return s;
    }

    internal Tank PutTank(double x, double y, double fuel = Tank.MaxFuel)
    {
        var t = new Tank { X = x, Y = y, Fuel = fuel };
        Tanks.Add(t);
        return t;
    }

    internal EnemyTank PutEnemyTank(double x, double y)
    {
        var e = new EnemyTank { X = x, Y = y, ToY = y, Hp = EnemyTankHp, FireIn = 0 };
        EnemyTanks.Add(e);
        return e;
    }

    internal Helicopter PutHelicopter(double x, double y)
    {
        var h = new Helicopter { X = x, Y = y, ToX = x, ToY = y, Hp = HelicopterHp, MaxHp = HelicopterHp, FireIn = 0, BombIn = 0 };
        Helicopters.Add(h);
        return h;
    }

    internal Pickup PutPickup(PickupKind kind, double x, double y)
    {
        var k = new Pickup { Kind = kind, X = x, Y = y };
        Pickups.Add(k);
        return k;
    }

    internal void PutPlayer(int index, double x, double y, int facing = 0)
    {
        var p = Players[index];
        if (!p.Joined) Join(index, quiet: true);
        p.X = x;
        p.Y = y;
        p.Facing = facing;
        p.Alive = true;
        p.Safe = 0;
    }

    /// <summary>The screen moved straight to a point up the map (the commandos with it).</summary>
    internal void MoveCamera(double camera)
    {
        Camera = Math.Min(camera, Stage.CameraEnd);
        foreach (var p in Fighting)
        {
            p.Y = Camera + 120;
            Stage.Push(ref p.X, ref p.Y, Warrior.Radius);
        }
        while (_spawnIndex < _spawns.Count && _spawns[_spawnIndex].Y < Camera) _spawnIndex++;
    }

    /// <summary>No more of the enemy come on: not what's waiting in the map, nor reinforcements.</summary>
    internal void Hold()
    {
        _spawnIndex = _spawns.Count;
        _ambientIn = double.MaxValue;
        _scrolled = double.MinValue;
    }

    internal void SetLives(int index, int n) => Players[index].Lives = n;
    internal void SetScore(int index, int n) => Players[index].Score = n;
    internal void HurtGate(int left) => GateHp = left;
}
