# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A one- or two-player run-and-gun war game in the style of SNK's 1986 arcade game Ikari Warriors, in C# on .NET 10
and Avalonia 12. One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). Everything in
it is original: the graphics are drawn in code, and the sounds and music are synthesised by
`tools/make_sounds.py` (the title and game-over tunes are public-domain melodies).

## Commands

```bash
dotnet build AVAIkariWarriors.slnx                # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAIkariWarriors.slnx                 # every test runs headless: no window opens
dotnet run --project src/AVAIkariWarriors         # play it; --no-audio for silence
IKARI_SHOTS=$PWD/Images dotnet test tests/AVAIkariWarriors.Tests --filter ScreenshotTests   # refresh the README's pictures
IKARI_PROBE=1 dotnet test tests/AVAIkariWarriors.Tests --filter PilotProbe   # how the computer does, stage by stage ("2" for both commandos, "lazy")
python3 tools/make_sounds.py                      # rebuild src/AVAIkariWarriors/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAIkariWarriors/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the phases of a stage.
  - `GameEngine.Play.cs` steps the commandos, their shots and grenades, the tanks and the camera;
    `GameEngine.Enemies.cs` brings on the enemy and runs it.
  - `Entities.cs` has `Controls`, the screen and world measures (`Field`), the stage maps and everything in
    them.
  - `WarriorPilot.cs` is the computer at a commando's controls, used by the demonstration and the tests.
  - `InputHandler.cs` (both players' keys) and `ControllerManager.cs` (SDL2 gamepads, one per player) produce
    each commando's `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAIkariWarriors/Views/`: `GameCanvas` (split over partial files) draws the 1024x768 frame and the score
  bar, and runs the 60 Hz timer.
- `src/AVAIkariWarriors/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVAIkariWarriors.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `InputTests` drives the keys through
  `InputHandler`; `PilotProbe` does nothing unless `IKARI_PROBE` is set.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` advances in fixed 1/60 s steps (`Field.Step`), independent of the frame
  rate, so a seed always plays the same game. Draw random numbers from the engine's seeded `_rng`, never from
  `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **World y grows upwards.** In this game up the map is `Dy = +1`, and the world's y counts up from the start of
  the stage. `InputHandler` produces that sign from keys, sticks and D-pad alike, so keep it when you add
  controls, and test them through `InputHandler` (as `InputTests` does), not by setting `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock (both Shifts throw grenades). `ShiftReleaseTests` covers this.
  - The two players share the keyboard on keys that never overlap. Keep it that way.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAIkariWarriors`); tests
  load the table from a temporary folder and run the engine with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
