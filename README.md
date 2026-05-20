# HeartPing

HeartPing is a small Windows-friendly .NET app that sends warm Telegram messages from your own Telegram account through MTProto, not from a bot. It can run either in the console for setup and manual actions or in the system tray for everyday background watch mode.

It is built for a simple use case: keep a lightweight local app running, let it choose send times inside your schedule window, and use your own account to deliver gentle messages without a separate server.

Use it gently. Telegram watches unofficial API clients for abuse, and automation should never flood chats or surprise people in harmful ways.

## What It Does

- Sends messages from your personal Telegram account, not a Telegram bot.
- Runs locally on your machine and can stay hidden in the Windows tray.
- Shows a minimal GUI with status, next planned action, and recent send history.
- Supports both rolling random intervals and fixed daily slots.
- Keeps logs locally and includes simple scripts for startup and runtime prep.

## Who It Is For

HeartPing fits best if you want a personal reminder or warmth-sender that lives on your own machine, stays out of the way, and does not require a hosted backend. It is more of a niche utility than a mass-market app, but that also means it stays small, direct, and easy to understand.

## Quick Start

1. Clone the repository.
2. Copy the sample config:

```powershell
Copy-Item .\src\HeartPing\appsettings.sample.json .\src\HeartPing\appsettings.json
```

3. Fill in your Telegram values in `src/HeartPing/appsettings.json`.
4. Log in once:

```powershell
dotnet run --project .\src\HeartPing\HeartPing.csproj -- --login-only
```

5. Start tray mode:

```powershell
dotnet publish .\src\HeartPing\HeartPing.csproj -c Release -o .artifacts/release
.\.artifacts\release\HeartPing.exe
```

At the moment the repository is source-first. If GitHub Releases are added later, the README can point non-technical users directly to a ready-made download.

## Architecture

- `Program.cs`: tiny entry point.
- `src/HeartPing/App/`: app startup, tray UI, runtime state, and logging.
- `src/HeartPing/Configuration/`: app options, validation, and environment overrides.
- `src/HeartPing/Scheduling/`: deterministic message slot planning.
- `src/HeartPing/Messaging/`: reads stable phrases from `messages.txt`.
- `src/HeartPing/Telegram/`: resolves the target user, checks recent outgoing messages, and sends through WTelegramClient.
- `scripts/`: Windows helper scripts for run, runtime prep, and startup shortcut installation.

The message source is intentionally isolated, so a future `LlmMessageSource` can replace the file source without changing scheduling or Telegram delivery.

## Local Setup

1. Get your `api_id` and `api_hash` at https://my.telegram.org under API development tools.
2. Create a local config from the sample and put your Telegram credentials, target, and schedule there.

```powershell
Copy-Item .\src\HeartPing\appsettings.sample.json .\src\HeartPing\appsettings.json
```
3. Run the first login locally:

```powershell
dotnet run --project .\src\HeartPing\HeartPing.csproj -- --login-only
```

4. Test the schedule without sending:

```powershell
dotnet run --project .\src\HeartPing\HeartPing.csproj -- --check
dotnet run --project .\src\HeartPing\HeartPing.csproj -- --dry-run
```

5. Start local watch mode for the day:

```powershell
dotnet run --project .\src\HeartPing\HeartPing.csproj -- --watch
```

For the normal always-on local run, use tray mode after the first login:

```powershell
dotnet publish .\src\HeartPing\HeartPing.csproj -c Release -o .artifacts/release
.\.artifacts\release\HeartPing.exe
.\.artifacts\release\HeartPing.exe --tray
```

Starting `HeartPing.exe` without arguments now uses tray mode automatically. Tray mode hides/detaches the console and keeps HeartPing running from the Windows notification area. Right-click the HeartPing icon to open logs, open the app folder, or exit cleanly. Keep `--check`, `--login-only`, and `--send-now` in the console because those modes are setup/manual commands.

Watch mode defaults to `rollingInterval`: it waits a random time between `rollingMinMinutes` and `rollingMaxMinutes`, sends if the current local time is inside the configured window, then plans the next random interval from that moment. With the default `30-90` minute range, the app averages about one message per hour while adapting to the time you actually started the computer. Overnight windows like `09:00-01:00` are handled as one continuous window.

If you prefer the older fixed daily plan, set `schedule.mode` to `dailySlots`. In that mode, watch mode calculates random daily message slots inside the configured window, sleeps until the next slot, wakes up, sends if due, and goes back to sleep. With `messagesPerDay` set to `10`, the app creates exactly 10 random send times for each day, for example `11:37`, `11:51`, `13:42`, `15:02`, and so on. When a message is actually sent, the next wake-up is pushed out so the app does not wake again inside the recent-message cooldown window.

Startup messages are disabled by default so the first send also follows the random schedule. If you ever want a test message immediately on watch start, set `schedule.sendOnWatchStart` to `true` or set `HEARTPING_SEND_ON_WATCH_START=true`.

6. Run one check immediately:

```powershell
dotnet run --project .\src\HeartPing\HeartPing.csproj -- --check
```

## Safe Verification

Use these checks before any real send:

```powershell
dotnet build HeartPing.slnx
dotnet run --project .\src\HeartPing\HeartPing.csproj -- --check
dotnet run --project .\src\HeartPing\HeartPing.csproj -- --dry-run
dotnet run --project .\src\HeartPing\HeartPing.csproj -- --login-only
dotnet run --project .\src\HeartPing\HeartPing.csproj -- --send-now --dry-run
dotnet run --project .\src\HeartPing\HeartPing.csproj -- --watch --dry-run
```

`--check` validates local config, messages, and schedule without touching Telegram. `--dry-run` never sends; it only previews a due message when a slot is due. `--send-now --dry-run` lets you test console input without Telegram. `--login-only` creates or refreshes the local WTelegram session without sending a message.

`--watch --dry-run` follows the normal schedule loop without sending real Telegram messages.

## Manual Send Test

To test real Telegram delivery without waiting for the schedule:

```powershell
dotnet publish .\src\HeartPing\HeartPing.csproj -c Release -o .artifacts/release
.\.artifacts\release\HeartPing.exe --send-now
```

The app will ask for one message in the console, resolve the configured target, run the same recent-message safety check, send that one message, and exit.

## Low Resource Local Run

For everyday local hosting, prefer a Release build and run the executable directly instead of `dotnet run`. The publish output includes your local `appsettings.json` and `messages.txt`, and all relative paths resolve from the executable folder, so you can move the whole folder to another machine and only edit `appsettings.json`.

```powershell
dotnet publish .\src\HeartPing\HeartPing.csproj -c Release -o .artifacts/release
.\.artifacts\release\HeartPing.exe --watch
```

This avoids keeping any build tooling involved. While watching, the app sleeps between checks and creates the Telegram client only when a message slot is due.

To avoid an accidental console close, run the release executable in tray mode instead:

```powershell
.\.artifacts\release\HeartPing.exe
.\.artifacts\release\HeartPing.exe --tray
```

The executable writes everything shown in the console to a daily log file next to the app:

```powershell
.\.artifacts\release\logs\heartping-YYYY-MM-DD.log
```

If the app stops unexpectedly, check the latest file in the `logs` folder to see the last schedule decision, sleep interval, Telegram login, and send result. In tray mode, double-clicking the tray icon opens the HeartPing status window.

## Portable Setup On Another Machine

1. Install the .NET 8 runtime or SDK.
2. Copy the published `.artifacts/release` folder to the target machine.
3. Edit `.artifacts/release/appsettings.json`.
4. Run the first login once:

```powershell
.\HeartPing.exe --login-only
```

5. Start normal watch mode:

```powershell
.\HeartPing.exe --watch
```

Or start it as a tray app:

```powershell
.\HeartPing.exe
.\HeartPing.exe --tray
```

If you keep the whole release folder together, editing only `appsettings.json` is enough.

## Environment Variables

- `HEARTPING_TELEGRAM_API_ID`
- `HEARTPING_TELEGRAM_API_HASH`
- `HEARTPING_TELEGRAM_PHONE_NUMBER`
- `HEARTPING_TELEGRAM_SESSION_PATH`
- `HEARTPING_TELEGRAM_SESSION_KEY`
- `HEARTPING_TARGET_USERNAME`
- `HEARTPING_TARGET_USER_ID`
- `HEARTPING_TARGET_PHONE_NUMBER`
- `HEARTPING_MESSAGES_FILE`
- `HEARTPING_SCHEDULE_MODE`
- `HEARTPING_TIME_ZONE`
- `HEARTPING_WINDOW_START`
- `HEARTPING_WINDOW_END`
- `HEARTPING_MESSAGES_PER_DAY`
- `HEARTPING_MINIMUM_GAP_MINUTES`
- `HEARTPING_ROLLING_MIN_MINUTES`
- `HEARTPING_ROLLING_MAX_MINUTES`
- `HEARTPING_SEND_ON_WATCH_START`
- `HEARTPING_MIN_MINUTES_BETWEEN_MESSAGES`
- `HEARTPING_SEED_SALT`

## GitHub Actions Hosting

GitHub Actions is a practical free non-local host for this MVP: a scheduled job wakes up every 15 minutes, restores the Telegram session from a secret, runs the app, and exits.

Set the same `HEARTPING_TELEGRAM_SESSION_KEY` locally before first login and in GitHub Secrets, so the WTelegram session can be decrypted on the runner.

After local login, encode `data/WTelegram.session` as Base64 and store it as `WT_SESSION_B64` in GitHub repository secrets. Also store `TG_API_ID`, `TG_API_HASH`, `TG_PHONE`, `TG_SESSION_KEY`, `TARGET_USERNAME`, and `HEARTPING_SEED_SALT`.

On Windows PowerShell:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("data/WTelegram.session"))
```

If Telegram invalidates the session, run `--login-only` locally again and update the secret.
