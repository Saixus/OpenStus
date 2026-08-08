using OpenCommander.Rendering;
using OpenCommander.Theming;
using OpenCommander.Ui;

namespace OpenCommander.Core;

/// <summary>
/// The modal user interface, implemented on top of the dialogs and menus in
/// <c>OpenCommander.Ui</c> and the application's own modal loop.
/// </summary>
/// <remarks>
/// Every method builds a component, hands it to <see cref="Application.RunModal"/> and reads the
/// answer back off it once the loop returns. All of them are therefore blocking, and all of them are
/// re-entrant: a dialog opened from inside another dialog simply lands higher on the modal stack.
/// </remarks>
public sealed class UiServices : IUiServices
{
    private readonly Application _app;
    private readonly Dictionary<string, List<string>> _histories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the service bound to one application instance.</summary>
    /// <param name="app">The application owning the screen and the modal loop.</param>
    public UiServices(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);
        _app = app;
    }

    /// <summary>How many entries one named input history keeps.</summary>
    public const int HistoryLimit = 64;

    private Theme Theme => _app.Theme;

    /// <inheritdoc/>
    public DialogResult Message(string title, string[] lines, MessageButtons buttons, bool warning = false)
    {
        var dialog = new MessageDialog(Theme, title ?? string.Empty, lines, buttons, warning);
        RunModal(dialog);
        return dialog.Result;
    }

    /// <inheritdoc/>
    public bool Confirm(string title, string[] lines, bool warning = false) =>
        Message(title, lines, MessageButtons.Yes | MessageButtons.No, warning) == DialogResult.Yes;

    /// <inheritdoc/>
    public void Error(string title, string message) =>
        Message(
            title ?? "Error",
            SplitLines(message),
            MessageButtons.Ok,
            warning: true);

    /// <inheritdoc/>
    public string? Input(string title, string prompt, string initial = "", string? historyKey = null)
    {
        List<string>? history = historyKey is null ? null : HistoryFor(historyKey);

        var dialog = new InputDialog(Theme, title ?? string.Empty, prompt ?? string.Empty, initial ?? string.Empty, history);
        RunModal(dialog);

        string? answer = dialog.AcceptedText;
        if (answer is not null && historyKey is not null && answer.Length > 0)
        {
            Remember(historyKey, answer);
        }

        return answer;
    }

    /// <inheritdoc/>
    public int Menu(string title, IReadOnlyList<MenuItem> items, int selected = 0, Rect? position = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        var menu = new PopupMenu(Theme, title, items, selected, position);
        RunModal(menu);
        return menu.Result;
    }

    /// <inheritdoc/>
    public void RunModal(IScreenComponent component) => _app.RunModal(component);

    /// <inheritdoc/>
    public void Redraw() => _app.RenderNow();

    /// <summary>
    /// Shows a list of strings and returns the chosen index.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="items">The rows.</param>
    /// <param name="selected">The row the cursor starts on.</param>
    /// <returns>The chosen index, or <c>-1</c> when the user cancelled.</returns>
    public int List(string title, IReadOnlyList<string> items, int selected = 0)
    {
        ArgumentNullException.ThrowIfNull(items);

        var dialog = new ListDialog(Theme, title ?? string.Empty, items, selected);
        RunModal(dialog);
        return dialog.AcceptedIndex;
    }

    /// <summary>The stored entries of one named history, oldest first.</summary>
    /// <param name="key">The history name.</param>
    /// <returns>The entries; an empty list when the history is new.</returns>
    public IReadOnlyList<string> History(string key) => HistoryFor(key);

    /// <summary>Adds an entry to a named history, moving a repeat to the end.</summary>
    /// <param name="key">The history name.</param>
    /// <param name="value">The entry; empty values are ignored.</param>
    public void Remember(string key, string value)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        List<string> list = HistoryFor(key);
        list.RemoveAll(e => string.Equals(e, value, StringComparison.Ordinal));
        list.Add(value);

        while (list.Count > HistoryLimit)
        {
            list.RemoveAt(0);
        }
    }

    private List<string> HistoryFor(string key)
    {
        if (!_histories.TryGetValue(key, out List<string>? list))
        {
            list = [];
            _histories[key] = list;
        }

        return list;
    }

    private static string[] SplitLines(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return [string.Empty];
        }

        return message.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }
}
