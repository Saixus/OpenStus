namespace OpenCommander.Input;

/// <summary>
/// The set of modifier keys held down when an input event was produced.
/// </summary>
/// <remarks>
/// Left and right variants of a modifier are folded into a single flag: the panel key bar and the
/// key binding table never distinguish them. AltGr (which Windows reports as
/// LeftCtrl+RightAlt) is deliberately reported as <see cref="None"/> by the Windows backend when it
/// produced a printable character, so that AltGr text input is not mistaken for a Ctrl+Alt chord.
/// </remarks>
[Flags]
public enum KeyMods
{
    /// <summary>No modifier is held.</summary>
    None = 0,

    /// <summary>Either Shift key is held.</summary>
    Shift = 1,

    /// <summary>Either Ctrl key is held.</summary>
    Ctrl = 2,

    /// <summary>Either Alt key is held.</summary>
    Alt = 4,
}
