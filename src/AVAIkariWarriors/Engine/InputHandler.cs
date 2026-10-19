using Avalonia.Input;

namespace AVAIkariWarriors.Engine;

/// <summary>
/// Keyboard and gamepads boiled down to each commando's controls. Both players share the keyboard, on keys that
/// never overlap: player 1 runs with W A S D, swings the aim with Q and E, fires with Space (or F) and throws with
/// Left Shift (or G); player 2 runs with the arrow keys (or the number pad's 8 4 6 2), swings the aim with U and I
/// (or 7 and 9), fires with J (or 0) and throws with K (or Right Shift, or the pad's point). Each gamepad is a
/// commando's too (the first player 1's, the second player 2's): the left stick or D-pad runs, the right stick aims
/// (and fires), A or the right trigger fires, B or the left trigger throws, the bumpers swing the aim, and Y gets out
/// of a tank. Up is +1, as in the engine.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly Dictionary<PhysicalKey, Key> _pressedAs = [];
    private readonly ControllerManager[] _pads = [new(0), new(1)];

    public void OnKeyDown(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        if (physical != PhysicalKey.None)
        {
            if (_pressedAs.TryGetValue(physical, out var was) && was != key) _held.Remove(was);
            _pressedAs[physical] = key;
        }
        _held.Remove(key);
        _held.Add(key);
    }

    /// <summary>A key comes up. The key it went down as is let go too: with the keyboard option
    /// shift:both_capslock_cancel, letting go of a Shift arrives as CapsLock, and the Shift would stay held.</summary>
    public void OnKeyUp(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        _held.Remove(key);
        if (physical != PhysicalKey.None && _pressedAs.Remove(physical, out var was)) _held.Remove(was);
    }

    public bool Has(Key key) => _held.Contains(key);

    public void Update()
    {
        foreach (var pad in _pads) pad.Update();
    }

    /// <summary>One player's keys.</summary>
    private sealed record Keys(Key[] Left, Key[] Right, Key[] Up, Key[] Down, Key[] TurnLeft, Key[] TurnRight, Key[] Fire, Key[] Grenade);

    private static readonly Keys[] Layout =
    [
        new([Key.A], [Key.D], [Key.W], [Key.S], [Key.Q], [Key.E], [Key.Space, Key.F], [Key.LeftShift, Key.G]),
        new([Key.Left, Key.NumPad4], [Key.Right, Key.NumPad6], [Key.Up, Key.NumPad8], [Key.Down, Key.NumPad2],
            [Key.U, Key.NumPad7], [Key.I, Key.NumPad9], [Key.J, Key.NumPad0], [Key.K, Key.RightShift, Key.Decimal]),
    ];

    /// <summary>The last-pressed of the keys held, as -1 or +1 (0 for neither).</summary>
    private int Axis(Key[] minus, Key[] plus)
    {
        for (int i = _held.Count - 1; i >= 0; i--)
        {
            if (minus.Contains(_held[i])) return -1;
            if (plus.Contains(_held[i])) return 1;
        }
        return 0;
    }

    private bool Any(Key[] keys) => keys.Any(_held.Contains);

    /// <summary>A stick's eight ways from a gamepad's analogue position (screen y down, so up the map is +1).</summary>
    private static (int, int) Stick(float x, float y, float dead = 0.4f)
    {
        if (x * x + y * y < dead * dead) return (0, 0);
        double a = Math.Atan2(-y, x);
        int octant = (int)Math.Round(a / (Math.PI / 4));
        return ((int)Math.Round(Math.Cos(octant * Math.PI / 4)), (int)Math.Round(Math.Sin(octant * Math.PI / 4)));
    }

    /// <summary>Player 0's or 1's controls, from their keys and their gamepad.</summary>
    public Controls For(int player)
    {
        var k = Layout[player];
        var pad = _pads[player];
        int dx = Axis(k.Left, k.Right), dy = Axis(k.Down, k.Up);
        if (dx == 0 && dy == 0)
        {
            (dx, dy) = Stick(pad.LeftStickX, pad.LeftStickY);
            if (dx == 0 && dy == 0) (dx, dy) = (pad.DPadLeft ? -1 : pad.DPadRight ? 1 : 0, pad.DPadUp ? 1 : pad.DPadDown ? -1 : 0);
        }
        int turn = Axis(k.TurnLeft, k.TurnRight);
        if (turn == 0) turn = pad.LeftBumper ? -1 : pad.RightBumper ? 1 : 0;
        var (ax, ay) = Stick(pad.RightStickX, pad.RightStickY, 0.5f);
        bool fire = Any(k.Fire) || pad.ButtonA || pad.RightTrigger > 0.5f || ax != 0 || ay != 0 || pad.ButtonY;
        bool grenade = Any(k.Grenade) || pad.ButtonB || pad.LeftTrigger > 0.5f || pad.ButtonY;
        return new Controls(dx, dy, fire, grenade, turn, ax, ay);
    }

    public Controls Player1 => For(0);
    public Controls Player2 => For(1);

    /// <summary>Player 1's start: Enter or 1, or the first pad's Start.</summary>
    public bool Start1 => Has(Key.Enter) || Has(Key.D1) || _pads[0].ButtonStart;
    /// <summary>Player 2's start (or join, or carry on): 2, or the second pad's Start.</summary>
    public bool Start2 => Has(Key.D2) || _pads[1].ButtonStart;

    private ControllerManager Pad => _pads[0];
    public bool PadUp => Pad.DPadUp || Pad.LeftStickY < -0.5f;
    public bool PadDown => Pad.DPadDown || Pad.LeftStickY > 0.5f;
    public bool PadLeft => Pad.DPadLeft || Pad.LeftStickX < -0.5f;
    public bool PadRight => Pad.DPadRight || Pad.LeftStickX > 0.5f;
    public bool PadAccept => Pad.ButtonA || Pad.ButtonX || Pad.ButtonB;
    public bool PadDone => Pad.ButtonStart;
    /// <summary>Either pad's Start (to pause, when its player is in the game).</summary>
    public bool PadPause(int player) => _pads[player].ButtonStart;
    public bool PadBack => _pads[0].ButtonBack || _pads[1].ButtonBack;

    public void Dispose()
    {
        foreach (var pad in _pads) pad.Dispose();
    }
}
