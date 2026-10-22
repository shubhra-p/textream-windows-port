using Textream.Windows.Services;
using Xunit;

namespace Textream.Windows.Tests;

public class TextProcessingTests
{
    [Fact]
    public void SplitTextIntoWords_SplitsStandardWordsAndAnnotations()
    {
        var text = "Hello world! [smile] This is a test.";
        var words = TextProcessor.SplitTextIntoWords(text);

        Assert.Equal(7, words.Count);
        Assert.Equal("Hello", words[0]);
        Assert.Equal("world!", words[1]);
        Assert.Equal("[smile]", words[2]);
        Assert.True(TextProcessor.IsAnnotation(words[2]));
        Assert.False(TextProcessor.IsAnnotation(words[0]));
    }

    [Fact]
    public void SplitTextIntoWords_SplitsCjkCharactersIndividually()
    {
        var text = "Hello 世界 test";
        var words = TextProcessor.SplitTextIntoWords(text);

        Assert.Equal(4, words.Count);
        Assert.Equal("Hello", words[0]);
        Assert.Equal("世", words[1]);
        Assert.Equal("界", words[2]);
        Assert.Equal("test", words[3]);
    }

    [Fact]
    public void ParagraphBreakWordIndices_CalculatesIndicesAcrossLines()
    {
        var text = "Paragraph one words.\n\nParagraph two begins here.\n\nAnd paragraph three.";
        var breaks = TextProcessor.ParagraphBreakWordIndices(text);

        // First paragraph has 3 words (0, 1, 2) -> break at word index 3
        // Second paragraph has 4 words (3, 4, 5, 6) -> break at word index 7
        Assert.Contains(3, breaks);
        Assert.Contains(7, breaks);
    }

    [Fact]
    public void LevenshteinDistance_ComputesCorrectMetric()
    {
        Assert.Equal(0, TextProcessor.LevenshteinDistance("teleprompter", "teleprompter"));
        Assert.Equal(1, TextProcessor.LevenshteinDistance("telepromter", "teleprompter"));
        Assert.Equal(3, TextProcessor.LevenshteinDistance("kitten", "sitting"));
    }
}
