using System.Collections.Concurrent;

namespace HeartPing;

internal static class HeartPingRuntimeState
{
    private const int MaxHistoryEntries = 100;
    private static readonly object Sync = new();
    private static readonly ConcurrentQueue<RuntimeHistoryEntry> History = new();

    public static event Action? Changed;

    public static string CurrentStatus { get; private set; } = "Starting...";

    public static string NextActionText { get; private set; } = "Waiting for schedule...";

    public static void SetStatus(string status)
    {
        lock (Sync)
        {
            CurrentStatus = status;
        }

        Changed?.Invoke();
    }

    public static void SetNextAction(string nextActionText)
    {
        lock (Sync)
        {
            NextActionText = nextActionText;
        }

        Changed?.Invoke();
    }

    public static void AddHistory(string text)
    {
        var entry = new RuntimeHistoryEntry(DateTimeOffset.Now, text);
        History.Enqueue(entry);

        while (History.Count > MaxHistoryEntries && History.TryDequeue(out _))
        {
        }

        Changed?.Invoke();
    }

    public static RuntimeSnapshot Snapshot()
    {
        lock (Sync)
        {
            return new RuntimeSnapshot(
                CurrentStatus,
                NextActionText,
                History.ToArray());
        }
    }
}

internal sealed record RuntimeSnapshot(
    string CurrentStatus,
    string NextActionText,
    RuntimeHistoryEntry[] History);

internal sealed record RuntimeHistoryEntry(DateTimeOffset Timestamp, string Text);
