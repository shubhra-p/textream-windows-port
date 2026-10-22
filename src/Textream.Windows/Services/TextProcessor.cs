using System.Text.RegularExpressions;
using Textream.Windows.Models;

namespace Textream.Windows.Services;

public static class TextProcessor
{
    private static readonly Regex AnnotationRegex = new(@"\[[^\]]+\]", RegexOptions.Compiled);

    public static bool IsCjk(char c)
    {
        return (c >= 0x4E00 && c <= 0x9FFF)    // CJK Unified Ideographs
            || (c >= 0x3400 && c <= 0x4DBF)    // CJK Extension A
            || (c >= 0x20000 && c <= 0x2A6DF)  // CJK Extension B
            || (c >= 0xF900 && c <= 0xFAFF)    // CJK Compatibility
            || (c >= 0x3040 && c <= 0x309F)    // Hiragana
            || (c >= 0x30A0 && c <= 0x30FF)    // Katakana
            || (c >= 0xAC00 && c <= 0xD7AF);   // Hangul Syllables
    }

    public static List<string> SplitTextIntoWords(string text)
    {
        var rawTokens = text.Replace("\r\n", " ")
                            .Replace('\n', ' ')
                            .Replace('\r', ' ')
                            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        var result = new List<string>();

        foreach (var token in rawTokens)
        {
            if (!token.Any(IsCjk))
            {
                result.Add(token);
                continue;
            }

            // Split CJK chars individually; consecutive non-CJK characters stay grouped
            var buffer = new List<char>();
            foreach (var ch in token)
            {
                if (IsCjk(ch))
                {
                    if (buffer.Count > 0)
                    {
                        result.Add(new string(buffer.ToArray()));
                        buffer.Clear();
                    }
                    result.Add(ch.ToString());
                }
                else
                {
                    buffer.Add(ch);
                }
            }

            if (buffer.Count > 0)
            {
                result.Add(new string(buffer.ToArray()));
            }
        }

        return result;
    }

    public static HashSet<int> ParagraphBreakWordIndices(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');

        var result = new HashSet<int>();
        var wordCount = 0;
        var hasContent = false;
        var hasPendingBreak = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var lineWords = SplitTextIntoWords(lines[i]);
            if (lineWords.Count > 0)
            {
                if (hasContent && hasPendingBreak)
                {
                    result.Add(wordCount);
                }
                wordCount += lineWords.Count;
                hasContent = true;
                hasPendingBreak = false;
            }

            if (i < lines.Length - 1 && hasContent)
            {
                hasPendingBreak = true;
            }
        }

        return result;
    }

    public static bool IsAnnotation(string token)
    {
        return token.StartsWith('[') && token.EndsWith(']');
    }

    public static List<WordItem> BuildWordItems(string text)
    {
        var words = SplitTextIntoWords(text);
        var items = new List<WordItem>(words.Count);
        var offset = 0;

        for (int i = 0; i < words.Count; i++)
        {
            var word = words[i];
            var isAnnotation = IsAnnotation(word);
            items.Add(new WordItem(i, word, offset, isAnnotation));
            offset += word.Length + 1; // plus space
        }

        return items;
    }

    public static int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;

        var d = new int[a.Length + 1, b.Length + 1];

        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = (b[j - 1] == a[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost
                );
            }
        }

        return d[a.Length, b.Length];
    }
}
