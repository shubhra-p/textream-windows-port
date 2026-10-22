using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Textream.Windows.Common;
using Textream.Windows.Models;
using Textream.Windows.Services;
using Textream.Windows.ViewModels;

namespace Textream.Windows.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ISpeechRecognizerService _speechService;
    private readonly ISettingsService _settingsService;
    private readonly IBrowserServer _browserServer;
    private readonly IDirectorServer _directorServer;

    private PrompterOverlayWindow? _prompterWindow;
    private ExternalPrompterWindow? _externalWindow;

    public MainWindow(
        MainViewModel viewModel,
        ISpeechRecognizerService speechService,
        ISettingsService settingsService,
        IBrowserServer browserServer,
        IDirectorServer directorServer)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _speechService = speechService;
        _settingsService = settingsService;
        _browserServer = browserServer;
        _directorServer = directorServer;

        DataContext = _viewModel;

        _viewModel.RequestOpenFile += OnRequestOpenFile;
        _viewModel.RequestSaveFileAs += OnRequestSaveFileAs;
        _viewModel.RequestOpenSettings += OnRequestOpenSettings;
        _viewModel.RequestOpenAbout += OnRequestOpenAbout;

        _viewModel.Coordinator.PropertyChanged += OnCoordinatorPropertyChanged;
        _speechService.StateChanged += OnSpeechStateChanged;

        // Connect Director server remote control
        _directorServer.SetTextRequested += text =>
        {
            Dispatcher.Invoke(() =>
            {
                _viewModel.CurrentPageText = text;
                _viewModel.StatusMessage = "Text updated by Director";
            });
        };

        _directorServer.UpdateTextRequested += (text, readChars) =>
        {
            Dispatcher.Invoke(() =>
            {
                _viewModel.CurrentPageText = text;
                _speechService.UpdateText(text, readChars);
                _viewModel.StatusMessage = "Script updated live by Director";
            });
        };

        _directorServer.StopRequested += () =>
        {
            Dispatcher.Invoke(() =>
            {
                _viewModel.Coordinator.IsPrompterActive = false;
                _viewModel.StatusMessage = "Prompter stopped by Director";
            });
        };

        Closing += MainWindow_Closing;
    }

    private void OnCoordinatorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ITextreamCoordinator.IsPrompterActive))
        {
            if (_viewModel.Coordinator.IsPrompterActive)
            {
                StartPrompterSession();
            }
            else
            {
                StopPrompterSession();
            }
        }
    }

    private void StartPrompterSession()
    {
        StopPrompterSession();

        try
        {
            _prompterWindow = new PrompterOverlayWindow(_speechService, _settingsService, _viewModel.Coordinator);
            _prompterWindow.Closed += (s, e) =>
            {
                _prompterWindow = null;
                if (_viewModel.Coordinator.IsPrompterActive)
                {
                    _viewModel.Coordinator.IsPrompterActive = false;
                }
                Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        Activate();
                        Focus();
                    }
                    catch
                    {
                    }
                });
            };
            _prompterWindow.Show();

            if (_settingsService.Settings.ExternalDisplayMode != ExternalDisplayMode.Off)
            {
                _externalWindow = new ExternalPrompterWindow(_speechService, _settingsService, _viewModel.Coordinator);
                _externalWindow.Closed += (s, e) => _externalWindow = null;
                _externalWindow.Show();
            }

            AppLogger.Info("Prompter session activated.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to launch prompter window", ex);
            MessageBox.Show(this, $"Failed to open teleprompter: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _viewModel.Coordinator.IsPrompterActive = false;
        }
    }

    private void StopPrompterSession()
    {
        if (_prompterWindow != null)
        {
            var w = _prompterWindow;
            _prompterWindow = null;
            w.Close();
        }

        if (_externalWindow != null)
        {
            var ew = _externalWindow;
            _externalWindow = null;
            ew.Close();
        }

        _speechService.Stop();

        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                Activate();
                Focus();
            }
            catch
            {
            }
        });
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape && _viewModel.Coordinator.IsPrompterActive)
        {
            _viewModel.Coordinator.IsPrompterActive = false;
            e.Handled = true;
        }
    }

    private void OnSpeechStateChanged()
    {
        if (!_viewModel.Coordinator.IsPrompterActive) return;

        Dispatcher.InvokeAsync(() =>
        {
            if (!_viewModel.Coordinator.IsPrompterActive) return;

            // Broadcast to web and director clients
            var text = _viewModel.CurrentPageText;
            var words = TextProcessor.SplitTextIntoWords(text);

            _browserServer.UpdateState(
                words,
                _speechService.RecognizedCharCount,
                _speechService.AudioLevels,
                _speechService.IsListening,
                !_viewModel.Coordinator.HasNextPage && _speechService.ShouldAdvancePage,
                _viewModel.Coordinator.HasNextPage,
                _speechService.LastSpokenText);

            _directorServer.UpdateState(
                words,
                _speechService.RecognizedCharCount,
                _speechService.AudioLevels,
                _speechService.IsListening,
                !_viewModel.Coordinator.HasNextPage && _speechService.ShouldAdvancePage,
                _speechService.LastSpokenText);
        });
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        StopPrompterSession();

        if (_viewModel.HasUnsavedChanges)
        {
            var result = MessageBox.Show(
                this,
                "You have unsaved changes. Do you want to save before closing?",
                "Textream",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                if (string.IsNullOrEmpty(_viewModel.Coordinator.CurrentFilePath))
                {
                    OnRequestSaveFileAs(this, EventArgs.Empty);
                    if (_viewModel.HasUnsavedChanges)
                    {
                        e.Cancel = true;
                    }
                }
                else
                {
                    _viewModel.Coordinator.SaveFileAsync().GetAwaiter().GetResult();
                }
            }
            else if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
            }
        }
    }

    private async void OnRequestOpenFile(object? sender, EventArgs e)
    {
        if (_viewModel.HasUnsavedChanges)
        {
            var confirm = MessageBox.Show(
                this,
                "Do you want to save your current changes before opening another file?",
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning
            );

            if (confirm == MessageBoxResult.Yes)
            {
                if (string.IsNullOrEmpty(_viewModel.Coordinator.CurrentFilePath))
                {
                    OnRequestSaveFileAs(this, EventArgs.Empty);
                    if (_viewModel.HasUnsavedChanges) return;
                }
                else
                {
                    await _viewModel.Coordinator.SaveFileAsync();
                }
            }
            else if (confirm == MessageBoxResult.Cancel)
            {
                return;
            }
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Supported Files (*.textream;*.pptx)|*.textream;*.pptx|Textream Documents (*.textream)|*.textream|PowerPoint Presentations (*.pptx)|*.pptx|All Files (*.*)|*.*",
            Title = "Open Script or Presentation"
        };

        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                await _viewModel.Coordinator.OpenFileAsync(dialog.FileName);
                _viewModel.StatusMessage = $"Opened {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void OnRequestSaveFileAs(object? sender, EventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Textream Document (*.textream)|*.textream",
            DefaultExt = ".textream",
            FileName = string.IsNullOrEmpty(_viewModel.Coordinator.CurrentFilePath)
                ? "Untitled.textream"
                : Path.GetFileName(_viewModel.Coordinator.CurrentFilePath),
            Title = "Save Script As"
        };

        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                await _viewModel.Coordinator.SaveFileAsync(dialog.FileName);
                _viewModel.StatusMessage = $"Saved to {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to save file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OnRequestOpenSettings(object? sender, EventArgs e)
    {
        var settingsWin = new SettingsWindow(_settingsService)
        {
            Owner = this
        };
        settingsWin.ShowDialog();
    }

    private void OnRequestOpenAbout(object? sender, EventArgs e)
    {
        MessageBox.Show(
            this,
            "Textream for Windows\nVersion 1.0.0\n\nA free, open-source teleprompter that guides you through your script in real-time.\n\nOriginal macOS App by Fatih Kadir Akin\nRecreated for Windows with full feature parity.",
            "About Textream",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                var file = files[0];
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".textream" or ".pptx")
                {
                    try
                    {
                        await _viewModel.Coordinator.OpenFileAsync(file);
                        _viewModel.StatusMessage = $"Imported {Path.GetFileName(file)}";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, $"Failed to import presentation: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show(this, "Unsupported file format. Please drop a .pptx presentation or .textream document.", "Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }
}
