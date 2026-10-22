using System.IO;
using System.Windows;
using Microsoft.Win32;
using Textream.Windows.Common;
using Textream.Windows.Services;
using Textream.Windows.ViewModels;
using Textream.Windows.Views;

namespace Textream.Windows;

public partial class App : Application
{
    private static readonly Logger Log = new(nameof(App));

    private ISettingsService? _settingsService;
    private ITextreamCoordinator? _coordinator;
    private IAudioCaptureService? _audioCapture;
    private ISpeechRecognizerService? _speechService;
    private IBrowserServer? _browserServer;
    private IDirectorServer? _directorServer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        SetupExceptionHandling();
        Log.Info("Textream for Windows starting up...");

        RegisterProtocolScheme();

        _settingsService = new SettingsService();
        _coordinator = new TextreamCoordinator();
        _audioCapture = new AudioCaptureService();
        _speechService = new SpeechRecognizerService(_audioCapture, _settingsService);
        _browserServer = new BrowserServer(_settingsService);
        _directorServer = new DirectorServer(_settingsService);

        // Start servers if configured
        if (_settingsService.Settings.BrowserServerEnabled)
        {
            _browserServer.Start();
        }
        if (_settingsService.Settings.DirectorServerEnabled)
        {
            _directorServer.Start();
        }

        if (e.Args.Length > 0)
        {
            HandleCommandLineArgs(e.Args);
        }

        var viewModel = new MainViewModel(_coordinator, _settingsService);
        var mainWindow = new MainWindow(
            viewModel,
            _speechService,
            _settingsService,
            _browserServer,
            _directorServer);

        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private void RegisterProtocolScheme()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\textream");
            key.SetValue("", "URL:Textream Protocol");
            key.SetValue("URL Protocol", "");

            using var shellKey = key.CreateSubKey(@"shell\open\command");
            shellKey.SetValue("", $"\"{exePath}\" \"%1\"");
            Log.Info("Registered textream:// URL scheme in HKCU.");
        }
        catch (Exception ex)
        {
            Log.Warning("Could not register textream:// protocol", ex);
        }
    }

    private void HandleCommandLineArgs(string[] args)
    {
        var arg = args[0];
        if (arg.StartsWith("textream://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(arg);
                if (uri.Host.Equals("read", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.Contains("read"))
                {
                    var query = uri.Query.TrimStart('?');
                    foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var kv = part.Split('=', 2);
                        if (kv.Length == 2 && kv[0].Equals("text", StringComparison.OrdinalIgnoreCase))
                        {
                            var text = Uri.UnescapeDataString(kv[1].Replace("+", " "));
                            if (!string.IsNullOrEmpty(text))
                            {
                                _coordinator?.Pages.Clear();
                                _coordinator?.Pages.Add(text);
                                _coordinator!.IsPrompterActive = true;
                                Log.Info($"Loaded text from textream:// protocol and started prompter: {text}");
                            }
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to parse protocol URL '{arg}'", ex);
            }
        }
        else if (File.Exists(arg))
        {
            try
            {
                _coordinator?.OpenFileAsync(arg).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to open file from command-line argument '{arg}'", ex);
            }
        }
    }

    private void SetupExceptionHandling()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            Log.Error("Unhandled UI exception caught", e.Exception);
            MessageBox.Show($"An unexpected error occurred:\n{e.Exception.Message}", "Textream Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log.Error("Unhandled AppDomain exception", ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Log.Error("Unobserved Task exception", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _speechService?.Dispose();
        _audioCapture?.Dispose();
        _browserServer?.Dispose();
        _directorServer?.Dispose();

        Log.Info($"Textream exiting with code {e.ApplicationExitCode}.");
        base.OnExit(e);
    }
}
