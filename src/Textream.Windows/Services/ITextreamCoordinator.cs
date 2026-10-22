using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Textream.Windows.Services;

public interface ITextreamCoordinator : INotifyPropertyChanged
{
    ObservableCollection<string> Pages { get; }
    int CurrentPageIndex { get; set; }
    string CurrentPageText { get; set; }
    string CurrentPage => CurrentPageText;
    int TotalPages => Pages.Count;
    HashSet<int> ReadPages { get; }
    bool HasNextPage { get; }
    bool HasPreviousPage => CurrentPageIndex > 0;
    bool HasUnsavedChanges { get; }
    string? CurrentFilePath { get; }
    string DocumentTitle { get; }
    bool IsPrompterActive { get; set; }

    void NewDocument();
    void AddPage();
    void DeletePage(int index);
    void SelectPage(int index);
    void MarkPageAsRead(int index);
    void MarkCurrentPageRead() => MarkPageAsRead(CurrentPageIndex);
    void AdvanceToNextPage();
    bool NextPage();
    bool PreviousPage();

    Task OpenFileAsync(string filePath);
    Task SaveFileAsync(string? filePath = null);
    Task ImportPptxAsync(string pptxPath);
}
