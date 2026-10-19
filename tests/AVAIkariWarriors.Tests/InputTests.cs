using Avalonia.Input;
using AVAIkariWarriors.Engine;

namespace AVAIkariWarriors.Tests;

/// <summary>The keys, through the InputHandler, as the game reads them.</summary>
public sealed class InputTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "avaikariwarriors-keys-" + Guid.NewGuid().ToString("N"));
    private readonly InputHandler _input = new();

    public void Dispose()
    {
        _input.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private void Down(params Key[] keys)
    {
        foreach (var k in keys) _input.OnKeyDown(k);
    }

    private void Up(params Key[] keys)
    {
        foreach (var k in keys) _input.OnKeyUp(k);
    }

    [Fact]
    public void Player_one_runs_with_WASD_turns_with_Q_and_E_fires_with_Space_and_throws_with_Shift()
    {
        Down(Key.W);
        Assert.Equal(new Controls(0, 1), _input.Player1);
        Down(Key.D);
        Assert.Equal(new Controls(1, 1), _input.Player1);
        Down(Key.Q);
        Assert.Equal(-1, _input.Player1.Turn);
        Up(Key.Q);
        Down(Key.E, Key.Space);
        Assert.Equal(new Controls(1, 1, Fire: true, Turn: 1), _input.Player1);
        Up(Key.W, Key.D, Key.E, Key.Space);
        Down(Key.S, Key.A, Key.LeftShift);
        Assert.Equal(new Controls(-1, -1, Grenade: true), _input.Player1);
        Up(Key.S, Key.A, Key.LeftShift);
        Down(Key.F, Key.G);
        Assert.Equal(new Controls(Fire: true, Grenade: true), _input.Player1);
        // None of it is player 2's.
        Assert.Equal(default, _input.Player2);
    }

    [Fact]
    public void Player_two_runs_with_the_arrows_turns_with_U_and_I_fires_with_J_and_throws_with_K()
    {
        Down(Key.Up, Key.Right);
        Assert.Equal(new Controls(1, 1), _input.Player2);
        Down(Key.U);
        Assert.Equal(-1, _input.Player2.Turn);
        Up(Key.U);
        Down(Key.I, Key.J);
        Assert.Equal(new Controls(1, 1, Fire: true, Turn: 1), _input.Player2);
        Up(Key.Up, Key.Right, Key.I, Key.J);
        Down(Key.Down, Key.Left, Key.K);
        Assert.Equal(new Controls(-1, -1, Grenade: true), _input.Player2);
        Up(Key.Down, Key.Left, Key.K);
        // The number pad does as well.
        Down(Key.NumPad8, Key.NumPad4, Key.NumPad9, Key.NumPad0, Key.Decimal);
        Assert.Equal(new Controls(-1, 1, true, true, 1), _input.Player2);
        Up(Key.NumPad8, Key.NumPad4, Key.NumPad9, Key.NumPad0, Key.Decimal);
        Down(Key.RightShift, Key.NumPad7);
        Assert.Equal(new Controls(Grenade: true, Turn: -1), _input.Player2);
        Assert.Equal(default, _input.Player1);
    }

    [Fact]
    public void No_key_belongs_to_both_players()
    {
        Key[] all = Enum.GetValues<Key>().Distinct().Where(k => k is not (Key.None or Key.Enter or Key.D1 or Key.D2)).ToArray();
        foreach (var k in all)
        {
            Down(k);
            bool one = _input.Player1 != default, two = _input.Player2 != default;
            Up(k);
            Assert.False(one && two, $"{k} works for both players");
        }
    }

    [Fact]
    public void The_last_of_two_opposite_keys_wins()
    {
        Down(Key.A, Key.D);
        Assert.Equal(1, _input.Player1.Dx);
        Down(Key.A);
        Assert.Equal(-1, _input.Player1.Dx);
        Up(Key.A);
        Assert.Equal(1, _input.Player1.Dx);
    }

    private void Tick(GameEngine g, int n = 1)
    {
        for (int i = 0; i < n; i++) g.Tick(_input.Player1, _input.Player2, _input.Start1, _input.Start2);
    }

    [Fact]
    public void Up_on_the_keys_is_up_the_screen_in_the_game_for_both_and_the_aim_turns_as_the_keys_say()
    {
        var g = new GameEngine(1, new HighScoreTable(_dir)) { Silent = true, Calm = true };
        g.Go(1, players: 2);
        g.MoveCamera(400);
        var (one, two) = (g.Players[0], g.Players[1]);
        double y1 = one.Y, y2 = two.Y;
        Down(Key.W, Key.Up);
        Tick(g, 20);
        Assert.True(one.Y > y1 + 1 && two.Y > y2 + 1, "W and the up arrow run up the map");
        Assert.Equal((0, 0), (one.Facing, two.Facing));
        Up(Key.W, Key.Up);
        // Space held: the aim stays up while he runs down; E swings it a step clockwise.
        Down(Key.S, Key.Space);
        Tick(g, 10);
        Assert.Equal(0, one.Facing);
        Assert.All(g.Bullets.Where(b => b.Owner == 0), b => Assert.True(b.Vy > 0));
        Down(Key.E);
        Tick(g);
        Assert.Equal(1, one.Facing);
        Up(Key.S, Key.Space, Key.E);
        Down(Key.U);
        Tick(g);
        Assert.Equal(7, two.Facing);
        Up(Key.U);
    }

    [Fact]
    public void One_and_two_start_a_game_for_one_or_two_and_two_joins_one_under_way()
    {
        var g = new GameEngine(1, new HighScoreTable(_dir)) { Silent = true };
        g.Idle(2);
        Down(Key.D2);
        Tick(g);
        Assert.Equal(GameState.Playing, g.State);
        Assert.True(g.Players[0].Joined && g.Players[1].Joined);
        Up(Key.D2);

        g = new GameEngine(1, new HighScoreTable(_dir)) { Silent = true };
        g.Idle(2);
        Down(Key.Enter);
        Tick(g);
        Assert.Equal(GameState.Playing, g.State);
        Assert.False(g.Players[1].Joined);
        Up(Key.Enter);
        Tick(g, 30);
        Down(Key.D2);
        Tick(g);
        Assert.True(g.Players[1].Fighting);
        Assert.True(_input.Start2);
        Assert.False(_input.Start1);
    }

    [Fact]
    public void A_key_let_go_under_another_name_is_let_go()
    {
        _input.OnKeyDown(Key.W, PhysicalKey.W);
        Assert.Equal(1, _input.Player1.Dy);
        // Its release reported as some other key: the physical key still lets it go.
        _input.OnKeyUp(Key.None, PhysicalKey.W);
        Assert.Equal(0, _input.Player1.Dy);
        // Player 2's grenade on the right Shift, let go as CapsLock.
        _input.OnKeyDown(Key.RightShift, PhysicalKey.ShiftRight);
        Assert.True(_input.Player2.Grenade);
        _input.OnKeyUp(Key.CapsLock, PhysicalKey.ShiftRight);
        Assert.False(_input.Player2.Grenade);
    }
}
