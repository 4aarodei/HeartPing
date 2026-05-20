using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HeartPing;

internal static class HeartPingTrayApp
{
    public static Task<int> RunAsync(string[] args)
    {
        var appArgs = StripTrayArgument(args);

        if (UsesConsoleMode(appArgs))
        {
            MessageBox.Show(
                "Tray mode is for background watch mode. Run --check, --login-only, and --send-now from PowerShell, then start HeartPing with --tray.",
                "HeartPing",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return Task.FromResult(1);
        }

        AppLog.Initialize(fileOnly: true);
        FreeConsole();

        ApplicationConfiguration.Initialize();
        using var context = new HeartPingTrayContext(appArgs);
        Application.Run(context);
        return Task.FromResult(context.ExitCode);
    }

    private static string[] StripTrayArgument(string[] args) =>
        args
            .Where(arg => !string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static bool UsesConsoleMode(string[] args) =>
        args.Contains("--login-only", StringComparer.OrdinalIgnoreCase)
        || args.Contains("--send-now", StringComparer.OrdinalIgnoreCase)
        || args.Contains("--check", StringComparer.OrdinalIgnoreCase);

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    private sealed class HeartPingTrayContext : ApplicationContext
    {
        private readonly CancellationTokenSource shutdown = new();
        private readonly NotifyIcon notifyIcon;
        private readonly ToolStripMenuItem statusItem;
        private readonly HeartPingStatusForm statusForm;

        public int ExitCode { get; private set; }

        public HeartPingTrayContext(string[] args)
        {
            statusItem = new ToolStripMenuItem("Starting...")
            {
                Enabled = false
            };

            notifyIcon = new NotifyIcon
            {
                Icon = TrayIconFactory.Create(),
                Text = "HeartPing",
                Visible = true,
                ContextMenuStrip = BuildMenu()
            };

            statusForm = new HeartPingStatusForm(
                onOpenLogs: OpenLogsFolder,
                onLogin: StartManualLogin,
                onOpenAppFolder: OpenAppFolder,
                onExit: ExitFromTray,
                notifyIcon.Icon);

            HeartPingInteractiveAuth.SetPromptHandler(statusForm.PromptForInput);
            notifyIcon.DoubleClick += (_, _) => ShowStatusWindow();
            SetStatus("HeartPing is running");
            _ = RunHeartPingAsync(args);
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open HeartPing", null, (_, _) => ShowStatusWindow());
            menu.Items.Add("Log in to Telegram", null, (_, _) => StartManualLogin());
            menu.Items.Add("Open logs", null, (_, _) => OpenLogsFolder());
            menu.Items.Add("Open app folder", null, (_, _) => OpenAppFolder());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit HeartPing", null, (_, _) => ExitFromTray());
            return menu;
        }

        private async Task RunHeartPingAsync(string[] args)
        {
            try
            {
                ExitCode = await HeartPingApp.RunAsync(args, shutdown.Token);

                if (!shutdown.IsCancellationRequested && ExitCode == 0)
                {
                    SetStatus("HeartPing stopped");
                    notifyIcon.ShowBalloonTip(3000, "HeartPing", "Watch mode stopped.", ToolTipIcon.Info);
                }
                else if (ExitCode != 0)
                {
                    SetStatus($"HeartPing stopped with code {ExitCode}");
                    notifyIcon.ShowBalloonTip(5000, "HeartPing", $"Stopped with code {ExitCode}. Check logs for details.", ToolTipIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                ExitCode = 1;
                Console.Error.WriteLine(ex);
                SetStatus("HeartPing crashed");
                notifyIcon.ShowBalloonTip(5000, "HeartPing", "Stopped after an error. Check logs for details.", ToolTipIcon.Error);
            }
        }

        private void StartManualLogin()
        {
            ShowStatusWindow();
            _ = RunManualLoginAsync();
        }

        private async Task RunManualLoginAsync()
        {
            try
            {
                HeartPingRuntimeState.SetStatus("Manual Telegram login in progress");
                HeartPingRuntimeState.SetNextAction("Complete Telegram login");
                HeartPingRuntimeState.AddHistory("Manual Telegram login requested.");
                var exitCode = await HeartPingApp.RunAsync(["--login-only"]);
                if (exitCode == 0)
                {
                    SetStatus("HeartPing is running");
                    HeartPingRuntimeState.SetNextAction("Ready to send");
                }
                else
                {
                    SetStatus($"Manual login failed with code {exitCode}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                SetStatus("Manual login failed");
                HeartPingRuntimeState.AddHistory($"Manual login failed: {ex.Message}");
            }
        }

        private void ExitFromTray()
        {
            SetStatus("Stopping HeartPing...");
            shutdown.Cancel();
            ExitThread();
        }

        private void SetStatus(string value)
        {
            statusItem.Text = value;
            notifyIcon.Text = value.Length <= 63 ? value : value[..63];
            statusForm.SetStatus(value);
        }

        private void ShowStatusWindow()
        {
            statusForm.RefreshRuntimeState();
            if (!statusForm.Visible)
            {
                statusForm.Show();
            }

            if (statusForm.WindowState == FormWindowState.Minimized)
            {
                statusForm.WindowState = FormWindowState.Normal;
            }

            statusForm.BringToFront();
            statusForm.Activate();
        }

        private static void OpenLogsFolder()
        {
            var logsPath = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logsPath);
            OpenFolder(logsPath);
        }

        private static void OpenAppFolder() => OpenFolder(AppContext.BaseDirectory);

        private static void OpenFolder(string path)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                HeartPingInteractiveAuth.SetPromptHandler(null);
                HeartPingRuntimeState.Changed -= statusForm.RefreshRuntimeState;
                shutdown.Cancel();
                shutdown.Dispose();
                statusForm.Dispose();
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class HeartPingStatusForm : Form
    {
        private readonly Label statusValueLabel;
        private readonly Label nextActionValueLabel;
        private readonly Label logPathValueLabel;
        private readonly TextBox historyTextBox;

        public HeartPingStatusForm(Action onOpenLogs, Action onLogin, Action onOpenAppFolder, Action onExit, Icon icon)
        {
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(660, 430);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "HeartPing";
            Icon = icon;
            BackColor = Color.FromArgb(248, 249, 252);

            var headerLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 18, FontStyle.Bold),
                Location = new Point(24, 20),
                Text = "HeartPing"
            };

            var subtitleLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(70, 74, 82),
                Location = new Point(26, 58),
                Text = "Background watch mode is active while this window is open or hidden."
            };

            var statusLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold),
                Location = new Point(26, 98),
                Text = "Status"
            };

            statusValueLabel = new Label
            {
                AutoSize = false,
                Font = new Font("Segoe UI", 10),
                Location = new Point(26, 120),
                Size = new Size(500, 24),
                Text = "Starting..."
            };

            var nextActionLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold),
                Location = new Point(26, 150),
                Text = "Next action"
            };

            nextActionValueLabel = new Label
            {
                AutoSize = false,
                Font = new Font("Segoe UI", 10),
                Location = new Point(26, 172),
                Size = new Size(500, 24),
                Text = "Waiting for schedule..."
            };

            var logPathLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold),
                Location = new Point(26, 208),
                Text = "Log file"
            };

            logPathValueLabel = new Label
            {
                AutoEllipsis = true,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9),
                Location = new Point(26, 230),
                Padding = new Padding(8, 6, 8, 6),
                Size = new Size(600, 34),
                Text = AppLog.CurrentLogPath ?? "Log file is not ready yet."
            };

            var previewLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold),
                Location = new Point(26, 280),
                Text = "History"
            };

            historyTextBox = new TextBox
            {
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Cascadia Mono", 9),
                Location = new Point(26, 302),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Size = new Size(600, 82)
            };

            var loginButton = CreateButton("Login now", new Point(26, 394), (_, _) => onLogin());
            var openLogsButton = CreateButton("Open logs", new Point(126, 394), (_, _) => onOpenLogs());
            var openFolderButton = CreateButton("Open app folder", new Point(246, 394), (_, _) => onOpenAppFolder());
            var hideButton = CreateButton("Hide", new Point(446, 394), (_, _) => Hide());
            var exitButton = CreateButton("Exit", new Point(536, 394), (_, _) => onExit());

            Controls.Add(headerLabel);
            Controls.Add(subtitleLabel);
            Controls.Add(statusLabel);
            Controls.Add(statusValueLabel);
            Controls.Add(nextActionLabel);
            Controls.Add(nextActionValueLabel);
            Controls.Add(logPathLabel);
            Controls.Add(logPathValueLabel);
            Controls.Add(previewLabel);
            Controls.Add(historyTextBox);
            Controls.Add(loginButton);
            Controls.Add(openLogsButton);
            Controls.Add(openFolderButton);
            Controls.Add(hideButton);
            Controls.Add(exitButton);

            FormClosing += (_, eventArgs) =>
            {
                if (eventArgs.CloseReason == CloseReason.UserClosing)
                {
                    eventArgs.Cancel = true;
                    Hide();
                }
            };

            HeartPingRuntimeState.Changed += RefreshRuntimeState;
            RefreshRuntimeState();
        }

        public void SetStatus(string value)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => SetStatus(value));
                return;
            }

            statusValueLabel.Text = value;
        }

        public void RefreshRuntimeState()
        {
            if (InvokeRequired)
            {
                BeginInvoke(RefreshRuntimeState);
                return;
            }

            var snapshot = HeartPingRuntimeState.Snapshot();
            var logPath = AppLog.CurrentLogPath;
            statusValueLabel.Text = snapshot.CurrentStatus;
            nextActionValueLabel.Text = snapshot.NextActionText;
            logPathValueLabel.Text = logPath ?? "Log file is not ready yet.";
            historyTextBox.Text = BuildHistoryText(snapshot.History);
        }

        public string? PromptForInput(string prompt, bool secret)
        {
            if (InvokeRequired)
            {
                return (string?)Invoke(() => PromptForInput(prompt, secret));
            }

            Show();
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            BringToFront();
            Activate();

            using var dialog = new TelegramPromptForm(prompt, secret)
            {
                StartPosition = FormStartPosition.CenterParent
            };

            return dialog.ShowDialog(this) == DialogResult.OK
                ? dialog.Value
                : null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                HeartPingRuntimeState.Changed -= RefreshRuntimeState;
            }

            base.Dispose(disposing);
        }

        private static Button CreateButton(string text, Point location, EventHandler onClick) =>
            new Button()
            {
                BackColor = Color.White,
                FlatStyle = FlatStyle.System,
                Location = location,
                Size = new Size(text == "Open app folder" ? 130 : 90, 30),
                Text = text
            }.Also(button => button.Click += onClick);

        private static string BuildHistoryText(RuntimeHistoryEntry[] entries)
        {
            if (entries.Length == 0)
            {
                return "Waiting for events...";
            }

            return string.Join(
                Environment.NewLine,
                entries
                    .TakeLast(8)
                    .Select(entry => $"{entry.Timestamp:HH:mm:ss}  {entry.Text}"));
        }
    }

    private sealed class TelegramPromptForm : Form
    {
        private readonly TextBox valueTextBox;

        public TelegramPromptForm(string prompt, bool secret)
        {
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(420, 160);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Text = prompt;
            BackColor = Color.White;

            var promptLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 10),
                Location = new Point(20, 20),
                Text = $"{prompt}:"
            };

            valueTextBox = new TextBox
            {
                Font = new Font("Segoe UI", 10),
                Location = new Point(20, 52),
                Size = new Size(380, 28),
                UseSystemPasswordChar = secret
            };

            var submitButton = new Button
            {
                DialogResult = DialogResult.OK,
                Location = new Point(230, 108),
                Size = new Size(80, 30),
                Text = "OK"
            };

            var cancelButton = new Button
            {
                DialogResult = DialogResult.Cancel,
                Location = new Point(320, 108),
                Size = new Size(80, 30),
                Text = "Cancel"
            };

            Controls.Add(promptLabel);
            Controls.Add(valueTextBox);
            Controls.Add(submitButton);
            Controls.Add(cancelButton);

            AcceptButton = submitButton;
            CancelButton = cancelButton;
        }

        public string Value => valueTextBox.Text.Trim();
    }

    private static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }

    private static class TrayIconFactory
    {
        public static Icon Create()
        {
            using var bitmap = new Bitmap(64, 64);
            using var graphics = Graphics.FromImage(bitmap);

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var background = new LinearGradientBrush(
                new Rectangle(0, 0, 64, 64),
                Color.FromArgb(240, 44, 94),
                Color.FromArgb(61, 111, 255),
                45f);
            graphics.FillEllipse(background, 4, 4, 56, 56);

            using var pulsePen = new Pen(Color.White, 5)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            var pulse = new[]
            {
                new Point(12, 34),
                new Point(22, 34),
                new Point(27, 24),
                new Point(34, 44),
                new Point(41, 30),
                new Point(52, 30)
            };

            graphics.DrawLines(pulsePen, pulse);

            var handle = bitmap.GetHicon();
            try
            {
                return (Icon)Icon.FromHandle(handle).Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
