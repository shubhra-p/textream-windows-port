using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Textream.Windows.Common;

namespace Textream.Windows.Services;

public class PresentationNotesExtractor : IPresentationNotesExtractor
{
    private static readonly Logger Log = new(nameof(PresentationNotesExtractor));
    private static readonly Regex NumberRegex = new(@"\d+", RegexOptions.Compiled);

    public List<string> ExtractNotes(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Presentation file does not exist", filePath);
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext != ".pptx")
        {
            throw new NotSupportedException("Unsupported file format. Please drop or open a PowerPoint (.pptx) file.");
        }

        using var fileStream = File.OpenRead(filePath);
        return ExtractNotesFromStream(fileStream);
    }

    public List<string> ExtractNotesFromStream(Stream stream)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException("The presentation file is corrupted or not a valid PowerPoint (.pptx) archive.", ex);
        }

        using (archive)
        {
            var noteEntries = archive.Entries
                .Where(e => e.FullName.StartsWith("ppt/notesSlides/notesSlide", StringComparison.OrdinalIgnoreCase)
                         && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => ExtractNumber(Path.GetFileName(e.FullName)))
                .ToList();

            if (noteEntries.Count == 0)
            {
                throw new InvalidOperationException("No presenter notes found in this presentation.");
            }

            var pages = new List<string>();

            foreach (var entry in noteEntries)
            {
                using var entryStream = entry.Open();
                var text = ParseNoteXml(entryStream);
                var trimmed = text.Trim();

                // Ignore empty notes or slides that only contain a numeric slide number
                if (!string.IsNullOrEmpty(trimmed) && !int.TryParse(trimmed, out _))
                {
                    pages.Add(trimmed);
                }
            }

            if (pages.Count == 0)
            {
                throw new InvalidOperationException("No presenter notes found in this presentation.");
            }

            Log.Info($"Successfully extracted {pages.Count} notes pages from presentation archive.");
            return pages;
        }
    }

    private static int ExtractNumber(string filename)
    {
        var match = NumberRegex.Match(filename);
        return match.Success && int.TryParse(match.Value, out var num) ? num : 0;
    }

    private static string ParseNoteXml(Stream xmlStream)
    {
        var doc = XDocument.Load(xmlStream);
        var paragraphs = new List<string>();

        // Look for shapes (<p:sp>)
        foreach (var shape in doc.Descendants().Where(e => e.Name.LocalName == "sp"))
        {
            // Check if placeholder should be skipped (sldNum, sldImg, dt, hdr, ftr)
            var placeholder = shape.Descendants().FirstOrDefault(e => e.Name.LocalName == "ph");
            if (placeholder != null)
            {
                var typeAttr = placeholder.Attribute("type")?.Value;
                if (typeAttr is "sldNum" or "sldImg" or "dt" or "hdr" or "ftr")
                {
                    continue;
                }
            }

            var txBody = shape.Elements().FirstOrDefault(e => e.Name.LocalName == "txBody");
            if (txBody == null) continue;

            foreach (var p in txBody.Elements().Where(e => e.Name.LocalName == "p"))
            {
                var sb = new StringBuilder();
                foreach (var child in p.Elements())
                {
                    if (child.Name.LocalName == "r") // Text run
                    {
                        var t = child.Elements().FirstOrDefault(e => e.Name.LocalName == "t");
                        if (t != null)
                        {
                            sb.Append(t.Value);
                        }
                    }
                    else if (child.Name.LocalName == "t")
                    {
                        sb.Append(child.Value);
                    }
                    else if (child.Name.LocalName == "br")
                    {
                        sb.Append('\n');
                    }
                }

                var paragraphText = sb.ToString().TrimEnd();
                if (!string.IsNullOrWhiteSpace(paragraphText))
                {
                    paragraphs.Add(paragraphText);
                }
            }
        }

        return string.Join("\n\n", paragraphs);
    }
}
