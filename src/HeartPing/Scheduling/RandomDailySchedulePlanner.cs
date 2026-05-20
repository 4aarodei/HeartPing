using System.Security.Cryptography;
using System.Text;

namespace HeartPing;

internal sealed class RandomDailySchedulePlanner
{
    public ScheduleDecision Decide(DateTimeOffset nowUtc, ScheduleOptions options)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        var window = FindCurrentOrNextWindow(nowLocal.DateTime, options);

        if (!window.ContainsLocalTime)
        {
            return ScheduleDecision.Skip($"Skipped: local time {nowLocal:yyyy-MM-dd HH:mm} is outside {options.WindowStart:hh\\:mm}-{options.WindowEnd:hh\\:mm} ({options.TimeZoneId}).");
        }

        if (options.UsesRollingInterval)
        {
            return ScheduleDecision.Send(nowLocal.DateTime, $"Rolling interval due: {nowLocal:yyyy-MM-dd HH:mm} local time.");
        }

        foreach (var slot in BuildSlots(window.Start, window.End, options))
        {
            if (slot <= nowLocal.DateTime)
            {
                var dueUntil = slot.AddMinutes(options.DecisionGraceMinutes);
                if (nowLocal.DateTime <= dueUntil)
                {
                    return ScheduleDecision.Send(slot, $"Slot due: {slot:yyyy-MM-dd HH:mm} local time.");
                }

                continue;
            }

            return ScheduleDecision.Skip($"Skipped: no slot is due now; next slot {slot:HH:mm}.");
        }

        return ScheduleDecision.Skip("Skipped: no slot is due now; no more slots today.");
    }

    public WatchDelayPlan PlanNextWakeUp(DateTimeOffset nowUtc, ScheduleOptions options, DateTimeOffset? notBeforeUtc = null)
    {
        if (options.UsesRollingInterval)
        {
            return PlanNextRollingWakeUp(nowUtc, options, notBeforeUtc);
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
        var effectiveUtc = notBeforeUtc is not null && notBeforeUtc > nowUtc ? notBeforeUtc.Value : nowUtc;
        var nowLocal = TimeZoneInfo.ConvertTime(effectiveUtc, timeZone);

        foreach (var window in BuildCandidateWindows(nowLocal.Date.AddDays(-1), options, 5))
        {
            if (window.End <= nowLocal.DateTime)
            {
                continue;
            }

            if (nowLocal.DateTime < window.Start)
            {
                return BuildDelayPlan(nowUtc, timeZone, window.Start, $"Sleeping until the message window opens at {window.Start:yyyy-MM-dd HH:mm}.");
            }

            foreach (var slot in BuildSlots(window.Start, window.End, options))
            {
                if (slot > nowLocal.DateTime)
                {
                    return BuildDelayPlan(nowUtc, timeZone, slot, $"Sleeping until next scheduled slot at {slot:yyyy-MM-dd HH:mm} local time.");
                }
            }
        }

        var fallbackWake = nowLocal.Date.AddDays(1) + options.WindowStart;
        return BuildDelayPlan(nowUtc, timeZone, fallbackWake, $"Sleeping until fallback wake at {fallbackWake:yyyy-MM-dd HH:mm}.");
    }

    private static WatchDelayPlan PlanNextRollingWakeUp(DateTimeOffset nowUtc, ScheduleOptions options, DateTimeOffset? notBeforeUtc)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
        var effectiveUtc = notBeforeUtc is not null && notBeforeUtc > nowUtc ? notBeforeUtc.Value : nowUtc;
        var effectiveLocal = TimeZoneInfo.ConvertTime(effectiveUtc, timeZone).DateTime;
        var window = FindCurrentOrNextWindow(effectiveLocal, options);

        if (!window.ContainsLocalTime)
        {
            return BuildDelayPlan(nowUtc, timeZone, window.Start, $"Sleeping until the message window opens at {window.Start:yyyy-MM-dd HH:mm}.");
        }

        var remainingMinutes = (int)Math.Floor((window.End - effectiveLocal).TotalMinutes);
        if (remainingMinutes < options.RollingMinMinutes)
        {
            var nextWindow = FindNextWindowAfter(window.End, options);
            return BuildDelayPlan(nowUtc, timeZone, nextWindow.Start, $"Sleeping until the next message window opens at {nextWindow.Start:yyyy-MM-dd HH:mm}.");
        }

        var maxDelayMinutes = Math.Min(options.RollingMaxMinutes, remainingMinutes);
        var delayMinutes = RandomNumberGenerator.GetInt32(options.RollingMinMinutes, maxDelayMinutes + 1);
        var wakeLocalTime = effectiveLocal.AddMinutes(delayMinutes);
        return BuildDelayPlan(nowUtc, timeZone, wakeLocalTime, $"Sleeping until next rolling interval at {wakeLocalTime:yyyy-MM-dd HH:mm} local time.");
    }

    private static IEnumerable<DateTime> BuildSlots(DateTime windowStart, DateTime windowEnd, ScheduleOptions options)
    {
        var windowMinutes = (int)Math.Floor((windowEnd - windowStart).TotalMinutes);
        var messagesPerDay = options.MessagesPerDay;
        var minimumGapMinutes = Math.Max(0, options.MinimumGapMinutes);
        var random = new Random(StableSeed($"{windowStart:yyyy-MM-dd}|{options.SeedSalt}|{messagesPerDay}|{minimumGapMinutes}"));
        var selectedMinutes = new List<int>(messagesPerDay);
        var maxAttempts = Math.Max(1000, messagesPerDay * windowMinutes * 4);

        for (var attempt = 0; attempt < maxAttempts && selectedMinutes.Count < messagesPerDay; attempt++)
        {
            var minute = random.Next(0, windowMinutes);
            if (selectedMinutes.Any(existing => Math.Abs(existing - minute) < minimumGapMinutes))
            {
                continue;
            }

            selectedMinutes.Add(minute);
        }

        if (selectedMinutes.Count < messagesPerDay)
        {
            throw new InvalidOperationException($"Cannot fit {messagesPerDay} random message slots into the configured window with a {minimumGapMinutes}-minute minimum gap.");
        }

        selectedMinutes.Sort();
        foreach (var minute in selectedMinutes)
        {
            yield return windowStart.AddMinutes(minute);
        }
    }

    public static int StableSeed(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToInt32(bytes, 0);
    }

    private static ScheduleWindow FindCurrentOrNextWindow(DateTime localTime, ScheduleOptions options)
    {
        ScheduleWindow? next = null;

        foreach (var window in BuildCandidateWindows(localTime.Date.AddDays(-1), options, 4))
        {
            if (localTime >= window.Start && localTime < window.End)
            {
                return window with { ContainsLocalTime = true };
            }

            if (window.Start > localTime && (next is null || window.Start < next.Start))
            {
                next = window;
            }
        }

        return next ?? BuildWindow(localTime.Date.AddDays(1), options);
    }

    private static ScheduleWindow FindNextWindowAfter(DateTime localTime, ScheduleOptions options)
    {
        foreach (var window in BuildCandidateWindows(localTime.Date, options, 4))
        {
            if (window.Start >= localTime)
            {
                return window;
            }
        }

        return BuildWindow(localTime.Date.AddDays(1), options);
    }

    private static IEnumerable<ScheduleWindow> BuildCandidateWindows(DateTime startDate, ScheduleOptions options, int days)
    {
        for (var dayOffset = 0; dayOffset < days; dayOffset++)
        {
            yield return BuildWindow(startDate.Date.AddDays(dayOffset), options);
        }
    }

    private static ScheduleWindow BuildWindow(DateTime baseDate, ScheduleOptions options)
    {
        var windowStart = baseDate.Date + options.WindowStart;
        var windowEnd = baseDate.Date + options.WindowEnd;

        if (windowEnd <= windowStart)
        {
            windowEnd = windowEnd.AddDays(1);
        }

        return new ScheduleWindow(windowStart, windowEnd, ContainsLocalTime: false);
    }

    private static WatchDelayPlan BuildDelayPlan(
        DateTimeOffset nowUtc,
        TimeZoneInfo timeZone,
        DateTime wakeLocalTime,
        string explanation)
    {
        var wakeUtc = TimeZoneInfo.ConvertTimeToUtc(wakeLocalTime, timeZone);
        var delay = wakeUtc - nowUtc.UtcDateTime;
        if (delay < TimeSpan.FromSeconds(1))
        {
            delay = TimeSpan.FromSeconds(1);
        }

        return new WatchDelayPlan(delay, wakeLocalTime, explanation);
    }

    private sealed record ScheduleWindow(DateTime Start, DateTime End, bool ContainsLocalTime);
}
