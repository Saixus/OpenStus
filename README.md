# Open Commander

Open Commander is an open source, cross-platform, [Far Manager](https://farmanager.com/) style
dual-pane console file manager written in C# on .NET 10.

Two panels, a command line, a function key bar and a clock — the layout every orthodox file manager
has used since the Norton Commander. Everything is drawn with plain VT escape sequences, so it looks
and behaves the same in Windows Terminal, conhost, and any terminal emulator on Linux or macOS.

- **No third-party dependencies.** The application is a single assembly on top of the base class
  library; xunit is used by the test project only.
- **Faithful to Far.** The palette, the key bar rows, the panel layout, the column headers, the
  totals line and the selection semantics are transcribed from Far Manager's own sources.
- **Testable rendering.** The whole UI paints into an in-memory `ScreenBuffer`, so a frame can be
  rendered, asserted on and printed without a console — which is what `--screenshot` does.

## Screenshot

Rendered with

```
oc --screenshot --size 120x28 --left . --right ./src/OpenCommander
```

```
┌───────────── C:\Work\!Lab\Git\OpenCommander ─────────────┐┌──── C:\Work\!Lab\Git\OpenCommander\src\OpenCommande1:14 PM
│            Name             │            Name            ││            Name             │            Name            │
│ ..                          │                            ││ ..                          │                            │
│ .github                     │                            ││ Core                        │                            │
│ src                         │                            ││ Editor                      │                            │
│ tests                       │                            ││ Files                       │                            │
│ .editorconfig               │                            ││ Input                       │                            │
│ .gitignore                  │                            ││ Operations                  │                            │
│ CHANGELOG.md                │                            ││ Panels                      │                            │
│ CONTRIBUTING.md             │                            ││ Rendering                   │                            │
│ Directory.Build.props       │                            ││ Shell                       │                            │
│ LICENSE                     │                            ││ Text                        │                            │
│ OpenCommander.sln           │                            ││ Theming                     │                            │
│ README.md                   │                            ││ Ui                          │                            │
│                             │                            ││ Viewer                      │                            │
│                             │                            ││ OpenCommander.csproj        │                            │
│                             │                            ││ Program.cs                  │                            │
│                             │                            ││                             │                            │
│                             │                            ││                             │                            │
│                             │                            ││                             │                            │
│                             │                            ││                             │                            │
│                             │                            ││                             │                            │
│                             │                            ││                             │                            │
├──────────────────────────────────────────────────────────┤├──────────────────────────────────────────────────────────┤
 ..                                      Up  08/08/26  10:04 ..                                      Up  08/08/26  10:39
└────────── Bytes: 35.1 K, files: 8, folders: 3 ───────────┘└────────── Bytes: 5.5 K, files: 2, folders: 12 ───────────┘
C:\Work\!Lab\Git\OpenCommander>
1Help    2UserMn    3View    4Edit    5Copy    6RenMov    7MkFold    8Delete   9ConfMn   10Quit   11Plugin   12Screen
```

On a real terminal the panels are cyan on blue, the active panel's path is black on cyan, the column
headers are yellow, and the key bar captions are black on cyan — `--screenshot --ansi` prints the
same frame with the SGR colour escapes intact.

## Building and running

Open Commander needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
git clone https://github.com/opencommander/opencommander.git
cd opencommander

dotnet build OpenCommander.sln
dotnet test  tests/OpenCommander.Tests/OpenCommander.Tests.csproj

# run it
dotnet run --project src/OpenCommander/OpenCommander.csproj
```

To produce a standalone executable named `oc`:

```sh
dotnet publish src/OpenCommander/OpenCommander.csproj -c Release -r win-x64   # or linux-x64, osx-arm64, ...
```

`PublishSingleFile` switches itself on as soon as a runtime identifier is given, so the result is one
file you can drop on the `PATH`.

### Command line

```
oc [startPath] [options]

  startPath              the folder both panels open in

  --left <path>          initial left panel folder
  --right <path>         initial right panel folder
  --theme <file.json>    palette file to load instead of the built-in one
  --view <1-9>           initial view mode for both panels
                         1 Brief  2 Medium  3 Full  4 Wide  5 Detailed
  --screenshot           render one frame to stdout and exit, without
                         touching the real console
  --ansi                 with --screenshot, emit SGR colour escapes
  --size <WxH>           with --screenshot, force the virtual screen
                         size (the default is 120x40)
  --version              print the version and exit
  --help                 print this text and exit
```

`--screenshot` is the project's automated verification hook. It builds the whole application against
a headless terminal, lays it out, draws exactly one frame and prints it — no alternate screen buffer,
no console mode changes, no input loop. The frame is written as UTF-8 regardless of the console's own
codepage, so the box drawing survives being piped to a file. It is safe to run from a script or a CI
job.

`--ansi` and `--size` only mean anything together with `--screenshot`, and `--size` is refused
without it rather than quietly doing nothing: a forced size makes the terminal headless, so an
interactive `oc --size 80x25` would paint into a screen no one can see and exit having printed
nothing. An interactive run always takes its size from the console and follows it as you resize the
window.

## Key bindings

Press <kbd>F1</kbd> inside the application for the same list. Both this table and the help screen are
generated from `HelpScreen.Bindings`, so they cannot drift apart.

### Panels

| Key | Action |
| --- | --- |
| `Up / Down` | Move the cursor one item |
| `Left / Right` | Move one column, or edit the command line |
| `PgUp / PgDn` | Scroll one page |
| `Home / End` | First / last item |
| `Enter` | Enter a folder, or run the file under the cursor |
| `Ctrl+PgDn` | Enter the folder under the cursor |
| `Ctrl+PgUp` | Go to the parent folder |
| `Ctrl+\` | Go to the root of the current drive |
| `Tab` | Switch the active panel |
| `Ctrl+U` | Swap the two panels |
| `Ctrl+R` | Re-read the active panel |
| `Ctrl+O` | Hide or show both panels |
| `Ctrl+P` | Hide or show the passive panel |
| `Ctrl+F1 / Ctrl+F2` | Hide or show the left / right panel |
| `Ctrl+H` | Show or hide hidden and system files |
| `Ctrl+B` | Show or hide the function key bar |
| `Alt+F1 / Alt+F2` | Change the drive of the left / right panel |
| `Alt+<letter>` | Quick search by name |

### Selection

| Key | Action |
| --- | --- |
| `Ins` | Tag the item and move down |
| `Shift+arrows` | Tag while moving the cursor |
| `Gray +` | Tag a group of files by mask |
| `Gray -` | Untag a group of files by mask |
| `Gray *` | Invert the selection |
| `Ctrl+Gray +` | Tag every file with the same extension |
| `Ctrl+Gray -` | Untag every file with the same extension |
| `Shift+Gray +` | Tag everything |
| `Shift+Gray -` | Untag everything |
| `Ctrl+A` | Tag every file in the panel |

### View modes

| Key | Action |
| --- | --- |
| `Ctrl+1` | Brief - three name columns |
| `Ctrl+2` | Medium - two name columns |
| `Ctrl+3` | Full - name, size, date and time |
| `Ctrl+4` | Wide - name and size |
| `Ctrl+5` | Detailed - with the attributes |

### Sorting

| Key | Action |
| --- | --- |
| `Ctrl+F3` | Sort by name |
| `Ctrl+F4` | Sort by extension |
| `Ctrl+F5` | Sort by last write time |
| `Ctrl+F6` | Sort by size |
| `Ctrl+F7` | Leave the panel unsorted |
| `Ctrl+F8` | Sort by creation time |
| `Ctrl+F9` | Sort by access time |
| `Ctrl+F12` | Show the sort modes menu |

### Commands

| Key | Action |
| --- | --- |
| `F1` | This help |
| `F2` | User menu |
| `F3` | View the file under the cursor |
| `F4` | Edit the file under the cursor |
| `F5` | Copy |
| `F6` | Rename or move |
| `F7` | Create a folder |
| `F8` | Delete |
| `F9` | Open the horizontal menu |
| `F10` | Quit |
| `F11` | Extras: file search, folder size, compare, swap |
| `F12` | Screens list |
| `Shift+F4` | Create and edit a new file |
| `Shift+F5` | Copy the item under the cursor into this folder |
| `Shift+F6` | Rename the item under the cursor |
| `Shift+F8` | Delete permanently, bypassing the recycle bin |
| `Shift+Del` | Delete permanently, bypassing the recycle bin |
| `Shift+F9` | Save the settings |
| `Ctrl+L` | Folder size of the tagged items |
| `Ctrl+Ins` | Copy the tagged names to the clipboard |
| `Alt+Shift+Ins` | Copy the tagged full paths to the clipboard |
| `Alt+F7` | Find file |
| `Alt+F8` | Command history |
| `Alt+F12` | Folders history |
| `Alt+F10` | Find folder - not implemented in this version |

### Viewer

| Key | Action |
| --- | --- |
| `Up / Down` | Scroll one line |
| `PgUp / PgDn` | Scroll one page |
| `Home / End` | Start / end of the file |
| `Left / Right` | Scroll sideways in unwrapped mode |
| `F2` | Toggle line wrapping |
| `F4` | Switch between text and hex |
| `F7` | Search |
| `Shift+F7` | Search again |
| `F10 / Esc` | Close the viewer |

### Editor

| Key | Action |
| --- | --- |
| `Arrows / PgUp / PgDn` | Move the caret |
| `Home / End` | Start / end of the line |
| `Ctrl+Home / Ctrl+End` | Start / end of the file |
| `Shift+arrows` | Select text |
| `Ctrl+Y` | Delete the current line |
| `F2` | Save |
| `F7` | Search |
| `Shift+F7` | Search again |
| `F10 / Esc` | Close the editor |

### Command line

| Key | Action |
| --- | --- |
| `Any character` | Type a command |
| `Enter` | Run the command |
| `Esc` | Clear the line |
| `Ctrl+Y` | Clear the line |
| `Up / Down` | Walk the command history |
| `Ctrl+E / Ctrl+X` | Walk the command history |
| `Tab` | Complete the path under the caret |
| `Ctrl+Left / Ctrl+Right` | Move one word |
| `Ctrl+Enter / Ctrl+J` | Insert the name under the cursor |
| `Ctrl+F` | Insert the full name under the cursor |
| `cd <path>` | Change the active panel's folder |

Hold <kbd>Shift</kbd>, <kbd>Ctrl</kbd> or <kbd>Alt</kbd> and the key bar captions change under your
fingers, exactly as they do in Far — the Windows input backend reports modifier presses on their own,
so the bar is live rather than guessed.

## Settings, themes and the user menu

Everything lives in one folder:

| Platform | Folder |
| --- | --- |
| Windows | `%APPDATA%\OpenCommander\` |
| Linux, macOS | `$XDG_CONFIG_HOME/OpenCommander/`, defaulting to `~/.config/OpenCommander/` |

| File | What it is |
| --- | --- |
| `settings.json` | The user preferences. Written by <kbd>Shift</kbd>+<kbd>F9</kbd>; every entry is optional. |
| `history.json` | The command line history. |
| `usermenu.json` | The <kbd>F2</kbd> user menu. |

A theme is a JSON file naming a foreground and a background `ConsoleColor` per palette slot; point at
one with `--theme <file.json>` or with the `themePath` setting. Anything the file omits keeps the
built-in Far palette, and an unreadable or malformed theme silently falls back to it — a broken
colour file must never keep you out of your file manager.

`usermenu.json` looks like this:

```json
{
  "items": [
    { "title": "&Build",  "command": "dotnet build" },
    { "title": "&Test",   "command": "dotnet test"  },
    { "title": "Git &status", "command": "git status" }
  ]
}
```

Each command runs in the active panel's folder. When the file is missing, <kbd>F2</kbd> says so and
tells you where to create it.

## Project layout

```
src/OpenCommander/
  Core/         the shell: Application, UiServices, KeyBindings, MainMenu, CommandLineArgs, Settings
  Rendering/    ScreenBuffer, CellStyle, box drawing, and the diffing VT Terminal
  Input/        KeyEvent, MouseEvent and the Windows / portable input backends
  Theming/      the Theme palette and its JSON serialisation
  Files/        FileEntry, directory reading, sorting, masks, size formatting, drives
  Panels/       FilePanel, the view modes, the column layout, quick search, folder history
  Ui/           dialogs, controls, popup and horizontal menus, key bar, command line, clock, help
  Viewer/       the F3 file viewer and its lazily indexed model
  Editor/       the F4 editor, its text buffer and undo stack
  Operations/   copy, move, delete, recycle bin, file search, folder sizes
  Shell/        running commands, the command history, path completion
  Text/         encoding detection and line ending handling

tests/OpenCommander.Tests/   xunit, no mocking framework, ~1360 tests
```

The dependency direction is one way: `Rendering`, `Input`, `Theming` and `Files` know nothing about
anything above them; `Core` declares the interfaces (`IAppContext`, `IUiServices`, `IFilePanel`,
`IScreenComponent`) that let the features talk to the shell without depending on it; `Application` is
the only type that wires the whole thing together.

## Design notes

- **One assembly, no globals.** Every feature receives an `IAppContext`. That is what makes the
  panels, the dialogs and the operations testable without a console.
- **Synchronous throughout.** There is no `async` anywhere in the event loop. A modal dialog is a
  blocking call that pumps the same loop, so dialogs nest naturally — the overwrite prompt of a copy
  simply lands on top of its progress dialog.
- **Draw, then diff.** Each frame is painted from scratch into the back buffer; `Terminal` then emits
  only the cells that changed, an SGR sequence only when the colour pair changes, and a cursor move
  only when there is a gap worth skipping. The whole frame goes out in one write wrapped in a
  synchronized update (`CSI ?2026h`), so conforming terminals present it atomically.
- **The console is always given back.** The alternate screen buffer, the cursor, autowrap, the
  colours and the Windows console modes are restored from `Dispose`, from `ProcessExit`, from an
  unhandled exception, from `Console.CancelKeyPress`, and from whichever interrupt path the platform
  uses: the console control handler on Windows, which also covers the window close button, and
  `SIGINT`, `SIGTERM` and `SIGHUP` registrations on Linux and macOS, where a signal would otherwise
  kill the process outright and leave you in the alternate buffer with a blind `reset` to type. None
  of those handlers swallow the interrupt — <kbd>Ctrl</kbd>+<kbd>C</kbd> still quits, it just quits
  tidily. Restoration is idempotent and never throws, which is what makes it safe from a signal
  context.

## Roadmap

- **Archives** — browse a `.zip` or `.7z` as if it were a folder, plus <kbd>Shift</kbd>+<kbd>F1</kbd>
  and <kbd>Shift</kbd>+<kbd>F2</kbd> to add and extract.
- **Plugins** — a real plugin surface behind <kbd>F11</kbd>, so a panel can be backed by something
  other than the file system.
- **Tree panel** (<kbd>Ctrl</kbd>+<kbd>T</kbd>) and **quick view** (<kbd>Ctrl</kbd>+<kbd>Q</kbd>).
- **FTP and SFTP** panels.
- Folder shortcuts, the file description database, sort groups and file highlighting rules.
- The remaining view modes 6-9 (descriptions, owners, links), which currently fall back to Full.

## Contributing

Patches are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for the build, test and style
expectations, and [CHANGELOG.md](CHANGELOG.md) for what has landed so far.

## Licence

MIT. See [LICENSE](LICENSE).

Far Manager is copyright its own authors; Open Commander is an independent re-implementation of its
user interface conventions and shares no code with it.
