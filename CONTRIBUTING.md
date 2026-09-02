# Contributing to Open Stus

Thanks for looking. Open Stus is a small, deliberately dependency-free codebase, and the rules
below exist mostly to keep it that way.

## Getting set up

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and nothing else.

```sh
dotnet build OpenStus.sln
dotnet test  tests/OpenStus.Tests/OpenStus.Tests.csproj
```

Both must be clean before you open a pull request. CI runs exactly these two commands, plus a
`--screenshot` smoke test, on `windows-latest` and `ubuntu-latest`.

### Seeing your change

This is a full-screen TUI, so `dotnet run` takes over the terminal and blocks. During development use
the screenshot hook instead — it renders one frame to stdout and exits:

```sh
dotnet run --project src/OpenStus/OpenStus.csproj -- --screenshot --size 120x40
dotnet run --project src/OpenStus/OpenStus.csproj -- --screenshot --ansi --size 120x40
```

`--size` forces a virtual screen, which also means the run never touches the real console: no
alternate screen buffer, no console mode changes, no input loop.

## House rules

- **No third-party NuGet packages in `src/`.** The application depends on the base class library and
  nothing else. The test project may use xunit, and only xunit.
- **C# 13**, nullable reference types on, implicit usings on, file-scoped namespaces.
- **XML doc comments** on public types and on any non-obvious member. Describe *why*, not *what* —
  the signature already says what.
- **No `async`.** The event loop is synchronous by design. If you find yourself wanting a `Task`, you
  probably want to pump the modal loop instead; see `Application.PumpBackground`.
- **Keep allocations out of the per-frame render path.** `ScreenBuffer` and `Terminal` are hot; a
  `string.Format` inside a per-cell loop is a real regression.
- **Cross-platform.** Everything has to work in Windows Terminal, in conhost, and on Linux and macOS.
  Guard Windows-only code with `OperatingSystem.IsWindows()` and provide a graceful fallback rather
  than a `PlatformNotSupportedException`.
- **Never leave the terminal broken.** Any new console state you set must be undone on every exit
  path, including the window close button and an unhandled exception.

## Where things live

| Namespace | Owns |
| --- | --- |
| `OpenStus.Rendering` | The cell buffer, box drawing, and the diffing VT terminal |
| `OpenStus.Input` | Key, mouse and resize events, and the two input backends |
| `OpenStus.Theming` | The colour palette and its JSON form |
| `OpenStus.Files` | Directory reading, sorting, masks, sizes, drives |
| `OpenStus.Panels` | The file panel, its view modes and its column layout |
| `OpenStus.Ui` | Dialogs, controls, menus, key bar, command line, clock, help |
| `OpenStus.Viewer` / `.Editor` | The F3 viewer and the F4 editor |
| `OpenStus.Operations` | Copy, move, delete, search, folder sizes |
| `OpenStus.Shell` | Running commands, history, path completion |
| `OpenStus.Core` | The interfaces the layers talk through, and the shell that wires them |

The dependency direction is one way, from `Core` outwards. If a low-level type needs to call up, add
an interface in `Core` rather than a reference.

## Adding a key binding

1. Add the command as a public method on `Application`.
2. Register the chord in `KeyBindings.BuildDefault`. Check first that `FilePanel.HandleKey` does not
   already want that chord — the global table is consulted *before* the panel, so anything it claims
   is taken away from the panel.
3. Add a row to `HelpScreen.Bindings`. That single list feeds the F1 screen *and* the README table,
   and a test asserts the README still matches, so run `dotnet test` and paste the regenerated table
   into `README.md` if it complains.

## Tests

- xunit, no mocking framework. Anything that needs a screen renders into a `ScreenBuffer` and asserts
  on the text; anything that needs a terminal uses `Terminal.Create(width, height)`, which is
  headless.
- Anything that touches the file system creates its own scratch tree under the temp folder and
  removes it — see `ShellTree` in `ScreenshotTests.cs`.
- A test must pass on Windows *and* on Linux. If a behaviour genuinely differs (symlink privileges,
  the recycle bin, ANSI code pages), gate the assertion on the capability, not on the OS name where
  you can.

## Commits and pull requests

- One logical change per pull request; keep unrelated reformatting out of it.
- Describe the user-visible effect in the description, and mention any established file-manager convention you were
  matching, with a pointer to where you checked it.
- Add an entry to `CHANGELOG.md` under `Unreleased`.

## Reporting bugs

Please include your OS and terminal emulator, the console size, and — where it helps — the output of

```sh
stus --screenshot --size 120x40
```

which is plain text you can paste straight into an issue.
