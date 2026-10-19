namespace AVAIkariWarriors.Engine;

/// <summary>
/// One commando's controls. Running: <paramref name="Dx"/> and <paramref name="Dy"/>, each -1, 0 or 1 (up the map is +1).
/// Fire and a grenade (both at once: out of a tank). Aiming, apart from running, the way the arcade's rotary stick
/// did: <paramref name="Turn"/> swings the aim a step anticlockwise (-1) or clockwise (+1), or
/// <paramref name="AimX"/>/<paramref name="AimY"/> point it straight at one of the eight ways (a right stick).
/// Otherwise he aims the way he runs, except while the trigger is held: firing locks the aim.
/// </summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false, bool Grenade = false, int Turn = 0, int AimX = 0, int AimY = 0);

/// <summary>
/// The screen and the world. The world runs up the map from 0 at the start of a stage, so y grows upwards; the
/// camera is the world y at the bottom of the screen, and only ever moves up.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Cell = 32;
    public const int Cols = 32;
    /// <summary>The score bar across the top of the screen.</summary>
    public const double Hud = 64;
    public const double View = Height - Hud;

    public static double ScreenY(double wy, double camera) => Height - (wy - camera);
    public static int ColOf(double x) => (int)Math.Floor(x / Cell);
    public static int RowOf(double y) => (int)Math.Floor(y / Cell);
    public static double CellX(int col) => (col + 0.5) * Cell;
    public static double CellY(int row) => (row + 0.5) * Cell;
    public static double Distance(double ax, double ay, double bx, double by) => Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));

    /// <summary>The eight ways to face, from up (0) round clockwise.</summary>
    public static readonly (int Dx, int Dy)[] Ways = [(0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1)];

    public static int WayOf(int dx, int dy) => Array.IndexOf(Ways, (Math.Sign(dx), Math.Sign(dy)));
    public static double AngleOf(int way) => Math.Atan2(Ways[way].Dy, Ways[way].Dx);

    /// <summary>A unit step along one of the ways.</summary>
    public static (double X, double Y) Unit(int way)
    {
        var (dx, dy) = Ways[way];
        double k = dx != 0 && dy != 0 ? Math.Sqrt(0.5) : 1;
        return (dx * k, dy * k);
    }

    /// <summary>The way nearest the direction from one point to another.</summary>
    public static int WayTowards(double ax, double ay, double bx, double by)
    {
        double a = Math.Atan2(by - ay, bx - ax);
        // Way 0 is up (π/2), and the ways go clockwise.
        int w = (int)Math.Round((Math.PI / 2 - a) / (Math.PI / 4));
        return ((w % 8) + 8) % 8;
    }
}

/// <summary>What stands in a cell of the map.</summary>
public static class Ground
{
    public const char Open = '.', Tree = 'T', Rock = 'R', Sandbag = 'S', Fence = 'H', Wall = 'W', Water = '~', Bridge = '=',
        Trench = '_', Ruin = 'U', Rubble = ',', Floor = 'P', Road = ':', Bunker = 'X', Gun = 'E', Fort = 'F', Gate = 'G';

    /// <summary>Whether a man on foot can't go there.</summary>
    public static bool BlocksFoot(char c) => c is Rock or Sandbag or Fence or Wall or Ruin or Bunker or Gun or Fort or Gate;

    /// <summary>Whether a tank can't go there. It flattens sandbags and wire, but can't swim.</summary>
    public static bool BlocksTank(char c) => c is Rock or Wall or Ruin or Bunker or Gun or Fort or Gate or Water;

    /// <summary>What a tank rolls flat.</summary>
    public static bool Crushable(char c) => c is Sandbag or Fence;

    /// <summary>Whether a bullet stops there (wire doesn't stop it; grenades fly over everything).</summary>
    public static bool Stops(char c) => c is Rock or Sandbag or Wall or Ruin or Bunker or Gun or Fort or Gate;

    /// <summary>How fast a man on foot goes through it: wading a river, or down in a trench.</summary>
    public static double Pace(char c) => c switch { Water => 0.45, Trench => 0.7, _ => 1 };
}

public enum SpawnKind { Rifleman, Grenadier, Trench, Tank, EnemyTank, Helicopter, Pickup }

public enum PickupKind
{
    /// <summary>A box of bullets.</summary>
    Bullets,
    /// <summary>A box of grenades.</summary>
    Grenades,
    /// <summary>S: he runs faster.</summary>
    Speed,
    /// <summary>F: his bullets fly further.</summary>
    Range,
    /// <summary>B: bigger grenades.</summary>
    Big,
    /// <summary>A can of gas: a tank's fuel topped up.</summary>
    Fuel,
    /// <summary>K: every enemy soldier on the screen falls.</summary>
    KillAll,
}

/// <summary>Something waiting in the map, that comes on when the top of the screen reaches it.</summary>
public sealed record Spawn(SpawnKind Kind, double X, double Y, PickupKind Item = PickupKind.Bullets);

/// <summary>
/// One stage of the war: a map of cells, bottom row first, built from a run of stretches of country (jungle, wire,
/// sandbag lines, trenches, rivers with a bridge, ruins, pillboxes and gun emplacements, a tank left for the taking)
/// and ending in a clearing where the stage's guard waits (a gunship, armour) or, the fourth time, at the gate of the
/// fortress. The second time round (the round after the gate) it's the same ground, more heavily held.
/// </summary>
public sealed class Stage
{
    public string Name { get; }
    /// <summary>Which stretch of the war: 0 the jungle, 1 the river, 2 the ruins, 3 the fortress.</summary>
    public int Theme { get; }
    /// <summary>The stage, counting from 1 and on through the rounds (5 is the jungle again).</summary>
    public int Number { get; }
    public int Round => (Number - 1) / Count + 1;
    public List<char[]> Rows { get; } = [];
    public List<Spawn> Spawns { get; } = [];
    /// <summary>The pillboxes (two cells by two) and the gun emplacements (one cell), by their bottom-left cells.</summary>
    public List<(int Col, int Row)> Bunkers { get; } = [];
    public List<(int Col, int Row)> Guns { get; } = [];
    /// <summary>Who waits at the end: they come on when the screen stops.</summary>
    public List<SpawnKind> Guard { get; } = [];
    /// <summary>The fortress's gate, at the end of the fourth stage (0 if there is none).</summary>
    public int GateRow { get; private set; }
    public const int GateCol = 13, GateWidth = 6;
    public bool HasGate => GateRow > 0;
    public bool GateOpen { get; set; }
    /// <summary>Where the screen stops, with the end of the stage near the top of it.</summary>
    public double CameraEnd { get; private set; }

    public double GateY => GateRow * Field.Cell;
    public double GateX => (GateCol + GateWidth / 2.0) * Field.Cell;

    public static readonly string[] Names = ["THE JUNGLE", "THE RIVER", "THE RUINS", "THE FORTRESS"];
    public static int Count => Names.Length;

    public char At(int col, int row) => col < 0 || col >= Field.Cols ? Ground.Rock : row < 0 ? Ground.Open : row >= Rows.Count ? Ground.Tree : Rows[row][col];
    public char AtPoint(double x, double y) => At(Field.ColOf(x), Field.RowOf(y));
    public void Set(int col, int row, char what)
    {
        if (row >= 0 && row < Rows.Count && col >= 0 && col < Field.Cols) Rows[row][col] = what;
    }

    public bool BlocksFoot(int col, int row) { char c = At(col, row); return Ground.BlocksFoot(c) && !(c == Ground.Gate && GateOpen); }
    public bool BlocksTank(int col, int row) { char c = At(col, row); return Ground.BlocksTank(c) && !(c == Ground.Gate && GateOpen); }

    /// <summary>Whether a bullet could fly straight from one point to the other.</summary>
    public bool Clear(double ax, double ay, double bx, double by)
    {
        double d = Field.Distance(ax, ay, bx, by);
        int n = Math.Max(1, (int)(d / 8));
        for (int i = 1; i < n; i++)
        {
            double t = (double)i / n;
            if (Ground.Stops(AtPoint(ax + (bx - ax) * t, ay + (by - ay) * t))) return false;
        }
        return true;
    }

    /// <summary>Pushes something round (a man, a tank) out of the cells it can't be in; true if it had to.</summary>
    public bool Push(ref double x, ref double y, double r, bool tank = false)
    {
        bool pushed = false;
        int c0 = Field.ColOf(x - r), c1 = Field.ColOf(x + r), r0 = Field.RowOf(y - r), r1 = Field.RowOf(y + r);
        for (int row = r0; row <= r1; row++)
        for (int col = c0; col <= c1; col++)
        {
            if (tank ? !BlocksTank(col, row) : !BlocksFoot(col, row)) continue;
            double left = col * Field.Cell, bottom = row * Field.Cell;
            double nx = Math.Clamp(x, left, left + Field.Cell), ny = Math.Clamp(y, bottom, bottom + Field.Cell);
            double dx = x - nx, dy = y - ny, d = Math.Sqrt(dx * dx + dy * dy);
            if (d >= r) continue;
            if (d < 1e-6)
            {
                double l = x - left, rt = left + Field.Cell - x, b = y - bottom, t = bottom + Field.Cell - y, m = Math.Min(Math.Min(l, rt), Math.Min(b, t));
                if (m == l) x = left - r; else if (m == rt) x = left + Field.Cell + r; else if (m == b) y = bottom - r; else y = bottom + Field.Cell + r;
            }
            else
            {
                x = nx + dx / d * r;
                y = ny + dy / d * r;
            }
            pushed = true;
        }
        return pushed;
    }

    private Stage(int number)
    {
        Number = number;
        Theme = (number - 1) % Count;
        Name = Names[Theme];
    }

    /// <summary>
    /// Stage <paramref name="number"/> (1 for the first): the same ground every time for the same stretch of the
    /// war, the four in turn, and more of the enemy each round.
    /// </summary>
    public static Stage Build(int number)
    {
        var s = new Stage(number);
        var rng = new Random(1986 + s.Theme * 7919);
        int extra = s.Round - 1;
        s.Open(9, rng, trees: 0.03, rocks: 0);
        // Each stretch of the war its own country, with a tank left for the taking twice along the way.
        string[] plan = s.Theme switch
        {
            0 => ["jungle", "sandbags", "jungle", "wire", "trench", "jungle", "bunkers", "guns", "jungle", "sandbags"],
            1 => ["river", "jungle", "sandbags", "river", "guns", "river", "trench", "jungle", "river", "bunkers"],
            2 => ["ruins", "rubble", "trench", "ruins", "guns", "sandbags", "ruins", "bunkers", "rubble", "ruins"],
            _ => ["walls", "wire", "guns", "bunkers", "trench", "walls", "sandbags", "guns", "wire", "bunkers"],
        };
        int stretches = 10;
        for (int i = 0; i < stretches; i++)
        {
            string kind = i is 2 or 6 ? "tank" : plan[i];
            switch (kind)
            {
                case "jungle": s.Open(13, rng, trees: s.Theme == 0 ? 0.13 : 0.08, rocks: 0.02, enemies: 2 + extra); break;
                case "rubble": s.Open(13, rng, trees: 0.02, rocks: 0.04, enemies: 2 + extra, floor: Ground.Rubble); break;
                case "sandbags": s.Sandbags(rng, extra); break;
                case "wire": s.Wire(rng, extra); break;
                case "trench": s.TrenchLine(rng, extra); break;
                case "river": s.River(rng, extra); break;
                case "bunkers": s.BunkerField(rng); break;
                case "guns": s.GunLine(rng, extra); break;
                case "ruins": s.Ruins(rng, extra); break;
                case "walls": s.WallLine(rng, extra); break;
                case "tank": s.TankPark(rng, extra); break;
            }
            // Enemy armour comes down the road in the later stretches of the war.
            if (i is 4 or 8 && s.Theme > 0) s.Spawns.Add(new Spawn(SpawnKind.EnemyTank, Field.CellX(8 + rng.Next(16)), (s.Rows.Count - 2) * Field.Cell));
            if (i == 7 && (s.Theme is 0 or 2 || extra > 0)) s.Spawns.Add(new Spawn(SpawnKind.Helicopter, Field.Width / 2, (s.Rows.Count - 2) * Field.Cell));
        }
        if (s.Theme == 3) s.Fortress(rng, extra);
        else s.Clearing(rng);
        return s;
    }

    private static char[] Row(char fill = Ground.Open) => Enumerable.Repeat(fill, Field.Cols).ToArray();

    /// <summary>A free spot in a row, away from the edges.</summary>
    private int FreeCol(Random rng, int row)
    {
        for (int i = 0; i < 40; i++)
        {
            int c = 2 + rng.Next(Field.Cols - 4);
            if (!Ground.BlocksFoot(Rows[row][c]) && Rows[row][c] != Ground.Water) return c;
        }
        return Field.Cols / 2;
    }

    private void Enemy(Random rng, int row, SpawnKind kind = SpawnKind.Rifleman) =>
        Spawns.Add(new Spawn(kind, Field.CellX(FreeCol(rng, row)), Field.CellY(row)));

    private void Item(Random rng, int row, PickupKind kind) =>
        Spawns.Add(new Spawn(SpawnKind.Pickup, Field.CellX(FreeCol(rng, row)), Field.CellY(row), kind));

    private static readonly PickupKind[] Letters = [PickupKind.Speed, PickupKind.Range, PickupKind.Big, PickupKind.KillAll];

    private void Open(int rows, Random rng, double trees, double rocks, int enemies = 0, char floor = Ground.Open)
    {
        int start = Rows.Count;
        // In the jungle and by the river, the trees crowd in at either side.
        double edge = Theme switch { 0 => 0.75, 1 => 0.5, _ => 0.15 };
        for (int r = 0; r < rows; r++)
        {
            var row = Row(floor);
            for (int c = 0; c < Field.Cols; c++)
            {
                bool side = c < 2 || c >= Field.Cols - 2;
                if (rng.NextDouble() < (side ? Math.Max(edge, trees) : trees)) row[c] = Ground.Tree;
                else if (floor == Ground.Rubble && rng.NextDouble() < 0.6) row[c] = Ground.Open;
            }
            Rows.Add(row);
        }
        // And in clumps, here and there.
        int groves = (int)(rows * trees * 2.2);
        for (int i = 0; i < groves; i++)
        {
            int c = 2 + rng.Next(Field.Cols - 5), r = start + rng.Next(rows - 1);
            for (int dc = 0; dc < 2 + rng.Next(2); dc++)
            for (int dr = 0; dr < 2; dr++)
                if (rng.Next(4) > 0) Rows[r + dr][c + dc] = Ground.Tree;
        }
        // Rocks in clumps of one to four, never closing the way.
        int clumps = (int)(rows * Field.Cols * rocks / 2.5);
        for (int i = 0; i < clumps; i++)
        {
            int c = 1 + rng.Next(Field.Cols - 3), r = start + 1 + rng.Next(Math.Max(1, rows - 2));
            Rows[r][c] = Ground.Rock;
            if (rng.Next(2) == 0) Rows[r][c + 1] = Ground.Rock;
            if (rng.Next(3) == 0 && r + 1 < start + rows) Rows[r + 1][c] = Ground.Rock;
        }
        for (int i = 0; i < enemies; i++) Enemy(rng, start + rows / 3 + rng.Next(rows * 2 / 3), rng.Next(4) == 0 ? SpawnKind.Grenadier : SpawnKind.Rifleman);
        if (rng.Next(3) == 0) Item(rng, start + rows / 2, rng.Next(2) == 0 ? PickupKind.Bullets : PickupKind.Grenades);
    }

    /// <summary>A line of sandbags across, with gaps, and riflemen and a grenadier behind it.</summary>
    private void Sandbags(Random rng, int extra)
    {
        Open(5, rng, 0.03, 0);
        int line = Rows.Count;
        var row = Row(Ground.Sandbag);
        row[0] = row[Field.Cols - 1] = Ground.Open;
        for (int g = 0; g < 3; g++)
        {
            int c = 3 + g * 9 + rng.Next(5);
            for (int k = 0; k < 3; k++) row[Math.Min(Field.Cols - 2, c + k)] = Ground.Open;
        }
        Rows.Add(row);
        Open(6, rng, 0.03, 0.02);
        Enemy(rng, line + 1);
        Enemy(rng, line + 2);
        Enemy(rng, line + 3, SpawnKind.Grenadier);
        for (int i = 0; i < extra; i++) Enemy(rng, line + 2);
    }

    /// <summary>A barbed-wire fence across with gaps cut in it. Bullets go through wire; men don't; tanks and grenades flatten it.</summary>
    private void Wire(Random rng, int extra)
    {
        Open(5, rng, 0.04, 0.01);
        int line = Rows.Count;
        var row = Row(Ground.Fence);
        foreach (int c in new[] { 3 + rng.Next(6), 13 + rng.Next(5), 23 + rng.Next(5) })
            for (int k = 0; k < 2; k++) row[c + k] = Ground.Open;
        Rows.Add(row);
        Open(6, rng, 0.04, 0.01);
        Enemy(rng, line + 2);
        Enemy(rng, line + 4);
        for (int i = 0; i < extra; i++) Enemy(rng, line + 3, SpawnKind.Grenadier);
        Item(rng, line - 2, Letters[rng.Next(Letters.Length)]);
    }

    /// <summary>A trench dug across: soldiers in it bob up to fire, and a bullet only finds them when they're up.</summary>
    private void TrenchLine(Random rng, int extra)
    {
        Open(6, rng, 0.03, 0.01);
        int line = Rows.Count;
        var row = Row(Ground.Trench);
        row[0] = row[1] = row[Field.Cols - 1] = row[Field.Cols - 2] = Ground.Open;
        Rows.Add(row);
        Open(6, rng, 0.03, 0.01);
        int men = 3 + extra;
        for (int i = 0; i < men; i++)
            Spawns.Add(new Spawn(SpawnKind.Trench, Field.CellX(3 + (i * 26 / men) + rng.Next(3)), Field.CellY(line)));
    }

    /// <summary>A river across, four rows wide, with a bridge a tank can cross by. Men wade it, slowly.</summary>
    private void River(Random rng, int extra)
    {
        Open(5, rng, 0.06, 0.01, enemies: 1);
        int b = 6 + rng.Next(18);
        for (int r = 0; r < 4; r++)
        {
            var row = Row(Ground.Water);
            for (int k = 0; k < 3; k++) row[b + k] = Ground.Bridge;
            Rows.Add(row);
        }
        Open(7, rng, 0.06, 0.02);
        Enemy(rng, Rows.Count - 5, SpawnKind.Grenadier);
        Enemy(rng, Rows.Count - 3);
        Enemy(rng, Rows.Count - 2);
        for (int i = 0; i < extra; i++) Enemy(rng, Rows.Count - 4);
    }

    /// <summary>Two pillboxes behind short sandbag wings: only grenades and shells take them out. Grenades nearby.</summary>
    private void BunkerField(Random rng)
    {
        Open(6, rng, 0.03, 0.02);
        int r = Rows.Count;
        Rows.Add(Row());
        Rows.Add(Row());
        Rows.Add(Row());
        foreach (int c in new[] { 4 + rng.Next(8), 19 + rng.Next(8) })
        {
            Rows[r][c] = Rows[r][c + 1] = Rows[r + 1][c] = Rows[r + 1][c + 1] = Ground.Bunker;
            Rows[r][c - 1] = Rows[r][c + 2] = Ground.Sandbag;
            Bunkers.Add((c, r));
        }
        Open(5, rng, 0.03, 0.02, enemies: 1);
        Item(rng, r - 4, PickupKind.Grenades);
    }

    /// <summary>Gun emplacements dug in across the way, with riflemen about them.</summary>
    private void GunLine(Random rng, int extra)
    {
        Open(6, rng, 0.03, 0.02);
        int r = Rows.Count;
        Rows.Add(Row());
        Rows.Add(Row());
        int guns = 2 + Math.Min(1, extra);
        for (int i = 0; i < guns; i++)
        {
            int c = 3 + i * (26 / guns) + rng.Next(4);
            Rows[r][c] = Ground.Gun;
            Guns.Add((c, r));
        }
        Open(5, rng, 0.03, 0.02, enemies: 1 + extra);
    }

    /// <summary>Broken walls: the shells of buildings, with their doorways and holes, rubble between.</summary>
    private void Ruins(Random rng, int extra)
    {
        int start = Rows.Count;
        Open(14, rng, 0.0, 0.0, enemies: 2 + extra, floor: Ground.Rubble);
        for (int b = 0; b < 3; b++)
        {
            int w = 4 + rng.Next(3), h = 4 + rng.Next(2);
            int c0 = 2 + b * 10 + rng.Next(3), r0 = start + 3 + rng.Next(5);
            for (int c = c0; c < c0 + w; c++)
            for (int r = r0; r < r0 + h; r++)
            {
                bool edge = c == c0 || c == c0 + w - 1 || r == r0 || r == r0 + h - 1;
                if (!edge)
                {
                    Rows[r][c] = Ground.Floor;
                    continue;
                }
                // Gaps: a doorway in the bottom and the top, and holes knocked in it.
                bool door = (r == r0 || r == r0 + h - 1) && (c == c0 + w / 2 || c == c0 + w / 2 - 1);
                if (door || rng.Next(5) == 0)
                {
                    Rows[r][c] = Ground.Floor;
                    continue;
                }
                Rows[r][c] = Ground.Ruin;
            }
        }
    }

    /// <summary>A stone wall across with openings wide enough for a tank, riflemen and a gun behind.</summary>
    private void WallLine(Random rng, int extra)
    {
        Open(6, rng, 0.02, 0.01);
        int line = Rows.Count;
        var row = Row(Ground.Wall);
        foreach (int c in new[] { 3 + rng.Next(7), 20 + rng.Next(7) })
            for (int k = 0; k < 4; k++) row[c + k] = Ground.Road;
        Rows.Add(row);
        Rows.Add((char[])row.Clone());
        Open(6, rng, 0.02, 0.02);
        Enemy(rng, line + 3);
        Enemy(rng, line + 4, SpawnKind.Grenadier);
        for (int i = 0; i < extra; i++) Enemy(rng, line + 5);
        int g = 13 + rng.Next(6);
        Rows[line + 4][g] = Ground.Gun;
        Guns.Add((g, line + 4));
    }

    /// <summary>A clearing with an empty tank in it, and a can of gas to go with it.</summary>
    private void TankPark(Random rng, int extra)
    {
        Open(4, rng, 0.03, 0);
        int r = Rows.Count;
        Open(8, rng, 0.0, 0.0, enemies: 1 + extra);
        int c = 6 + rng.Next(20);
        for (int dr = -2; dr <= 2; dr++)
        for (int dc = -2; dc <= 2; dc++)
            Set(c + dc, r + 2 + dr, Ground.Open);
        Spawns.Add(new Spawn(SpawnKind.Tank, Field.CellX(c), Field.CellY(r + 2)));
        Item(rng, r + 5, PickupKind.Fuel);
    }

    /// <summary>The end of a stage: a clearing where its guard comes for you, then the country beyond.</summary>
    private void Clearing(Random rng)
    {
        Open(6, rng, 0.03, 0);
        int r = Rows.Count;
        Open(16, rng, 0.0, 0.0);
        for (int i = r; i < Rows.Count; i++) Rows[i][0] = Rows[i][Field.Cols - 1] = Ground.Tree;
        CameraEnd = (r + 15) * Field.Cell - Field.View;
        Guard.AddRange(Theme switch
        {
            0 => [SpawnKind.Helicopter],
            1 => [SpawnKind.EnemyTank, SpawnKind.EnemyTank],
            _ => [SpawnKind.Helicopter, SpawnKind.EnemyTank],
        });
        Open(8, rng, 0.25, 0.02);
    }

    /// <summary>The fortress: open ground up to its wall, guns on the wall either side of the gate, pillboxes before it.</summary>
    private void Fortress(Random rng, int extra)
    {
        Open(6, rng, 0.02, 0.0);
        int r = Rows.Count;
        Open(10, rng, 0.0, 0.0, enemies: 1 + extra);
        foreach (int c in new[] { 5, 25 })
        {
            Rows[r + 4][c] = Rows[r + 4][c + 1] = Rows[r + 5][c] = Rows[r + 5][c + 1] = Ground.Bunker;
            Bunkers.Add((c, r + 4));
        }
        // The road up to the gate.
        for (int k = r - 6; k < Rows.Count; k++)
            for (int j = 1; j < GateWidth - 1; j++)
                if (Rows[k][GateCol + j] is Ground.Open or Ground.Tree) Rows[k][GateCol + j] = Ground.Road;
        GateRow = Rows.Count;
        for (int k = 0; k < 2; k++)
        {
            var row = Row(Ground.Wall);
            for (int j = 0; j < GateWidth; j++) row[GateCol + j] = Ground.Gate;
            Rows.Add(row);
        }
        foreach (int c in new[] { GateCol - 3, GateCol + GateWidth + 2 })
        {
            Rows[GateRow][c] = Ground.Gun;
            Guns.Add((c, GateRow));
        }
        for (int k = 0; k < 12; k++) Rows.Add(Row(Ground.Fort));
        CameraEnd = GateY + 3 * Field.Cell - Field.View;
    }
}

/// <summary>A commando: player 1 in the red bandana, player 2 in the blue.</summary>
public sealed class Warrior(int index)
{
    public int Index { get; } = index;
    public string Name => Index == 0 ? "PLAYER 1" : "PLAYER 2";
    /// <summary>In the game (now or earlier in it).</summary>
    public bool Joined;
    public bool Alive;
    public double X, Y, Walk;
    /// <summary>Which way he aims: one of <see cref="Field.Ways"/>.</summary>
    public int Facing;
    public double Safe, FireIn, ThrowIn, TurnIn, StepIn;
    /// <summary>Down: how long until he's back in (or out of the game, with no lives left).</summary>
    public double Down;
    /// <summary>Out of lives: how long the offer to carry on stands.</summary>
    public double Continue;
    public int Score, Lives, Ammo, Grenades, Continues;
    /// <summary>The letters: S (speed), F (far-flying bullets), B (big grenades); kept until he falls.</summary>
    public bool Fast, Far, Big;
    public Tank? Tank;
    public bool Wading, Trench;
    public string Cause = "";
    internal bool BothPrev;
    internal int NextExtra;
    public const double Radius = 10;

    /// <summary>On the field and fighting (on foot or in a tank).</summary>
    public bool Fighting => Joined && Alive;
    /// <summary>Out of lives and done.</summary>
    public bool Out => Joined && !Alive && Lives <= 0 && Down <= 0;
}

/// <summary>A tank: empty, waiting for someone to climb in, or driven. It has its own fuel and takes its own damage.</summary>
public sealed class Tank
{
    public double X, Y, Tread;
    /// <summary>The way the hull points, and the way the gun does (the driver's aim).</summary>
    public int Body, Turret;
    public double Fuel = MaxFuel, Damage, FireIn, EnterIn;
    /// <summary>Burning: seconds until it goes up (or -1 while it's sound).</summary>
    public double Fuse = -1;
    public Warrior? Driver;
    public bool Dead, Moving;
    public bool Doomed => Fuse >= 0;
    public const double Radius = 25, MaxFuel = 100;
}

public enum SoldierKind
{
    /// <summary>Runs to a spot near you, stops, aims and fires.</summary>
    Rifleman,
    /// <summary>Keeps further off and lobs grenades over cover.</summary>
    Grenadier,
    /// <summary>In a trench: bobs up to fire, and down again.</summary>
    Trench,
}

public sealed class Soldier
{
    public SoldierKind Kind;
    public double X, Y, ToX, ToY, Facing, T, Wait, FireIn, Walk, Stuck;
    /// <summary>Stood still, about to fire.</summary>
    public bool Aiming;
    /// <summary>In a trench: up and in the open.</summary>
    public bool Up;
    public bool Dead;
    public const double Radius = 10;
}

/// <summary>An enemy tank: it rolls down the map, stops to shell you, and runs down anyone in its way. Bullets glance off.</summary>
public sealed class EnemyTank
{
    public double X, Y, ToY, Turret = -Math.PI / 2, FireIn, Tread, Flash;
    public int Hp;
    public bool Dead;
    public const double Radius = 27;
}

/// <summary>A gunship: it hovers over the field, fires bursts and drops bombs. Bullets wear it down.</summary>
public sealed class Helicopter
{
    public double X, Y, ToX, ToY, FireIn, BombIn, T, Flash, Tilt;
    public int Hp, MaxHp, Burst;
    public bool Dead;
    public const double Radius = 30;
}

/// <summary>A gun emplacement in the map: it swings round to follow you. Bullets or a grenade take it out.</summary>
public sealed class Gun
{
    public int Col, Row, Hp;
    public double FireIn = 1, Angle = -Math.PI / 2, Flash;
    public bool Dead;
    public double X => (Col + 0.5) * Field.Cell;
    public double Y => (Row + 0.5) * Field.Cell;
}

/// <summary>A concrete pillbox in the map: it fires a spread of three, and only a grenade or a shell takes it out.</summary>
public sealed class Pillbox
{
    public int Col, Row;
    public double FireIn = 1;
    public bool Dead;
    public double X => (Col + 1) * Field.Cell;
    public double Y => (Row + 1) * Field.Cell;
}

public sealed class Bullet
{
    public double X, Y, Vx, Vy, Life;
    public bool Hostile;
    /// <summary>Whose it is (a player's index), for the points.</summary>
    public int Owner;
}

/// <summary>A grenade in the air: from where it was thrown to where it will land and go off, over whatever's between.</summary>
public sealed class Grenade
{
    public double FromX, FromY, ToX, ToY, T, Time, Radius;
    public bool Hostile, Big;
    public int Owner;
    public double X => FromX + (ToX - FromX) * Math.Min(1, T / Time);
    public double Y => FromY + (ToY - FromY) * Math.Min(1, T / Time);
    /// <summary>How high it is, for the drawing: up and down in an arc.</summary>
    public double Height => Math.Sin(Math.PI * Math.Min(1, T / Time)) * 60;
}

/// <summary>A tank's cannon shell: it flies straight, and goes off at what it hits or at the end of its range.</summary>
public sealed class Shell
{
    public double X, Y, Vx, Vy, Range;
    public bool Hostile, Done;
    public int Owner;
}

public sealed class Pickup
{
    public PickupKind Kind;
    public double X, Y, Age;
    /// <summary>How long it lies there (dropped ones don't stay for ever).</summary>
    public double Life = double.PositiveInfinity;
}

/// <summary>A fleck of something, flying out.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Age, Life = 1;
    public uint Colour;
}

public enum EffectKind { Score, Blast, Word, Puff, Splash, Flash }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
