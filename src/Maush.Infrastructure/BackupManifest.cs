namespace Maush.Infrastructure;

public sealed record BackupManifest
{
    public const int CurrentBackupVersion = 1;

    public int BackupVersion { get; init; } = CurrentBackupVersion;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> Sha256ByPath { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
