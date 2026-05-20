namespace HeartPing;

internal sealed class FileMessageSource
{
    private readonly string[] messages;

    private FileMessageSource(string[] messages) => this.messages = messages;

    public int Count => messages.Length;

    public static async Task<FileMessageSource> LoadAsync(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Message file was not found: {path}", path);
        }

        var lines = await File.ReadAllLinesAsync(path);
        var messages = lines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (messages.Length == 0)
        {
            throw new InvalidOperationException($"Message file has no usable messages: {path}");
        }

        return new FileMessageSource(messages);
    }

    public string PickFor(DateTime scheduledLocalTime, string seedSalt)
    {
        var seed = RandomDailySchedulePlanner.StableSeed($"{scheduledLocalTime:O}|{seedSalt}|message");
        var index = Math.Abs(seed % messages.Length);
        return messages[index];
    }
}
