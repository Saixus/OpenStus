# Changelog

All notable changes to Open Stus are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **A startup notice on the user screen.** Before the panels come up, the program prints a text
  portrait of Vasyl Stus beside the version, the licence and the credit for the photograph, so
  `Ctrl+O` reveals it underneath the panels the way it reveals command output. The portrait is
  drawn in the block shades, which fill a whole cell and so survive any terminal font; the notice
  beside it is ASCII. It is laid out for an 80 column terminal, dropped to the text alone when the
  terminal is narrower and skipped entirely when the terminal comes up headless.
  The portrait is derived from his 1980 arrest photograph, which is in the public domain and came
  from Wikimedia Commons.

- **Tabs in the panels.** `Ctrl+T` opens a new tab on the current folder, `Ctrl+W` closes it,
  `Ctrl+Tab` / `Ctrl+Shift+Tab` (or `Ctrl+Alt+Right` / `Ctrl+Alt+Left`, for terminals that keep
  Ctrl+Tab for themselves) move between them, and a click on a caption switches. Each tab keeps its
  own folder, cursor, sort order and view mode. The strip - folder names, the shown one in the
  active-title colours - only appears once a panel has two tabs, so a single-tab panel still looks
  exactly as before. The open tabs are remembered on exit (`session.json` beside the settings) and
  come back next time, focus included, unless the command line names a folder of its own or
  Options → *Remember tabs on exit* is off; a folder that has gone since is skipped.

- **A command line that behaves like a modern terminal.** It sits on the console's black, as
  a terminal does, and colours what you type as you type it - the command word yellow, options grey,
  quoted strings cyan, `%VAR%` / `$var` green. Up and Down walk the history, but only the entries
  that start like the typed text (`git` then Up visits only the git commands; Ctrl+E / Ctrl+X still
  walk everything); the newest matching entry is shown as a grey ghost after the caret and taken
  with Right or End, one word at a time with Ctrl+Right; Ctrl+Backspace and Ctrl+Delete remove a
  word. Ctrl+R is bash's incremental reverse search - the prompt turns into
  `(reverse-i-search)'query':`, typing narrows, Ctrl+R steps older, Ctrl+S newer, Enter keeps the
  match, Escape restores the line - started from the typed text, or from an empty line while Ctrl+O
  hides the panels (on an empty line with panels up it stays the panel re-read). Tab completes file and
  folder names of the active panel's folder the way a shell does: a
  single match is taken outright, several are first narrowed to what they share, and when nothing
  more is shared a list opens above the token to pick from. The user screen behind Ctrl+O is
  anchored the same way: the prompt stays on the bottom row
  instead of jumping to wherever the last command left the cursor, every command run leaves a
  coloured `path> command` line above its output so the screen reads like a terminal session, and
  the ghost suggestion is drawn there too.

- **Syntax highlighting in the viewer and editor.** C#, JavaScript/TypeScript, JSON (with keys
  coloured separately), SQL, C/C++, Java, Python, PowerShell and the `#`-commented shell/config
  family (sh, yaml, toml, .gitignore, ...) are recognised by extension; keywords, strings, numbers,
  comments and preprocessor directives each get their own colour, with multi-line constructs
  (block comments, C# verbatim strings, JS template literals, Python triple quotes) carried
  correctly across lines. XML/HTML (including csproj, xaml, svg, resx and friends) gets its own
  markup scanner - tag names, attribute values, entities, CDATA, comments and declarations - and
  Markdown gets headings, fenced code blocks, inline code, links, list bullets and block quotes,
  both with their multi-line constructs (open tags, `<!-- -->`, CDATA, code fences) carried
  across lines. CSV and TSV files draw the header row white and cycle the body columns through
  the palette so neighbouring columns are told apart at a glance; quoted fields keep their
  separators, doubled quotes, and even line breaks - an open field carries its colour to the
  next line. The five `Syntax*` colours are theme slots like every other, the feature
  can be switched off under Options → *Syntax highlighting*, and a file type nothing recognises is
  simply drawn plain. Lines beyond 20 000 characters are coloured up to the cap so a minified
  bundle cannot stall a repaint.

- **24-bit colour.** The renderer can write literal RGB (`38;2;r;g;b` / `48;2;r;g;b`) resolved
  through a 16-entry palette instead of naming the terminal's own colour slots, so the classic look
  survives whatever scheme the terminal is themed with. The default palette is the Windows console
  table of the Windows NT console; the older classic VGA table and Windows
  Terminal's Campbell scheme ship alongside it as `--palette vga` and `--palette campbell`.
- **`--colors <auto|truecolor|indexed>`** and the matching `colors` setting. `auto` — the default —
  detects what the terminal can take from `COLORTERM`, `WT_SESSION`, `ConEmuANSI`, `TERM_PROGRAM`,
  `TERM` and the Windows build number, in that order of trust. `indexed` is the escape hatch for
  anyone who themes their terminal deliberately.
- **`--palette <name|file>`** and the matching `palettePath` setting: the RGB behind the 16 colour
  slots, as `"#RRGGBB"` keyed by `ConsoleColor` name, index or the usual aliases. Also takes a
  built-in preset by name — `nt`, `vga` or `campbell`. Omitted slots keep their built-in value, and
  a missing or malformed file falls back to the built-in table.
- **`--clock <HH:mm|off>`.** Pins the corner clock to a fixed time, or hides it. The wall clock is
  the only thing in a rendered frame that changes on its own, so `--screenshot` output could not be
  compared against a golden file; now it can. Times are parsed under the invariant culture in both
  24-hour and `3:07 PM` form, so a build machine's locale cannot move the result.
- **`NO_COLOR` support.** Present and non-empty, whatever its value, it pins the run to the 16
  indexed slots so the terminal's own scheme stays in charge. Only an explicit `--colors` overrides
  it; the saved setting and the detection do not.

### Fixed

A full review pass against the classic orthodox-file-manager conventions (and a plain bug hunt) produced the following, on top of
the four reported issues at the top of this list.

- **Quick search no longer needs Alt held down.** Once `Alt`+letter opens the box, plain typing
  keeps feeding it — the panel now gets first pick of the keys while the search is open, where
  previously the command line swallowed every unmodified character. The three-second idle
  timeout is gone too: the box stays open until `Esc`, `Enter`, a cursor key, or deleting the last
  character closes it. `Ctrl+Enter` / `Ctrl+Shift+Enter` walk to the next and previous match, and
  `Enter` only closes the box instead of activating the entry, all as expected. Switching panels
  cancels a half-typed search.
- **`Ctrl+O` is a real toggle.** The panels used to reappear on the next keypress; now the command
  line stays live while they are hidden — typing edits it, `Enter` runs it, `Up`/`Down` walk the
  history even on an empty line — and only `Ctrl+O` (or a mouse click) brings the panels back,
  exactly like a terminal.
- **The drive menu shows the capacity.** `Alt+F1` / `Alt+F2` list each ready drive's total size
  next to its free space, instead of free space alone.
- **The frame matches the classic look.** The panel border is a double line with single-line column dividers and
  a single-line `╟────╢` separator above the status line, where everything used to be single. The
  sort mode letter (`n`, `x`, `w`, `s`, … — uppercase when reversed) is drawn in the top-left header
  cell, the frame's verticals now run through the status row, and the path caption slides left
  instead of running underneath the corner clock.
- **`Ctrl+O` shows the real user screen.** Hiding the panels now leaves the alternate screen buffer,
  so the output of every command run until then is what you see — the whole point of the key — with the
  live prompt echoed at the cursor. Commands run with the panels off stay on the user screen, and
  the *"Press any key to continue..."* pause after every command is gone, matching how a shell works. The
  held `Ctrl+Alt+Shift` peek shows the same screen.
- **Running a child no longer leaves the console input raw.** The saved cooked input mode is
  restored around every shell command, so a child's `Ctrl+C` interrupts it again and its prompts
  echo; the raw mode comes back when the child exits.
- **Copying or moving a directory junction/symlink no longer produces an empty plain folder** — the
  link itself is recreated at the destination (or reported as an error), and a cross-volume move no
  longer deletes a source link whose contents were never transferred.
- **`Shift+F4` on an existing file opens it** instead of silently truncating it with an empty
  buffer on save.
- **Overwrite and error prompts no longer default to Cancel.** Buttons are ordered affirmative
  first with Cancel last, `Ctrl`+letter chords no longer trigger dialog hotkeys (`Ctrl+Y` on the
  command line could answer *Yes* to a delete), `Enter` during a file operation no longer cancels
  it, and `Enter` on a focused checkbox presses the default button instead of toggling. The
  overwrite prompt now shows both files' size and timestamp.
- **Command line routing.** `Shift`+arrows reach the panel's selection with text on
  the line, plain `Up`/`Down` always belong to the panel (history stays on `Ctrl+E`/`Ctrl+X`, and
  on `Up`/`Down` while `Ctrl+O` has the panels hidden), `Shift+Ins`/`Ctrl+V` paste, `Ctrl+Enter`
  inserts the name quoted with a trailing space like `Ctrl+J`, and `Ctrl+[`/`Ctrl+]` insert the
  left/right panel's path.
- **`cd` handling matches `cmd`**: `cd..`, `cd\`, `cd/` work, `cd x && y` falls through to the
  shell instead of silently dropping the chain, and a `cd` to a folder that does not exist says so.
- **Delete semantics**: deleting a non-empty folder asks one extra confirmation (behind
  its own setting, independent of the general one), read-only files are confirmed even
  when going to the recycle bin, `Shift+F8` deletes only the
  item under the cursor (honouring the recycle bin) while `Shift+Del` stays the permanent variant,
  and an operation no longer clears the passive panel's selection.
- **Editor and viewer fidelity**: editor search no longer skips a match under the caret or adjacent
  occurrences, `Ctrl+Up`/`Ctrl+Down` and the wheel scroll without snapping back, the viewer wraps
  by default and its percentage reaches 100%, scrolling stops at the end of the file, the editor's
  default tab stop is 8, backwards search sees matches straddling the start column, legacy ANSI
  files decode through the system code page instead of Latin-1, and a refused open no longer shows
  a second, wrong error dialog.
- **Dialogs and menus**: edit fields open with their initial text selected so typing replaces it,
  `Esc` in an open pull-down closes the whole menu, the wheel over an empty list no longer throws,
  and the `Alt`+numpad character entry that Windows delivers on the Alt key-up is no longer dropped.
- **Panels**: the *Wide* mode (`Ctrl+4`) is one wide name column plus size, executables are
  light green and hidden entries dark cyan per the classic highlighting, the size column shows
  `Folder`/`Up` without brackets and plain digits, extension sort leaves directories ordered by
  name, and file masks accept the `include|exclude` syntax.

### Changed

- **Renamed to Open Stus.** The solution, the projects, the namespaces and the settings folder are
  `OpenStus`, and the executable is `stus` (it was `oc`). The name honours the poet Vasyl Stus; the
  README says why. Settings and history now live under `OpenStus` in the profile folder.
- `--screenshot --ansi` now renders at the colour depth and through the palette the run resolved,
  rather than always emitting the indexed slots, so a screenshot shows what the live terminal is
  sent. Piping it to a file still yields indexed output, since redirected output is one of the
  signals detection reads; `--colors truecolor` overrides that.
- The panel colours are corrected: the cyan backgrounds are `DarkCyan`, not
  the bright `Cyan` the interface was using as a background, which made the cursor bar, the key bar
  captions, the clock and the active panel title much louder than the classic scheme. The clock, the key bar
  numbers, the panel totals, the menu colours, the dialog highlight and the viewer text were brought
  back in line with the same source.
- `--theme` is described as a theme file rather than a palette file, now that the two are separate
  things: a palette says what "cyan" is, a theme says which parts of the interface are cyan.

### Known limitations

- The renderer assumes every character is one column wide: East-Asian fullwidth characters in file
  names shift everything to their right, and non-BMP characters (emoji) render as two replacement
  glyphs. A width-aware cell model is planned.
- The classic *Detailed* mode is a fullscreen mode with packed size and all three timestamps; Open
  Stus's `Ctrl+5` stays at half width with modified time and attributes only.

## [0.1.0] - 2026-08-08

The first release: a working dual-pane file manager.

### Added

- **Rendering.** An in-memory `ScreenBuffer` of styled cells with clipping writes, fixed-width and
  hotkey-aware text, box drawing in five styles, shadows, and plain-text and ANSI dumps. `Terminal`
  turns it into console output with a per-cell diff, an SGR sequence only when the colour pair
  changes, a cursor move only when there is a gap worth skipping, and one write per frame wrapped in
  a synchronized update (`CSI ?2026h`).
- **Terminal lifecycle.** Virtual terminal processing and the alternate screen buffer on Windows,
  with the console modes, the cursor, the colours and the code page restored from `Dispose`, from
  `ProcessExit`, from an unhandled exception and from the console control handler — so closing the
  window does not leave a broken shell behind.
- **Input.** A Windows backend over `ReadConsoleInputW` that reports mouse events, buffer resizes and
  modifier presses on their own, and a portable `Console.ReadKey` backend for everything else.
- **Theming.** The complete classic console palette, loadable from
  and saveable to JSON, tolerant of missing and unknown entries.
- **Panels.** Two file panels with the Brief, Medium, Full, Wide and Detailed view modes, column-major
  fill, the column header row, the per-item status line, the totals and selected-totals lines, a
  scroll bar, quick search, folder history, nine sort modes with reverse, the classic selection semantics
  (`Ins`, `Shift`+arrows, the Gray key group commands) and full mouse support.
- **Dialogs and menus.** A dialog framework with labels, edit fields with selection, history and
  clipboard support, buttons, check boxes, radio groups, lists and separators; message, input, list
  and progress dialogs; a popup menu with type-search; and the F9 horizontal menu.
- **Viewer.** An offset-anchored file viewer that opens a file of any size, with text and hex modes,
  optional wrapping, forward and backward search, encoding detection and a lazily built line index.
- **Editor.** A text editor with per-line terminators (so a mixed-ending file round-trips byte for
  byte), a grouping undo and redo stack, selection, search and replace, and encoding preservation.
- **File operations.** Copy, move (with a same-volume rename fast path and a copy-then-delete
  fallback), delete through the recycle bin on Windows, folder creation and rename; all of them with
  progress reporting, cancellation, overwrite prompts that remember *All* and *Skip all*, error
  prompts that can retry, and attribute and timestamp preservation. Reparse points are never followed
  unless asked for.
- **Search.** A recursive finder by name mask and by file content, streaming with an overlap so a
  match straddling a chunk boundary is still found, with per-file encoding detection.
- **Shell integration.** A command line with history, path completion and environment expansion; `cd`
  handled internally; other commands run through the platform shell with the alternate screen buffer
  temporarily released so their output is visible.
- **The application shell.** `Application` owns the terminal, the input backend, the panels, the key
  bar, the command line, the clock and a modal component stack, and runs the frame-paced event loop.
  Modal components nest, the key bar captions change live while a modifier is held, and the hardware
  cursor sits on the command line caret when nothing modal is up.
- **Command line interface.** `oc [startPath] [--left --right --theme --view --size --screenshot
  --ansi --version --help]`. `--screenshot` renders exactly one frame to stdout against a headless
  terminal and exits, which is what the automated verification uses.
- **Help.** An F1 screen listing every binding, generated from the same table the README is.
- **Tests.** Around 1260 xunit tests covering rendering, input, theming, sorting, masks, panels,
  dialogs, the viewer, the editor, file operations, search, the command line and the assembled shell.

### Known limitations

- View modes 6 to 9 (descriptions, long descriptions, file owners, links) fall back to Full.
- The tree panel, the quick view panel and the info panel are not implemented, so `Ctrl+Q` and the
  info panel entry are absent rather than stubbed; `Ctrl+T` opens a tab instead.
- `Alt+F10` (find folder) reports that it is not implemented in this version.
- Archives are ordinary files: `Shift+F1`, `Shift+F2` and `Ctrl+PgDn`-into-an-archive do nothing yet.
- `F11` opens the built-in extras (file search, folder size, compare folders, swap panels) rather than
  a real plugin surface.
- `F12` lists the open screens, which at the panel level means just the panels themselves — the
  viewer and the editor are modal, so they cannot yet be left running in the background.

[Unreleased]: https://github.com/Saixus/OpenStus/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Saixus/OpenStus/releases/tag/v0.1.0
