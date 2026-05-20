using System.Text.Json;

namespace HeartPing;

internal static class AppOptionsLoader
{
    public static AppOptions Load(string path)
    {
        var resolvedPath = ResolvePathFromBaseDirectory(path);
        AppOptions options;
        if (File.Exists(resolvedPath))
        {
            var json = File.ReadAllText(resolvedPath);
            options = JsonSerializer.Deserialize<AppOptions>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? new AppOptions();
        }
        else
        {
            options = new AppOptions();
        }

        ApplyEnvironment(options);
        NormalizePaths(options);
        return options;
    }

    private static void ApplyEnvironment(AppOptions options)
    {
        options.Telegram.ApiId = GetInt("HEARTPING_TELEGRAM_API_ID", options.Telegram.ApiId);
        options.Telegram.ApiHash = Get("HEARTPING_TELEGRAM_API_HASH", options.Telegram.ApiHash);
        options.Telegram.PhoneNumber = Get("HEARTPING_TELEGRAM_PHONE_NUMBER", options.Telegram.PhoneNumber);
        options.Telegram.SessionPath = Get("HEARTPING_TELEGRAM_SESSION_PATH", options.Telegram.SessionPath);
        options.Telegram.SessionKey = Get("HEARTPING_TELEGRAM_SESSION_KEY", options.Telegram.SessionKey);
        options.Telegram.TargetUsername = Get("HEARTPING_TARGET_USERNAME", options.Telegram.TargetUsername);
        options.Telegram.TargetPhoneNumber = Get("HEARTPING_TARGET_PHONE_NUMBER", options.Telegram.TargetPhoneNumber);
        options.Telegram.TargetUserId = GetLong("HEARTPING_TARGET_USER_ID", options.Telegram.TargetUserId);

        options.Messages.FilePath = Get("HEARTPING_MESSAGES_FILE", options.Messages.FilePath);

        options.Schedule.Mode = Get("HEARTPING_SCHEDULE_MODE", options.Schedule.Mode);
        options.Schedule.TimeZoneId = Get("HEARTPING_TIME_ZONE", options.Schedule.TimeZoneId);
        options.Schedule.WindowStart = GetTime("HEARTPING_WINDOW_START", options.Schedule.WindowStart);
        options.Schedule.WindowEnd = GetTime("HEARTPING_WINDOW_END", options.Schedule.WindowEnd);
        options.Schedule.MessagesPerDay = GetInt("HEARTPING_MESSAGES_PER_DAY", options.Schedule.MessagesPerDay);
        options.Schedule.MinimumGapMinutes = GetInt("HEARTPING_MINIMUM_GAP_MINUTES", options.Schedule.MinimumGapMinutes);
        options.Schedule.RollingMinMinutes = GetInt("HEARTPING_ROLLING_MIN_MINUTES", options.Schedule.RollingMinMinutes);
        options.Schedule.RollingMaxMinutes = GetInt("HEARTPING_ROLLING_MAX_MINUTES", options.Schedule.RollingMaxMinutes);
        options.Schedule.DecisionGraceMinutes = GetInt("HEARTPING_DECISION_GRACE_MINUTES", options.Schedule.DecisionGraceMinutes);
        options.Schedule.SendOnWatchStart = GetBool("HEARTPING_SEND_ON_WATCH_START", options.Schedule.SendOnWatchStart);
        options.Schedule.SeedSalt = Get("HEARTPING_SEED_SALT", options.Schedule.SeedSalt);

        options.Safety.MinMinutesBetweenSentMessages = GetInt("HEARTPING_MIN_MINUTES_BETWEEN_MESSAGES", options.Safety.MinMinutesBetweenSentMessages);
        options.Safety.RecentHistoryLimit = GetInt("HEARTPING_RECENT_HISTORY_LIMIT", options.Safety.RecentHistoryLimit);
        options.Safety.RespectRecentOutgoingMessages = GetBool("HEARTPING_RESPECT_RECENT_OUTGOING", options.Safety.RespectRecentOutgoingMessages);
    }

    private static void NormalizePaths(AppOptions options)
    {
        options.Telegram.SessionPath = ResolvePathFromBaseDirectory(options.Telegram.SessionPath);
        options.Messages.FilePath = ResolvePathFromBaseDirectory(options.Messages.FilePath);
    }

    private static string ResolvePathFromBaseDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private static string Get(string name, string current) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : current;

    private static int GetInt(string name, int current) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : current;

    private static long? GetLong(string name, long? current) =>
        long.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : current;

    private static bool GetBool(string name, bool current) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : current;

    private static TimeSpan GetTime(string name, TimeSpan current) =>
        TimeSpan.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : current;
}
