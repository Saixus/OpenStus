using System.Diagnostics;
using OpenCommander.Files;
using OpenCommander.Rendering;

namespace OpenCommander.Shell;

/// <summary>
/// Runs command lines through the platform shell, and opens files with their operating system
/// association.
/// </summary>
/// <remarks>
/// <para>
/// A command needs the real screen: it writes to stdout, it may prompt, it may draw its own
/// progress. So the alternate screen buffer is left before the child starts and re-entered
/// afterwards, which puts the command's output on the main screen where the scrollback keeps it and
/// leaves the panels untouched underneath.
/// </para>
/// <para>
/// <c>cd</c> is handled here rather than being handed to the shell. A child process cannot change
/// its parent's directory, so spawning <c>cmd /c cd ..</c> would do nothing at all; instead
/// <see cref="Run(string, string, Terminal)"/> reports <see cref="DirectoryChanged"/> and the
/// application navigates the active panel.
/// </para>
/// </remarks>
public static class CommandExecutor
{
    /// <summary>
    /// Returned by <see cref="Run(string, string, Terminal)"/> when the command line was a
    /// <c>cd</c> that was handled internally rather than run as a process.
    /// </summary>
    /// <remarks>
    /// A deliberately unreachable value: process exit codes are 32 bit, but no shell reports
    /// <see cref="int.MinValue"/>, so the sentinel cannot collide with a real result.
    /// </remarks>
    public const int DirectoryChanged = int.MinValue;

    /// <summary>Returned when the shell itself could not be started at all.</summary>
    public const int CouldNotStart = -1;

    /// <summary>The prompt shown before returning to the panels.</summary>
    public const string ContinuePrompt = "Press any key to continue...";

    private const string Esc = "\u001b";

    // End any open synchronized update, autowrap back on, reset the colours, show the cursor, and
    // only then hand the main screen back.
    private const string SuspendSequence =
        Esc + "[?2026l" + Esc + "[?7h" + Esc + "[0m" + Esc + "[?25h" + Esc + "[?1049l";

    // Retake the alternate buffer exactly the way Terminal.Create does.
    private const string ResumeSequence =
        Esc + "[?1049h" + Esc + "[?25l" + Esc + "[?7l" + Esc + "[2J" + Esc + "[H";

    /// <summary>
    /// Runs a command line, showing its output on the main screen.
    /// </summary>
    /// <param name="command">The command line, exactly as typed.</param>
    /// <param name="workingDirectory">The directory the command runs in.</param>
    /// <param name="terminal">The terminal to suspend and restore around the command.</param>
    /// <returns>
    /// The child's exit code, <see cref="DirectoryChanged"/> when the command was a <c>cd</c>,
    /// <see cref="CouldNotStart"/> when the shell could not be launched, or <c>0</c> for a blank
    /// command line.
    /// </returns>
    public static int Run(string command, string workingDirectory, Terminal terminal) =>
        Run(command, workingDirectory, terminal, out _);

    /// <summary>
    /// Runs a command line, reporting the target of a <c>cd</c> instead of running it.
    /// </summary>
    /// <param name="command">The command line, exactly as typed.</param>
    /// <param name="workingDirectory">The directory the command runs in.</param>
    /// <param name="terminal">The terminal to suspend and restore around the command.</param>
    /// <param name="changeDirectory">
    /// The absolute directory the panel should navigate to when the result is
    /// <see cref="DirectoryChanged"/>; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The child's exit code, <see cref="DirectoryChanged"/> when the command was a <c>cd</c>,
    /// <see cref="CouldNotStart"/> when the shell could not be launched, or <c>0</c> for a blank
    /// command line.
    /// </returns>
    public static int Run(string command, string workingDirectory, Terminal terminal, out string? changeDirectory)
    {
        ArgumentNullException.ThrowIfNull(terminal);

        changeDirectory = null;

        if (string.IsNullOrWhiteSpace(command))
        {
            return 0;
        }

        if (TryParseCd(command, workingDirectory, out string target))
        {
            changeDirectory = target;
            return DirectoryChanged;
        }

        // Headless means there is no console to hand over - screenshot mode and the tests. Running
        // the command anyway would scribble over the caller's stdout, so it is a no-op.
        if (terminal.IsHeadless)
        {
            return 0;
        }

        Suspend();

        int exitCode = CouldNotStart;
        bool producedOutput = true;
        try
        {
            (int row, int column) start = CursorPosition();

            using Process? process = Process.Start(BuildStartInfo(command, workingDirectory));
            if (process is null)
            {
                WriteLine($"Cannot start the shell for: {command}");
            }
            else
            {
                process.WaitForExit();
                exitCode = process.ExitCode;

                (int row, int column) end = CursorPosition();

                // An unreadable cursor means we cannot tell, so assume the command said something
                // worth reading rather than wiping it away.
                producedOutput = start.row < 0 || end.row < 0 || end != start;
            }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException or IOException or UnauthorizedAccessException)
        {
            WriteLine($"Cannot run: {command}");
            WriteLine(e.Message);
            exitCode = CouldNotStart;
        }

        if (producedOutput || exitCode != 0)
        {
            WaitForKey();
        }

        Resume(terminal);
        return exitCode;
    }

    /// <summary>
    /// Recognises a <c>cd</c> command and resolves its argument.
    /// </summary>
    /// <param name="command">The command line, exactly as typed.</param>
    /// <param name="currentDir">The directory relative arguments are measured from.</param>
    /// <param name="target">The absolute target directory, or an empty string when this is not a <c>cd</c>.</param>
    /// <returns><see langword="true"/> when the command line is a directory change.</returns>
    /// <remarks>
    /// Recognises <c>cd</c>, <c>chdir</c> and the Unix habit of typing <c>cd</c> with no argument to
    /// mean "go home". A bare drive letter such as <c>D:</c> counts too, because that is how a
    /// Windows user changes drive from a prompt. The <c>/d</c> switch <c>cmd</c> accepts is ignored:
    /// this always changes drive as well as directory.
    /// </remarks>
    public static bool TryParseCd(string command, string currentDir, out string target)
    {
        target = string.Empty;

        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        string text = command.Trim();

        // A bare drive letter: "D:" or "D:\".
        if (OperatingSystem.IsWindows() &&
            text.Length is 2 or 3 &&
            char.IsLetter(text[0]) &&
            text[1] == ':' &&
            (text.Length == 2 || text[2] == '\\' || text[2] == '/'))
        {
            target = FileSystemProvider.NormalizeDisplayPath(text[..2] + Path.DirectorySeparatorChar);
            return true;
        }

        int space = text.IndexOfAny(Whitespace);
        string verb = space < 0 ? text : text[..space];
        if (!string.Equals(verb, "cd", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(verb, "chdir", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string argument = space < 0 ? string.Empty : text[(space + 1)..].Trim();

        // "cd /d C:\Work" - cmd's "change drive too" switch, which is what we do regardless.
        if (OperatingSystem.IsWindows() &&
            (argument.StartsWith("/d ", StringComparison.OrdinalIgnoreCase) ||
             argument.StartsWith("/D ", StringComparison.Ordinal)))
        {
            argument = argument[3..].Trim();
        }

        if (argument.Length == 0)
        {
            // Bare "cd" prints the current directory on Windows and goes home on Unix. Going home is
            // the useful reading in a file manager, and it is what the key bar promises.
            string home = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolderOption.DoNotVerify);

            if (home.Length == 0)
            {
                return false;
            }

            target = FileSystemProvider.NormalizeDisplayPath(home);
            return true;
        }

        argument = PathCompletion.ExpandEnvironment(argument);

        if (!FileSystemProvider.TryResolve(currentDir ?? string.Empty, argument, out string resolved))
        {
            return false;
        }

        target = resolved;
        return true;
    }

    /// <summary>
    /// Opens a file or folder with the association the operating system has for it.
    /// </summary>
    /// <param name="path">The file or folder to open.</param>
    /// <param name="workingDirectory">The directory the launched process starts in.</param>
    /// <remarks>
    /// Never throws: an unassociated file type, a missing helper or a refused launch simply does
    /// nothing, because there is no useful way for a panel to recover from it.
    /// </remarks>
    public static void Launch(string path, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            ProcessStartInfo info;
            if (OperatingSystem.IsWindows())
            {
                info = new ProcessStartInfo(path) { UseShellExecute = true };
            }
            else
            {
                string opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
                info = new ProcessStartInfo(opener) { UseShellExecute = false };
                info.ArgumentList.Add(path);
            }

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                info.WorkingDirectory = workingDirectory;
            }

            // The launched process outlives us; disposing the handle does not kill it.
            using Process? process = Process.Start(info);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException or ObjectDisposedException or IOException)
        {
            // No association, no helper, or the user cancelled the shell's own dialog.
        }
    }

    /// <summary>
    /// Builds the shell invocation for a command line.
    /// </summary>
    /// <param name="command">The command line, exactly as typed.</param>
    /// <param name="workingDirectory">The directory the command runs in.</param>
    /// <returns>
    /// A start info that runs the command through <c>cmd.exe /c</c> on Windows or
    /// <c>$SHELL -c</c> elsewhere, with its output going straight to the console.
    /// </returns>
    public static ProcessStartInfo BuildStartInfo(string command, string? workingDirectory)
    {
        var info = new ProcessStartInfo
        {
            // Nothing is redirected: the child writes to the console the user is looking at, which
            // is the whole point of leaving the alternate screen buffer first.
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            RedirectStandardInput = false,
            CreateNoWindow = false,
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            info.WorkingDirectory = workingDirectory;
        }

        if (OperatingSystem.IsWindows())
        {
            info.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";

            // /c with the whole command line in one argument: cmd's own quoting rules apply, which
            // is what the user typing at this prompt expects.
            info.Arguments = "/c " + command;
        }
        else
        {
            string shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/sh";
            info.FileName = shell;
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(command);
        }

        return info;
    }

    private static void Suspend() => Write(SuspendSequence);

    private static void Resume(Terminal terminal)
    {
        Write(ResumeSequence);
        terminal.Invalidate();
    }

    private static void Write(string text)
    {
        try
        {
            Console.Out.Write(text);
            Console.Out.Flush();
        }
        catch (IOException)
        {
            // The console went away; there is nothing to restore.
        }
    }

    private static void WriteLine(string text)
    {
        try
        {
            Console.Out.WriteLine(text);
            Console.Out.Flush();
        }
        catch (IOException)
        {
            // Ignored.
        }
    }

    private static void WaitForKey()
    {
        WriteLine(string.Empty);
        Write(ContinuePrompt);

        try
        {
            if (!Console.IsInputRedirected)
            {
                Console.ReadKey(intercept: true);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or IOException)
        {
            // No console input; fall through and repaint.
        }

        WriteLine(string.Empty);
    }

    /// <summary>
    /// The cursor position, used only to tell whether the command wrote anything. Unreadable on
    /// some terminals, in which case the caller conservatively assumes output.
    /// </summary>
    private static (int Row, int Column) CursorPosition()
    {
        try
        {
            return (Console.CursorTop, Console.CursorLeft);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or PlatformNotSupportedException)
        {
            return (-1, -1);
        }
    }

    private static readonly char[] Whitespace = [' ', '\t'];
}
