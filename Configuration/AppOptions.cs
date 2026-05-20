namespace HeartPing;

internal sealed class AppOptions
{
    public TelegramOptions Telegram { get; set; } = new();
    public MessageOptions Messages { get; set; } = new();
    public ScheduleOptions Schedule { get; set; } = new();
    public SafetyOptions Safety { get; set; } = new();

    public void Validate(bool loginOnly, bool dryRun)
    {
        if (!dryRun && Telegram.ApiId <= 0)
        {
            throw new InvalidOperationException("Telegram ApiId is required. Set Telegram:ApiId or HEARTPING_TELEGRAM_API_ID.");
        }

        if (!dryRun && string.IsNullOrWhiteSpace(Telegram.ApiHash))
        {
            throw new InvalidOperationException("Telegram ApiHash is required. Set Telegram:ApiHash or HEARTPING_TELEGRAM_API_HASH.");
        }

        if (!dryRun && string.IsNullOrWhiteSpace(Telegram.PhoneNumber))
        {
            throw new InvalidOperationException("Telegram PhoneNumber is required. Set Telegram:PhoneNumber or HEARTPING_TELEGRAM_PHONE_NUMBER.");
        }

        if (!loginOnly && !dryRun && string.IsNullOrWhiteSpace(Telegram.TargetUsername) && Telegram.TargetUserId is null && string.IsNullOrWhiteSpace(Telegram.TargetPhoneNumber))
        {
            throw new InvalidOperationException("Target is required. Set TargetUsername, TargetUserId, or TargetPhoneNumber.");
        }

        if (!Schedule.UsesRollingInterval && !Schedule.UsesDailySlots)
        {
            throw new InvalidOperationException("Schedule mode must be rollingInterval or dailySlots.");
        }

        if (Schedule.MessagesPerDay <= 0)
        {
            throw new InvalidOperationException("MessagesPerDay must be greater than zero.");
        }

        if (Schedule.MinimumGapMinutes < 0)
        {
            throw new InvalidOperationException("MinimumGapMinutes cannot be negative.");
        }

        var windowEnd = Schedule.WindowEnd <= Schedule.WindowStart
            ? Schedule.WindowEnd.Add(TimeSpan.FromDays(1))
            : Schedule.WindowEnd;
        var windowMinutes = (int)Math.Floor((windowEnd - Schedule.WindowStart).TotalMinutes);
        if (windowMinutes <= 0)
        {
            throw new InvalidOperationException("Message window must be at least one minute long.");
        }

        if (Schedule.UsesDailySlots && Schedule.MessagesPerDay > windowMinutes)
        {
            throw new InvalidOperationException("MessagesPerDay cannot be greater than the number of minutes in the message window.");
        }

        if (Schedule.RollingMinMinutes <= 0)
        {
            throw new InvalidOperationException("RollingMinMinutes must be greater than zero.");
        }

        if (Schedule.RollingMaxMinutes < Schedule.RollingMinMinutes)
        {
            throw new InvalidOperationException("RollingMaxMinutes cannot be less than RollingMinMinutes.");
        }
    }
}

internal sealed class TelegramOptions
{
    public int ApiId { get; set; }
    public string ApiHash { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string SessionPath { get; set; } = "data/WTelegram.session";
    public string SessionKey { get; set; } = "";
    public string TargetUsername { get; set; } = "";
    public long? TargetUserId { get; set; }
    public string TargetPhoneNumber { get; set; } = "";
}

internal sealed class MessageOptions
{
    public string FilePath { get; set; } = "messages.txt";
}

internal sealed class ScheduleOptions
{
    public string Mode { get; set; } = "rollingInterval";
    public string TimeZoneId { get; set; } = "Europe/Kyiv";
    public TimeSpan WindowStart { get; set; } = new(11, 0, 0);
    public TimeSpan WindowEnd { get; set; } = new(23, 0, 0);
    public int MessagesPerDay { get; set; } = 10;
    public int MinimumGapMinutes { get; set; } = 10;
    public int RollingMinMinutes { get; set; } = 30;
    public int RollingMaxMinutes { get; set; } = 90;
    public int DecisionGraceMinutes { get; set; } = 2;
    public bool SendOnWatchStart { get; set; } = false;
    public string SeedSalt { get; set; } = "change-me";

    public bool UsesRollingInterval =>
        string.Equals(Mode, "rollingInterval", StringComparison.OrdinalIgnoreCase);

    public bool UsesDailySlots =>
        string.Equals(Mode, "dailySlots", StringComparison.OrdinalIgnoreCase);
}

internal sealed class SafetyOptions
{
    public int MinMinutesBetweenSentMessages { get; set; } = 90;
    public int RecentHistoryLimit { get; set; } = 30;
    public bool RespectRecentOutgoingMessages { get; set; } = true;
}
