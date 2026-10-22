using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Textream.Windows.Models;
using Textream.Windows.Services;

namespace Textream.Windows.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ITextreamCoordinator _coordinator;
    private readonly ISettingsService _settingsService;
    private bool _isDictating;
    private string _statusMessage = "Ready";

    private System.Threading.Timer? _languageDebounceTimer;
    private LanguageSuggestion? _languageSuggestion;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? RequestOpenFile;
    public event EventHandler? RequestSaveFileAs;
    public event EventHandler? RequestOpenSettings;
    public event EventHandler? RequestOpenAbout;

    public ITextreamCoordinator Coordinator => _coordinator;
    public NotchSettings Settings => _settingsService.Current;

    public ObservableCollection<PageItem> PageList { get; } = new();

    public ICommand NewCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand AddPageCommand { get; }
    public ICommand DeletePageCommand { get; }
    public ICommand TogglePrompterCommand { get; }
    public ICommand ToggleDictationCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand OpenAboutCommand { get; }
    public ICommand SwitchLanguageCommand { get; }
    public ICommand DismissLanguageCommand { get; }

    public LanguageSuggestion? LanguageSuggestion
    {
        get => _languageSuggestion;
        set
        {
            if (_languageSuggestion != value)
            {
                _languageSuggestion = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasLanguageSuggestion));
            }
        }
    }

    public bool HasLanguageSuggestion => _languageSuggestion != null;

    public MainViewModel(ITextreamCoordinator coordinator, ISettingsService settingsService)
    {
        _coordinator = coordinator;
        _settingsService = settingsService;

        _coordinator.PropertyChanged += (_, _) =>
        {
            RefreshPageList();
            OnPropertyChanged(nameof(DocumentTitle));
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(IsPrompterActive));
            OnPropertyChanged(nameof(PrompterButtonLabel));
            OnPropertyChanged(nameof(PrompterButtonBackground));
            OnPropertyChanged(nameof(PrompterButtonBorder));
            OnPropertyChanged(nameof(CurrentPageText));
            OnPropertyChanged(nameof(SelectedPageIndex));
        };

        NewCommand = new RelayCommand(() => _coordinator.NewDocument());
        OpenCommand = new RelayCommand(() => RequestOpenFile?.Invoke(this, EventArgs.Empty));
        SaveCommand = new RelayCommand(async () =>
        {
            if (string.IsNullOrEmpty(_coordinator.CurrentFilePath))
            {
                RequestSaveFileAs?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                await _coordinator.SaveFileAsync();
            }
        });
        SaveAsCommand = new RelayCommand(() => RequestSaveFileAs?.Invoke(this, EventArgs.Empty));
        AddPageCommand = new RelayCommand(() => _coordinator.AddPage());
        DeletePageCommand = new RelayCommand(param =>
        {
            if (param is int idx) _coordinator.DeletePage(idx);
            else _coordinator.DeletePage(_coordinator.CurrentPageIndex);
        }, _ => _coordinator.Pages.Count > 1);

        TogglePrompterCommand = new RelayCommand(() =>
        {
            _coordinator.IsPrompterActive = !_coordinator.IsPrompterActive;
            StatusMessage = _coordinator.IsPrompterActive ? "Prompter running..." : "Ready";
        });

        ToggleDictationCommand = new RelayCommand(() =>
        {
            IsDictating = !IsDictating;
            StatusMessage = IsDictating ? "Listening for dictation..." : "Ready";
        });

        OpenSettingsCommand = new RelayCommand(() => RequestOpenSettings?.Invoke(this, EventArgs.Empty));
        OpenAboutCommand = new RelayCommand(() => RequestOpenAbout?.Invoke(this, EventArgs.Empty));

        SwitchLanguageCommand = new RelayCommand(() =>
        {
            if (LanguageSuggestion != null)
            {
                Settings.SpeechLocale = LanguageSuggestion.LocaleIdentifier;
                _settingsService.SaveSettings();
                StatusMessage = $"Switched speech recognition locale to {LanguageSuggestion.LanguageName} ({LanguageSuggestion.LocaleIdentifier})";
                LanguageSuggestion = null;
            }
        });

        DismissLanguageCommand = new RelayCommand(() =>
        {
            LanguageSuggestion = null;
        });

        RefreshPageList();
    }

    public string DocumentTitle => _coordinator.DocumentTitle;
    public bool HasUnsavedChanges => _coordinator.HasUnsavedChanges;
    public bool IsPrompterActive => _coordinator.IsPrompterActive;

    public string CurrentPageText
    {
        get => _coordinator.CurrentPageText;
        set
        {
            if (_coordinator.CurrentPageText != value)
            {
                _coordinator.CurrentPageText = value;
                RefreshPageList();
                OnPropertyChanged();

                // 2.5s debounce language detection matching LanguageDetection.swift
                _languageDebounceTimer?.Dispose();
                _languageDebounceTimer = new System.Threading.Timer(OnLanguageCheck, null, 2500, Timeout.Infinite);
            }
        }
    }

    private void OnLanguageCheck(object? state)
    {
        var suggestion = LanguageDetector.DetectSuggestion(CurrentPageText, Settings.SpeechLocale);
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            LanguageSuggestion = suggestion;
        });
    }

    public int SelectedPageIndex
    {
        get => _coordinator.CurrentPageIndex;
        set
        {
            if (_coordinator.CurrentPageIndex != value)
            {
                _coordinator.SelectPage(value);
                OnPropertyChanged();
            }
        }
    }

    public bool IsDictating
    {
        get => _isDictating;
        set
        {
            if (_isDictating != value)
            {
                _isDictating = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DictationButtonLabel));
                OnPropertyChanged(nameof(DictationButtonBackground));
                OnPropertyChanged(nameof(DictationButtonBorder));
            }
        }
    }

    public string PrompterButtonLabel => IsPrompterActive ? "⏹  Stop Teleprompter (Esc)" : "▶  Start Teleprompter (Ctrl+Enter)";
    public string PrompterButtonBackground => IsPrompterActive ? "#DC2626" : "#2563EB";
    public string PrompterButtonBorder => IsPrompterActive ? "#EF4444" : "#3B82F6";
    public string DictationButtonLabel => IsDictating ? "🔴  Stop Dictating" : "🎤  Dictate";
    public string DictationButtonBackground => IsDictating ? "#D97706" : "#27272A";
    public string DictationButtonBorder => IsDictating ? "#F59E0B" : "#3F3F46";


    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public string ListeningModeLabel => Settings.ListeningMode.GetLabel();

    private void RefreshPageList()
    {
        PageList.Clear();
        for (int i = 0; i < _coordinator.Pages.Count; i++)
        {
            var text = _coordinator.Pages[i].Trim();
            var words = text.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var preview = words.Length > 0 ? string.Join(" ", words.Take(5)) : "Empty";
            if (preview.Length > 30) preview = preview[..30] + "…";

            PageList.Add(new PageItem
            {
                Index = i,
                Text = _coordinator.Pages[i],
                Preview = preview,
                IsRead = _coordinator.ReadPages.Contains(i)
            });
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
