using System.Text.RegularExpressions;

namespace Textream.Windows.Services;

public record LanguageSuggestion(
    string LanguageCode,
    string LocaleIdentifier,
    string LanguageName,
    double Confidence);

public static class LanguageDetector
{
    private static readonly Regex AnnotationRegex = new(@"\[[^\]]*\]", RegexOptions.Compiled);

    private static readonly Dictionary<string, string[]> LanguageStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["es"] = new[] { "el", "la", "los", "las", "de", "del", "al", "que", "y", "en", "un", "una", "se", "no", "por", "para", "con", "su", "sus", "como", "estar", "este", "esta", "más", "pero" },
        ["fr"] = new[] { "le", "la", "les", "de", "du", "des", "et", "un", "une", "être", "ce", "il", "ils", "qui", "ne", "sur", "se", "pas", "plus", "avec", "tout", "faire", "son", "sa", "ses", "mais" },
        ["de"] = new[] { "der", "die", "das", "und", "in", "den", "von", "zu", "mit", "sich", "des", "auf", "für", "ist", "nicht", "ein", "eine", "auch", "dass" },
        ["it"] = new[] { "il", "la", "di", "che", "un", "in", "per", "una", "sono", "del", "non", "da", "con", "si", "più", "questo", "come", "ma", "tutto" },
        ["pt"] = new[] { "o", "a", "de", "que", "do", "da", "em", "um", "para", "com", "não", "uma", "os", "no", "se", "na", "por", "mais", "como", "mas" },
        ["en"] = new[] { "the", "be", "to", "of", "and", "a", "in", "that", "have", "it", "for", "not", "on", "with", "as", "you", "do", "at", "this", "but", "his", "from", "they", "we" }
    };

    private static readonly Dictionary<string, (string Locale, string Name)> SupportedLocales = new(StringComparer.OrdinalIgnoreCase)
    {
        ["es"] = ("es-ES", "Spanish"),
        ["fr"] = ("fr-FR", "French"),
        ["de"] = ("de-DE", "German"),
        ["it"] = ("it-IT", "Italian"),
        ["pt"] = ("pt-BR", "Portuguese"),
        ["en"] = ("en-US", "English"),
        ["zh"] = ("zh-CN", "Chinese"),
        ["ja"] = ("ja-JP", "Japanese"),
        ["ko"] = ("ko-KR", "Korean"),
        ["ru"] = ("ru-RU", "Russian")
    };

    public static LanguageSuggestion? DetectSuggestion(string text, string currentLocale)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Strip bracket annotations
        var scrubbed = AnnotationRegex.Replace(text, " ");
        var sample = scrubbed.Length > 4000 ? scrubbed[..4000] : scrubbed;

        // Must have at least 20 letters
        int letterCount = sample.Count(char.IsLetter);
        if (letterCount < 20) return null;

        // Check non-Latin scripts first
        int cjkCount = sample.Count(TextProcessor.IsCjk);
        if (cjkCount > 15)
        {
            // Japanese check (Hiragana/Katakana)
            if (sample.Any(c => (c >= 0x3040 && c <= 0x309F) || (c >= 0x30A0 && c <= 0x30FF)))
            {
                return CreateSuggestion("ja", currentLocale, 0.95);
            }
            // Korean check (Hangul)
            if (sample.Any(c => c >= 0xAC00 && c <= 0xD7AF))
            {
                return CreateSuggestion("ko", currentLocale, 0.95);
            }
            // Chinese check (Hanzi)
            return CreateSuggestion("zh", currentLocale, 0.90);
        }

        // Cyrillic script check
        int cyrillicCount = sample.Count(c => c >= 0x0400 && c <= 0x04FF);
        if (cyrillicCount > 15)
        {
            return CreateSuggestion("ru", currentLocale, 0.95);
        }

        // Latin script stop word matching
        var words = sample.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\r', '\n', ',', '.', ';', ':', '!', '?', '"', '\'', '-', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();

        string bestLang = "en";
        int bestScore = 0;
        int secondBestScore = 0;

        foreach (var (lang, stopWords) in LanguageStopWords)
        {
            int score = stopWords.Count(sw => words.Contains(sw));
            if (score > bestScore)
            {
                secondBestScore = bestScore;
                bestScore = score;
                bestLang = lang;
            }
            else if (score > secondBestScore)
            {
                secondBestScore = score;
            }
        }

        if (bestScore < 2 || bestScore <= secondBestScore) return null;

        double confidence = (double)bestScore / Math.Max(bestScore + 1, bestScore + secondBestScore);
        if (confidence < 0.55) return null;

        return CreateSuggestion(bestLang, currentLocale, confidence);
    }

    private static LanguageSuggestion? CreateSuggestion(string detectedLangCode, string currentLocale, double confidence)
    {
        if (!SupportedLocales.TryGetValue(detectedLangCode, out var info)) return null;

        // If current locale already starts with the detected language, no change needed
        if (currentLocale.StartsWith(detectedLangCode, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new LanguageSuggestion(
            detectedLangCode,
            info.Locale,
            info.Name,
            confidence);
    }
}
