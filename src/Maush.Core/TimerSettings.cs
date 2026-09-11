namespace Maush.Core;

public sealed record TimerSettings
{
    public const int MinimumMinutes = 1;
    public const int MaximumMinutes = 999;

    public int WorkDurationMinutes { get; init; } = 30;
    public int RestDurationMinutes { get; init; } = 5;
    public bool IsLoopEnabled { get; init; } = true;
    public string? RestReminderAudioId { get; init; }
    public string? ResumeWorkReminderAudioId { get; init; }

    public TimeSpan WorkDuration => TimeSpan.FromMinutes(WorkDurationMinutes);
    public TimeSpan RestDuration => TimeSpan.FromMinutes(RestDurationMinutes);

    public void Validate()
    {
        ValidateMinutes(WorkDurationMinutes, nameof(WorkDurationMinutes));
        ValidateMinutes(RestDurationMinutes, nameof(RestDurationMinutes));
    }

    private static void ValidateMinutes(int value, string parameterName)
    {
        if (value is < MinimumMinutes or > MaximumMinutes)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"时长必须在 {MinimumMinutes} 到 {MaximumMinutes} 分钟之间。");
        }
    }
}
