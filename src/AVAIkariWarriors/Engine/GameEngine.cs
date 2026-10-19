namespace AVAIkariWarriors.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

/// <summary>Where a stage has got to.</summary>
public enum Phase
{
    /// <summary>The stage's name, before the fighting starts.</summary>
    Start,
    /// <summary>The fight up the map, and at the end of it.</summary>
    Play,
    /// <summary>The stage taken: the bonus, then the next.</summary>
    Clear,
    /// <summary>The fortress gate blown: it opens, they march in, and the war starts over, harder.</summary>
    Finale,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Fire, Empty, Throw, Blast, EnemyFire, EnemyDown, Pickup, Cannon, TankEngine, TankBoom, Clank, Helicopter, Wade,
    Warning, Death, Join, GateOpen, StageStart, StageClear, ExtraLife,
    /// <summary>The drums, looping while the fighting's on.</summary>
    Music, MusicStop,
    TitleTune, StartTune, GameOver,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and both players' controls; it runs
/// in fixed 60 Hz steps, so it plays the same at any frame rate and a seed always plays the same game.
/// </summary>
public sealed partial class GameEngine
{
    public const double TitleIdleSeconds = 15;
    public const double AttractSeconds = 50;
    public const double HighScoreSeconds = 9;
    public const double GameOverSeconds = 3.5;
    public const double ContinueSeconds = 10;
    public const int StartingLives = 3;

    private Random _rng;
    private readonly int _seed;
    private double _accumulator;
    private bool _start1Prev, _start2Prev;
    private bool _titleTunePending = true;
    private bool _music;
    private int _demos;
    private readonly Queue<int> _entries = new();

    public GameEngine(int seed = 1986, HighScoreTable? table = null)
    {
        _seed = seed;
        _rng = new Random(seed);
        HighScores = table ?? HighScoreTable.Load();
        Players = [new Warrior(0), new Warrior(1)];
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    /// <summary>Whose initials are being entered.</summary>
    public int EntryPlayer { get; private set; } = -1;
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }

    /// <summary>The two commandos (the second only there when someone's playing him).</summary>
    public Warrior[] Players { get; }
    /// <summary>The computer at the controls in the demonstration, for either commando (or null).</summary>
    public WarriorPilot?[] Pilots { get; } = new WarriorPilot?[2];
    public IEnumerable<Warrior> Fighting => Players.Where(p => p.Fighting);
    public int TopScore => Players.Max(p => p.Score);

    public List<Effect> Effects { get; } = [];

    /// <summary>A word across the screen, and for how much longer.</summary>
    public string? Message { get; private set; }
    public string? SubMessage { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    private void Say(string text, string? sub = null, double seconds = 2.5)
    {
        Message = text;
        SubMessage = sub;
        MessageTime = seconds;
    }

    public void Update(double dt, Controls p1, Controls p2 = default, bool start1 = false, bool start2 = false)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(p1, p2, start1, start2);
        }
    }

    /// <summary>One 1/60 s step.</summary>
    public void Tick(Controls p1, Controls p2 = default, bool start1 = false, bool start2 = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool one = start1 && !_start1Prev, two = start2 && !_start2Prev;
        _start1Prev = start1;
        _start2Prev = start2;
        foreach (var e in Effects) e.Age += dt;
        Effects.RemoveAll(e => e.Done);
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = SubMessage = null; }

        switch (State)
        {
            case GameState.Title:
                if (_titleTunePending) { _titleTunePending = false; Play(Sfx.TitleTune); }
                if (one || two) StartGame(two ? 2 : 1);
                else if (StateTime > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilots[0]?.Decide(this, 0) ?? default, Pilots[1]?.Decide(this, 1) ?? default);
                if (one || two) { StartGame(two ? 2 : 1); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) ShowHighScores(-1);
                break;
            case GameState.Playing:
                if (one) Join(0);
                if (two) Join(1);
                Simulate(dt, p1, p2);
                break;
            case GameState.GameOver:
                if (StateTime > GameOverSeconds)
                {
                    _entries.Clear();
                    foreach (var p in Players.Where(p => p.Joined).OrderByDescending(p => p.Score)) _entries.Enqueue(p.Index);
                    NewEntryIndex = -1;
                    NextEntry();
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    var p = Players[EntryPlayer];
                    NewEntryIndex = HighScores.Insert(done.Initials, p.Score, Stage.Number);
                    HighScores.Save();
                    NextEntry();
                }
                break;
            case GameState.HighScores:
                if (one || two) StartGame(two ? 2 : 1);
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    /// <summary>The next player whose score makes the table enters initials; then the table.</summary>
    private void NextEntry()
    {
        while (_entries.Count > 0)
        {
            int i = _entries.Dequeue();
            if (!HighScores.Qualifies(Players[i].Score)) continue;
            State = GameState.EnterInitials;
            StateTime = 0;
            EntryPlayer = i;
            Initials = new InitialsEntry(HighScores.LastInitials);
            return;
        }
        Initials = null;
        EntryPlayer = -1;
        ShowHighScores(NewEntryIndex);
    }

    /// <summary>A game for one commando, or both.</summary>
    public void StartGame(int players = 1)
    {
        Pilots[0] = Pilots[1] = null;
        NewGame();
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
        for (int i = 0; i < players; i++) Join(i, quiet: true);
        Play(Sfx.StartTune);
        BeginStage(1);
    }

    /// <summary>The demonstration: the computer plays one commando or both, touring the stages in turn.</summary>
    private void StartAttract()
    {
        NewGame();
        State = GameState.Attract;
        StateTime = 0;
        int demo = _demos++;
        int players = demo % 2 == 0 ? 2 : 1;
        for (int i = 0; i < players; i++)
        {
            Join(i, quiet: true);
            Pilots[i] = new WarriorPilot();
        }
        BeginStage(1 + demo % Stage.Count);
    }

    /// <summary>
    /// A commando steps in. New to this game: full lives and kit. Back after running out of lives: carrying on,
    /// with the score kept. Either way at the bottom of the screen, safe for a moment.
    /// </summary>
    public void Join(int index, bool quiet = false)
    {
        var p = Players[index];
        if (p.Joined && (p.Alive || p.Lives > 0)) return;
        if (p.Joined) p.Continues++;
        else p.Score = 0;
        p.Joined = true;
        p.Lives = StartingLives;
        p.Continue = 0;
        p.Down = 0;
        p.NextExtra = (p.Score / ExtraLifeEvery + 1) * ExtraLifeEvery;
        Kit(p);
        PlaceAtBottom(p);
        if (quiet) return;
        Play(Sfx.Join);
        Say($"{p.Name}", p.Continues > 0 ? "CARRIES ON" : "JOINS THE FIGHT", 1.8);
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        Pilots[0] = Pilots[1] = null;
        _titleTunePending = true;
        Music(false);
        Effects.Clear();
        Message = SubMessage = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
        Pilots[0] = Pilots[1] = null;
        Music(false);
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase is Phase.Play or Phase.Start) Music(true);
    }

    private void GameOver()
    {
        Music(false);
        if (State == GameState.Attract)
        {
            ShowHighScores(-1);
            return;
        }
        Play(Sfx.GameOver);
        State = GameState.GameOver;
        StateTime = 0;
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }
}
