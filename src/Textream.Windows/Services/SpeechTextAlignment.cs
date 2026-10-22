namespace Textream.Windows.Services;

/// <summary>
/// Bracket annotation parsing and speech-to-text alignment logic.
/// Direct port of Textream macOS SpeechTextAlignment.swift.
/// </summary>
public static class SpeechTextAlignment
{
    public static List<bool> AnnotationFlags(IReadOnlyList<string> words)
    {
        var closingAtOrAfter = new bool[words.Count];
        var hasClosing = false;
        for (int i = words.Count - 1; i >= 0; i--)
        {
            if (words[i].Contains(']'))
            {
                hasClosing = true;
            }
            closingAtOrAfter[i] = hasClosing;
        }

        var flags = new List<bool>(words.Count);
        var isInsideAnnotation = false;
        for (int i = 0; i < words.Count; i++)
        {
            var word = words[i];
            var beginsAnnotation = word.StartsWith('[') && closingAtOrAfter[i];
            var isAnnotation = isInsideAnnotation || beginsAnnotation;
            flags.Add(isAnnotation);
            if (beginsAnnotation)
            {
                isInsideAnnotation = true;
            }
            if (isInsideAnnotation && word.Contains(']'))
            {
                isInsideAnnotation = false;
            }
        }
        return flags;
    }

    public static List<(int Start, int End)> AnnotationRanges(string text)
    {
        var ranges = new List<(int Start, int End)>();
        int? openingIndex = null;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '[' && openingIndex == null)
            {
                openingIndex = i;
            }
            else if (text[i] == ']' && openingIndex != null)
            {
                ranges.Add((openingIndex.Value, i + 1));
                openingIndex = null;
            }
        }

        return ranges;
    }

    public static int AdvancePastAnnotations(
        string text,
        IReadOnlyList<(int Start, int End)> ranges,
        int offset)
    {
        var current = Math.Max(0, Math.Min(offset, text.Length));
        var skippedAnnotation = false;

        while (current < text.Length)
        {
            var inRange = ranges.FirstOrDefault(r => current >= r.Start && current < r.End);
            if (inRange.End > 0)
            {
                current = inRange.End;
                skippedAnnotation = true;
                continue;
            }

            var next = current;
            while (next < text.Length && char.IsWhiteSpace(text[next]))
            {
                next++;
            }

            var nextInRange = ranges.FirstOrDefault(r => r.Start == next);
            if (nextInRange.End > 0)
            {
                current = nextInRange.End;
                skippedAnnotation = true;
                continue;
            }

            return skippedAnnotation ? next : current;
        }

        return current;
    }

    public static int BestOffset(int characterResult, int wordResult, int agreementTolerance = 20)
    {
        if (Math.Abs(characterResult - wordResult) <= agreementTolerance)
        {
            return (characterResult + wordResult) / 2;
        }
        return Math.Max(characterResult, wordResult);
    }

    public static bool ShouldCommit(
        int characterResult,
        int wordResult,
        int current,
        int rawCandidate,
        int candidate,
        bool confirmed)
    {
        var bothProgressed = Math.Min(characterResult, wordResult) > 0;
        var skippedAnnotation = candidate > rawCandidate;
        var smallStep = candidate - current <= 15;
        return bothProgressed || skippedAnnotation || confirmed || smallStep;
    }
}
