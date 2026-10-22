using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Textream.Windows.Common;

namespace Textream.Windows.Services;

public class TextreamCoordinator : ITextreamCoordinator
{
    private static readonly Logger Log = new(nameof(TextreamCoordinator));
    private readonly IPresentationNotesExtractor _presentationExtractor;

    private int _currentPageIndex;
    private string? _currentFilePath;
    private bool _isPrompterActive;
    private List<string> _savedPages = new();

    public const string DefaultWelcomeText =
@"Welcome to Textream! This is your personal teleprompter that sits right below your screen. [smile]

As you read aloud, the text will highlight in real-time, following your voice. The speech recognition matches your words and keeps track of your progress. [pause]

You can pause at any time, go back and re-read sections, and the highlighting will follow along. When you finish reading all the text, the overlay will automatically close with a smooth animation. [nod]

Try reading this passage out loud to see how the highlighting works. The waveform at the bottom shows your voice activity, and you'll see the last few words you spoke displayed next to it.

Happy presenting! [wave]";

    public ObservableCollection<string> Pages { get; } = new();
    public HashSet<int> ReadPages { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public TextreamCoordinator(IPresentationNotesExtractor? presentationExtractor = null)
    {
        _presentationExtractor = presentationExtractor ?? new PresentationNotesExtractor();
        NewDocument();
    }

    public int CurrentPageIndex
    {
        get => _currentPageIndex;
        set
        {
            if (_currentPageIndex != value && value >= 0 && value < Pages.Count)
            {
                _currentPageIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentPageText));
                OnPropertyChanged(nameof(HasNextPage));
            }
        }
    }

    public string CurrentPageText
    {
        get => _currentPageIndex < Pages.Count ? Pages[_currentPageIndex] : string.Empty;
        set
        {
            if (_currentPageIndex < Pages.Count && Pages[_currentPageIndex] != value)
            {
                Pages[_currentPageIndex] = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }
        }
    }

    public bool HasNextPage
    {
        get
        {
            for (int i = CurrentPageIndex + 1; i < Pages.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(Pages[i])) return true;
            }
            return false;
        }
    }

    public bool HasUnsavedChanges => !Pages.SequenceEqual(_savedPages);

    public string? CurrentFilePath
    {
        get => _currentFilePath;
        private set
        {
            _currentFilePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DocumentTitle));
        }
    }

    public string DocumentTitle => string.IsNullOrEmpty(CurrentFilePath)
        ? "Untitled"
        : Path.GetFileNameWithoutExtension(CurrentFilePath);

    public bool IsPrompterActive
    {
        get => _isPrompterActive;
        set
        {
            if (_isPrompterActive != value)
            {
                _isPrompterActive = value;
                OnPropertyChanged();
            }
        }
    }

    public void NewDocument()
    {
        Pages.Clear();
        Pages.Add(DefaultWelcomeText);
        _savedPages = new List<string>(Pages);
        _currentPageIndex = 0;
        ReadPages.Clear();
        CurrentFilePath = null;

        OnPropertyChanged(nameof(CurrentPageIndex));
        OnPropertyChanged(nameof(CurrentPageText));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(DocumentTitle));
    }

    public void AddPage()
    {
        Pages.Add(string.Empty);
        CurrentPageIndex = Pages.Count - 1;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasNextPage));
    }

    public void DeletePage(int index)
    {
        if (Pages.Count <= 1 || index < 0 || index >= Pages.Count) return;

        Pages.RemoveAt(index);
        ReadPages.Remove(index);

        if (_currentPageIndex >= Pages.Count)
        {
            _currentPageIndex = Pages.Count - 1;
        }
        else if (_currentPageIndex > index)
        {
            _currentPageIndex--;
        }

        OnPropertyChanged(nameof(CurrentPageIndex));
        OnPropertyChanged(nameof(CurrentPageText));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasNextPage));
    }

    public void SelectPage(int index)
    {
        if (index >= 0 && index < Pages.Count)
        {
            CurrentPageIndex = index;
        }
    }

    public void MarkPageAsRead(int index)
    {
        if (index >= 0 && index < Pages.Count)
        {
            ReadPages.Add(index);
            OnPropertyChanged(nameof(ReadPages));
        }
    }

    public void AdvanceToNextPage()
    {
        var next = CurrentPageIndex + 1;
        while (next < Pages.Count && string.IsNullOrWhiteSpace(Pages[next]))
        {
            next++;
        }

        if (next < Pages.Count)
        {
            SelectPage(next);
            MarkPageAsRead(next);
        }
    }

    public bool NextPage()
    {
        if (HasNextPage)
        {
            CurrentPageIndex++;
            return true;
        }
        return false;
    }

    public bool PreviousPage()
    {
        if (CurrentPageIndex > 0)
        {
            CurrentPageIndex--;
            return true;
        }
        return false;
    }

    public async Task OpenFileAsync(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".pptx")
        {
            await ImportPptxAsync(filePath);
            return;
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File not found", filePath);
        }

        List<string>? loadedPages;
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            loadedPages = JsonSerializer.Deserialize<List<string>>(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The file is not a valid Textream document.", ex);
        }

        if (loadedPages == null || loadedPages.Count == 0)
        {
            throw new InvalidOperationException("The selected .textream file contains no readable pages.");
        }

        Pages.Clear();
        foreach (var p in loadedPages) Pages.Add(p);

        _savedPages = new List<string>(Pages);
        _currentPageIndex = 0;
        ReadPages.Clear();
        CurrentFilePath = filePath;

        OnPropertyChanged(nameof(CurrentPageIndex));
        OnPropertyChanged(nameof(CurrentPageText));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(DocumentTitle));

        Log.Info($"Opened document with {Pages.Count} pages from '{filePath}'");
    }

    public async Task SaveFileAsync(string? filePath = null)
    {
        var target = filePath ?? CurrentFilePath;
        if (string.IsNullOrEmpty(target))
        {
            throw new InvalidOperationException("No target file path specified for save.");
        }

        var json = JsonSerializer.Serialize(Pages.ToList(), new JsonSerializerOptions { WriteIndented = true });
        var tempFile = target + ".tmp";
        await File.WriteAllTextAsync(tempFile, json);
        File.Move(tempFile, target, overwrite: true);

        _savedPages = new List<string>(Pages);
        CurrentFilePath = target;

        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(DocumentTitle));
        Log.Info($"Saved document to '{target}'");
    }

    public async Task ImportPptxAsync(string pptxPath)
    {
        var notes = await Task.Run(() => _presentationExtractor.ExtractNotes(pptxPath));
        if (notes.Count == 0) return;

        Pages.Clear();
        foreach (var n in notes) Pages.Add(n);

        _savedPages = new List<string>(Pages);
        _currentPageIndex = 0;
        ReadPages.Clear();
        CurrentFilePath = null; // Imported notes start as an unsaved presentation document

        OnPropertyChanged(nameof(CurrentPageIndex));
        OnPropertyChanged(nameof(CurrentPageText));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(DocumentTitle));
        Log.Info($"Imported {notes.Count} slides from '{pptxPath}'");
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
