namespace OpenStus.Files;

/// <summary>
/// The panel sort orders behind Ctrl+F3..Ctrl+F12 and the sort menu.
/// </summary>
public enum SortMode
{
    /// <summary>Leave the entries in the order the file system handed them over.</summary>
    Unsorted,

    /// <summary>By name.</summary>
    Name,

    /// <summary>By extension, then by name.</summary>
    Extension,

    /// <summary>By last write time.</summary>
    Modified,

    /// <summary>By size in bytes; directories all count as zero.</summary>
    Size,

    /// <summary>By creation time.</summary>
    Created,

    /// <summary>By last access time.</summary>
    Accessed,

    /// <summary>By file description. No description source exists yet, so this sorts by name.</summary>
    Description,

    /// <summary>By owner. No owner source exists yet, so this sorts by name.</summary>
    Owner,
}
