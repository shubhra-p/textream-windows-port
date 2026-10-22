using System.IO;
using Textream.Windows.Models;
using Textream.Windows.Services;
using Xunit;

namespace Textream.Windows.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempSettingsFile;

    public SettingsServiceTests()
    {
        _tempSettingsFile = Path.Combine(Path.GetTempPath(), $"textream_settings_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempSettingsFile))
        {
            try { File.Delete(_tempSettingsFile); } catch { }
        }
    }

    [Fact]
    public void DefaultSettings_MatchesExpectedDefaults()
    {
        var service = new SettingsService(_tempSettingsFile);
        var s = service.Current;

        Assert.Equal(340.0, s.NotchWidth);
        Assert.Equal(150.0, s.TextAreaHeight);
        Assert.Equal("en-US", s.SpeechLocale);
        Assert.Equal(FontSizePreset.Lg, s.FontSizePreset);
        Assert.Equal(FontFamilyPreset.Sans, s.FontFamilyPreset);
        Assert.Equal(FontColorPreset.White, s.FontColorPreset);
        Assert.Equal(CueBrightness.Dim, s.CueBrightness);
        Assert.Equal(OverlayMode.Pinned, s.OverlayMode);
        Assert.Equal(NotchDisplayMode.FollowMouse, s.NotchDisplayMode);
        Assert.Equal(ListeningMode.WordTracking, s.ListeningMode);
        Assert.Equal(3.0, s.ScrollSpeed);
        Assert.True(s.HideFromScreenShare);
        Assert.True(s.ShowElapsedTime);
        Assert.False(s.KeepScreenAwake);
        Assert.Equal(ReadingPosition.Centered, s.ReadingPosition);
        Assert.False(s.AutoNextPage);
        Assert.Equal(3, s.AutoNextPageDelay);
        Assert.Equal((ushort)7373, s.BrowserServerPort);
        Assert.Equal((ushort)7575, s.DirectorServerPort);
    }

    [Fact]
    public void SaveAndReload_PersistsChangesAccurately()
    {
        var service = new SettingsService(_tempSettingsFile);
        service.Current.NotchWidth = 420.0;
        service.Current.ListeningMode = ListeningMode.SilencePaused;
        service.Current.FontSizePreset = FontSizePreset.Xl;
        service.Current.SpeechLocale = "fr-FR";
        service.Current.AutoNextPage = true;
        service.Current.AutoNextPageDelay = 5;
        service.Save();

        var reloadedService = new SettingsService(_tempSettingsFile);
        var s = reloadedService.Current;

        Assert.Equal(420.0, s.NotchWidth);
        Assert.Equal(ListeningMode.SilencePaused, s.ListeningMode);
        Assert.Equal(FontSizePreset.Xl, s.FontSizePreset);
        Assert.Equal("fr-FR", s.SpeechLocale);
        Assert.True(s.AutoNextPage);
        Assert.Equal(5, s.AutoNextPageDelay);
    }

    [Fact]
    public void Sanitize_ClampsOutOfRangeValues()
    {
        var service = new SettingsService(_tempSettingsFile);
        service.Current.NotchWidth = 1000.0; // max 500
        service.Current.TextAreaHeight = 20.0; // min 100
        service.Current.BrowserServerPort = 80; // min 1024
        service.Current.AutoNextPageDelay = 7; // allowed: 0, 1, 3, 5
        service.Save();

        var s = service.Current;
        Assert.Equal(500.0, s.NotchWidth);
        Assert.Equal(100.0, s.TextAreaHeight);
        Assert.Equal((ushort)7373, s.BrowserServerPort);
        Assert.Equal(3, s.AutoNextPageDelay);
    }
}
