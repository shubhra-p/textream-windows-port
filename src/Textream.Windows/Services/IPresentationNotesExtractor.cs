using System.IO;

namespace Textream.Windows.Services;

public interface IPresentationNotesExtractor
{
    List<string> ExtractNotes(string filePath);
    List<string> ExtractNotesFromStream(Stream stream);
}
