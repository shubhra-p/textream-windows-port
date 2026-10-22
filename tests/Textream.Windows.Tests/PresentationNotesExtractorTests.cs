using System.IO;
using System.IO.Compression;
using Textream.Windows.Services;
using Xunit;

namespace Textream.Windows.Tests;

public class PresentationNotesExtractorTests
{
    [Fact]
    public void ExtractNotesFromStream_ExtractsAndFiltersSlideNotes()
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Note 1: Standard note with two paragraphs
            var entry1 = archive.CreateEntry("ppt/notesSlides/notesSlide1.xml");
            using (var writer = new StreamWriter(entry1.Open()))
            {
                writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<p:notes xmlns:p=""http://schemas.openxmlformats.org/presentationml/2006/main"" xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"">
  <p:cSld>
    <p:spTree>
      <p:sp>
        <p:nvSpPr><p:cNvPr id=""2"" name=""Slide Number""/>
          <p:nvPr><p:ph type=""sldNum""/></p:nvPr>
        </p:nvSpPr>
        <p:txBody><a:p><a:r><a:t>1</a:t></a:r></a:p></p:txBody>
      </p:sp>
      <p:sp>
        <p:nvSpPr><p:cNvPr id=""3"" name=""Notes Body""/><p:nvPr><p:ph type=""body""/></p:nvPr></p:nvSpPr>
        <p:txBody>
          <a:p><a:r><a:t>Welcome everyone to our quarterly presentation.</a:t></a:r></a:p>
          <a:p><a:r><a:t>Please turn to the next slide.</a:t></a:r></a:p>
        </p:txBody>
      </p:sp>
    </p:spTree>
  </p:cSld>
</p:notes>");
            }

            // Note 2: Slide with only slide number (must be ignored)
            var entry2 = archive.CreateEntry("ppt/notesSlides/notesSlide2.xml");
            using (var writer = new StreamWriter(entry2.Open()))
            {
                writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<p:notes xmlns:p=""http://schemas.openxmlformats.org/presentationml/2006/main"" xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"">
  <p:cSld>
    <p:spTree>
      <p:sp>
        <p:nvSpPr><p:cNvPr id=""2"" name=""Slide Number""/><p:nvPr><p:ph type=""sldNum""/></p:nvPr></p:nvSpPr>
        <p:txBody><a:p><a:r><a:t>2</a:t></a:r></a:p></p:txBody>
      </p:sp>
    </p:spTree>
  </p:cSld>
</p:notes>");
            }

            // Note 3: Note with line breaks
            var entry3 = archive.CreateEntry("ppt/notesSlides/notesSlide3.xml");
            using (var writer = new StreamWriter(entry3.Open()))
            {
                writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<p:notes xmlns:p=""http://schemas.openxmlformats.org/presentationml/2006/main"" xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"">
  <p:cSld>
    <p:spTree>
      <p:sp>
        <p:nvSpPr><p:cNvPr id=""4"" name=""Notes Placeholder""/><p:nvPr><p:ph type=""body""/></p:nvPr></p:nvSpPr>
        <p:txBody>
          <a:p>
            <a:r><a:t>Here is a point.</a:t></a:r>
            <a:br/>
            <a:r><a:t>Followed by another line.</a:t></a:r>
          </a:p>
        </p:txBody>
      </p:sp>
    </p:spTree>
  </p:cSld>
</p:notes>");
            }
        }

        memoryStream.Position = 0;
        var extractor = new PresentationNotesExtractor();
        var notes = extractor.ExtractNotesFromStream(memoryStream);

        Assert.Equal(2, notes.Count);
        Assert.Contains("Welcome everyone to our quarterly presentation.", notes[0]);
        Assert.Contains("Please turn to the next slide.", notes[0]);
        Assert.Contains("Here is a point.\nFollowed by another line.", notes[1]);
    }

    [Fact]
    public void ExtractNotesFromStream_WithCorruptedStream_ThrowsInvalidOperationException()
    {
        var corruptedData = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0xFF, 0xFF };
        using var stream = new MemoryStream(corruptedData);
        var extractor = new PresentationNotesExtractor();

        var ex = Assert.Throws<InvalidOperationException>(() => extractor.ExtractNotesFromStream(stream));
        Assert.Contains("corrupted or not a valid PowerPoint", ex.Message);
    }

    [Fact]
    public void ExtractNotesFromStream_WithNoNotes_ThrowsInvalidOperationException()
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("ppt/presentation.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("<p:presentation xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"/>");
        }

        memoryStream.Position = 0;
        var extractor = new PresentationNotesExtractor();

        var ex = Assert.Throws<InvalidOperationException>(() => extractor.ExtractNotesFromStream(memoryStream));
        Assert.Contains("No presenter notes found", ex.Message);
    }
}

