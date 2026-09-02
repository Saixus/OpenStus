using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCommander.Core;

namespace OpenCommander.Shell;

/// <summary>
/// The bounded, de-duplicated list of command lines the user has run, persisted next to the
/// settings file.
/// </summary>
/// <remarks>
/// <para>
/// Entries are held newest first. Re-running a command does not add a duplicate: the existing entry
/// is moved back to the front, which is what makes a short list of frequently used commands stay
/// reachable with one or two presses of Ctrl+E.
/// </para>
/// <para>
/// A cursor walks the list for recall. <see cref="Previous"/> steps towards older entries,
/// <see cref="Next"/> back towards newer ones, and stepping past the newest entry returns
/// <see langword="null"/> so the caller can put the half-typed line the user started with back into
/// the edit field. <see cref="Add"/> resets the cursor.
/// </para>
/// <para>
/// Nothing here throws: an unreadable or malformed history file yields an empty history, and a
/// configuration directory that cannot be written to silently keeps the history in memory only.
/// </para>
/// </remarks>
public sealed class CommandHistory
{
    /// <summary>The most entries kept; older ones fall off the end.</summary>
    public const int MaxEntries = 256;

    /// <summary>The file name used under the configuration directory.</summary>
    public const string FileName = "history.json";

    private readonly List<string> _entries = [];
    private int _cursor = -1;

    /// <summary>Creates an empty in-memory history that is not backed by a file.</summary>
    public CommandHistory()
        : this(null)
    {
    }

    /// <summary>
    /// Creates an empty history bound to a file.
    /// </summary>
    /// <param name="filePath">
    /// The file <see cref="Save"/> writes, or <see langword="null"/> for a history that is never
    /// persisted.
    /// </param>
    public CommandHistory(string? filePath)
    {
        FilePath = filePath;
    }

    /// <summary>
    /// Where <see cref="Load"/> and <see cref="Save"/> look: <c>history.json</c> beside the settings
    /// file.
    /// </summary>
    public static string DefaultFilePath => Path.Combine(Settings.ConfigDirectory, FileName);

    /// <summary>The file this history persists to, or <see langword="null"/> when it is memory only.</summary>
    public string? FilePath { get; }

    /// <summary>Every remembered command, newest first.</summary>
    public IReadOnlyList<string> All => _entries;

    /// <summary>How many commands are remembered.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// The recall cursor: <c>-1</c> when it sits on the line the user is typing, otherwise the index
    /// into <see cref="All"/> of the entry most recently recalled.
    /// </summary>
    public int Cursor => _cursor;

    /// <summary>
    /// Loads the history from <see cref="DefaultFilePath"/>. Never throws.
    /// </summary>
    /// <returns>The loaded history, or an empty one bound to the same path.</returns>
    public static CommandHistory Load() => LoadFrom(DefaultFilePath);

    /// <summary>
    /// Loads the history from an explicit path. Never throws.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The loaded history, or an empty one bound to the same path.</returns>
    public static CommandHistory LoadFrom(string path)
    {
        var history = new CommandHistory(path);

        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return history;
            }

            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return history;
            }

            var file = JsonSerializer.Deserialize(json, CommandHistoryJsonContext.Default.CommandHistoryFile);
            if (file?.Commands is null)
            {
                return history;
            }

            // The file is newest first; replaying it oldest first through the normal insertion path
            // reuses the de-duplication and the bound instead of trusting the file's contents.
            for (int i = file.Commands.Count - 1; i >= 0; i--)
            {
                history.Add(file.Commands[i]);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            history._entries.Clear();
        }

        history._cursor = -1;
        return history;
    }

    /// <summary>Writes the history to <see cref="FilePath"/>. Never throws.</summary>
    /// <returns><see langword="true"/> when the file was written.</returns>
    public bool Save() => FilePath is not null && SaveTo(FilePath);

    /// <summary>
    /// Writes the history to an explicit path, creating the directory if needed. Never throws.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <returns><see langword="true"/> when the file was written.</returns>
    public bool SaveTo(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string full = Path.GetFullPath(path);
            string? dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var file = new CommandHistoryFile { Commands = [.. _entries] };
            File.WriteAllText(full, JsonSerializer.Serialize(file, CommandHistoryJsonContext.Default.CommandHistoryFile));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Remembers a command and resets the recall cursor. Blank commands are ignored; a command that
    /// is already remembered is moved to the front rather than duplicated.
    /// </summary>
    /// <param name="command">The command line, exactly as the user typed it.</param>
    public void Add(string? command)
    {
        _cursor = -1;

        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        string text = command.Trim();

        // Case sensitive even on Windows: "DIR" and "dir" are the same command, but the user typed
        // one of them and that is the one they will want back.
        _entries.RemoveAll(e => string.Equals(e, text, StringComparison.Ordinal));
        _entries.Insert(0, text);

        if (_entries.Count > MaxEntries)
        {
            _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        }
    }

    /// <summary>
    /// Steps the recall cursor one entry towards the oldest command.
    /// </summary>
    /// <returns>
    /// The recalled command, or <see langword="null"/> when the history is empty or the cursor is
    /// already on the oldest entry.
    /// </returns>
    public string? Previous() => Previous(string.Empty);

    /// <summary>
    /// Steps the recall cursor towards the oldest command that starts with
    /// <paramref name="prefix"/> - the shell habit of typing <c>git</c> and pressing Up to walk
    /// only the git commands. An empty prefix walks everything.
    /// </summary>
    /// <param name="prefix">What the recalled command must start with; compared case-insensitively.</param>
    /// <returns>The recalled command, or <see langword="null"/> when nothing older matches.</returns>
    public string? Previous(string? prefix)
    {
        for (int i = _cursor + 1; i < _entries.Count; i++)
        {
            if (Matches(_entries[i], prefix))
            {
                _cursor = i;
                return _entries[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Steps the recall cursor one entry towards the newest command.
    /// </summary>
    /// <returns>
    /// The recalled command, or <see langword="null"/> once the cursor has stepped off the newest
    /// entry and back onto the line the user was typing.
    /// </returns>
    public string? Next() => Next(string.Empty);

    /// <summary>
    /// Steps the recall cursor towards the newest command that starts with
    /// <paramref name="prefix"/>; see <see cref="Previous(string?)"/>.
    /// </summary>
    /// <param name="prefix">What the recalled command must start with; compared case-insensitively.</param>
    /// <returns>
    /// The recalled command, or <see langword="null"/> once the cursor has stepped off the newest
    /// match and back onto the line the user was typing.
    /// </returns>
    public string? Next(string? prefix)
    {
        if (_cursor < 0)
        {
            return null;
        }

        for (int i = _cursor - 1; i >= 0; i--)
        {
            if (Matches(_entries[i], prefix))
            {
                _cursor = i;
                return _entries[i];
            }
        }

        _cursor = -1;
        return null;
    }

    /// <summary>
    /// The newest command that starts with <paramref name="prefix"/> and goes on past it - what
    /// the command line shows as a ghost completion while the user types.
    /// </summary>
    /// <param name="prefix">The text typed so far.</param>
    /// <returns>The suggested command, or <see langword="null"/> when nothing qualifies.</returns>
    public string? Suggest(string? prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return null;
        }

        foreach (string entry in _entries)
        {
            if (entry.Length > prefix.Length && Matches(entry, prefix))
            {
                return entry;
            }
        }

        return null;
    }

    private static bool Matches(string entry, string? prefix) =>
        string.IsNullOrEmpty(prefix) || entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Puts the recall cursor back on the line the user is typing.</summary>
    public void ResetCursor() => _cursor = -1;

    /// <summary>Forgets every command.</summary>
    public void Clear()
    {
        _entries.Clear();
        _cursor = -1;
    }
}

/// <summary>The on-disk shape of <see cref="CommandHistory"/>.</summary>
public sealed class CommandHistoryFile
{
    /// <summary>The remembered commands, newest first.</summary>
    public List<string> Commands { get; set; } = [];
}

/// <summary>
/// Source-generated serialisation metadata for <see cref="CommandHistoryFile"/>, so history I/O
/// stays trimming, AOT and single-file friendly.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(CommandHistoryFile))]
public sealed partial class CommandHistoryJsonContext : JsonSerializerContext
{
}
