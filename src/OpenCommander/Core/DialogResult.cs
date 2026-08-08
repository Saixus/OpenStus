namespace OpenCommander.Core;

/// <summary>
/// How a modal dialog ended. <see cref="None"/> is the value of a dialog that is still open, so
/// callers can use it as "no answer yet" without a nullable.
/// </summary>
public enum DialogResult
{
    /// <summary>The dialog has not produced an answer (still open, or dismissed without one).</summary>
    None,

    /// <summary>The user confirmed with OK.</summary>
    Ok,

    /// <summary>The user cancelled (Esc or the Cancel button).</summary>
    Cancel,

    /// <summary>The user answered Yes.</summary>
    Yes,

    /// <summary>The user answered No.</summary>
    No,

    /// <summary>The user asked to retry the failed operation.</summary>
    Retry,

    /// <summary>The user asked to skip this item.</summary>
    Skip,

    /// <summary>The user asked to skip this item and every later one that asks the same question.</summary>
    SkipAll,

    /// <summary>The user applied the answer to every remaining item.</summary>
    All,

    /// <summary>Append to the existing destination file.</summary>
    Append,

    /// <summary>Overwrite the existing destination file.</summary>
    Overwrite,

    /// <summary>Write under a different name.</summary>
    Rename,
}

/// <summary>
/// The buttons a message box offers. Combine with <c>|</c>; the buttons are laid out left to right
/// in the order the flags are declared here, which matches Far Manager.
/// </summary>
[Flags]
public enum MessageButtons
{
    /// <summary>The OK button.</summary>
    Ok = 1,

    /// <summary>The Cancel button.</summary>
    Cancel = 2,

    /// <summary>The Yes button.</summary>
    Yes = 4,

    /// <summary>The No button.</summary>
    No = 8,

    /// <summary>The Retry button.</summary>
    Retry = 16,

    /// <summary>The Skip button.</summary>
    Skip = 32,

    /// <summary>The "Skip all" button.</summary>
    SkipAll = 64,

    /// <summary>The "All" button.</summary>
    All = 128,
}
