using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dvopan.Core;

/// <summary>
/// Source-generated serialisation metadata for <see cref="Settings"/>. Using the generated context
/// instead of reflection keeps settings I/O trimming, AOT and single-file friendly.
/// </summary>
/// <remarks>
/// The reader is deliberately forgiving - case-insensitive names, comments skipped, trailing commas
/// allowed - because the settings file is meant to be hand-editable.
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(Settings))]
public sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
