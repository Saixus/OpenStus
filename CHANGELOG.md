# Changelog

All notable changes to Open Commander are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Nothing yet.

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
- **Theming.** The complete Far Manager default palette, transcribed from its sources, loadable from
  and saveable to JSON, tolerant of missing and unknown entries.
- **Panels.** Two file panels with the Brief, Medium, Full, Wide and Detailed view modes, column-major
  fill, the column header row, the per-item status line, the totals and selected-totals lines, a
  scroll bar, quick search, folder history, nine sort modes with reverse, Far's selection semantics
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
- The tree panel, the quick view panel and the info panel are not implemented, so `Ctrl+T`, `Ctrl+Q`
  and the info panel entry are absent rather than stubbed.
- `Alt+F10` (find folder) reports that it is not implemented in this version.
- Archives are ordinary files: `Shift+F1`, `Shift+F2` and `Ctrl+PgDn`-into-an-archive do nothing yet.
- `F11` opens the built-in extras (file search, folder size, compare folders, swap panels) rather than
  a real plugin surface.
- `F12` lists the open screens, which at the panel level means just the panels themselves — the
  viewer and the editor are modal, so they cannot yet be left running in the background.

[Unreleased]: https://github.com/opencommander/opencommander/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/opencommander/opencommander/releases/tag/v0.1.0
