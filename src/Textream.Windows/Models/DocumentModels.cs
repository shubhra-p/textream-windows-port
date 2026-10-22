namespace Textream.Windows.Models;

public record WordItem(
    int Id,
    string Word,
    int CharOffset,
    bool IsAnnotation
);

public class PageItem
{
    public int Index { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Preview { get; set; } = string.Empty;
    public bool IsRead { get; set; }
}

public class TextreamDocument
{
    public List<string> Pages { get; set; } = new();
}
