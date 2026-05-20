using System.Security.Cryptography;
using System.Text;
using WTelegram;

namespace HeartPing;

internal static class HeartPingApp
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        AppLog.Initialize();
        HeartPingRuntimeState.SetStatus("Starting...");
        HeartPingRuntimeState.SetNextAction("Waiting for schedule...");

        var settings = RunSettings.FromArgs(args);

        if (settings.Check)
        {
            return await RunCheckAsync(settings);
        }

        if (settings.SendNow && settings.Watch)
        {
            throw new InvalidOperationException("--send-now and --watch cannot be used together. Use --send-now for one manual test message.");
        }

        if (settings.Watch && settings.LoginOnly)
        {
            throw new InvalidOperationException("--watch and --login-only cannot be used together. Run --login-only once, then start --watch.");
        }

        if (settings.SendNow)
        {
            return await RunManualSendAsync(settings);
        }

        if (settings.Watch)
        {
            return await RunWatchAsync(settings, cancellationToken);
        }

        return await RunOnceAsync(settings, DateTimeOffset.UtcNow);
    }

    private static async Task<int> RunCheckAsync(RunSettings settings)
    {
        var options = AppOptionsLoader.Load(settings.ConfigPath);
        options.Validate(loginOnly: false, dryRun: true);

        var messages = await FileMessageSource.LoadAsync(options.Messages.FilePath);
        var planner = new RandomDailySchedulePlanner();
        var decision = planner.Decide(DateTimeOffset.UtcNow, options.Schedule);
        var resolvedConfigPath = ResolveConfigPath(settings.ConfigPath);
        var configDescription = File.Exists(resolvedConfigPath)
            ? resolvedConfigPath
            : $"{resolvedConfigPath} not found; using defaults and environment variables";

        Console.WriteLine($"Config OK: {configDescription}.");
        Console.WriteLine($"Messages OK: {messages.Count} usable messages from {options.Messages.FilePath}.");
        Console.WriteLine(decision.Explanation);
        Console.WriteLine("Check completed. No Telegram login and no message send.");
        return 0;
    }

    private static async Task<int> RunManualSendAsync(RunSettings settings)
    {
        var options = AppOptionsLoader.Load(settings.ConfigPath);
        options.Validate(loginOnly: false, dryRun: settings.DryRun);

        Directory.CreateDirectory(Path.GetDirectoryName(options.Telegram.SessionPath) ?? ".");

        Console.Write("Message to send now: ");
        var message = Console.ReadLine()?.Trim();

        if (string.IsNullOrWhiteSpace(message))
        {
            Console.WriteLine("Skipped: message is empty.");
            return 0;
        }

        if (settings.DryRun)
        {
            Console.WriteLine($"Dry run: would send now: {message}");
            return 0;
        }

        using var client = new Client(what => TelegramConfig(what, options.Telegram));
        ConfigureTelegramLogging();

        var me = await client.LoginUserIfNeeded();
        Console.WriteLine($"Logged in as {me}.");

        var telegram = new TelegramSender(client, options.Telegram);
        var target = await telegram.ResolveTargetAsync();
        var safety = await telegram.CheckSafetyAsync(target, options.Safety, DateTimeOffset.UtcNow);

        if (!safety.CanSend)
        {
            Console.WriteLine(safety.Reason);
            return 0;
        }

        await client.SendMessageAsync(target, message);
        Console.WriteLine("Manual message sent.");
        return 0;
    }

    private static async Task<int> RunWatchAsync(RunSettings settings, CancellationToken cancellationToken)
    {
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var isFirstIteration = true;
        var planner = new RandomDailySchedulePlanner();
        DateTimeOffset? notBeforeUtc = null;
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        Console.WriteLine("Watch mode started. Press Ctrl+C to stop.");
        HeartPingRuntimeState.SetStatus("Watch mode is running");
        HeartPingRuntimeState.AddHistory("Watch mode started.");

        while (!shutdown.IsCancellationRequested)
        {
            var options = AppOptionsLoader.Load(settings.ConfigPath);
            var nowUtc = DateTimeOffset.UtcNow;
            var runResult = await RunWatchIterationAsync(settings, nowUtc, options, isFirstIteration);
            isFirstIteration = false;
            if (runResult.ExitCode != 0)
            {
                return runResult.ExitCode;
            }

            notBeforeUtc = runResult.SentMessage
                ? nowUtc.AddMinutes(options.Safety.MinMinutesBetweenSentMessages)
                : null;

            var delayPlan = planner.PlanNextWakeUp(DateTimeOffset.UtcNow, options.Schedule, notBeforeUtc);
            Console.WriteLine(delayPlan.Explanation);
            Console.WriteLine($"Sleeping for {delayPlan.Delay.TotalMinutes:0} minutes.");
            HeartPingRuntimeState.SetNextAction($"Next wake: {delayPlan.WakeLocalTime:HH:mm}");
            HeartPingRuntimeState.AddHistory($"Sleep until next send check at {delayPlan.WakeLocalTime:HH:mm}.");

            try
            {
                await Task.Delay(delayPlan.Delay, shutdown.Token);
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                break;
            }
        }

        Console.WriteLine("Watch mode stopped.");
        HeartPingRuntimeState.SetStatus("Stopped");
        HeartPingRuntimeState.SetNextAction("Stopped");
        HeartPingRuntimeState.AddHistory("Watch mode stopped.");
        return 0;
    }

    private static async Task<int> RunOnceAsync(RunSettings settings, DateTimeOffset nowUtc)
    {
        var options = AppOptionsLoader.Load(settings.ConfigPath);
        var result = await RunOnceCoreAsync(settings, nowUtc, options);
        return result.ExitCode;
    }

    private static async Task<RunResult> RunWatchIterationAsync(
        RunSettings settings,
        DateTimeOffset nowUtc,
        AppOptions options,
        bool isFirstIteration)
    {
        options.Validate(settings.LoginOnly, settings.DryRun);

        if (isFirstIteration && options.Schedule.UsesRollingInterval && !options.Schedule.SendOnWatchStart)
        {
            Console.WriteLine("Watch start: rolling interval mode is waiting for the first planned interval.");
            HeartPingRuntimeState.AddHistory("Waiting for the first planned interval.");
            return RunResult.Success(sentMessage: false);
        }

        if (!isFirstIteration || !options.Schedule.SendOnWatchStart)
        {
            return await RunScheduledAttemptAsync(settings, nowUtc, options);
        }

        var startupResult = await RunStartupAttemptAsync(settings, nowUtc, options);
        if (startupResult.ExitCode != 0 || startupResult.SentMessage)
        {
            return startupResult;
        }

        return await RunScheduledAttemptAsync(settings, nowUtc, options);
    }

    private static async Task<RunResult> RunOnceCoreAsync(
        RunSettings settings,
        DateTimeOffset nowUtc,
        AppOptions options)
    {
        options.Validate(settings.LoginOnly, settings.DryRun);

        Directory.CreateDirectory(Path.GetDirectoryName(options.Telegram.SessionPath) ?? ".");

        if (settings.LoginOnly)
        {
            using var loginClient = new Client(what => TelegramConfig(what, options.Telegram));
            ConfigureTelegramLogging();
            var loggedInUser = await loginClient.LoginUserIfNeeded();
            Console.WriteLine($"Logged in as {loggedInUser}.");
            Console.WriteLine($"Login completed. Session stored at {options.Telegram.SessionPath}.");
            return RunResult.Success(sentMessage: false);
        }

        return await RunScheduledAttemptAsync(settings, nowUtc, options);
    }

    private static async Task<RunResult> RunStartupAttemptAsync(
        RunSettings settings,
        DateTimeOffset nowUtc,
        AppOptions options)
    {
        options.Validate(settings.LoginOnly, settings.DryRun);

        Directory.CreateDirectory(Path.GetDirectoryName(options.Telegram.SessionPath) ?? ".");

        var startupLocalTime = TimeZoneInfo.ConvertTime(nowUtc, TimeZoneInfo.FindSystemTimeZoneById(options.Schedule.TimeZoneId)).DateTime;
        Console.WriteLine("Watch start: sending one message immediately before switching to the normal schedule.");
        HeartPingRuntimeState.AddHistory($"Watch start message scheduled for {startupLocalTime:HH:mm}.");
        return await SendPickedMessageAsync(settings, options, nowUtc, startupLocalTime, "Dry run: would send immediately on watch start");
    }

    private static async Task<RunResult> RunScheduledAttemptAsync(
        RunSettings settings,
        DateTimeOffset nowUtc,
        AppOptions options)
    {
        var planner = new RandomDailySchedulePlanner();
        var decision = planner.Decide(nowUtc, options.Schedule);

        Console.WriteLine(decision.Explanation);
        if (!decision.ShouldSend || decision.ScheduledLocalTime is null)
        {
            HeartPingRuntimeState.SetNextAction("Waiting for next due slot...");
            return RunResult.Success(sentMessage: false);
        }

        var scheduledLocalTime = decision.ScheduledLocalTime
            ?? throw new InvalidOperationException("Schedule decision is missing the due local time.");
        HeartPingRuntimeState.SetNextAction($"Next message due: {scheduledLocalTime:HH:mm}");
        HeartPingRuntimeState.AddHistory($"Next message due at {scheduledLocalTime:HH:mm}.");

        return await SendPickedMessageAsync(settings, options, nowUtc, scheduledLocalTime, "Dry run: would send at");
    }

    private static async Task<RunResult> SendPickedMessageAsync(
        RunSettings settings,
        AppOptions options,
        DateTimeOffset nowUtc,
        DateTime messageLocalTime,
        string dryRunPrefix)
    {
        var messages = await FileMessageSource.LoadAsync(options.Messages.FilePath);
        var message = messages.PickFor(messageLocalTime, options.Schedule.SeedSalt);

        if (settings.DryRun)
        {
            Console.WriteLine($"{dryRunPrefix} {messageLocalTime:yyyy-MM-dd HH:mm}: {message}");
            HeartPingRuntimeState.AddHistory($"Dry run: \"{message}\" at {messageLocalTime:HH:mm}.");
            return RunResult.Success(sentMessage: false);
        }

        using var client = new Client(what => TelegramConfig(what, options.Telegram));
        ConfigureTelegramLogging();

        var me = await client.LoginUserIfNeeded();
        Console.WriteLine($"Logged in as {me}.");

        var telegram = new TelegramSender(client, options.Telegram);
        var target = await telegram.ResolveTargetAsync();
        var safety = await telegram.CheckSafetyAsync(target, options.Safety, nowUtc);

        if (!safety.CanSend)
        {
            Console.WriteLine(safety.Reason);
            HeartPingRuntimeState.AddHistory($"Send skipped: {safety.Reason}");
            return RunResult.Success(sentMessage: false);
        }

        await client.SendMessageAsync(target, message);
        Console.WriteLine("Message sent.");
        HeartPingRuntimeState.AddHistory($"\"{message}\" - delivered successfully.");
        HeartPingRuntimeState.SetStatus("Message delivered");
        return RunResult.Success(sentMessage: true);
    }

    private static void ConfigureTelegramLogging()
    {
        Helpers.Log = (level, message) =>
        {
            if (level <= 2)
            {
                Console.WriteLine($"telegram[{level}]: {message}");
            }
        };
    }

    private static string? TelegramConfig(string what, TelegramOptions options) =>
        what switch
        {
            "api_id" => options.ApiId.ToString(),
            "api_hash" => options.ApiHash,
            "phone_number" => options.PhoneNumber,
            "session_pathname" => options.SessionPath,
            "session_key" => NormalizeSessionKey(options.SessionKey),
            "verification_code" => ReadSecretFromConsole("Telegram code"),
            "password" => ReadSecretFromConsole("Telegram 2FA password"),
            "device_model" => "HeartPing",
            "app_version" => "0.1.0",
            _ => null
        };

    private static string NormalizeSessionKey(string sessionKey)
    {
        var trimmed = sessionKey.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        if (trimmed.Length % 2 == 0 && IsHexString(trimmed))
        {
            return trimmed;
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trimmed)));
    }

    private static bool IsHexString(string value)
    {
        foreach (var ch in value)
        {
            if (!Uri.IsHexDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static string? ReadSecretFromConsole(string prompt)
    {
        Console.Write($"{prompt}: ");
        return Console.ReadLine();
    }

    private static string? GetArgumentValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private sealed record RunSettings(bool DryRun, bool LoginOnly, bool Watch, bool Check, bool SendNow, string ConfigPath)
    {
        public static RunSettings FromArgs(string[] args)
        {
            var dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
            var loginOnly = args.Contains("--login-only", StringComparer.OrdinalIgnoreCase);
            var check = args.Contains("--check", StringComparer.OrdinalIgnoreCase);
            var sendNow = args.Contains("--send-now", StringComparer.OrdinalIgnoreCase);
            var explicitWatch = args.Contains("--watch", StringComparer.OrdinalIgnoreCase);
            var hasModeFlag = explicitWatch || check || loginOnly || sendNow;

            return new(
                DryRun: dryRun,
                LoginOnly: loginOnly,
                Watch: explicitWatch || !hasModeFlag,
                Check: check,
                SendNow: sendNow,
                ConfigPath: GetArgumentValue(args, "--config") ?? "appsettings.json");
        }
    }

    private static string ResolveConfigPath(string path) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));

    private sealed record RunResult(int ExitCode, bool SentMessage)
    {
        public static RunResult Success(bool sentMessage) => new(0, sentMessage);
    }
}
