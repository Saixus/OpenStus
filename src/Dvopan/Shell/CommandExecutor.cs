using System.Diagnostics;
using Dvopan.Files;
using Dvopan.Rendering;

namespace Dvopan.Shell;

/// <summary>
/// Runs command lines through the platform shell, and opens files with their operating system
/// association.
/// </summary>
/// <remarks>
/// <para>
/// A command needs the real screen: it writes to stdout, it may prompt, it may draw its own
/// progress. So the alternate screen buffer is left before the child starts and re-entered
/// afterwards, which puts the command's output on the main screen where the scrollback keeps it and
/// leaves the panels untouched underneath. The panels come back the moment the child exits - there
/// is no "press any key" pause, in the classic orthodox-file-manager fashion - because the output
/// stays on the main screen, where Ctrl+O can reveal it.
/// </para>
/// <para>
/// The console input mode needs the same handover: the raw mode the panels run under is a property
/// of the input buffer every child inherits, so the cooked startup mode is put back around the
/// child via <see cref="Terminal.SuspendConsoleInputMode"/> and
/// <see cref="Terminal.RestoreConsoleInputMode"/>. Without that, the child gets no echo, no line
/// editing, and a Ctrl+C that queues as a key instead of interrupting it.
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
    /// <param name="resumeAltScreen">
    /// Whether to retake the alternate screen once the child exits; <see langword="false"/> keeps
    /// the user screen up, which is what Ctrl+O wants.
    /// </param>
    /// <param name="echo">
    /// The prompt and command to log on the user screen's bottom row before the child runs, VT
    /// colour escapes allowed, or <see langword="null"/> to log nothing.
    /// </param>
    /// <returns>
    /// The child's exit code, <see cref="DirectoryChanged"/> when the command was a <c>cd</c>,
    /// <see cref="CouldNotStart"/> when the shell could not be launched, or <c>0</c> for a blank
    /// command line.
    /// </returns>
    public static int Run(
        string command,
        string workingDirectory,
        Terminal terminal,
        out string? changeDirectory,
        bool resumeAltScreen = true,
        string? echo = null)
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

        Suspend(terminal);

        // The user screen reads like a terminal session: the prompt and the command on the bottom
        // row, then a newline so the child's output scrolls up from underneath it. Anchoring on the
        // bottom row - rather than wherever the primary buffer's cursor was left - is what keeps the
        // prompt at the bottom when Ctrl+O shows this screen afterwards.
        if (echo is not null)
        {
            terminal.WriteUserScreenLine(echo);
        }

        int exitCode = CouldNotStart;
        try
        {
            using Process? process = Process.Start(BuildStartInfo(command, workingDirectory));
            if (process is null)
            {
                WriteLine($"Cannot start the shell for: {command}");
            }
            else
            {
                process.WaitForExit();
                exitCode = process.ExitCode;
            }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException or IOException or UnauthorizedAccessException)
        {
            WriteLine($"Cannot run: {command}");
            WriteLine(e.Message);
            exitCode = CouldNotStart;
        }

        Resume(terminal, resumeAltScreen);
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
    /// Recognises <c>cd</c>, <c>chdir</c>, the Unix habit of typing <c>cd</c> with no argument to
    /// mean "go home", and <c>cmd</c>'s spacing-free spellings <c>cd..</c>, <c>cd\</c> and
    /// <c>cd/</c>. A bare drive letter such as <c>D:</c> counts too, because that is how a Windows
    /// user changes drive from a prompt. The <c>/d</c> switch <c>cmd</c> accepts is ignored: this
    /// always changes drive as well as directory. An argument containing an unquoted shell
    /// operator - <c>cd src &amp;&amp; dotnet build</c> - is not a directory change: the whole line
    /// belongs to the shell, whose operators then run as typed.
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
        string argument;
        if (string.Equals(verb, "cd", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(verb, "chdir", StringComparison.OrdinalIgnoreCase))
        {
            argument = space < 0 ? string.Empty : text[(space + 1)..].Trim();
        }
        else if (!TrySplitCompactCd(text, out argument))
        {
            return false;
        }

        // An unquoted "&", "|", "<" or ">" means the line is a compound command or a redirection,
        // not a path that happens to contain one - all four are legal filename characters on some
        // platform, which is why the check respects quotes. Handing the line to the shell keeps
        // the operators doing what the user asked instead of silently discarding everything after
        // the "cd".
        if (ContainsShellOperator(argument))
        {
            return false;
        }

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
    /// Recognises <c>cmd</c>'s spacing-free <c>cd</c> spellings, where the argument starts right
    /// after the verb: <c>cd..</c>, <c>cd\</c>, <c>cd/</c> and longer forms like <c>cd..\sub</c>.
    /// </summary>
    /// <param name="text">The trimmed command line.</param>
    /// <param name="argument">The argument following the verb, or an empty string.</param>
    /// <returns><see langword="true"/> when the text is a spacing-free directory change.</returns>
    /// <remarks>
    /// Only <c>.</c>, <c>\</c> and <c>/</c> may follow the verb directly - exactly the characters
    /// <c>cmd</c> accepts there. Anything else (<c>cdrom</c>, <c>cd&amp;&amp;whoami</c>) is an
    /// ordinary command for the shell.
    /// </remarks>
    private static bool TrySplitCompactCd(string text, out string argument)
    {
        foreach (string verb in CdVerbs)
        {
            if (text.Length > verb.Length &&
                text.StartsWith(verb, StringComparison.OrdinalIgnoreCase) &&
                text[verb.Length] is '.' or '\\' or '/')
            {
                argument = text[verb.Length..].Trim();
                return true;
            }
        }

        argument = string.Empty;
        return false;
    }

    /// <summary>
    /// Whether the argument contains a shell operator - <c>&amp;</c>, <c>|</c>, <c>&lt;</c> or
    /// <c>&gt;</c> - outside double quotes, which makes the line the shell's to run.
    /// </summary>
    /// <param name="argument">The would-be <c>cd</c> argument, exactly as typed.</param>
    /// <returns><see langword="true"/> when an unquoted operator is present.</returns>
    private static bool ContainsShellOperator(string argument)
    {
        bool quoted = false;
        foreach (char c in argument)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && c is '&' or '|' or '<' or '>')
            {
                return true;
            }
        }

        return false;
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

    private static void Suspend(Terminal terminal)
    {
        // Through the terminal rather than a raw write, so its record of which buffer is up stays
        // truthful - Ctrl+O may already have the user screen showing, making this a no-op.
        terminal.ShowUserScreen();

        // After the screen, the keyboard: hand the child the cooked input mode it expects, so
        // its prompts echo and its Ctrl+C interrupts.
        terminal.SuspendConsoleInputMode();
    }

    private static void Resume(Terminal terminal, bool altScreen)
    {
        terminal.RestoreConsoleInputMode();

        // With the panels hidden (Ctrl+O) the shell asks to stay on the user screen, as the classic
        // orthodox file managers do after running a command with the panels off.
        if (altScreen)
        {
            terminal.ShowPanelsScreen();
            terminal.Invalidate();
        }
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

    private static readonly char[] Whitespace = [' ', '\t'];

    /// <summary>The directory-change verbs, longest first so a prefix test tries them in order.</summary>
    private static readonly string[] CdVerbs = ["chdir", "cd"];
}
