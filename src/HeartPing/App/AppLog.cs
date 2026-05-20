using System.Text;

namespace HeartPing;

internal static class AppLog
{
    private static bool initialized;

    public static string? CurrentLogPath { get; private set; }

    public static void Initialize(bool fileOnly = false)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;

        var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDirectory);
        DeleteOldLogs(logDirectory, TimeSpan.FromDays(2));

        var logPath = Path.Combine(logDirectory, $"heartping-{DateTime.Now:yyyy-MM-dd}.log");
        CurrentLogPath = logPath;
        var logWriter = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };

        var standardOut = fileOnly ? TextWriter.Null : Console.Out;
        var standardError = fileOnly ? TextWriter.Null : Console.Error;

        Console.SetOut(new TimestampedTeeWriter(standardOut, logWriter));
        Console.SetError(new TimestampedTeeWriter(standardError, logWriter));
        Console.WriteLine($"Log file: {logPath}");
    }

    private static void DeleteOldLogs(string logDirectory, TimeSpan maxAge)
    {
        var cutoff = DateTime.Now.Subtract(maxAge);

        foreach (var logFile in Directory.EnumerateFiles(logDirectory, "*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(logFile) < cutoff)
                {
                    File.Delete(logFile);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class TimestampedTeeWriter : TextWriter
    {
        private readonly TextWriter consoleWriter;
        private readonly TextWriter fileWriter;

        public TimestampedTeeWriter(TextWriter consoleWriter, TextWriter fileWriter)
        {
            this.consoleWriter = consoleWriter;
            this.fileWriter = fileWriter;
        }

        public override Encoding Encoding => consoleWriter.Encoding;

        public override void Write(char value)
        {
            consoleWriter.Write(value);
            fileWriter.Write(value);
        }

        public override void WriteLine(string? value)
        {
            consoleWriter.WriteLine(value);
            fileWriter.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {value}");
        }

        public override void Flush()
        {
            consoleWriter.Flush();
            fileWriter.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fileWriter.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
