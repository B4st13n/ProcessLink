using System.IO;
using System.Diagnostics;
using System.Windows;
using ProcessLink.Services;
using ProcessLink.ViewModels;
using WinForms = System.Windows.Forms;

namespace ProcessLink;

public partial class App : System.Windows.Application
{
    private WinForms.NotifyIcon? _notifyIcon;
    private MainWindow? _window;
    private MainViewModel? _viewModel;
    private ProcessMonitor? _monitor;
    private Mutex? _singleInstanceMutex;
    private bool _consoleMode;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _consoleMode = e.Args.Contains("--console", StringComparer.OrdinalIgnoreCase);
        if (_consoleMode)
            NativeMethods.AllocConsole();

        _singleInstanceMutex = new Mutex(true, @"Global\ProcessLink_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("ProcessLink is already running.", "ProcessLink",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var configService = new ConfigService();
        var config = configService.Load();
        var holder = new ConfigHolder(config);
        var eventSink = new FileEventSink();

        var monitor = new ProcessMonitor(
            new SystemProcessSource(),
            new CmdLauncher(),
            () => holder.Config,
            eventSink);
        _monitor = monitor;

        monitor.TransitionsOccurred += (_, transitions) =>
        {
            foreach (var transition in transitions)
            {
                var verb = transition.Transition == ProcessTransitionKind.Appeared ? "APPEARED " : "DISAPPEARED";
                var line = $"[{DateTime.Now:HH:mm:ss}] {verb} {transition.ProcessName}";
                Console.WriteLine(line);
            }
        };

        var viewModel = new MainViewModel(configService, monitor, holder, eventSink);
        _viewModel = viewModel;

        var window = new MainWindow(viewModel);
        _window = window;
        window.Show();

        if (!_consoleMode)
            CreateTrayIcon();
        else
            Console.WriteLine("ProcessLink console mode — no tray icon. Press Ctrl+C or close the window to quit.");
    }

    private void CreateTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowWindow());
        menu.Items.Add("Reload config", null, (_, _) => _viewModel?.ReloadConfig());
        menu.Items.Add("Open event log", null, (_, _) => OpenEventLog());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());

        _notifyIcon = new WinForms.NotifyIcon
        {
            Text = "ProcessLink",
            Icon = TrayIconFactory.CreateIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == WinForms.MouseButtons.Left)
                ToggleWindow();
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
    }

    private void ShowWindow()
    {
        if (_window == null) return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ToggleWindow()
    {
        if (_window == null) return;
        if (_window.IsVisible)
            _window.Hide();
        else
            ShowWindow();
    }

    private static void OpenEventLog()
    {
        try
        {
            if (!File.Exists(AppPaths.EventLogPath))
                File.WriteAllText(AppPaths.EventLogPath, string.Empty);
            Process.Start(new ProcessStartInfo(AppPaths.EventLogPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Could not open the event log: {ex.Message}", "ProcessLink",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Quit()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        if (_window != null)
        {
            _window.AllowClose = true;
            _window.Close();
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _monitor?.Dispose();
        _notifyIcon?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static extern bool AllocConsole();
}
