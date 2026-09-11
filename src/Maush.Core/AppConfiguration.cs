namespace Maush.Core;

public sealed record AppConfiguration
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public TimerSettings Timer { get; init; } = new();
    public Dictionary<string, AudioAsset> AudioAssets { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
