using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAIkariWarriors.Engine;
using Effect = AVAIkariWarriors.Engine.Effect;

namespace AVAIkariWarriors.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAIkariWarriors/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF9AA88A, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;
    /// <summary>Each commando's colour: player 1's red bandana, player 2's blue.</summary>
    private static readonly uint[] Bandana = [0xFFE0302A, 0xFF2A70E8];
    private static readonly uint[] PlayerText = [0xFFFF7A6A, 0xFF7AB0FF];

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    /// <summary>A colour made lighter (k &gt; 0) or darker (k &lt; 0).</summary>
    private static uint Shade(uint colour, double k)
    {
        uint Ch(int shift)
        {
            double v = (colour >> shift) & 0xFF;
            v = k >= 0 ? v + (255 - v) * k : v * (1 + k);
            return (uint)Math.Clamp(v, 0, 255) << shift;
        }
        return (colour & 0xFF000000) | Ch(16) | Ch(8) | Ch(0);
    }

    /// <summary>Drawing about a point, turned so that "up" (-y) points along a screen angle.</summary>
    private static DrawingContext.PushedState Turned(DrawingContext dc, double x, double y, double angle) =>
        dc.PushTransform(Matrix.CreateRotation(angle + Math.PI / 2) * Matrix.CreateTranslation(x, y));

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    private const uint Skin = 0xFFE0A878;

    /// <summary>The colours of each stretch of the war: the ground, its flecks, the rocks, and the trees.</summary>
    private static readonly (uint Ground, uint Fleck, uint Rock, uint Tree)[] Themes =
    [
        (0xFF3C6A2A, 0xFF4E7E34, 0xFF7A7A6A, 0xFF1E4A1A),
        (0xFF4E6E30, 0xFF5E7E3A, 0xFF7A806A, 0xFF24521E),
        (0xFF7E745E, 0xFF8E8470, 0xFF8A8A84, 0xFF3A5A2A),
        (0xFF9A8458, 0xFFAA9466, 0xFF8A8070, 0xFF4A6A2A),
    ];

    private double Sy(double wy) => Field.ScreenY(wy, Engine.Camera);

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF101010), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame)
        {
            DrawGround(dc, g.Stage, g.Camera);
            foreach (var k in g.Pickups) DrawPickup(dc, k.Kind, k.X, Sy(k.Y), k.Life - k.Age < 3 && (int)(_clock * 8) % 2 == 0);
            foreach (var b in g.Pillboxes) DrawPillbox(dc, b);
            foreach (var gun in g.Guns) if (!gun.Dead) DrawGun(dc, gun.X, Sy(gun.Y), -gun.Angle, gun.Flash > 0);
            foreach (var t in g.Tanks) DrawTank(dc, t);
            foreach (var e in g.EnemyTanks) DrawEnemyTank(dc, e.X, Sy(e.Y), -e.Turret, e.Tread, e.Flash > 0);
            foreach (var gr in g.Grenades) dc.DrawEllipse(Brush(0x50000000), null, new Point(gr.X, Sy(gr.Y)), 5, 3);
            foreach (var s in g.Soldiers) DrawSoldier(dc, s);
            foreach (var p in g.Players) DrawWarrior(dc, p);
            DrawTrees(dc, g.Stage, g.Camera);
            foreach (var h in g.Helicopters) DrawHelicopter(dc, h.X, Sy(h.Y), h.Tilt, h.Flash > 0, shadowOnly: true, k: 1.3);
            foreach (var h in g.Helicopters) DrawHelicopter(dc, h.X, Sy(h.Y), h.Tilt, h.Flash > 0, k: 1.3);
            foreach (var b in g.Bullets)
            {
                var at = new Point(b.X, Sy(b.Y));
                uint glow = b.Hostile ? 0x60FF6A3Au : b.Owner == 1 ? 0x60A0D0FFu : 0x60FFF0A0u;
                dc.DrawEllipse(Brush(glow), null, at, 5, 5);
                dc.DrawEllipse(Brush(b.Hostile ? 0xFFFF8A4A : 0xFFFFFFD0), null, at, 2.4, 2.4);
            }
            foreach (var s in g.Shells)
            {
                var at = new Point(s.X, Sy(s.Y));
                double a = Math.Atan2(-s.Vy, s.Vx);
                dc.DrawLine(Pen(Alpha(s.Hostile ? 0xFFFF6A3A : 0xFFFFE080, 0.45), 6), at, new Point(at.X - Math.Cos(a) * 16, at.Y - Math.Sin(a) * 16));
                dc.DrawEllipse(Brush(s.Hostile ? 0xFFFFA060 : 0xFFFFF4C0), Pen(0xFF3A2A1A, 1), at, 4, 4);
            }
            foreach (var gr in g.Grenades)
            {
                var at = new Point(gr.X, Sy(gr.Y) - gr.Height);
                double r = gr.Big ? 6 : 4.5;
                dc.DrawEllipse(Brush(gr.Hostile ? 0xFF4A4A3A : 0xFF3A5A2A), Pen(0xFF101010, 1), at, r, r);
                dc.DrawLine(Pen(0xFFC0C0B0, 1.4), new Point(at.X + 1, at.Y - r), new Point(at.X + 3, at.Y - r - 2.5));
            }
            foreach (var d in g.Wreckage)
            {
                double a = Math.Clamp(1 - d.Age / d.Life, 0, 1);
                dc.DrawEllipse(Brush(Alpha(d.Colour, a)), null, new Point(d.X, Sy(d.Y)), 2.2, 2.2);
            }
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS 1 OR 2 TO PLAY", new Point(Field.Width / 2, Field.Height - 16), 26, Yellow, center: true);
    }

    // ───────────────────────────── the ground ─────────────────────────────

    private static bool Wet(char c) => c is Ground.Water or Ground.Bridge;

    /// <summary>The cells on the screen: the ground with its flecks, and whatever stands on it (but the trees, drawn over the soldiers).</summary>
    private void DrawGround(DrawingContext dc, Stage stage, double camera)
    {
        var (ground, fleck, rock, _) = Themes[stage.Theme];
        dc.FillRectangle(Brush(ground), new Rect(0, Field.Hud, Field.Width, Field.View));
        int r0 = Field.RowOf(camera), r1 = Field.RowOf(camera + Field.Height) + 1;
        // Broad patches of lighter and darker ground, so it isn't one flat colour.
        for (int br = r0 / 3 - 1; br <= r1 / 3 + 1; br++)
        for (int bc = 0; bc <= Field.Cols / 3; bc++)
        {
            uint h = Hash(bc, br, 77 + stage.Theme);
            if (h % 3 == 0) continue;
            double px = (bc * 3 + (h >> 4) % 3) * Field.Cell, py = Field.ScreenY((br * 3 + (h >> 8) % 3) * Field.Cell, camera);
            dc.DrawEllipse(Brush(Alpha(Shade(ground, h % 2 == 0 ? -0.12 : 0.08), 0.55)), null, new Point(px, py), 60 + h % 40, 34 + (h >> 12) % 24);
        }
        for (int r = r0; r <= r1; r++)
        for (int c = 0; c < Field.Cols; c++)
        {
            double x = c * Field.Cell, y = Field.ScreenY((r + 1) * Field.Cell, camera);
            var cell = new Rect(x, y, Field.Cell, Field.Cell);
            uint h = Hash(c, r, stage.Theme);
            char at = stage.At(c, r);
            switch (at)
            {
                case Ground.Water:
                case Ground.Bridge:
                {
                    dc.FillRectangle(Brush(0xFF2A6488), cell);
                    if (h % 3 == 0) dc.FillRectangle(Brush(0xFF2E6C92), new Rect(x, y + h % 20, Field.Cell, 8));
                    double wave = (_clock * 24 + h % 32) % 32;
                    dc.DrawLine(Pen(0x80A8E0FF, 1.5), new Point(x + 4 + h % 8, y + wave * 0.9), new Point(x + 16 + h % 8, y + wave * 0.9));
                    // A muddy bank where the water meets the ground.
                    if (!Wet(stage.At(c, r + 1))) { dc.FillRectangle(Brush(0xFF6A5A36), new Rect(x, y, Field.Cell, 5)); dc.FillRectangle(Brush(0x60D0F0FF), new Rect(x, y + 5, Field.Cell, 2)); }
                    if (!Wet(stage.At(c, r - 1))) dc.FillRectangle(Brush(0xFF6A5A36), new Rect(x, y + Field.Cell - 5, Field.Cell, 5));
                    if (at == Ground.Bridge)
                    {
                        dc.FillRectangle(Brush(0xFF8A5A30), new Rect(x, y - 1, Field.Cell, Field.Cell + 2));
                        for (int k = 0; k < 4; k++) dc.DrawLine(Pen(0xFF5A3A1A, 1.5), new Point(x, y + k * 8 + 4), new Point(x + Field.Cell, y + k * 8 + 4));
                        if (stage.At(c - 1, r) != Ground.Bridge) dc.FillRectangle(Brush(0xFF4A2E14), new Rect(x, y - 1, 4, Field.Cell + 2));
                        if (stage.At(c + 1, r) != Ground.Bridge) dc.FillRectangle(Brush(0xFF4A2E14), new Rect(x + Field.Cell - 4, y - 1, 4, Field.Cell + 2));
                    }
                    break;
                }
                case Ground.Trench:
                    dc.FillRectangle(Brush(Shade(ground, -0.25)), cell);
                    dc.FillRectangle(Brush(0xFF3A2A1A), new Rect(x, y + 7, Field.Cell, 18));
                    dc.FillRectangle(Brush(0xFF2A1E12), new Rect(x, y + 7, Field.Cell, 5));
                    dc.DrawLine(Pen(0xFF5A4630, 2), new Point(x + 2 + h % 12, y + 17), new Point(x + 14 + h % 12, y + 17));
                    if (stage.At(c - 1, r) != Ground.Trench) dc.FillRectangle(Brush(Shade(ground, -0.25)), new Rect(x, y + 7, 4, 18));
                    if (stage.At(c + 1, r) != Ground.Trench) dc.FillRectangle(Brush(Shade(ground, -0.25)), new Rect(x + Field.Cell - 4, y + 7, 4, 18));
                    break;
                case Ground.Rock:
                    dc.DrawEllipse(Brush(0x40000000), null, new Point(x + 18, y + 20), 15, 12);
                    Poly(dc, rock, 0xFF3A3A34, 1.2, new Point(x + 4, y + 18), new Point(x + 9, y + 5), new Point(x + 22, y + 3), new Point(x + 29, y + 14), new Point(x + 26, y + 28), new Point(x + 10, y + 29));
                    dc.DrawLine(Pen(0x50FFFFFF, 2), new Point(x + 10, y + 8), new Point(x + 20, y + 6));
                    break;
                case Ground.Sandbag:
                    dc.FillRectangle(Brush(0x30000000), new Rect(x, y + 20, Field.Cell, 10));
                    for (int k = 0; k < 2; k++)
                    for (int j = 0; j < 2; j++)
                        dc.DrawRectangle(Brush(k == 0 ? 0xFFC8B880u : 0xFFB8A870u), Pen(0xFF6A5A30, 1), new Rect(x + j * 16 - (k == 1 ? 8 : 0), y + 4 + k * 10, 16, 11), 4, 4);
                    if (stage.At(c - 1, r) != Ground.Sandbag) dc.DrawRectangle(Brush(0xFFB8A870), Pen(0xFF6A5A30, 1), new Rect(x, y + 14, 8, 11), 3, 3);
                    break;
                case Ground.Fence:
                {
                    Fleck(dc, x, y, c, r, fleck);
                    dc.DrawLine(Pen(0xFF5A4028, 3), new Point(x + 4, y + 6), new Point(x + 4, y + 26));
                    dc.DrawLine(Pen(0x60000000, 2), new Point(x + 6, y + 27), new Point(x + 12, y + 29));
                    foreach (double wy in new[] { 11.0, 19.0 })
                    {
                        dc.DrawLine(Pen(0xFFB0B0B8, 1.2), new Point(x, y + wy), new Point(x + Field.Cell, y + wy + 1));
                        for (int k = 0; k < 4; k++)
                        {
                            double bx = x + 6 + k * 8;
                            dc.DrawLine(Pen(0xFFD0D0D8, 1), new Point(bx - 2, y + wy - 2), new Point(bx + 2, y + wy + 2));
                            dc.DrawLine(Pen(0xFFD0D0D8, 1), new Point(bx + 2, y + wy - 2), new Point(bx - 2, y + wy + 2));
                        }
                    }
                    break;
                }
                case Ground.Wall:
                    DrawStones(dc, x, y, 0xFF8A8478, 0xFF5A564E, h);
                    if (stage.At(c, r - 1) != Ground.Wall) WallFace(dc, x, y, 0xFF6A645A);
                    break;
                case Ground.Ruin:
                    DrawStones(dc, x, y, 0xFF9A9284, 0xFF5E584E, h);
                    // Broken: a bite out of the top, cracks, and stones fallen beside it.
                    if (h % 3 == 0) Poly(dc, ground, null, 0, new Point(x + 8 + h % 10, y - 1), new Point(x + 26, y - 1), new Point(x + 22, y + 9), new Point(x + 14 + h % 6, y + 6));
                    dc.DrawLine(Pen(0xFF4A463E, 1.2), new Point(x + 6 + h % 14, y + 4), new Point(x + 12 + h % 10, y + 18));
                    if (stage.At(c, r - 1) != Ground.Ruin) WallFace(dc, x, y, 0xFF6E685C);
                    if (h % 4 == 1 && !Ground.BlocksFoot(stage.At(c + 1, r))) dc.DrawEllipse(Brush(0xFF8A8478), Pen(0xFF4A463E, 1), new Point(x + Field.Cell + 5, y + 22), 5, 4);
                    break;
                case Ground.Rubble:
                    for (int k = 0; k < 5; k++)
                    {
                        uint f = Hash(c, r, k + 31);
                        dc.DrawEllipse(Brush(k % 2 == 0 ? 0xFF8A8478u : 0xFF6E685Eu), null, new Point(x + 3 + f % 26, y + 3 + (f >> 8) % 26), 2.5 + f % 3, 2 + (f >> 4) % 2);
                    }
                    break;
                case Ground.Floor:
                    // The floor of a ruined building: cracked flagstones, some gone.
                    dc.FillRectangle(Brush(0xFF8E8676), cell);
                    dc.DrawLine(Pen(0xFF6A6458, 1), new Point(x, y + 16), new Point(x + Field.Cell, y + 16));
                    dc.DrawLine(Pen(0xFF6A6458, 1), new Point(x + 16, y), new Point(x + 16, y + Field.Cell));
                    dc.DrawRectangle(null, Pen(0xFF6A6458, 1), cell);
                    if (h % 4 == 0) dc.FillRectangle(Brush(Shade(ground, -0.1)), new Rect(x + (h >> 4) % 2 * 16, y + (h >> 5) % 2 * 16, 16, 16));
                    if (h % 3 == 1) dc.DrawLine(Pen(0xFF5A5448, 1), new Point(x + 3 + h % 10, y + 2), new Point(x + 12 + h % 14, y + 13));
                    break;
                case Ground.Road:
                    dc.FillRectangle(Brush(Shade(ground, 0.12)), cell);
                    for (int k = 0; k < 3; k++)
                    {
                        uint f = Hash(c, r, k + 41);
                        dc.FillRectangle(Brush(Shade(ground, -0.1)), new Rect(x + f % 28, y + (f >> 8) % 28, 4, 2));
                    }
                    break;
                case Ground.Fort:
                    dc.FillRectangle(Brush(0xFF5A5048), cell);
                    if (h % 4 == 0) dc.DrawRectangle(null, Pen(0xFF3A3430, 1), new Rect(x + 4, y + 4, 24, 24));
                    break;
                case Ground.Gate:
                    dc.FillRectangle(Brush(0xFF1A1410), cell);
                    break;
                case Ground.Bunker:
                case Ground.Gun:
                    Fleck(dc, x, y, c, r, fleck);
                    break;
                default:
                    Fleck(dc, x, y, c, r, fleck);
                    if (stage.Theme <= 1 && h % 4 == 0)
                    {
                        // A tuft of grass, or a fern.
                        uint blade = Shade(ground, h % 8 == 0 ? -0.3 : 0.18);
                        double gx = x + 6 + h % 20, gy = y + 10 + (h >> 6) % 16;
                        for (int k = -2; k <= 2; k++) dc.DrawLine(Pen(blade, 1.6), new Point(gx, gy + 6), new Point(gx + k * 3.5, gy - 3 + Math.Abs(k) * 2));
                    }
                    else if (stage.Theme >= 2 && h % 5 == 0)
                        dc.DrawEllipse(Brush(Shade(ground, -0.2)), null, new Point(x + 6 + h % 20, y + 8 + (h >> 6) % 18), 2.5, 2);
                    break;
            }
        }
        DrawFortress(dc, stage, camera);
    }

    /// <summary>The face of a wall, seen a little from the south: darker, with its courses, and a shadow at its foot.</summary>
    private static void WallFace(DrawingContext dc, double x, double y, uint face)
    {
        dc.FillRectangle(Brush(face), new Rect(x, y + 20, Field.Cell, 12));
        dc.DrawLine(Pen(Shade(face, -0.35), 1), new Point(x, y + 26), new Point(x + Field.Cell, y + 26));
        dc.DrawLine(Pen(Shade(face, -0.35), 1), new Point(x + 10, y + 20), new Point(x + 10, y + 26));
        dc.DrawLine(Pen(Shade(face, -0.35), 1), new Point(x + 24, y + 26), new Point(x + 24, y + 32));
        dc.FillRectangle(Brush(0x30FFFFFF), new Rect(x, y + 19, Field.Cell, 1.5));
        dc.FillRectangle(Brush(0x48000000), new Rect(x, y + Field.Cell, Field.Cell, 7));
    }

    private static void Fleck(DrawingContext dc, double x, double y, int c, int r, uint fleck)
    {
        for (int k = 0; k < 3; k++)
        {
            uint f = Hash(c, r, k + 11);
            dc.FillRectangle(Brush(fleck), new Rect(x + f % 28, y + (f >> 8) % 28, 3, 2));
        }
    }

    /// <summary>A cell of laid stone: courses of blocks, lit along the top.</summary>
    private static void DrawStones(DrawingContext dc, double x, double y, uint face, uint mortar, uint h)
    {
        dc.FillRectangle(Brush(face), new Rect(x, y, Field.Cell, Field.Cell));
        for (int k = 0; k < 4; k++)
        {
            double o = k % 2 == 0 ? 0 : 8;
            dc.DrawLine(Pen(mortar, 1), new Point(x, y + k * 8), new Point(x + Field.Cell, y + k * 8));
            dc.DrawLine(Pen(mortar, 1), new Point(x + o + 8, y + k * 8), new Point(x + o + 8, y + k * 8 + 8));
            dc.DrawLine(Pen(mortar, 1), new Point(x + o + 24, y + k * 8), new Point(x + o + 24, y + k * 8 + 8));
        }
        if (h % 5 == 0) dc.FillRectangle(Brush(Shade(face, -0.12)), new Rect(x + 9 + h % 6, y + 9, 14, 6));
        dc.FillRectangle(Brush(0x30FFFFFF), new Rect(x, y, Field.Cell, 2));
    }

    /// <summary>The fortress: battlements along the top of its wall, towers, the gate (swinging open at the end) and how much it has left in it.</summary>
    private void DrawFortress(DrawingContext dc, Stage stage, double camera)
    {
        if (!stage.HasGate) return;
        double top = Field.ScreenY(stage.GateY + 2 * Field.Cell, camera), bottom = Field.ScreenY(stage.GateY, camera);
        if (top > Field.Height || bottom < Field.Hud - 80) return;
        for (double x = 0; x < Field.Width; x += 32)
            if (x < Stage.GateCol * Field.Cell - 32 || x >= (Stage.GateCol + Stage.GateWidth + 1) * Field.Cell)
                dc.DrawRectangle(Brush(0xFF9A9488), Pen(0xFF5A564E, 1), new Rect(x + 6, top - 12, 18, 12));
        double gx0 = Stage.GateCol * Field.Cell, gx1 = (Stage.GateCol + Stage.GateWidth) * Field.Cell, w = gx1 - gx0;
        // A tower each side of the gate.
        foreach (double tx in new[] { gx0 - 40, gx1 })
        {
            dc.DrawRectangle(Brush(0xFF7A746A), Pen(0xFF4A463E, 2), new Rect(tx, top - 34, 40, bottom - top + 40));
            for (int k = 0; k < 3; k++) dc.FillRectangle(Brush(0xFF7A746A), new Rect(tx + 2 + k * 14, top - 44, 9, 10));
            dc.FillRectangle(Brush(0xFF1A1410), new Rect(tx + 16, top - 18, 8, 16));
        }
        // The arch over the gate.
        dc.FillRectangle(Brush(0xFF6A645A), new Rect(gx0, top - 16, w, 18));
        Text(dc, "FORTRESS", new Point((gx0 + gx1) / 2, top - 7), 18, 0xFFE0D8C0, center: true, shadow: false);
        // The doors: two leaves of planks with iron bands, swinging in as the gate opens.
        double open = Engine.GateSwing, leaf = w / 2 * (1 - open * 0.85);
        for (int side = 0; side < 2; side++)
        {
            double lx = side == 0 ? gx0 : gx1 - leaf;
            dc.DrawRectangle(Brush(0xFF7A4A22), Pen(0xFF2A1808, 2), new Rect(lx, top + 2, leaf, bottom - top - 2));
            for (double px = lx + 9; px < lx + leaf - 2; px += 11) dc.DrawLine(Pen(0xFF5A3418, 1.5), new Point(px, top + 4), new Point(px, bottom - 2));
            foreach (double band in new[] { 0.25, 0.75 })
                dc.FillRectangle(Brush(0xFF3A3A3E), new Rect(lx, top + (bottom - top) * band - 3, leaf, 6));
        }
        if (open > 0 && open < 1) dc.FillRectangle(Brush(Alpha(0xFFFFE0A0, 0.25 * (1 - open))), new Rect(gx0 + leaf, top, w - 2 * leaf, bottom - top));
        if (Engine.AtEnd && !stage.GateOpen && Engine.GateMaxHp > 0)
        {
            double f = (double)Engine.GateHp / Engine.GateMaxHp;
            dc.FillRectangle(Brush(0xC0101010), new Rect(gx0, bottom + 6, w, 8));
            dc.FillRectangle(Brush(f > 0.35 ? 0xFFE0C04A : Red), new Rect(gx0 + 1, bottom + 7, (w - 2) * f, 6));
        }
    }

    /// <summary>The trees: canopies over everything on the ground, so a man under one is half hidden.</summary>
    private void DrawTrees(DrawingContext dc, Stage stage, double camera)
    {
        var tree = Themes[stage.Theme].Tree;
        int r0 = Field.RowOf(camera) - 1, r1 = Field.RowOf(camera + Field.Height) + 1;
        // Shadows first, all of them, so no tree's shadow falls on another's leaves.
        for (int r = r0; r <= r1; r++)
        for (int c = 0; c < Field.Cols; c++)
            if (stage.At(c, r) == Ground.Tree)
                dc.DrawEllipse(Brush(0x40000000), null, new Point(c * Field.Cell + 24, Field.ScreenY((r + 1) * Field.Cell, camera) + 27), 22, 17);
        for (int r = r1; r >= r0; r--)
        for (int c = 0; c < Field.Cols; c++)
        {
            if (stage.At(c, r) != Ground.Tree) continue;
            uint h = Hash(c, r, 5);
            double x = c * Field.Cell + 16 + (h % 7) - 3, y = Field.ScreenY((r + 1) * Field.Cell, camera) + 16 + ((h >> 3) % 7) - 3;
            if (stage.Theme is 0 or 1 && h % 3 == 0) DrawPalm(dc, x, y, tree, h);
            else DrawBroadleaf(dc, x, y, tree, h);
        }
    }

    /// <summary>A palm from above: long fronds out from the crown, each with its rib.</summary>
    private static void DrawPalm(DrawingContext dc, double x, double y, uint tree, uint h)
    {
        int n = 7 + (int)(h % 2);
        for (int k = 0; k < n; k++)
        {
            double a = k * Math.PI * 2 / n + h % 10 * 0.1, len = 24 + (h >> (k + 2)) % 5;
            uint leaf = k % 2 == 0 ? Shade(tree, 0.22) : Shade(tree, 0.08);
            using (Turned(dc, x, y, a))
            {
                Poly(dc, leaf, Shade(tree, -0.3), 0.8, new Point(0, 0), new Point(5.5, -len * 0.45), new Point(1.5, -len), new Point(-1, -len * 0.98), new Point(-5, -len * 0.45));
                dc.DrawLine(Pen(Shade(tree, 0.5), 1), new Point(0, -2), new Point(0.5, -len * 0.9));
            }
        }
        dc.DrawEllipse(Brush(0xFF6A4A22), Pen(0xFF3A2A12, 1), new Point(x, y), 4.5, 4.5);
    }

    /// <summary>A broadleaf tree from above: a ring of leaves round a crown, darker beneath, lit from the top left.</summary>
    private static void DrawBroadleaf(DrawingContext dc, double x, double y, uint tree, uint h)
    {
        double s = 0.92 + h % 5 * 0.05, turn = h % 13 * 0.4;
        for (int k = 0; k < 9; k++)
            using (Turned(dc, x, y, turn + k * Math.PI * 2 / 9))
                dc.DrawEllipse(Brush(Shade(tree, -0.12)), null, new Point(0, -12 * s), 7.5 * s, 11 * s);
        for (int k = 0; k < 7; k++)
            using (Turned(dc, x, y, turn + 0.4 + k * Math.PI * 2 / 7))
                dc.DrawEllipse(Brush(tree), null, new Point(0, -8 * s), 7 * s, 9.5 * s);
        dc.DrawEllipse(Brush(Shade(tree, 0.16)), null, new Point(x - 3 * s, y - 3 * s), 10 * s, 9 * s);
        for (int k = 0; k < 4; k++)
        {
            uint f = Hash((int)h, k, 3);
            dc.DrawEllipse(Brush(Shade(tree, 0.34)), null, new Point(x - 9 * s + f % 12, y - 10 * s + (f >> 5) % 10), 3.2 * s, 2.6 * s);
        }
    }

    private void DrawPillbox(DrawingContext dc, Pillbox b)
    {
        double x = b.X, y = Sy(b.Y);
        if (b.Dead)
        {
            for (int k = 0; k < 7; k++)
            {
                uint h = Hash(b.Col, b.Row, k);
                dc.DrawEllipse(Brush(0xFF7A7A72), Pen(0xFF4A4A44, 1), new Point(x - 24 + h % 48, y - 20 + (h >> 8) % 40), 6, 5);
            }
            dc.DrawEllipse(Brush(0x60202020), null, new Point(x, y), 14, 10);
            return;
        }
        DrawPillboxAt(dc, x, y);
    }

    private static void DrawPillboxAt(DrawingContext dc, double x, double y)
    {
        dc.DrawEllipse(Brush(0x50000000), null, new Point(x + 5, y + 6), 33, 31);
        Poly(dc, 0xFF9A9A90, 0xFF4A4A44, 2, new Point(x - 14, y - 30), new Point(x + 14, y - 30), new Point(x + 30, y - 14), new Point(x + 30, y + 14),
            new Point(x + 14, y + 30), new Point(x - 14, y + 30), new Point(x - 30, y + 14), new Point(x - 30, y - 14));
        dc.DrawEllipse(Brush(0xFFB2B2A8), Pen(0xFF5A5A54, 1.5), new Point(x, y), 17, 17);
        dc.DrawEllipse(Brush(0x30000000), null, new Point(x + 3, y + 3), 9, 9);
        // The firing slit, facing down the map.
        dc.FillRectangle(Brush(0xFF101010), new Rect(x - 16, y + 18, 32, 6));
    }

    /// <summary>A gun emplacement: a ring of sandbags round a gun on a pivot, its barrel on the angle.</summary>
    private static void DrawGun(DrawingContext dc, double x, double y, double angle, bool flash)
    {
        dc.DrawEllipse(Brush(0x40000000), null, new Point(x + 3, y + 4), 19, 17);
        for (int k = 0; k < 8; k++)
        {
            double a = k * Math.PI / 4;
            dc.DrawEllipse(Brush(k % 2 == 0 ? 0xFFC8B880u : 0xFFB4A470u), Pen(0xFF6A5A30, 1), new Point(x + Math.Cos(a) * 13, y + Math.Sin(a) * 13), 6.5, 5.5);
        }
        dc.DrawEllipse(Brush(0xFF3A3A30), null, new Point(x, y), 9, 9);
        using (Turned(dc, x, y, angle))
        {
            dc.DrawRectangle(Brush(flash ? 0xFFFFFFFF : 0xFF5A5E50), Pen(0xFF202018, 1), new Rect(-6, -6, 12, 12), 3, 3);
            dc.DrawRectangle(Brush(0xFF2A2A26), null, new Rect(-2, -22, 4, 17));
        }
    }

    /// <summary>A pickup lying on the ground: a box of bullets or grenades, a can of gas, or a letter on a medal.</summary>
    private static void DrawPickup(DrawingContext dc, PickupKind kind, double x, double y, bool blink)
    {
        if (blink) return;
        dc.DrawEllipse(Brush(0x40000000), null, new Point(x + 2, y + 10), 13, 5);
        switch (kind)
        {
            case PickupKind.Bullets:
            case PickupKind.Grenades:
                dc.DrawRectangle(Brush(0xFF6A7A3A), Pen(0xFF2A3010, 1.5), new Rect(x - 13, y - 9, 26, 18), 2, 2);
                dc.FillRectangle(Brush(0xFF4A5A2A), new Rect(x - 13, y - 9, 26, 5));
                if (kind == PickupKind.Bullets)
                    for (int k = 0; k < 4; k++)
                    {
                        dc.FillRectangle(Brush(0xFFE0B040), new Rect(x - 8 + k * 5, y - 3, 3, 9));
                        dc.FillRectangle(Brush(0xFFB08030), new Rect(x - 8 + k * 5, y - 5, 3, 3));
                    }
                else
                    foreach (int k in new[] { -1, 1 })
                    {
                        dc.DrawEllipse(Brush(0xFF3A5A2A), Pen(0xFF101010, 1), new Point(x + k * 6, y + 2), 4.5, 5);
                        dc.DrawLine(Pen(0xFFC0C0B0, 1.2), new Point(x + k * 6, y - 3), new Point(x + k * 6 + 2, y - 5));
                    }
                break;
            case PickupKind.Fuel:
                dc.DrawRectangle(Brush(0xFFC03020), Pen(0xFF501008, 1.5), new Rect(x - 9, y - 12, 18, 23), 3, 3);
                dc.DrawLine(Pen(0xFF701810, 2), new Point(x - 6, y - 8), new Point(x + 6, y + 7));
                dc.DrawLine(Pen(0xFF701810, 2), new Point(x + 6, y - 8), new Point(x - 6, y + 7));
                dc.FillRectangle(Brush(0xFF303030), new Rect(x + 2, y - 16, 5, 5));
                break;
            default:
            {
                string letter = kind switch { PickupKind.Speed => "S", PickupKind.Range => "F", PickupKind.Big => "B", _ => "K" };
                uint face = kind == PickupKind.KillAll ? 0xFFE04040 : 0xFFE0B83A;
                dc.DrawEllipse(Brush(Shade(face, -0.4)), null, new Point(x, y + 1.5), 13, 13);
                dc.DrawEllipse(Brush(face), Pen(Shade(face, 0.5), 1.5), new Point(x, y), 12, 12);
                Text(dc, letter, new Point(x, y - 1), 26, 0xFF201808, center: true, shadow: false);
                break;
            }
        }
    }

    // ───────────────────────────── tanks and the gunship ─────────────────────────────

    /// <summary>
    /// A tank seen from above, the hull along one screen angle and the gun along another: tracks with their links
    /// rolling, a hull with its engine deck, and the turret, with its hatch open and the driver's head in it.
    /// </summary>
    private static void DrawTankBody(DrawingContext dc, double x, double y, double body, double turret, double tread, uint hull, int? driver, bool flash, double k = 1)
    {
        dc.DrawEllipse(Brush(0x48000000), null, new Point(x + 5 * k, y + 7 * k), 27 * k, 25 * k);
        using (Turned(dc, x, y, body))
        {
            foreach (int side in new[] { -1, 1 })
            {
                var track = new Rect(side * 15 * k - 6 * k, -24 * k, 12 * k, 48 * k);
                dc.DrawRectangle(Brush(0xFF2A2A24), Pen(0xFF101010, 1), track, 3 * k, 3 * k);
                double roll = tread * 40 % (6 * k);
                for (double ty = -24 * k + roll; ty < 24 * k; ty += 6 * k) dc.DrawLine(Pen(0xFF5A5A50, 1.5 * k), new Point(track.Left + 1, ty), new Point(track.Right - 1, ty));
            }
            dc.DrawRectangle(Brush(flash ? 0xFFFFFFFF : hull), Pen(Shade(hull, -0.5), 1.5), new Rect(-12 * k, -21 * k, 24 * k, 42 * k), 3 * k, 3 * k);
            dc.FillRectangle(Brush(Shade(hull, -0.2)), new Rect(-10 * k, 11 * k, 20 * k, 8 * k));
            for (int i = 0; i < 3; i++) dc.DrawLine(Pen(Shade(hull, -0.45), 1), new Point(-8 * k, (13 + i * 2.5) * k), new Point(8 * k, (13 + i * 2.5) * k));
            dc.FillRectangle(Brush(Shade(hull, 0.15)), new Rect(-12 * k, -21 * k, 24 * k, 3 * k));
        }
        using (Turned(dc, x, y, turret))
        {
            dc.DrawRectangle(Brush(Shade(hull, -0.3)), Pen(0xFF101010, 1), new Rect(-2.5 * k, -32 * k, 5 * k, 24 * k));
            dc.FillRectangle(Brush(0xFF101010), new Rect(-3.2 * k, -33 * k, 6.4 * k, 3 * k));
            dc.DrawEllipse(Brush(flash ? 0xFFFFFFFF : Shade(hull, 0.1)), Pen(Shade(hull, -0.5), 1.5), new Point(0, 0), 11 * k, 12 * k);
        }
        if (driver is { } d)
        {
            dc.DrawEllipse(Brush(0xFF1A1A14), null, new Point(x, y + 2 * k), 5.5 * k, 5.5 * k);
            dc.DrawEllipse(Brush(Skin), Pen(0xC0101010, 1), new Point(x, y + 1 * k), 4.2 * k, 4.2 * k);
            dc.DrawEllipse(Brush(d == 0 ? 0xFF2A1A10u : 0xFFD8B050u), null, new Point(x, y), 4.2 * k, 3 * k);
            dc.DrawLine(Pen(Bandana[d], 1.8 * k), new Point(x - 4 * k, y + 1 * k), new Point(x + 4 * k, y + 1 * k));
        }
        else dc.DrawEllipse(Brush(Shade(hull, -0.25)), Pen(Shade(hull, -0.55), 1), new Point(x, y + 1 * k), 5 * k, 5 * k);
    }

    private void DrawTank(DrawingContext dc, Tank t)
    {
        double x = t.X, y = Sy(t.Y);
        bool burning = t.Doomed && (int)(_clock * 10) % 2 == 0;
        DrawTankBody(dc, x, y, -Field.AngleOf(t.Body), -Field.AngleOf(t.Turret), t.Tread, 0xFF5E7A34, t.Driver?.Index, burning, 1.2);
        if (t.Doomed || t.Damage > 50)
        {
            // Smoke from the engine deck.
            for (int k = 0; k < 4; k++)
            {
                double age = (_clock * 1.6 + k * 0.25) % 1;
                dc.DrawEllipse(Brush(Alpha(0xFF303030, 0.55 * (1 - age))), null, new Point(x + Math.Sin(k * 2.1 + _clock) * 6, y - age * 40), 5 + age * 10, 5 + age * 10);
            }
        }
        if (t.Driver == null && !t.Doomed && t.Fuel > 0 && t.Y > Engine.Camera && (int)(_clock * 2.5) % 2 == 0)
            Text(dc, "TANK", new Point(x, y - 46), 20, 0xFF8AFF8A, center: true);
        if (t.Driver != null)
        {
            // Its own fuel gauge, under it.
            double f = t.Fuel / Tank.MaxFuel;
            dc.FillRectangle(Brush(0xC0101010), new Rect(x - 22, y + 36, 44, 6));
            dc.FillRectangle(Brush(f > 0.2 ? 0xFF6AD04A : Red), new Rect(x - 21, y + 37, 42 * f, 4));
        }
    }

    private static void DrawEnemyTank(DrawingContext dc, double x, double y, double turret, double tread, bool flash)
    {
        DrawTankBody(dc, x, y, Math.PI / 2, turret, tread, 0xFF6E6A5E, null, flash, 1.28);
        // The enemy's mark on its turret.
        dc.DrawEllipse(Brush(0xFFE0E0D8), Pen(0xFF202020, 1), new Point(x, y), 5, 5);
        dc.DrawEllipse(Brush(0xFFB02020), null, new Point(x, y), 2.5, 2.5);
    }

    /// <summary>A gunship from above, nose down the map: its shadow on the ground below it, the body, and the rotor turning.</summary>
    private void DrawHelicopter(DrawingContext dc, double x, double y, double tilt, bool flash, bool shadowOnly = false, double k = 1)
    {
        if (shadowOnly)
        {
            dc.DrawEllipse(Brush(0x40000000), null, new Point(x + 26 * k, y + 64 * k), 14 * k, 26 * k);
            dc.DrawLine(Pen(0x40000000, 5 * k), new Point(x + 26 * k, y + 40 * k), new Point(x + 26 * k, y + 22 * k));
            return;
        }
        using (dc.PushTransform(Matrix.CreateRotation(tilt * 0.25) * Matrix.CreateTranslation(x, y)))
        {
            uint body = flash ? 0xFFFFFFFF : 0xFF4E5E48;
            // The tail boom, up the screen, with its fin and tail rotor.
            dc.DrawRectangle(Brush(Shade(body, -0.15)), Pen(0xFF1A2018, 1), new Rect(-3.5 * k, -48 * k, 7 * k, 34 * k), 2, 2);
            dc.DrawRectangle(Brush(body), Pen(0xFF1A2018, 1), new Rect(-11 * k, -50 * k, 22 * k, 5 * k), 2, 2);
            double tr = _clock * 40;
            dc.DrawLine(Pen(0xC0202020, 2 * k), new Point(9 * k + Math.Cos(tr) * 1, -48 * k + Math.Sin(tr) * 7 * k), new Point(9 * k - Math.Cos(tr), -48 * k - Math.Sin(tr) * 7 * k));
            // Stub wings with rocket pods.
            dc.DrawRectangle(Brush(Shade(body, -0.2)), Pen(0xFF1A2018, 1), new Rect(-22 * k, -2 * k, 44 * k, 6 * k), 2, 2);
            foreach (int s in new[] { -1, 1 }) dc.DrawRectangle(Brush(0xFF2A2E28), null, new Rect(s * 20 * k - 3 * k, -5 * k, 6 * k, 13 * k), 2, 2);
            // The body, the cockpit glass at the nose (down the screen).
            dc.DrawEllipse(Brush(body), Pen(0xFF1A2018, 1.5), new Point(0, 2 * k), 12 * k, 22 * k);
            dc.DrawEllipse(Brush(0xFF8AC0D0), Pen(0xFF1A2018, 1), new Point(0, 15 * k), 7 * k, 8 * k);
            dc.DrawEllipse(Brush(0x80FFFFFF), null, new Point(-2 * k, 12 * k), 2.5 * k, 3 * k);
            dc.DrawLine(Pen(0xFF202020, 2.5 * k), new Point(0, 22 * k), new Point(0, 28 * k));
        }
        // The rotor: two long blades, a blur.
        double a = _clock * 23;
        for (int b = 0; b < 2; b++)
        {
            double ba = a + b * Math.PI / 2;
            dc.DrawLine(Pen(0x90101010, 3 * k), new Point(x - Math.Cos(ba) * 46 * k, y - Math.Sin(ba) * 46 * k), new Point(x + Math.Cos(ba) * 46 * k, y + Math.Sin(ba) * 46 * k));
        }
        dc.DrawEllipse(null, Pen(0x22FFFFFF, 1.5), new Point(x, y), 46 * k, 46 * k);
        dc.DrawEllipse(Brush(0xFF202020), null, new Point(x, y), 3.5 * k, 3.5 * k);
    }

    // ───────────────────────────── the men ─────────────────────────────

    private void DrawWarrior(DrawingContext dc, Warrior p)
    {
        if (!p.Fighting || p.Tank != null) return;
        if (Engine.Phase == Phase.Finale && p.Y > Engine.Stage.GateY + Field.Cell * 1.2) return;
        if (p.Safe > 0 && (int)(_clock * 10) % 2 == 0) return;
        DrawPerson(dc, p.X, Sy(p.Y), -Field.AngleOf(p.Facing), p.Walk, p.Index == 0 ? Look.Red : Look.Blue, 1.6, wading: p.Wading, low: p.Trench);
        // His aim, as the rotary stick shows it: a little sight a way out along it.
        var (ux, uy) = Field.Unit(p.Facing);
        var sight = new Point(p.X + ux * 40, Sy(p.Y) - 2 - uy * 40);
        dc.DrawEllipse(null, Pen(Alpha(Bandana[p.Index], 0.75), 1.5), sight, 4, 4);
        dc.DrawEllipse(Brush(Alpha(Bandana[p.Index], 0.9)), null, sight, 1.3, 1.3);
    }

    /// <summary>Who a figure is, for how he's dressed and what he carries.</summary>
    private enum Look { Red, Blue, Soldier, Grenadier }

    /// <summary>
    /// A man as an arcade sprite draws him, seen from a little above: boots and legs striding as he runs, a body
    /// with a belt, arms holding his rifle out the way he faces, and his head (a face when he's turned our way, the
    /// back of his head when he's turned away). The commandos are stripped to the waist, a bandolier across the
    /// chest and a bandana round the head; the enemy are in uniform and helmets. <paramref name="facing"/> is an angle
    /// on the screen, so up the screen is -π/2; (x, y) is the middle of him, with his feet a little below. Wading,
    /// the water's at his waist; in a trench, only his top half shows.
    /// </summary>
    private static void DrawPerson(DrawingContext dc, double x, double y, double facing, double walk, Look look, double k, bool aiming = false, bool wading = false, bool low = false)
    {
        double fx = Math.Cos(facing), fy = Math.Sin(facing);
        bool away = fy < -0.35, side = Math.Abs(fx) > 0.5, commando = look is Look.Red or Look.Blue;
        var (shirt, trousers, arms) = look switch
        {
            Look.Red => (Skin, 0xFF4E6232u, Skin),
            Look.Blue => (Skin, 0xFF6A6044u, Skin),
            Look.Grenadier => (0xFF8A6E4Au, 0xFF5E4A30u, 0xFF8A6E4Au),
            _ => (0xFF5A6A88u, 0xFF3A4A66u, 0xFF5A6A88u),
        };
        var dark = Pen(0xC0101010, 1);
        bool legs = !wading && !low;
        if (legs) dc.DrawEllipse(Brush(0x45000000), null, new Point(x + 1, y + 12 * k), 8.5 * k, 3.4 * k);
        // The legs, stepping: forward and back when he's side on, up and down when he's facing us or away.
        if (legs)
        {
            double phase = Math.Sin(walk * 13);
            foreach (int leg in new[] { -1, 1 })
            {
                double hx = x + leg * 2.6 * k, hy = y + 3 * k, footX, footY;
                if (side)
                {
                    footX = hx + Math.Sign(fx) * leg * phase * 4 * k;
                    footY = y + 11 * k - (leg * phase > 0 ? 1.2 * k : 0);
                }
                else
                {
                    footX = hx;
                    footY = y + 11 * k - Math.Max(0, leg * phase) * 2.6 * k;
                }
                dc.DrawLine(Pen(trousers, 3.6 * k), new Point(hx, hy), new Point(footX, footY));
                dc.DrawEllipse(Brush(0xFF2A2018), null, new Point(footX + (side ? fx * 0.8 * k : 0), footY + 0.6 * k), 2.4 * k, 1.6 * k);
            }
        }
        // Where his hands hold the gun, and the gun itself: behind him when he's facing away.
        double gx = x + fx * 5 * k, gy = y - 1 * k + fy * 3.5 * k;
        void Rifle()
        {
            var back = new Point(gx - fx * 5 * k, gy - fy * 5 * k + 1.5 * k);
            var muzzle = new Point(gx + fx * (aiming ? 15 : 13) * k, gy + fy * (aiming ? 15 : 13) * k);
            dc.DrawLine(Pen(commando ? 0xFF2A2A2Au : 0xFF5A3A1Au, 3 * k), back, new Point(gx, gy));
            dc.DrawLine(Pen(0xFF1A1A1A, 2.2 * k), new Point(gx, gy), muzzle);
            if (commando) dc.DrawLine(Pen(0xFF3A3A3A, 1.6 * k), new Point(gx + fx * 2 * k, gy + fy * 2 * k + 1.6 * k), new Point(gx + fx * 4 * k, gy + fy * 4 * k + 3 * k));
        }
        if (away) Rifle();
        // The body: a bare, broad chest with a bandolier for a commando; a tunic and belt for the enemy.
        var body = new Rect(x - 5.4 * k, y - 6.8 * k, 10.8 * k, 10.8 * k);
        dc.DrawRectangle(Brush(shirt), dark, body, 3.2 * k, 3.2 * k);
        dc.FillRectangle(Brush(0xFF2A2418), new Rect(x - 5.4 * k, y + 1.4 * k, 10.8 * k, 1.8 * k));
        if (commando)
        {
            if (!away) dc.DrawLine(Pen(0x40000000, 1 * k), new Point(x, y - 5 * k), new Point(x, y - 1 * k));
            dc.DrawLine(Pen(0xFF7A5A2A, 1.8 * k), new Point(x - 4.6 * k, y - 6 * k), new Point(x + 4.4 * k, y + 1.2 * k));
            for (int i = 0; i < 3; i++) dc.DrawEllipse(Brush(0xFFE0B040), null, new Point(x - 2.6 * k + i * 2.6 * k, y - 4 * k + i * 1.9 * k), 0.8 * k, 0.8 * k);
        }
        else if (!away) dc.DrawLine(Pen(Alpha(0xFF202020, 0.45), 1.2 * k), new Point(x - 4 * k, y - 5.5 * k), new Point(x + 3.5 * k, y + 1.4 * k));
        // The arms: out to the gun (a grenadier's raised with one ready to throw).
        var left = new Point(x - 5 * k, y - 4.5 * k);
        var right = new Point(x + 5 * k, y - 4.5 * k);
        if (look == Look.Grenadier && aiming)
        {
            dc.DrawLine(Pen(arms, 2.8 * k), left, new Point(x - 5 * k, y + 1 * k));
            var up = new Point(x + 6 * k, y - 12 * k);
            dc.DrawLine(Pen(arms, 2.8 * k), right, up);
            dc.DrawEllipse(Brush(0xFF3A4A2A), dark, up, 2.2 * k, 2.2 * k);
        }
        else
        {
            dc.DrawLine(Pen(arms, 2.8 * k), left, new Point(gx - fx * 2 * k, gy - fy * 2 * k + 0.5 * k));
            dc.DrawLine(Pen(arms, 2.8 * k), right, new Point(gx, gy));
            dc.DrawEllipse(Brush(Skin), null, new Point(gx, gy), 1.4 * k, 1.4 * k);
        }
        if (!away) Rifle();
        // The head.
        var head = new Point(x + fx * 0.6 * k, y - 10 * k);
        dc.DrawEllipse(Brush(Skin), dark, head, 4.2 * k, 4.2 * k);
        if (commando)
        {
            // Hair (dark for player 1, fair for player 2), and the bandana with its tails streaming out behind.
            uint hair = look == Look.Red ? 0xFF2A1A10u : 0xFFD8B050u, band = look == Look.Red ? 0xFFE0302Au : 0xFF2A70E8u;
            dc.DrawEllipse(Brush(hair), null, new Point(head.X, head.Y - (away ? 0.8 : 2) * k), 4.3 * k, (away ? 3.6 : 2.4) * k);
            dc.DrawLine(Pen(band, 1.9 * k), new Point(head.X - 4.2 * k, head.Y - 1.2 * k), new Point(head.X + 4.2 * k, head.Y - 1.2 * k));
            double wave = Math.Sin(walk * 9) * 1.5 * k, tx = side ? -Math.Sign(fx) : 0.6, ty = side ? 0 : 1;
            var knot = new Point(head.X + tx * 3.8 * k, head.Y - 1.2 * k);
            dc.DrawLine(Pen(band, 1.5 * k), knot, new Point(knot.X + tx * 7 * k, knot.Y + ty * 4 * k + wave));
            dc.DrawLine(Pen(band, 1.5 * k), knot, new Point(knot.X + tx * 6 * k, knot.Y + ty * 5 * k + 2 * k - wave));
        }
        else
        {
            uint helmet = look == Look.Grenadier ? 0xFF5A4A30u : 0xFF46566Au;
            dc.DrawEllipse(Brush(helmet), dark, new Point(head.X, head.Y - 1.8 * k), 5.3 * k, (away ? 4.4 : 3.6) * k);
            dc.DrawLine(Pen(Alpha(0xFF000000, 0.5), 1 * k), new Point(head.X - 5.3 * k, head.Y - 0.6 * k), new Point(head.X + 5.3 * k, head.Y - 0.6 * k));
        }
        if (!away)
        {
            // The eyes: two looking at us, or one in profile.
            if (side) dc.DrawEllipse(Brush(0xFF101010), null, new Point(head.X + fx * 2.6 * k, head.Y + 0.6 * k), 0.8 * k, 0.8 * k);
            else
                foreach (int e in new[] { -1, 1 })
                    dc.DrawEllipse(Brush(0xFF101010), null, new Point(head.X + e * 1.6 * k + fx * k, head.Y + 0.6 * k), 0.75 * k, 0.75 * k);
        }
        if (wading)
        {
            // The river at his waist: the water over his legs, and a ring of ripples round him.
            dc.DrawEllipse(Brush(0xE02A6488), null, new Point(x, y + 6 * k), 9 * k, 5 * k);
            dc.DrawEllipse(null, Pen(0xC0B8E8FF, 1.4), new Point(x, y + 3.5 * k), 8.5 * k + Math.Sin(walk * 8) * k, 3.2 * k);
        }
        else if (low)
        {
            // In a trench: the earth hides him from the waist down, and the near lip of it runs in front of him.
            dc.FillRectangle(Brush(0xFF3A2A1A), new Rect(x - 11 * k, y + 1 * k, 22 * k, 7 * k));
            dc.FillRectangle(Brush(0xFF2A1E12), new Rect(x - 11 * k, y + 1 * k, 22 * k, 2.4 * k));
        }
    }

    private static void DrawSoldier(DrawingContext dc, Soldier s, Func<double, double> sy)
    {
        double x = s.X, y = sy(s.Y), f = -s.Facing;
        if (s.Kind == SoldierKind.Trench && !s.Up)
        {
            // Down in the trench: only the top of his helmet shows.
            dc.DrawEllipse(Brush(0xFF46566A), Pen(0xC0101010, 1), new Point(x, y + 3), 6.5, 3.4);
            return;
        }
        var look = s.Kind == SoldierKind.Grenadier ? Look.Grenadier : Look.Soldier;
        DrawPerson(dc, x, y - (s.Kind == SoldierKind.Trench ? 4 : 0), f, s.Walk, look, 1.55, aiming: s.Aiming, low: s.Kind == SoldierKind.Trench);
        // A soldier about to fire: a glint at the muzzle, as warning.
        if (s.Aiming && look == Look.Soldier)
            dc.DrawEllipse(Brush(0xC0FFF0A0), null, new Point(x + Math.Cos(f) * 22, y - 1 + Math.Sin(f) * 21), 2.8, 2.8);
    }

    private void DrawSoldier(DrawingContext dc, Soldier s) => DrawSoldier(dc, s, Sy);

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, Sy(e.Y));
        if (e.Kind != EffectKind.Score && e.Kind != EffectKind.Word && !InGame) return;
        switch (e.Kind)
        {
            case EffectKind.Blast:
            {
                // A scorch on the ground, a ring of dust thrown out, smoke rising, and the fireball in its lobes.
                double r = (24 + 44 * Math.Sqrt(t)) * e.Size;
                dc.DrawEllipse(Brush(Alpha(0xFF201810, 0.45 * (1 - t))), null, new Point(c.X, c.Y + 4), r * 0.9, r * 0.55);
                dc.DrawEllipse(null, Pen(Alpha(0xFFE0D0B0, 0.6 * (1 - t)), 3 * e.Size), c, r * 1.3, r * 0.85);
                for (int k = 0; k < 3; k++)
                    dc.DrawEllipse(Brush(Alpha(0xFF4A4440, 0.5 * (1 - t))), null, new Point(c.X + (k - 1) * r * 0.45, c.Y - t * 34 * e.Size - r * 0.2), r * 0.6, r * 0.55);
                if (t < 0.8)
                {
                    double f = 1 - t / 0.8;
                    for (int k = 0; k < 6; k++)
                    {
                        double a = k * Math.PI / 3 + e.X;
                        dc.DrawEllipse(Brush(Alpha(0xFFE0501A, f)), null, new Point(c.X + Math.Cos(a) * r * 0.45, c.Y + Math.Sin(a) * r * 0.34), r * 0.46, r * 0.42);
                    }
                    dc.DrawEllipse(Brush(Alpha(0xFFFF9A2A, f)), null, c, r * 0.62, r * 0.54);
                    dc.DrawEllipse(Brush(Alpha(0xFFFFD050, f)), null, c, r * 0.44, r * 0.38);
                    dc.DrawEllipse(Brush(Alpha(0xFFFFFFE0, f * f)), null, c, r * 0.24, r * 0.21);
                }
                break;
            }
            case EffectKind.Splash:
            {
                double r = (10 + 30 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(0xFFD0F0FF, 1 - t), 2), c, r, r * 0.55);
                dc.DrawEllipse(Brush(Alpha(0xFFE0F8FF, 0.7 * (1 - t))), null, new Point(c.X, c.Y - t * 10 * e.Size), r * 0.4, r * 0.5);
                break;
            }
            case EffectKind.Flash:
                if (e.Size > 5) dc.FillRectangle(Brush(Alpha(0xFFFFFFFF, 0.7 * (1 - t))), new Rect(0, Field.Hud, Field.Width, Field.View));
                else dc.DrawEllipse(Brush(Alpha(0xFFFFF0A0, 1 - t)), null, c, 9, 9);
                break;
            case EffectKind.Puff:
                dc.DrawEllipse(Brush(Alpha(e.Colour == 0xFFFFFFFF ? 0xFFE0D0B0 : e.Colour, 0.7 * (1 - t))), null, c, 3 + 6 * t, 3 + 6 * t);
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 22, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 28, e.Colour, center: true);
                break;
        }
    }
}
