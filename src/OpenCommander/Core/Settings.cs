using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCommander.Core;

/// <summary>
/// How much colour the renderer writes - the <c>--colors</c> option and the <c>colors</c> setting.
/// </summary>
/// <remarks>
/// The 16 indexed ANSI colours are only names: the terminal's own scheme decides what they look
/// like, and under Windows Terminal's default "Campbell" scheme blue and cyan are two neighbouring
/// blues, which washes the panels out. <see cref="TrueColor"/> writes literal RGB from a
/// <see cref="Rendering.Palette"/> instead, pinning the classic look; <see cref="Indexed"/> is the
/// escape hatch for anyone who themes their terminal deliberately.
/// </remarks>
[JsonConverter(typeof(ColorModeJsonConverter))]
public enum ColorMode
{
    /// <summary>Ask <see cref="Rendering.ColorDepthDetector"/> what the terminal can be trusted with.</summary>
    Auto,

    /// <summary>Always write 24-bit colour, whatever the terminal advertises.</summary>
    TrueColor,

    /// <summary>Always write the 16 indexed slots, leaving the terminal's own scheme in charge.</summary>
    Indexed,
}

/// <summary>
/// Reads and writes <see cref="ColorMode"/> as the same words <c>--colors</c> accepts.
/// </summary>
/// <remarks>
/// Deliberately forgiving on the way in: an unrecognised value falls back to
/// <see cref="ColorMode.Auto"/> rather than throwing, so one mistyped colour word cannot discard
/// every other entry in the settings file.
/// </remarks>
public sealed class ColorModeJsonConverter : JsonConverter<ColorMode>
{
    /// <summary>The canonical spelling of a mode, as written to the settings file.</summary>
    /// <param name="value">The mode.</param>
    /// <returns><c>auto</c>, <c>truecolor</c> or <c>indexed</c>.</returns>
    public static string ToText(ColorMode value) => value switch
    {
        ColorMode.TrueColor => "truecolor",
        ColorMode.Indexed => "indexed",
        _ => "auto",
    };

    /// <inheritdoc/>
    public override ColorMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            && CommandLineArgs.TryParseColorMode(reader.GetString(), out ColorMode mode)
                ? mode
                : ColorMode.Auto;

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ColorMode value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(ToText(value));
    }
}

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

    /// <summary>Colour the viewer and editor text by file type (C#, JSON, SQL, ...).</summary>
    public bool SyntaxHighlight { get; set; } = true;

    /// <summary>
    /// Remember each panel's tabs on exit and open them again next time, unless the command line
    /// names folders of its own.
    /// </summary>
    public bool RememberTabs { get; set; } = true;

    /// <summary>Ask before deleting.</summary>
    public bool ConfirmDelete { get; set; } = true;

    /// <summary>
    /// Ask a second time before deleting a folder that has content - Far keeps this question
    /// separate from the general delete confirmation, and so does this flag.
    /// </summary>
    public bool ConfirmDeleteNonEmptyFolders { get; set; } = true;

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
    /// How much colour the renderer writes; <see cref="ColorMode.Auto"/> detects it from the
    /// terminal. Overridden for one run by <c>--colors</c>, and by the <c>NO_COLOR</c> environment
    /// variable - see <see cref="Application.ResolveColorDepth(CommandLineArgs, Settings)"/>.
    /// </summary>
    public ColorMode Colors { get; set; } = ColorMode.Auto;

    /// <summary>
    /// Path of a palette file giving the RGB behind the 16 colour slots, or <see langword="null"/>
    /// for the built-in classic VGA table. Only consulted in
    /// <see cref="Rendering.ColorDepth.TrueColor"/>; a missing or malformed file falls back to the
    /// built-in one rather than failing the start-up.
    /// </summary>
    public string? PalettePath { get; set; }

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
