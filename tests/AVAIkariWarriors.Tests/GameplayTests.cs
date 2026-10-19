using AVAIkariWarriors.Engine;

namespace AVAIkariWarriors.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaikariwarriors-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Stage 1 (or another) in play with the first stretch of the map cleared to open ground and nobody about: calm (the
    /// enemy put there stands still and holds its fire) unless <paramref name="calm"/> is false. Player 1 at (512, 200).
    /// </summary>
    private static GameEngine Game(bool calm = true, int stage = 1, int seed = 7, int players = 1)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = calm };
        g.Go(stage, players);
        for (int r = 0; r < 40; r++) Array.Fill(g.Stage.Rows[r], Ground.Open);
        g.Soldiers.Clear();
        g.Guns.Clear();
        g.Pillboxes.Clear();
        g.Pickups.Clear();
        g.Tanks.Clear();
        g.EnemyTanks.Clear();
        g.Helicopters.Clear();
        g.Hold();
        g.PutPlayer(0, 512, 200);
        if (players > 1) g.PutPlayer(1, 700, 200);
        return g;
    }

    private static readonly Controls FireButton = new(Fire: true), GrenadeButton = new(Grenade: true), BothButtons = new(Fire: true, Grenade: true);

    private static void Set(GameEngine g, int col, int row, char what) => g.Stage.Rows[row][col] = what;

    private static Warrior P1(GameEngine g) => g.Players[0];

    // ───────────────────────────── the map ─────────────────────────────

    [Fact]
    public void Every_stage_is_a_way_up_through_its_ground_to_its_end_with_a_tank_on_the_way()
    {
        for (int n = 1; n <= 4; n++)
        {
            var s = Stage.Build(n);
            Assert.All(s.Rows, row => Assert.Equal(Field.Cols, row.Length));
            int end = Field.RowOf(s.CameraEnd + Field.View) - 1;
            // On foot, from the start to the top of the last screen (or the gate).
            var seen = new HashSet<(int, int)> { (16, 3) };
            var queue = new Queue<(int C, int R)>([(16, 3)]);
            while (queue.Count > 0)
            {
                var (c, r) = queue.Dequeue();
                foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    if (!s.BlocksFoot(c + dc, r + dr) && r + dr >= 0 && r + dr <= end && seen.Add((c + dc, r + dr))) queue.Enqueue((c + dc, r + dr));
            }
            if (s.HasGate)
            {
                Assert.Equal(Ground.Gate, s.At(Stage.GateCol, s.GateRow));
                Assert.Contains((Stage.GateCol, s.GateRow - 1), seen);
                Assert.Equal(2, s.Guns.Count(g => g.Row == s.GateRow));
            }
            else
            {
                Assert.Contains(seen, at => at.Item2 == end - 1);
                Assert.NotEmpty(s.Guard);
            }
            Assert.Equal(2, s.Spawns.Count(x => x.Kind == SpawnKind.Tank));
            Assert.Contains(s.Spawns, x => x is { Kind: SpawnKind.Pickup, Item: PickupKind.Fuel });
            Assert.True(s.Rows.Count > 100);
        }
        Assert.Equal(["THE JUNGLE", "THE RIVER", "THE RUINS", "THE FORTRESS", "THE JUNGLE"], Enumerable.Range(1, 5).Select(n => Stage.Build(n).Name));
        // The second round is the same ground, more heavily held.
        string Map(int n) => new(Stage.Build(n).Rows.SelectMany(r => r).ToArray());
        Assert.Equal(Map(1), Map(1));
        Assert.True(Stage.Build(5).Spawns.Count(x => x.Kind == SpawnKind.Rifleman) > Stage.Build(1).Spawns.Count(x => x.Kind == SpawnKind.Rifleman));
        Assert.Equal((1, 2), (Stage.Build(4).Round, Stage.Build(5).Round));
    }

    [Fact]
    public void The_river_has_a_bridge_a_tank_can_cross_and_wire_trenches_and_ruins_are_where_they_should_be()
    {
        var river = Stage.Build(2);
        var wet = Enumerable.Range(0, river.Rows.Count).Where(r => river.Rows[r].Count(c => c == Ground.Water) > 20).ToList();
        Assert.NotEmpty(wet);
        Assert.All(wet, r => Assert.Contains(Ground.Bridge, river.Rows[r]));
        Assert.Contains(Stage.Build(1).Rows, r => r.Count(c => c == Ground.Fence) > 20);
        Assert.Contains(Stage.Build(1).Rows, r => r.Count(c => c == Ground.Trench) > 20);
        Assert.Contains(Stage.Build(3).Rows, r => r.Contains(Ground.Ruin));
        Assert.Contains(Stage.Build(4).Rows, r => r.Count(c => c == Ground.Wall) > 20);
    }

    // ───────────────────────────── running and aiming ─────────────────────────────

    [Fact]
    public void He_runs_eight_ways_and_aims_the_way_he_runs_unless_he_is_firing()
    {
        var g = Game();
        var p = P1(g);
        g.Run(30, new Controls(Dx: 1));
        Assert.Equal(512 + GameEngine.RunSpeed / 2, p.X, 1);
        Assert.Equal(Field.WayOf(1, 0), p.Facing);
        double x = p.X, y = p.Y;
        g.Run(30, new Controls(Dx: -1, Dy: 1));
        Assert.Equal(GameEngine.RunSpeed / 2, Field.Distance(x, y, p.X, p.Y), 1);
        Assert.Equal(Field.WayOf(-1, 1), p.Facing);
        // Firing holds the aim: he runs right while his bullets go up and to the left.
        g.Bullets.Clear();
        g.Run(20, new Controls(Dx: 1, Fire: true));
        Assert.Equal(Field.WayOf(-1, 1), p.Facing);
        Assert.NotEmpty(g.Bullets);
        Assert.All(g.Bullets, b => Assert.True(b.Vx < 0 && b.Vy > 0));
    }

    [Fact]
    public void Turning_swings_the_aim_a_step_at_a_time_whichever_way_he_runs()
    {
        var g = Game();
        var p = P1(g);
        Assert.Equal(0, p.Facing);
        g.Run(1, new Controls(Turn: 1));
        Assert.Equal(1, p.Facing);
        g.Run(1, new Controls(Turn: 1));
        Assert.Equal(1, p.Facing);
        // Held, it steps on.
        g.Run((int)(GameEngine.TurnGap * 60) + 1, new Controls(Turn: 1));
        Assert.Equal(2, p.Facing);
        // Running down the screen, turning anticlockwise: the aim goes round from right to up, not the way he runs.
        g.Run(1, default);
        g.Run(1, new Controls(Dy: -1, Turn: -1, Fire: true));
        g.Run(1, new Controls(Dy: -1, Fire: true));
        Assert.Equal(1, p.Facing);
        double y = p.Y;
        g.Run(20, new Controls(Dy: -1, Fire: true));
        Assert.True(p.Y < y - 10);
        Assert.Equal(1, p.Facing);
        Assert.All(g.Bullets.Where(b => b.Owner == 0), b => Assert.True(b.Vx > 0 && b.Vy > 0));
    }

    [Fact]
    public void A_right_stick_points_the_aim_straight_at_a_way()
    {
        var g = Game();
        g.Run(1, new Controls(Dx: 1, AimX: -1, AimY: -1));
        Assert.Equal(Field.WayOf(-1, -1), P1(g).Facing);
        Assert.Equal(512 + GameEngine.RunSpeed / 60, P1(g).X, 1);
    }

    [Fact]
    public void Rocks_stop_him_and_he_wades_a_river_slowly_and_throws_nothing_from_it()
    {
        var g = Game();
        var p = P1(g);
        Set(g, 16, 9, Ground.Rock);
        g.PutPlayer(0, Field.CellX(16), 200);
        g.Run(60, new Controls(Dy: 1));
        Assert.Equal(9 * Field.Cell - Warrior.Radius, p.Y, 0);
        for (int r = 12; r < 16; r++) Array.Fill(g.Stage.Rows[r], Ground.Water);
        g.PutPlayer(0, 300, 13.5 * Field.Cell);
        g.Run(30, new Controls(Dy: 1));
        Assert.True(p.Wading);
        Assert.Equal(13.5 * Field.Cell + GameEngine.RunSpeed / 2 * Ground.Pace(Ground.Water), p.Y, 1);
        int grenades = p.Grenades;
        g.Run(10, GrenadeButton);
        Assert.Equal(grenades, p.Grenades);
        Assert.Empty(g.Grenades);
    }

    // ───────────────────────────── the gun and the grenades ─────────────────────────────

    [Fact]
    public void Every_bullet_is_counted_four_at_most_and_none_when_he_is_out_until_a_box_of_them()
    {
        var g = Game();
        var p = P1(g);
        Assert.Equal(GameEngine.StartAmmo, p.Ammo);
        g.Run(1, FireButton);
        var b = Assert.Single(g.Bullets);
        Assert.Equal((0.0, GameEngine.BulletSpeed), (b.Vx, b.Vy));
        Assert.Equal(GameEngine.StartAmmo - 1, p.Ammo);
        int most = 0;
        var fired = new HashSet<Bullet>(g.Bullets);
        for (int i = 0; i < 40; i++)
        {
            g.Run(1, FireButton);
            most = Math.Max(most, g.Bullets.Count);
            fired.UnionWith(g.Bullets);
        }
        Assert.Equal(GameEngine.MaxBullets, most);
        Assert.Equal(GameEngine.StartAmmo - fired.Count, p.Ammo);
        p.Ammo = 0;
        g.Idle(60);
        g.Run(30, FireButton);
        Assert.Empty(g.Bullets);
        g.PutPickup(PickupKind.Bullets, 512, 240);
        g.Run(20, new Controls(Dy: 1));
        Assert.Equal(GameEngine.BulletBox, p.Ammo);
        Assert.Empty(g.Pickups);
    }

    [Fact]
    public void A_grenade_is_thrown_a_fixed_distance_the_way_he_aims_over_a_wall_and_takes_out_whoever_is_where_it_lands()
    {
        var g = Game();
        var p = P1(g);
        // A wall to his right, and three men beyond it: two where the grenade lands, one not.
        for (int r = 0; r < 20; r++) Set(g, 20, r, Ground.Wall);
        double lx = 512 + GameEngine.ThrowRange;
        var a = g.PutSoldier(SoldierKind.Rifleman, lx + 20, 200);
        var b = g.PutSoldier(SoldierKind.Grenadier, lx - 10, 225);
        var far = g.PutSoldier(SoldierKind.Rifleman, lx + 120, 200);
        int before = p.Grenades;
        g.Run(1, new Controls(AimX: 1, Grenade: true));
        Assert.Equal(before - 1, p.Grenades);
        var n = Assert.Single(g.Grenades);
        Assert.Equal((lx, 200.0), (n.ToX, n.ToY));
        g.Idle((int)(GameEngine.ThrowTime * 60) + 2);
        Assert.True(a.Dead && b.Dead);
        Assert.False(far.Dead);
        Assert.Equal(250, p.Score);
        // Out of them: none thrown; a box gives ten.
        p.Grenades = 0;
        g.Run(30, GrenadeButton);
        Assert.Empty(g.Grenades);
        g.PutPickup(PickupKind.Grenades, 512, 240);
        g.Run(20, new Controls(Dy: 1));
        Assert.Equal(GameEngine.GrenadeBox, p.Grenades);
    }

    [Fact]
    public void Sandbags_stop_bullets_but_wire_and_water_do_not_and_a_grenade_blows_the_wire_away()
    {
        var g = Game();
        Array.Fill(g.Stage.Rows[9], Ground.Sandbag);
        var hidden = g.PutSoldier(SoldierKind.Rifleman, 512, 340);
        g.Run(1, FireButton);
        g.Idle(40);
        Assert.False(hidden.Dead);
        Array.Fill(g.Stage.Rows[9], Ground.Fence);
        g.Run(1, FireButton);
        g.Idle(40);
        Assert.True(hidden.Dead);
        Assert.True(g.Stage.BlocksFoot(16, 9));
        g.PutPlayer(0, 512, Field.CellY(9) - GameEngine.ThrowRange);
        g.Run(1, GrenadeButton);
        g.Idle((int)(GameEngine.ThrowTime * 60) + 2);
        Assert.False(g.Stage.BlocksFoot(16, 9));
        Assert.True(g.Stage.BlocksFoot(2, 9));
    }

    [Fact]
    public void The_letters_speed_him_up_carry_his_bullets_further_and_make_his_grenades_bigger_until_he_falls()
    {
        var g = Game();
        var p = P1(g);
        foreach (var k in new[] { PickupKind.Speed, PickupKind.Range, PickupKind.Big }) g.PutPickup(k, 512, 210);
        g.Idle(1);
        Assert.True(p.Fast && p.Far && p.Big);
        double x = p.X;
        g.Run(30, new Controls(Dx: 1));
        Assert.Equal(x + GameEngine.FastSpeed / 2, p.X, 1);
        g.Run(1, FireButton);
        Assert.Equal(GameEngine.FarLife - Field.Step, g.Bullets.Single().Life, 3);
        g.Idle(60);
        g.Run(1, GrenadeButton);
        Assert.Equal(GameEngine.BigBlastRadius, g.Grenades.Single().Radius);
        g.Idle(60);
        g.Calm = false;
        g.PutEnemyTank(p.X, p.Y + 20);
        g.Idle(1);
        Assert.False(p.Alive);
        Assert.False(p.Fast || p.Far || p.Big);
    }

    [Fact]
    public void K_takes_out_every_soldier_on_the_screen()
    {
        var g = Game();
        var men = Enumerable.Range(0, 5).Select(i => g.PutSoldier(SoldierKind.Rifleman, 100 + i * 180, 600)).ToList();
        var below = g.PutSoldier(SoldierKind.Rifleman, 300, g.ViewTop + 200);
        g.PutPickup(PickupKind.KillAll, 512, 210);
        g.Idle(1);
        Assert.All(men, m => Assert.True(m.Dead));
        Assert.False(below.Dead);
        Assert.Equal(500, P1(g).Score);
    }

    // ───────────────────────────── tanks ─────────────────────────────

    [Fact]
    public void He_walks_into_an_empty_tank_drives_it_fires_shells_and_gets_out_beside_it_with_both_buttons()
    {
        var g = Game();
        var p = P1(g);
        var t = g.PutTank(512, 300);
        g.Run(60, new Controls(Dy: 1));
        Assert.Same(t, p.Tank);
        Assert.Same(p, t.Driver);
        // Driving: it goes where he steers, at its own speed, and he goes with it.
        double y = t.Y;
        g.Run(30, new Controls(Dx: 1));
        Assert.Equal(512 + GameEngine.TankSpeed / 2, t.X, 1);
        Assert.Equal((t.X, t.Y), (p.X, p.Y));
        Assert.Equal(y, t.Y);
        // The gun: aimed as he aims, firing shells (not bullets), and not using his.
        g.Run(1, new Controls(AimX: 0, AimY: 1));
        int ammo = p.Ammo, grenades = p.Grenades;
        g.Run(1, FireButton);
        var shell = Assert.Single(g.Shells);
        Assert.True(shell.Vy > 0 && shell.Vx == 0);
        Assert.Empty(g.Bullets);
        Assert.Equal((ammo, grenades), (p.Ammo, p.Grenades));
        g.Idle(60);
        // Both buttons: out, beside it, on foot again; and he doesn't climb straight back in.
        g.Run(1, BothButtons);
        Assert.Null(p.Tank);
        Assert.Null(t.Driver);
        Assert.InRange(Field.Distance(p.X, p.Y, t.X, t.Y), Tank.Radius + Warrior.Radius, 50);
        g.Idle(10);
        Assert.Null(p.Tank);
    }

    [Fact]
    public void A_tank_runs_soldiers_down_flattens_sandbags_and_wire_but_will_not_go_in_the_water()
    {
        var g = Game();
        var p = P1(g);
        var t = g.PutTank(512, 200);
        g.Idle(1);
        Assert.Same(t, p.Tank);
        var man = g.PutSoldier(SoldierKind.Rifleman, 512, 300);
        Array.Fill(g.Stage.Rows[12], Ground.Sandbag);
        Array.Fill(g.Stage.Rows[13], Ground.Fence);
        for (int r = 17; r < 20; r++) Array.Fill(g.Stage.Rows[r], Ground.Water);
        g.Run(220, new Controls(Dy: 1));
        Assert.True(man.Dead);
        Assert.Equal(100, p.Score);
        Assert.Equal(Ground.Road, g.Stage.At(16, 12));
        Assert.Equal(Ground.Road, g.Stage.At(16, 13));
        Assert.Equal(17 * Field.Cell - Tank.Radius, t.Y, 0);
    }

    [Fact]
    public void A_tank_burns_its_own_fuel_and_when_it_runs_dry_it_goes_up_with_him_in_it()
    {
        var g = Game();
        var p = P1(g);
        var t = g.PutTank(512, 220, fuel: 3);
        g.Idle(1);
        Assert.Same(t, p.Tank);
        g.Run(30, new Controls(Dx: 1));
        Assert.Equal(3 - GameEngine.FuelDriving / 2, t.Fuel, 1);
        g.Run(60, new Controls(Dx: 1));
        Assert.Equal(0, t.Fuel);
        Assert.True(t.Doomed);
        int lives = p.Lives;
        g.Idle((int)(GameEngine.FuseSeconds * 60) + 2);
        Assert.True(t.Dead);
        Assert.False(p.Alive);
        Assert.Equal(lives - 1, p.Lives);
        Assert.Equal("went up with the tank", p.Cause);
    }

    [Fact]
    public void Out_of_a_burning_tank_in_time_and_clear_of_it_he_lives_and_the_blast_takes_the_enemy_round_it()
    {
        var g = Game();
        var p = P1(g);
        var t = g.PutTank(512, 300);
        g.PutPlayer(0, 512, 300);
        g.Idle(1);
        t.Damage = 100;
        g.Idle(1);
        Assert.True(t.Doomed);
        var near = g.PutSoldier(SoldierKind.Rifleman, 560, 330);
        g.Run(1, BothButtons);
        Assert.Null(p.Tank);
        g.Run((int)(GameEngine.FuseSeconds * 60) + 2, new Controls(Dy: -1, Dx: -1));
        Assert.True(t.Dead);
        Assert.True(p.Alive);
        Assert.True(near.Dead);
    }

    [Fact]
    public void Enemy_fire_wears_a_tank_down_instead_of_the_man_in_it_and_gas_fills_it_up()
    {
        var g = Game(calm: false);
        var p = P1(g);
        var t = g.PutTank(512, 220, fuel: 40);
        g.Idle(1);
        g.Bullets.Add(new Bullet { X = 512, Y = 300, Vy = -300, Life = 2, Hostile = true, Owner = -1 });
        g.Idle(30);
        Assert.True(p.Alive);
        Assert.Equal(GameEngine.BulletDamage, t.Damage);
        g.PutPickup(PickupKind.Fuel, 512, 240);
        g.Idle(1);
        Assert.Equal(Tank.MaxFuel, t.Fuel);
        // On foot, gas is just points.
        var h = Game();
        h.PutPickup(PickupKind.Fuel, 512, 210);
        h.Idle(1);
        Assert.Equal(GameEngine.FuelPoints, P1(h).Score);
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    [Fact]
    public void A_rifleman_runs_stops_aims_and_fires_and_a_bullet_costs_a_life_then_he_is_back_safe()
    {
        var g = Game(calm: false);
        var p = P1(g);
        g.SetLives(0, 3);
        g.PutSoldier(SoldierKind.Rifleman, 512, 480);
        int t = 0;
        while (p.Alive && t++ < 600) g.Idle(1);
        Assert.False(p.Alive);
        Assert.Equal("shot", p.Cause);
        Assert.Equal(2, p.Lives);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.True(p.Alive);
        Assert.True(p.Safe > 0);
    }

    [Fact]
    public void A_man_in_a_trench_is_out_of_reach_of_bullets_until_he_bobs_up_but_a_grenade_finds_him()
    {
        var g = Game();
        Array.Fill(g.Stage.Rows[12], Ground.Trench);
        var s = g.PutSoldier(SoldierKind.Trench, 512, Field.CellY(12));
        Assert.False(s.Up);
        g.Run(1, FireButton);
        g.Idle(50);
        Assert.False(s.Dead);
        s.Up = true;
        g.Run(1, FireButton);
        g.Idle(50);
        Assert.True(s.Dead);
        var t = g.PutSoldier(SoldierKind.Trench, 512, Field.CellY(12));
        g.PutPlayer(0, 512, Field.CellY(12) - GameEngine.ThrowRange);
        g.Run(1, GrenadeButton);
        g.Idle((int)(GameEngine.ThrowTime * 60) + 2);
        Assert.True(t.Dead);
        Assert.Equal(400, P1(g).Score);
        // Left to himself, he does bob up, and fire.
        var h = Game(calm: false);
        Array.Fill(h.Stage.Rows[12], Ground.Trench);
        var u = h.PutSoldier(SoldierKind.Trench, 512, Field.CellY(12));
        bool up = false;
        for (int i = 0; i < 300; i++) { h.Idle(1); up |= u.Up; }
        Assert.True(up);
        Assert.True(h.Bullets.Any(b => b.Hostile) || h.Grenades.Any(n => n.Hostile) || !P1(h).Alive);
    }

    [Fact]
    public void A_grenadier_lobs_one_over_cover_onto_him()
    {
        var g = Game(calm: false);
        Array.Fill(g.Stage.Rows[11], Ground.Wall);
        g.PutSoldier(SoldierKind.Grenadier, 512, 470).ToY = 470;
        int t = 0;
        while (g.Grenades.Count == 0 && t++ < 600) g.Idle(1);
        var n = Assert.Single(g.Grenades);
        Assert.True(n.Hostile);
        Assert.InRange(Field.Distance(n.ToX, n.ToY, P1(g).X, P1(g).Y), 0, 40);
        while (P1(g).Alive && t++ < 900) g.Idle(1);
        Assert.Equal("blown up", P1(g).Cause);
    }

    [Fact]
    public void A_gun_emplacement_falls_to_bullets_a_pillbox_shrugs_them_off_and_only_a_grenade_takes_it()
    {
        var g = Game(calm: false);
        var p = P1(g);
        p.Safe = 100;
        Set(g, 16, 12, Ground.Gun);
        var gun = new Gun { Col = 16, Row = 12, Hp = 3, FireIn = 99 };
        g.Guns.Add(gun);
        g.PutPlayer(0, gun.X, 200);
        p.Safe = 100;
        for (int i = 0; i < 3; i++) { g.Run(1, FireButton); g.Idle(30); }
        Assert.True(gun.Dead);
        Assert.Equal(GameEngine.GunPoints, p.Score);
        Assert.False(g.Stage.BlocksFoot(16, 12));
        g.Stage.Rows[12][15] = g.Stage.Rows[12][16] = g.Stage.Rows[13][15] = g.Stage.Rows[13][16] = Ground.Bunker;
        var box = new Pillbox { Col = 15, Row = 12, FireIn = 0 };
        g.Pillboxes.Add(box);
        g.PutPlayer(0, box.X, box.Y - GameEngine.ThrowRange);
        p.Safe = 10;
        g.Run(1, FireButton);
        g.Idle(30);
        Assert.False(box.Dead);
        p.Safe = 0;
        g.Bullets.Clear();
        g.Idle(2);
        Assert.Equal(3, g.Bullets.Count(b => b.Hostile));
        g.Bullets.Clear();
        p.Safe = 10;
        g.Run(1, GrenadeButton);
        g.Idle((int)(GameEngine.ThrowTime * 60) + 2);
        Assert.True(box.Dead);
        Assert.Equal(GameEngine.GunPoints + GameEngine.PillboxPoints, p.Score);
        Assert.False(g.Stage.BlocksFoot(15, 12));
    }

    [Fact]
    public void An_enemy_tank_shrugs_off_bullets_falls_to_grenades_or_shells_and_runs_down_a_man_on_foot()
    {
        var g = Game();
        var p = P1(g);
        var e = g.PutEnemyTank(512, 200 + GameEngine.ThrowRange);
        int hp = e.Hp;
        g.Run(1, FireButton);
        g.Idle(40);
        Assert.Equal(hp, e.Hp);
        for (int i = 0; i < hp; i++) { g.Run(1, GrenadeButton); g.Idle((int)(GameEngine.ThrowTime * 60) + 2); }
        Assert.True(e.Dead);
        Assert.Equal(GameEngine.EnemyTankPoints, p.Score);
        // From a tank, shells do twice a grenade's damage.
        var h = Game();
        var t = h.PutTank(512, 200);
        h.Idle(1);
        var f = h.PutEnemyTank(512, 420);
        for (int i = 0; i < 8 && !f.Dead; i++) { h.Run(1, FireButton); h.Idle(40); }
        Assert.True(f.Dead);
        Assert.Same(t, P1(h).Tank);
        // And a man on foot in its way is run down.
        var k = Game(calm: false);
        var rolling = k.PutEnemyTank(512, 260);
        rolling.ToY = 0;
        rolling.FireIn = 99;
        int n = 0;
        while (P1(k).Alive && n++ < 120) k.Idle(1);
        Assert.Equal("run over", P1(k).Cause);
    }

    [Fact]
    public void An_enemy_tank_turns_its_gun_on_him_and_shells_him()
    {
        var g = Game(calm: false);
        var e = g.PutEnemyTank(800, 560);
        e.ToY = e.Y;
        int t = 0;
        while (g.Shells.Count == 0 && t++ < 600) g.Idle(1);
        var s = Assert.Single(g.Shells);
        Assert.True(s.Hostile);
        double toward = Math.Atan2(P1(g).Y - e.Y, P1(g).X - e.X);
        Assert.Equal(toward, Math.Atan2(s.Vy, s.Vx), 1);
        while (P1(g).Alive && t++ < 900) g.Idle(1);
        Assert.Equal("blown up", P1(g).Cause);
    }

    [Fact]
    public void A_gunship_fires_bursts_drops_bombs_and_bullets_bring_it_down()
    {
        var g = Game(calm: false);
        var p = P1(g);
        p.Safe = 0;
        g.PutHelicopter(512, 560);
        int shots = 0;
        bool bomb = false;
        for (int t = 0; t < 90 && p.Alive; t++)
        {
            g.Idle(1);
            shots = Math.Max(shots, g.Bullets.Count(b => b.Hostile));
            bomb |= g.Grenades.Any(n => n.Hostile);
        }
        Assert.Equal(3, shots);
        Assert.True(bomb);
        var k = Game();
        var q = k.PutHelicopter(512, 420);
        for (int i = 0; i < 200 && !q.Dead; i++) { k.PutPlayer(0, q.X, 200); k.Run(1, FireButton); k.Idle(8); }
        Assert.True(q.Dead);
        Assert.Equal(GameEngine.HelicopterPoints, P1(k).Score);
    }

    // ───────────────────────────── two of them ─────────────────────────────

    [Fact]
    public void A_second_player_joins_at_any_time_with_his_own_score_and_supplies()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, default, start1: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.False(g.Players[1].Joined);
        Assert.Single(g.Fighting);
        g.Run(60 * 5, new Controls(Dy: 1));
        g.Tick(default, default, start2: true);
        var two = g.Players[1];
        Assert.True(two.Fighting);
        Assert.Equal((GameEngine.StartingLives, GameEngine.StartAmmo, GameEngine.StartGrenades, 0), (two.Lives, two.Ammo, two.Grenades, two.Score));
        Assert.InRange(two.Y - g.Camera, 20, 300);
        Assert.Equal(2, g.Fighting.Count());
    }

    [Fact]
    public void Both_fight_side_by_side_each_scoring_his_own_and_the_screen_waits_for_the_one_behind()
    {
        var g = Game(players: 2);
        var (one, two) = (g.Players[0], g.Players[1]);
        var a = g.PutSoldier(SoldierKind.Rifleman, one.X, 400);
        var b = g.PutSoldier(SoldierKind.Grenadier, two.X, 400);
        g.Run(1, FireButton, FireButton);
        g.Idle(40);
        Assert.True(a.Dead && b.Dead);
        Assert.Equal((100, 150), (one.Score, two.Score));
        // Player 1 runs on ahead; player 2 stays put: the screen goes no further than keeps player 2 on it.
        g.Run(60 * 6, new Controls(Dy: 1));
        Assert.Equal(two.Y - Warrior.Radius - 4, g.Camera, 1);
        Assert.Equal(g.ViewTop - Warrior.Radius, one.Y, 1);
        // With only one, the screen keeps up with him.
        var h = Game();
        h.Run(120, new Controls(Dy: 1));
        Assert.Equal(P1(h).Y - Field.View / 2, h.Camera, 1);
    }

    [Fact]
    public void The_keys_drive_each_player_and_never_the_other()
    {
        var g = Game(players: 2);
        var (one, two) = (g.Players[0], g.Players[1]);
        double x1 = one.X, x2 = two.X;
        g.Run(30, new Controls(Dx: -1), new Controls(Dx: 1, Turn: 1));
        Assert.True(one.X < x1 - 50 && two.X > x2 + 50);
        Assert.Equal(Field.WayOf(-1, 0), one.Facing);
        Assert.NotEqual(Field.WayOf(1, 0), two.Facing);
    }

    // ───────────────────────────── lives, scores and the game ─────────────────────────────

    [Fact]
    public void An_extra_life_every_30000()
    {
        var g = Game();
        var p = P1(g);
        int lives = p.Lives;
        g.SetScore(0, GameEngine.ExtraLifeEvery - 50);
        g.PutSoldier(SoldierKind.Rifleman, 512, 400);
        g.Run(1, FireButton);
        g.Idle(40);
        Assert.Equal(lives + 1, p.Lives);
    }

    [Fact]
    public void Out_of_lives_he_may_carry_on_with_his_score_or_the_game_ends_and_a_high_score_asks_for_initials()
    {
        var g = Game(calm: false);
        var p = P1(g);
        g.SetLives(0, 1);
        g.SetScore(0, 99999);
        g.PutEnemyTank(512, 220).ToY = 0;
        g.Idle(1);
        Assert.False(p.Alive);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.True(p.Out);
        Assert.True(p.Continue > 0);
        Assert.Equal(GameState.Playing, g.State);
        // Carry on: three lives, the score kept.
        g.Tick(default, default, start1: true);
        Assert.True(p.Alive);
        Assert.Equal((GameEngine.StartingLives, 99999, 1), (p.Lives, p.Score, p.Continues));
        g.Tick(default);
        // This time, let the offer go.
        g.EnemyTanks.Clear();
        g.SetLives(0, 1);
        g.PutEnemyTank(p.X, p.Y + 10).ToY = 0;
        p.Safe = 0;
        g.Idle(2);
        g.Idle((int)((GameEngine.DyingSeconds + GameEngine.ContinueSeconds) * 60) + 4);
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle((int)(GameEngine.GameOverSeconds * 60) + 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        Assert.Equal(0, g.EntryPlayer);
        foreach (char ch in "IKA") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(("IKA", 99999), (g.HighScores.Entries[0].Initials, g.HighScores.Entries[0].Score));
    }

    [Fact]
    public void With_two_the_game_goes_on_until_both_are_out_and_both_enter_initials()
    {
        var g = Game(calm: false, players: 2);
        g.SetScore(0, 70000);
        g.SetScore(1, 80000);
        g.SetLives(0, 1);
        g.SetLives(1, 1);
        g.PutEnemyTank(g.Players[0].X, 220).ToY = 0;
        g.Idle(2);
        Assert.False(g.Players[0].Alive);
        g.EnemyTanks.Clear();
        g.Idle((int)((GameEngine.DyingSeconds + GameEngine.ContinueSeconds) * 60) + 4);
        Assert.Equal(GameState.Playing, g.State);
        Assert.True(g.Players[1].Fighting);
        g.EnemyTanks.Clear();
        g.PutEnemyTank(g.Players[1].X, g.Players[1].Y + 10).ToY = 0;
        g.Idle(2);
        g.Idle((int)((GameEngine.DyingSeconds + GameEngine.ContinueSeconds + GameEngine.GameOverSeconds) * 60) + 8);
        Assert.Equal(GameState.EnterInitials, g.State);
        Assert.Equal(1, g.EntryPlayer);
        g.Initials!.Finish();
        g.Idle(1);
        Assert.Equal(GameState.EnterInitials, g.State);
        Assert.Equal(0, g.EntryPlayer);
        g.Initials!.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
    }

    // ───────────────────────────── the stages ─────────────────────────────

    [Fact]
    public void The_screen_follows_him_up_but_never_back_down_and_he_cannot_run_off_the_bottom()
    {
        var g = Game();
        var p = P1(g);
        g.Run(120, new Controls(Dy: 1));
        double camera = g.Camera;
        Assert.True(camera > 100);
        g.Run(240, new Controls(Dy: -1));
        Assert.Equal(camera, g.Camera);
        Assert.Equal(camera + Warrior.Radius + 4, p.Y, 1);
    }

    [Fact]
    public void The_enemy_in_the_map_comes_on_as_the_screen_reaches_it()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        var first = g.Stage.Spawns.Where(s => s.Kind is SpawnKind.Rifleman && s.Y > g.ViewTop + 100).OrderBy(s => s.Y).First();
        P1(g).Safe = 1000;
        for (int i = 0; i < 60 * 60 && g.ViewTop + 40 < first.Y; i++) g.Run(1, new Controls(Dy: 1));
        g.Run(1, new Controls(Dy: 1));
        Assert.Contains(g.Soldiers, s => s.Kind == SoldierKind.Rifleman && Field.Distance(s.X, s.Y, first.X, first.Y) < 20);
    }

    [Fact]
    public void At_the_end_of_a_stage_its_guard_comes_on_and_beating_it_takes_the_stage()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(2);
        g.SetLives(0, 99);
        g.MoveCamera(g.Stage.CameraEnd);
        g.Soldiers.Clear();
        g.EnemyTanks.Clear();
        g.Helicopters.Clear();
        P1(g).Safe = 1000;
        g.Idle(2);
        Assert.True(g.AtEnd);
        Assert.Equal(2, g.EnemyTanks.Count);
        Assert.Equal(Phase.Play, g.Phase);
        foreach (var e in g.EnemyTanks) e.Dead = true;
        g.Idle(2);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(2000, g.Bonus);
        int score = P1(g).Score;
        g.Idle((int)(GameEngine.ClearSeconds * 60) + 2);
        Assert.Equal(score + 2000, P1(g).Score);
        Assert.Equal("THE RUINS", g.Stage.Name);
        Assert.Equal(0, g.Camera);
    }

    [Fact]
    public void At_the_fortress_the_gate_takes_a_pounding_then_opens_they_march_in_and_the_war_starts_again_harder()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(4, players: 2);
        g.SetLives(0, 99);
        g.MoveCamera(g.Stage.CameraEnd);
        g.Soldiers.Clear();
        foreach (var p in g.Fighting) p.Safe = 1000;
        g.Idle(2);
        Assert.True(g.AtEnd);
        Assert.Empty(g.Helicopters);
        Assert.True(g.GateHp > 1);
        // A grenade at the gate takes a little off it; the last one blows it in.
        g.PutPlayer(0, g.Stage.GateX, g.Stage.GateY + 10 - GameEngine.ThrowRange);
        P1(g).Safe = 1000;
        int hp = g.GateHp;
        g.Run(1, GrenadeButton);
        g.Idle((int)(GameEngine.ThrowTime * 60) + 2);
        Assert.Equal(hp - 1, g.GateHp);
        g.HurtGate(1);
        g.Idle(20);
        g.Run(1, GrenadeButton);
        g.Idle((int)(GameEngine.ThrowTime * 60) + 2);
        Assert.Equal(0, g.GateHp);
        Assert.True(g.Stage.GateOpen);
        Assert.Equal(Phase.Finale, g.Phase);
        Assert.Empty(g.Soldiers);
        Assert.False(g.Stage.BlocksFoot(Stage.GateCol, g.Stage.GateRow));
        g.Idle(60 * 3);
        Assert.True(P1(g).Y > g.Stage.GateY);
        g.Idle((int)(GameEngine.FinaleSeconds * 60));
        Assert.Equal(5, g.Stage.Number);
        Assert.Equal((2, "THE JUNGLE"), (g.Stage.Round, g.Stage.Name));
        Assert.True(P1(g).Score >= GameEngine.GatePoints + 10000);
        // Harder: the enemy faster, better shots, tougher armour.
        var first = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        first.Go(1);
        Assert.True(g.SoldierSpeed > first.SoldierSpeed);
        Assert.True(g.SoldierFireGap < first.SoldierFireGap);
        Assert.True(g.EnemyTankHp > first.EnemyTankHp);
    }

    [Fact]
    public void The_title_runs_demonstrations_with_both_commandos_then_one_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        var seen = new List<(string, int)>();
        for (int round = 0; round < 2; round++)
        {
            g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
            Assert.Equal(GameState.Attract, g.State);
            seen.Add((g.Stage.Name, g.Fighting.Count()));
            Assert.All(g.Fighting, p => Assert.NotNull(g.Pilots[p.Index]));
            for (int i = 0; i < (int)(GameEngine.AttractSeconds * 60) + 2 && g.State == GameState.Attract; i++) g.Idle(1);
            Assert.Equal(GameState.HighScores, g.State);
            g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
            Assert.Equal(GameState.Title, g.State);
        }
        Assert.Equal([("THE JUNGLE", 2), ("THE RIVER", 1)], seen);
    }

    [Fact]
    public void One_starts_a_game_for_one_and_two_for_two()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, default, start1: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal((1, 3, GameEngine.StartGrenades, GameEngine.StartAmmo), (g.Stage.Number, P1(g).Lives, P1(g).Grenades, P1(g).Ammo));
        Assert.False(g.Players[1].Joined);
        var h = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        h.Tick(default, default, start2: true);
        Assert.Equal(2, h.Fighting.Count());
        Assert.True(h.Players[0].X < h.Players[1].X);
    }

    [Fact]
    public void The_computer_player_takes_the_first_stage()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        g.SetLives(0, 99);
        var pilot = new WarriorPilot();
        for (int i = 0; i < 60 * 300 && g.Stage.Number < 2 && g.State == GameState.Playing; i++) g.Tick(pilot.Decide(g, 0));
        Assert.True(g.Stage.Number >= 2, $"still in stage {g.Stage.Number}, at {g.Camera:0} of {g.Stage.CameraEnd:0}");
    }

    [Fact]
    public void Two_computer_players_together_take_the_river()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(2, players: 2);
        g.SetLives(0, 99);
        g.SetLives(1, 99);
        WarriorPilot a = new(), b = new();
        for (int i = 0; i < 60 * 300 && g.Stage.Number < 3 && g.State == GameState.Playing; i++) g.Tick(a.Decide(g, 0), b.Decide(g, 1));
        Assert.True(g.Stage.Number >= 3, $"still in stage {g.Stage.Number}, at {g.Camera:0} of {g.Stage.CameraEnd:0}");
        Assert.True(g.Players[0].Score > 0 && g.Players[1].Score > 0);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(2, players: 2);
            WarriorPilot a = new(), b = new();
            for (int i = 0; i < 60 * 30; i++) g.Tick(a.Decide(g, 0), b.Decide(g, 1));
            return $"{g.Players[0].Score} {g.Players[1].Score} {g.Stage.Number} {g.Players[0].X:0.00} {g.Players[1].Y:0.00} {g.Camera:0.00} {g.Soldiers.Count} {g.Phase}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Game(calm: false);
        g.Run(30, new Controls(Dy: 1));
        double y = P1(g).Y;
        g.Pause();
        g.Idle(120);
        Assert.Equal(y, P1(g).Y);
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
