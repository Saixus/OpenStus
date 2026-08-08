using System.Text.Json;

namespace OpenCommander.Core;

/// <summary>
/// The persisted user preferences.
/// </summary>
/// <remarks>
/// <para>
/// Every property has the Far Manager default as its initialiser, so a brand new
/// <see cref="Settings"/> is already the shipping configuration and a settings file only has to
/// carry the entries the user changed.
/// </para>
/// <para>
/// Loading never throws: a missing, unreadable or malformed file yields the defaults. That keeps a
/// corrupt settings file from bricking the application.
/// </para>
/// </remarks>
public sealed class Settings
{
    /// <summary>The folder name used under the platform configuration directory.</summary>
    public const string AppFolderName = "OpenCommander";

    /// <summary>The settings file name.</summary>
    public const string FileName = "settings.json";

    /// <summary>Show files and folders carrying the Hidden or System attribute (Ctrl+H).</summary>
    public bool ShowHiddenFiles { get; set; } = true;

    /// <summary>Sort directories ahead of files.</summary>
    public bool DirectoriesFirst { get; set; } = true;

    /// <summary>Compare embedded digit runs numerically, so <c>file2</c> sorts before <c>file10</c>.</summary>
    public bool NumericSort { get; set; }

    /// <summary>Compare names case sensitively.</summary>
    public bool CaseSensitiveSort { get; set; }

    /// <summary>Draw the per-panel status line above the bottom frame.</summary>
    public bool ShowStatusBar { get; set; } = true;

    /// <summary>Draw the function key bar on the bottom screen row (Ctrl+B).</summary>
    public bool ShowKeyBar { get; set; } = true;

    /// <summary>Draw the clock in the top-right corner.</summary>
    public bool ShowClock { get; set; } = true;

    /// <summary>Draw the command line above the key bar.</summary>
    public bool ShowCommandLine { get; set; } = true;

    /// <summary>Ask before deleting.</summary>
    public bool ConfirmDelete { get; set; } = true;

    /// <summary>Ask before overwriting an existing destination file.</summary>
    public bool ConfirmOverwrite { get; set; } = true;

    /// <summary>Delete to the recycle bin rather than permanently, where the platform has one.</summary>
    public bool UseRecycleBin { get; set; } = true;

    /// <summary>Drop the tag marks once a copy, move or delete has finished.</summary>
    public bool ClearSelectionAfterOperation { get; set; } = true;

    /// <summary>Follow the active panel with the process working directory.</summary>
    /// <remarks>
    /// Applied by <see cref="Application.SyncWorkingDirectory"/> after every navigation and every
    /// active-panel change. A folder that cannot become the working directory is ignored, so the
    /// option can never break navigation.
    /// </remarks>
    public bool AutoChangeDirectory { get; set; }

    /// <summary>Path of a theme file to load instead of the built-in palette, or <see langword="null"/>.</summary>
    public string? ThemePath { get; set; }

    /// <summary>
    /// Where <see cref="Load"/> and <see cref="Save"/> look: <c>%APPDATA%\OpenCommander\settings.json</c>
    /// on Windows, <c>$XDG_CONFIG_HOME/OpenCommander/settings.json</c> (defaulting to
    /// <c>~/.config</c>) elsewhere.
    /// </summary>
    public static string SettingsFilePath => Path.Combine(ConfigDirectory, FileName);

    /// <summary>The directory <see cref="SettingsFilePath"/> lives in.</summary>
    public static string ConfigDirectory
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                string appData = Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData,
                    Environment.SpecialFolderOption.DoNotVerify);

                if (!string.IsNullOrEmpty(appData))
                {
                    return Path.Combine(appData, AppFolderName);
                }
            }
            else
            {
                string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (!string.IsNullOrEmpty(xdg))
                {
                    return Path.Combine(xdg, AppFolderName);
                }

                string home = Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile,
                    Environment.SpecialFolderOption.DoNotVerify);

                if (!string.IsNullOrEmpty(home))
                {
                    return Path.Combine(home, ".config", AppFolderName);
                }
            }

            return Path.Combine(AppContext.BaseDirectory, AppFolderName);
        }
    }

    /// <summary>
    /// Loads the settings from <see cref="SettingsFilePath"/>, falling back to the defaults for a
    /// missing, unreadable or malformed file. Never throws.
    /// </summary>
    /// <returns>The loaded settings, or a default instance.</returns>
    public static Settings Load() => LoadFrom(SettingsFilePath);

    /// <summary>
    /// Loads the settings from an explicit path, falling back to the defaults for a missing,
    /// unreadable or malformed file. Never throws.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The loaded settings, or a default instance.</returns>
    public static Settings LoadFrom(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new Settings();
            }

            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Settings();
            }

            return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.Settings) ?? new Settings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            return new Settings();
        }
    }

    /// <summary>
    /// Writes the settings to <see cref="SettingsFilePath"/>, creating the directory if needed.
    /// Never throws; a read-only or unavailable configuration directory silently does nothing.
    /// </summary>
    public void Save() => SaveTo(SettingsFilePath);

    /// <summary>
    /// Writes the settings to an explicit path, creating the directory if needed. Never throws.
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

            File.WriteAllText(full, ToJson());
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Serialises the settings to the same indented JSON <see cref="Save"/> writes.</summary>
    /// <returns>The JSON text.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, SettingsJsonContext.Default.Settings);

    /// <summary>
    /// Parses settings from JSON text. Unknown members are ignored and missing ones keep their
    /// default, so an older or newer file still loads.
    /// </summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The parsed settings, or a default instance when the text is empty or <c>null</c>.</returns>
    /// <exception cref="JsonException">The text is not valid JSON.</exception>
    public static Settings FromJson(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? new Settings()
            : JsonSerializer.Deserialize(json, SettingsJsonContext.Default.Settings) ?? new Settings();

    /// <summary>Returns an independent copy of these settings.</summary>
    /// <returns>The copy.</returns>
    public Settings Clone() => (Settings)MemberwiseClone();
}
