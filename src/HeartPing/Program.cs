using HeartPing;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--tray", StringComparer.OrdinalIgnoreCase))
        {
            return await HeartPingTrayApp.RunAsync(args);
        }

        ConsoleHost.EnsureConsole();
        return await HeartPingApp.RunAsync(args);
    }
}
