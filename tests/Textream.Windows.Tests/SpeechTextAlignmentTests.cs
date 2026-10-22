using Textream.Windows.Services;
using Xunit;

namespace Textream.Windows.Tests;

public class SpeechTextAlignmentTests
{
    [Fact]
    public void AnnotationFlags_IdentifiesSingleAndMultiWordBracketCues()
    {
        var words = new List<string> { "Hello", "[smile]", "world", "[pause", "and", "breathe]", "everyone" };
        var flags = SpeechTextAlignment.AnnotationFlags(words);

        Assert.Equal(7, flags.Count);
        Assert.False(flags[0]); // Hello
        Assert.True(flags[1]);  // [smile]
        Assert.False(flags[2]); // world
        Assert.True(flags[3]);  // [pause
        Assert.True(flags[4]);  // and
        Assert.True(flags[5]);  // breathe]
        Assert.False(flags[6]); // everyone
    }

    [Fact]
    public void AnnotationRanges_DetectsPreciseCharBoundaries()
    {
        var text = "Welcome [smile] to Textream! [take a deep breath]";
        var ranges = SpeechTextAlignment.AnnotationRanges(text);

        Assert.Equal(2, ranges.Count);
        Assert.Equal(8, ranges[0].Start);
        Assert.Equal(15, ranges[0].End);
        Assert.Equal("[smile]", text[ranges[0].Start..ranges[0].End]);

        Assert.Equal(29, ranges[1].Start);
        Assert.Equal(49, ranges[1].End);
        Assert.Equal("[take a deep breath]", text[ranges[1].Start..ranges[1].End]);
    }

    [Fact]
    public void AdvancePastAnnotations_SkipsStartingAnnotationsAutomatically()
    {
        var text = "[smile] Hello world";
        var ranges = SpeechTextAlignment.AnnotationRanges(text);

        // From offset 0, it should skip past "[smile] " directly to index 8 ("H")
        var advanced = SpeechTextAlignment.AdvancePastAnnotations(text, ranges, 0);
        Assert.Equal(8, advanced);
        Assert.Equal('H', text[advanced]);
    }

    [Fact]
    public void BestOffset_ConsensusAndPreferenceLogic()
    {
        // When results agree within tolerance (20 chars), returns average
        var offsetAgreed = SpeechTextAlignment.BestOffset(characterResult: 40, wordResult: 50, agreementTolerance: 20);
        Assert.Equal(45, offsetAgreed);

        // When results disagree (>20 chars), prefers the further (word-level) scan
        var offsetDisagreed = SpeechTextAlignment.BestOffset(characterResult: 20, wordResult: 70, agreementTolerance: 20);
        Assert.Equal(70, offsetDisagreed);
    }

    [Fact]
    public void ShouldCommit_GatingRules()
    {
        // Both progressed -> true
        Assert.True(SpeechTextAlignment.ShouldCommit(
            characterResult: 10, wordResult: 12, current: 0, rawCandidate: 10, candidate: 10, confirmed: false));

        // Skipped annotation -> true
        Assert.True(SpeechTextAlignment.ShouldCommit(
            characterResult: 0, wordResult: 10, current: 0, rawCandidate: 10, candidate: 18, confirmed: false));

        // Small step (<= 15 chars) -> true
        Assert.True(SpeechTextAlignment.ShouldCommit(
            characterResult: 0, wordResult: 8, current: 0, rawCandidate: 8, candidate: 8, confirmed: false));

        // Big jump unconfirmed without dual progress -> false
        Assert.False(SpeechTextAlignment.ShouldCommit(
            characterResult: 0, wordResult: 40, current: 0, rawCandidate: 40, candidate: 40, confirmed: false));

        // Big jump confirmed -> true
        Assert.True(SpeechTextAlignment.ShouldCommit(
            characterResult: 0, wordResult: 40, current: 0, rawCandidate: 40, candidate: 40, confirmed: true));
    }
}
