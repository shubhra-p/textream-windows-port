using Textream.Windows.Services;
using Xunit;

namespace Textream.Windows.Tests;

public class LanguageDetectorTests
{
    [Fact]
    public void DetectSuggestion_WhenScriptMatchesCurrentLocale_ReturnsNull()
    {
        var englishText = "Welcome to Textream, this is an English teleprompter script for presentations.";
        var suggestion = LanguageDetector.DetectSuggestion(englishText, "en-US");
        Assert.Null(suggestion);
    }

    [Fact]
    public void DetectSuggestion_WhenSpanishDetectedWithEnglishLocale_ReturnsSpanishSuggestion()
    {
        var spanishText = "Hola a todos, bienvenidos a la presentación donde vamos a leer un guion con mucha alegría y pasión por el proyecto.";
        var suggestion = LanguageDetector.DetectSuggestion(spanishText, "en-US");

        Assert.NotNull(suggestion);
        Assert.Equal("es", suggestion.LanguageCode);
        Assert.Equal("es-ES", suggestion.LocaleIdentifier);
        Assert.Equal("Spanish", suggestion.LanguageName);
        Assert.True(suggestion.Confidence > 0.6);
    }

    [Fact]
    public void DetectSuggestion_WhenFrenchDetected_ReturnsFrenchSuggestion()
    {
        var frenchText = "Bonjour tout le monde, ceci est une présentation avec les détails sur le projet et nous allons faire de notre mieux.";
        var suggestion = LanguageDetector.DetectSuggestion(frenchText, "en-US");

        Assert.NotNull(suggestion);
        Assert.Equal("fr", suggestion.LanguageCode);
        Assert.Equal("fr-FR", suggestion.LocaleIdentifier);
        Assert.Equal("French", suggestion.LanguageName);
    }

    [Fact]
    public void DetectSuggestion_WhenCyrillicDetected_ReturnsRussianSuggestion()
    {
        var russianText = "Привет всем, это тестовый текст для проверки распознавания языка в телесуфлере.";
        var suggestion = LanguageDetector.DetectSuggestion(russianText, "en-US");

        Assert.NotNull(suggestion);
        Assert.Equal("ru", suggestion.LanguageCode);
        Assert.Equal("ru-RU", suggestion.LocaleIdentifier);
        Assert.Equal("Russian", suggestion.LanguageName);
    }

    [Fact]
    public void DetectSuggestion_WhenJapaneseDetected_ReturnsJapaneseSuggestion()
    {
        var japaneseText = "こんにちは、これは日本語のプロンプタースクリプトです。よろしくお願いいたします。";
        var suggestion = LanguageDetector.DetectSuggestion(japaneseText, "en-US");

        Assert.NotNull(suggestion);
        Assert.Equal("ja", suggestion.LanguageCode);
        Assert.Equal("ja-JP", suggestion.LocaleIdentifier);
        Assert.Equal("Japanese", suggestion.LanguageName);
    }

    [Fact]
    public void DetectSuggestion_WhenKoreanDetected_ReturnsKoreanSuggestion()
    {
        var koreanText = "안녕하세요 여러분, 이것은 한국어로 작성된 텔레프롬프터 대본입니다.";
        var suggestion = LanguageDetector.DetectSuggestion(koreanText, "en-US");

        Assert.NotNull(suggestion);
        Assert.Equal("ko", suggestion.LanguageCode);
        Assert.Equal("ko-KR", suggestion.LocaleIdentifier);
        Assert.Equal("Korean", suggestion.LanguageName);
    }

    [Fact]
    public void DetectSuggestion_WhenChineseDetected_ReturnsChineseSuggestion()
    {
        var chineseText = "各位观众朋友们好，这是一个用于提词器的中文演讲稿测试。";
        var suggestion = LanguageDetector.DetectSuggestion(chineseText, "en-US");

        Assert.NotNull(suggestion);
        Assert.Equal("zh", suggestion.LanguageCode);
        Assert.Equal("zh-CN", suggestion.LocaleIdentifier);
        Assert.Equal("Chinese", suggestion.LanguageName);
    }

    [Fact]
    public void DetectSuggestion_ScrubsBracketCuesBeforeDetecting()
    {
        var textWithCues = "[smile] [pause and take deep breath] [nod] Hola amigos, bienvenidos a la presentación de Textream y gracias por venir.";
        var suggestion = LanguageDetector.DetectSuggestion(textWithCues, "en-US");

        Assert.NotNull(suggestion);
        Assert.Equal("es", suggestion.LanguageCode);
    }
}
