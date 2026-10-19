using Avalonia;
using Avalonia.Media;
using AVAIkariWarriors.Engine;

namespace AVAIkariWarriors.Views;

public sealed partial class GameCanvas
{
    // ───────────────────────────── the score bar ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xF0101410), new Rect(0, 0, Field.Width, Field.Hud));
        dc.FillRectangle(Brush(0xFF5A6A3A), new Rect(0, Field.Hud - 2, Field.Width, 2));
        Text(dc, "HI", new Point(Field.Width / 2 - 60, 18), 20, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.TopScore).ToString("D7"), new Point(Field.Width / 2 - 52, 18), 28, 0xFFE0D8B0);
        if (InGame)
        {
            string where = (g.Stage.Round > 1 ? $"ROUND {g.Stage.Round}  " : "") + $"STAGE {g.Stage.Theme + 1}  {g.Stage.Name}";
            Text(dc, where, new Point(Field.Width / 2, 46), 22, 0xFFB0D080, center: true);
        }
        foreach (var p in g.Players) DrawPlayerPanel(dc, p);
    }

    /// <summary>A commando's corner of the score bar: his score, his lives, bullets and grenades, his letters, and a tank's fuel.</summary>
    private void DrawPlayerPanel(DrawingContext dc, Warrior p)
    {
        var g = Engine;
        bool left = p.Index == 0;
        double edge = left ? 14 : Field.Width - 14;
        double X(double offset) => left ? edge + offset : edge - offset;
        bool blink = (int)(_clock * 2.5) % 2 == 0;
        Text(dc, left ? "1UP" : "2UP", new Point(X(0), 18), 22, PlayerText[p.Index], right: !left);
        if (!p.Joined || !InGame)
        {
            if (p.Joined || p.Score > 0) Text(dc, p.Score.ToString("D7"), new Point(X(44), 18), 28, White, right: !left);
            if (InGame && g.State != GameState.Attract && blink) Text(dc, $"PRESS {p.Index + 1} TO JOIN", new Point(X(0), 46), 22, PlayerText[p.Index], right: !left);
            return;
        }
        Text(dc, p.Score.ToString("D7"), new Point(X(44), 18), 28, White, right: !left);
        if (p.Out)
        {
            if (p.Continue > 0 && blink) Text(dc, $"CONTINUE? {Math.Ceiling(p.Continue)} - PRESS {p.Index + 1}", new Point(X(0), 46), 22, Yellow, right: !left);
            else if (p.Continue <= 0) Text(dc, g.State == GameState.Attract ? "" : $"PRESS {p.Index + 1} TO JOIN", new Point(X(0), 46), 22, PlayerText[p.Index], right: !left);
            return;
        }
        // Lives (spare heads, in his bandana).
        int spare = Math.Max(0, p.Lives - (p.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 4); i++)
        {
            var at = new Point(X(6 + i * 15), 46);
            dc.DrawEllipse(Brush(Skin), Pen(0xC0101010, 1), at, 5, 5);
            dc.DrawLine(Pen(Bandana[p.Index], 2.4), new Point(at.X - 5, at.Y - 1.5), new Point(at.X + 5, at.Y - 1.5));
        }
        // Bullets and grenades.
        var b = new Point(X(78), 46);
        dc.FillRectangle(Brush(0xFFE0B040), new Rect(b.X - 2, b.Y - 4, 4, 10));
        dc.DrawEllipse(Brush(0xFFB08030), null, new Point(b.X, b.Y - 4), 2, 3);
        Text(dc, p.Ammo.ToString(), new Point(X(88), 46), 24, p.Ammo == 0 ? Red : p.Ammo < 20 && blink ? Yellow : White, right: !left);
        var gr = new Point(X(142), 46);
        dc.DrawEllipse(Brush(0xFF3A5A2A), Pen(0xFF101010, 1), gr, 5, 5.5);
        Text(dc, p.Grenades.ToString(), new Point(X(152), 46), 24, p.Grenades == 0 ? Red : White, right: !left);
        // The letters he's carrying.
        double lx = 198;
        foreach (var (on, letter) in new[] { (p.Fast, "S"), (p.Far, "F"), (p.Big, "B") })
        {
            if (!on) continue;
            var at = new Point(X(lx), 46);
            dc.DrawEllipse(Brush(0xFFE0B83A), null, at, 9, 9);
            Text(dc, letter, new Point(at.X, at.Y - 1), 18, 0xFF201808, center: true, shadow: false);
            lx += 22;
        }
        // In a tank: its fuel.
        if (p.Tank is { } t)
        {
            double f = t.Fuel / Tank.MaxFuel, gx = left ? X(lx + 4) : X(lx + 4) - 100;
            Text(dc, "FUEL", new Point(left ? gx : gx + 100, 32), 16, f < 0.2 && blink ? Red : Dim, right: !left);
            dc.FillRectangle(Brush(0xFF202020), new Rect(gx, 42, 100, 9));
            dc.FillRectangle(Brush(f > 0.2 ? 0xFF6AD04A : Red), new Rect(gx + 1, 43, 98 * f, 7));
            if (t.Damage > 0) dc.FillRectangle(Brush(0xFFFF8A2A), new Rect(gx + 1, 51, 98 * Math.Min(1, t.Damage / 100), 3));
        }
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 270;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 14) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xC0181C10), new Rect(cx - w / 2, cy - 46, w, h), 6);
        dc.DrawRectangle(null, Pen(0xFF8A9A4A, 1.5), new Rect(cx - w / 2, cy - 46, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 44 : 54, msg.Contains("OVER") || msg.Contains("OUT") ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    // ───────────────────────────── the title, the scores and the initials ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF22361A), new Rect(0, Field.Hud, Field.Width, Field.View));
        for (int i = 0; i < 360; i++)
        {
            uint h = Hash(i, 2, 9);
            dc.FillRectangle(Brush(0xFF2C4420), new Rect(h % 1024, Field.Hud + (h >> 10) % 704, 4, 3));
        }
        // The jungle along either side.
        for (int i = 0; i < 14; i++)
            foreach (double side in new[] { 18.0, Field.Width - 18 })
            {
                double y = Field.Hud + 30 + i * 52;
                dc.DrawEllipse(Brush(0xFF163012), null, new Point(side, y), 34, 30);
                dc.DrawEllipse(Brush(0xFF1E4A1A), null, new Point(side - 4, y - 4), 24, 20);
            }
        string title = "IKARI WARRIORS";
        Text(dc, "AVA", new Point(cx, 100), 44, 0xFFB0C090, center: true);
        for (int i = 0; i < title.Length; i++)
        {
            double bob = Math.Sin(_clock * 3 + i * 0.5) * 3;
            double lx = cx - title.Length * 24 + 24 + i * 48;
            Text(dc, title[i].ToString(), new Point(lx + 3, 158 + bob + 3), 100, 0xFF5A1010, center: true, shadow: false);
            Text(dc, title[i].ToString(), new Point(lx, 158 + bob), 100, i < 5 ? 0xFFFF5A3A : 0xFFFFD04A, center: true);
        }
        Text(dc, "AFTER THE 1986 ARCADE CLASSIC", new Point(cx, 214), 26, 0xFFB0C090, center: true);
        // The two of them, back to back, and the tank between.
        DrawTankBody(dc, cx, 296, -Math.PI / 2, -Math.PI / 2 + Math.Sin(_clock * 0.8) * 0.6, _clock, 0xFF5E7A34, null, false, 1.5);
        DrawPerson(dc, cx - 120, 300, -Math.PI / 2 - 0.5 + Math.Sin(_clock) * 0.3, _clock, Look.Red, 3.0);
        DrawPerson(dc, cx + 120, 300, -Math.PI / 2 + 0.5 - Math.Sin(_clock) * 0.3, _clock + 0.4, Look.Blue, 3.0);
        Text(dc, "PLAYER 1", new Point(cx - 120, 360), 22, PlayerText[0], center: true);
        Text(dc, "PLAYER 2", new Point(cx + 120, 360), 22, PlayerText[1], center: true);
        (string Name, string Points, Action<double> Draw)[] legend =
        [
            ("SOLDIER", "100", x => DrawPerson(dc, x, 418, Math.PI / 2, _clock, Look.Soldier, 1.9)),
            ("TRENCH", "200", x =>
            {
                dc.FillRectangle(Brush(0xFF3A2A1A), new Rect(x - 34, 412, 68, 22));
                dc.FillRectangle(Brush(0xFF2A1E12), new Rect(x - 34, 412, 68, 6));
                DrawPerson(dc, x, 416, Math.PI / 2, _clock, Look.Soldier, 1.9, low: true);
            }),
            ("GUN", "500", x => DrawGun(dc, x, 418, Math.PI / 2 + Math.Sin(_clock) * 0.5, false)),
            ("PILLBOX", "1000", x => { using (dc.PushTransform(Matrix.CreateTranslation(-x, -418) * Matrix.CreateScale(0.7, 0.7) * Matrix.CreateTranslation(x, 418))) DrawPillboxAt(dc, x, 418); }),
            ("TANK", "2000", x => DrawEnemyTank(dc, x, 418, Math.PI / 2, _clock, false)),
            ("GUNSHIP", "3000", x => { using (dc.PushTransform(Matrix.CreateTranslation(-x, -414) * Matrix.CreateScale(0.62, 0.62) * Matrix.CreateTranslation(x, 414))) DrawHelicopter(dc, x, 414, 0, false); }),
        ];
        for (int i = 0; i < legend.Length; i++)
        {
            double lx = cx - 375 + i * 150;
            legend[i].Draw(lx);
            Text(dc, legend[i].Name, new Point(lx, 462), 20, 0xFFB0C090, center: true);
            Text(dc, legend[i].Points, new Point(lx, 484), 22, Yellow, center: true);
        }
        // The pickups.
        (PickupKind Kind, string Word)[] items = [(PickupKind.Bullets, "BULLETS"), (PickupKind.Grenades, "GRENADES"), (PickupKind.Fuel, "GAS"), (PickupKind.Speed, "SPEED"), (PickupKind.Range, "FAR SHOT"), (PickupKind.Big, "BIG BLAST"), (PickupKind.KillAll, "KILL ALL")];
        for (int i = 0; i < items.Length; i++)
        {
            double lx = cx - 390 + i * 130;
            DrawPickup(dc, items[i].Kind, lx - 26, 524, false);
            Text(dc, items[i].Word, new Point(lx - 8, 524), 18, 0xFFB0C090);
        }
        Text(dc, "WALK INTO AN EMPTY TANK TO DRIVE IT - BOTH BUTTONS TO GET OUT BEFORE IT BLOWS", new Point(cx, 560), 21, 0xFFB0C090, center: true);
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS 1 OR 2 PLAYERS - FREE PLAY", new Point(cx, 608), 44, Yellow, center: true);
        Text(dc, "P1: WASD RUN  Q/E AIM  SPACE FIRE  SHIFT GRENADE      P2: ARROWS RUN  U/I AIM  J FIRE  K GRENADE", new Point(cx, 664), 20, 0xFFB0C090, center: true);
        Text(dc, $"GAMEPADS: LEFT STICK RUN, RIGHT STICK AIM    P: PAUSE    M: SOUND    EXTRA LIFE EVERY {GameEngine.ExtraLifeEvery}", new Point(cx, 692), 20, 0xFFB0C090, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF1A2812), new Rect(0, Field.Hud, Field.Width, Field.View));
        for (int i = 0; i < 9; i++)
            foreach (double side in new[] { 30.0, Field.Width - 30 })
                dc.DrawEllipse(Brush(0xFF163012), null, new Point(side, Field.Hud + 40 + i * 80), 40, 36);
        Text(dc, "THE TOUGHEST", new Point(cx, 132), 68, 0xFFFF5A3A, center: true);
        Text(dc, "RANK NAME     SCORE   STAGE", new Point(cx, 200), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,9}   {e.Stage,4}", new Point(cx, 246 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS 1 OR 2 TO PLAY", new Point(cx, 708), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF1A2812), new Rect(0, Field.Hud, Field.Width, Field.View));
        int who = Math.Max(0, g.EntryPlayer);
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 190), 64, Yellow, center: true);
        Text(dc, $"PLAYER {who + 1}", new Point(cx, 250), 36, PlayerText[who], center: true);
        Text(dc, g.Players[who].Score.ToString(), new Point(cx, 300), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 370), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 450), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 490, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 570), 26, White, center: true);
    }
}
