namespace HeartPing;

internal sealed record ScheduleDecision(bool ShouldSend, DateTime? ScheduledLocalTime, string Explanation)
{
    public static ScheduleDecision Send(DateTime scheduledLocalTime, string explanation) =>
        new(true, scheduledLocalTime, explanation);

    public static ScheduleDecision Skip(string explanation) =>
        new(false, null, explanation);
}

internal sealed record WatchDelayPlan(TimeSpan Delay, DateTime WakeLocalTime, string Explanation);
