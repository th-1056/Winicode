namespace Winicode;

public record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public bool ShowDockButton { get; init; } = true;
    public bool EnableZeroKey { get; init; } = true;
    public bool CarryPreedit { get; init; } = true;
    public bool NumericSelection { get; init; } = true;
    public string Theme { get; init; } = "system";
    public string FontPreset { get; init; } = "default";
    public string[] ExcludedApplications { get; init; } = [];
    public bool CheckUpdatesAutomatically { get; init; } = false;
    public bool FirstRunCompleted { get; init; } = false;

    public AppSettings Normalize()
    {
        var theme = Theme?.Trim().ToLowerInvariant();
        var font = FontPreset?.Trim().ToLowerInvariant();
        return this with
        {
            SchemaVersion = 1,
            Theme = theme is "system" or "light" or "dark" ? theme : "system",
            FontPreset = font is "small" or "default" or "large" ? font : "default",
            ExcludedApplications = (ExcludedApplications ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                // Store executable names only; paths and window titles do not belong here.
                .Where(name => name.Length <= 260 && name.IndexOfAny(['\\', '/', ':']) < 0
                    && !name.Any(char.IsControl))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(256).ToArray()
        };
    }
}
