namespace HeartPing;

internal static class HeartPingInteractiveAuth
{
    private static readonly object Sync = new();
    private static Func<string, bool, string?>? promptHandler;

    public static void SetPromptHandler(Func<string, bool, string?>? handler)
    {
        lock (Sync)
        {
            promptHandler = handler;
        }
    }

    public static string? Prompt(string prompt, bool secret = false)
    {
        Func<string, bool, string?>? handler;
        lock (Sync)
        {
            handler = promptHandler;
        }

        if (handler is not null)
        {
            return handler(prompt, secret);
        }

        Console.Write($"{prompt}: ");
        return Console.ReadLine();
    }
}
