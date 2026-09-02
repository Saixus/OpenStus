using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dvopan.Core;

/// <summary>
/// What the shell remembers between runs beyond the settings: which folders each panel had open in
/// its tabs, which tab was showing, and which panel had the focus. Written on exit, read on the
/// next start when the command line names no folders of its own.
/// </summary>
/// <remarks>
/// Nothing here throws: a missing, unreadable or malformed file yields <see langword="null"/> from
/// <see cref="Load"/>, and a configuration directory that cannot be written to makes
/// <see cref="Save"/> answer <see langword="false"/> and keep quiet - losing the tab list is never
/// worth a dialog at shutdown.
/// </remarks>
public sealed class SessionState
{
    /// <summary>The file name used under the configuration directory.</summary>
    public const string FileName = "session.json";

    /// <summary>The left panel's tabs.</summary>
    public PanelSession Left { get; set; } = new();

    /// <summary>The right panel's tabs.</summary>
    public PanelSession Right { get; set; } = new();

    /// <summary>Whether the left panel had the focus.</summary>
    public bool LeftActive { get; set; } = true;

    /// <summary>Where <see cref="Load"/> and <see cref="Save"/> look: <c>session.json</c> beside the settings file.</summary>
    public static string DefaultFilePath => Path.Combine(Settings.ConfigDirectory, FileName);

    /// <summary>Reads the session from <see cref="DefaultFilePath"/>; <see langword="null"/> when there is none.</summary>
    public static SessionState? Load() => LoadFrom(DefaultFilePath);

    /// <summary>Reads a session file; <see langword="null"/> when it is missing or unreadable.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The session, or <see langword="null"/>.</returns>
    public static SessionState? LoadFrom(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            SessionState? state = JsonSerializer.Deserialize(json, SessionStateJsonContext.Default.SessionState);
            if (state is null)
            {
                return null;
            }

            state.Left ??= new PanelSession();
            state.Right ??= new PanelSession();
            state.Left.Tabs ??= [];
            state.Right.Tabs ??= [];
            return state;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Writes the session to <see cref="DefaultFilePath"/>. Never throws.</summary>
    /// <returns><see langword="true"/> when the file was written.</returns>
    public bool Save() => SaveTo(DefaultFilePath);

    /// <summary>Writes the session to an explicit path, creating the directory if needed. Never throws.</summary>
    /// <param name="path">The file to write.</param>
    /// <returns><see langword="true"/> when the file was written.</returns>
    public bool SaveTo(string? path)
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

            File.WriteAllText(full, JsonSerializer.Serialize(this, SessionStateJsonContext.Default.SessionState));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>One panel's tabs as remembered between runs.</summary>
public sealed class PanelSession
{
    /// <summary>The folder of each tab, in strip order.</summary>
    public List<string> Tabs { get; set; } = [];

    /// <summary>The index of the tab that was showing.</summary>
    public int Active { get; set; }
}

/// <summary>
/// Source-generated serialisation metadata for <see cref="SessionState"/>, so session I/O stays
/// trimming, AOT and single-file friendly.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(SessionState))]
public sealed partial class SessionStateJsonContext : JsonSerializerContext
{
}
