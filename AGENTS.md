# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A maze game in the style of Konami's 1981 arcade game Amidar, in C# on .NET 10 and Avalonia 12. One
self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). The graphics are drawn in code,
and the sounds, tunes and the spoken word are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAAmidar.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAAmidar.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVAAmidar          # play it; --no-audio for silence
AMIDAR_SHOTS=$PWD/Images dotnet test tests/AVAAmidar.Tests --filter ScreenshotTests   # refresh the README's pictures
AMIDAR_PROBE=1 dotnet test tests/AVAAmidar.Tests --filter PilotProbe   # how the computer player gets on, for tuning
python3 tools/make_sounds.py                # rebuild src/AVAAmidar/Sounds (standard-library Python only)
```

`PilotProbe` also reads `AMIDAR_LEVELS` (how many levels to play, 6 by default), and `AMIDAR_PROBE=lazy` makes the
player wander instead. Without `AMIDAR_PROBE` it does nothing.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAAmidar/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the level flow.
  - `World.cs` has the screen measures (`Field`), the seeded `Dice`, the `Board` of lines, edges and boxes, the
    movers and warriors, and the copyable `World`.
  - `Sim.cs` is the rules, one 60th of a second at a time (`Sim.Step`).
  - `Pilot.cs` is the computer player, used by the demonstration and the tests. It tries each way on a copy of the
    world.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAAmidar/Views/`: `GameCanvas` (split over two partial files) draws the fixed 1024x768 screen and runs the
  60 Hz timer.
- `src/AVAAmidar/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVAAmidar.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay tests, screenshot tests and the
  pilot probe.
- `tools/`: `make_sounds.py` and the formant voice `speech.py` it uses.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` runs the simulation in fixed 1/60 s steps (`Field.Step`), independent of
  the frame rate, so tests play deterministically. Draw random numbers from the world's seeded `Dice` (`w.Dice`),
  never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** Controls are a `Dir` (`Way`) and a jump, not a `dy`. Up keys and the
  stick pushed up (`LeftStickY < -0.5`) give `Dir.Up`, and `Board.Step(Dir.Up)` is `(0, -1)`: negative y is up
  the screen. Keep keys and stick agreeing when adding controls.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAAmidar`); tests load the
  table from a fresh folder under the system temp path and run with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
