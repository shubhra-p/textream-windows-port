namespace Textream.Windows.Models;

public enum FontSizePreset
{
    Xs,
    Sm,
    Lg,
    Xl
}

public static class FontSizePresetExtensions
{
    public static string GetLabel(this FontSizePreset preset) => preset switch
    {
        FontSizePreset.Xs => "XS",
        FontSizePreset.Sm => "SM",
        FontSizePreset.Lg => "LG",
        FontSizePreset.Xl => "XL",
        _ => "LG"
    };

    public static double GetPointSize(this FontSizePreset preset) => preset switch
    {
        FontSizePreset.Xs => 14.0,
        FontSizePreset.Sm => 16.0,
        FontSizePreset.Lg => 20.0,
        FontSizePreset.Xl => 24.0,
        _ => 20.0
    };
}

public enum FontFamilyPreset
{
    Sans,
    Serif,
    Mono,
    Dyslexia
}

public static class FontFamilyPresetExtensions
{
    public static string GetLabel(this FontFamilyPreset preset) => preset switch
    {
        FontFamilyPreset.Sans => "Sans",
        FontFamilyPreset.Serif => "Serif",
        FontFamilyPreset.Mono => "Mono",
        FontFamilyPreset.Dyslexia => "Dyslexia",
        _ => "Sans"
    };

    public static string GetFontFamilyName(this FontFamilyPreset preset) => preset switch
    {
        FontFamilyPreset.Sans => "Segoe UI, Arial, sans-serif",
        FontFamilyPreset.Serif => "Georgia, Times New Roman, serif",
        FontFamilyPreset.Mono => "Cascadia Code, Consolas, Courier New, monospace",
        FontFamilyPreset.Dyslexia => "pack://application:,,,/Resources/Fonts/#OpenDyslexic3, Segoe UI",
        _ => "Segoe UI"
    };
}

public enum FontColorPreset
{
    White,
    Yellow,
    Green,
    Blue,
    Pink,
    Orange
}

public static class FontColorPresetExtensions
{
    public static string GetLabel(this FontColorPreset preset) => preset.ToString();

    public static string GetHexColor(this FontColorPreset preset) => preset switch
    {
        FontColorPreset.White => "#FFFFFF",
        FontColorPreset.Yellow => "#FFD60A",
        FontColorPreset.Green => "#33D64A",
        FontColorPreset.Blue => "#4F8CFF",
        FontColorPreset.Pink => "#FF6191",
        FontColorPreset.Orange => "#FF9E0A",
        _ => "#FFFFFF"
    };
}

public enum CueBrightness
{
    Dim,
    Low,
    Medium,
    Bright
}

public static class CueBrightnessExtensions
{
    public static double GetUnreadOpacity(this CueBrightness brightness) => brightness switch
    {
        CueBrightness.Dim => 0.20,
        CueBrightness.Low => 0.35,
        CueBrightness.Medium => 0.50,
        CueBrightness.Bright => 0.80,
        _ => 0.20
    };

    public static double GetReadOpacity(this CueBrightness brightness) => brightness switch
    {
        CueBrightness.Dim => 0.50,
        CueBrightness.Low => 0.60,
        CueBrightness.Medium => 0.70,
        CueBrightness.Bright => 1.00,
        _ => 0.50
    };
}

public enum OverlayMode
{
    Pinned,
    Floating,
    Fullscreen
}

public enum NotchDisplayMode
{
    FollowMouse,
    FixedDisplay
}

public enum ExternalDisplayMode
{
    Off,
    Teleprompter,
    Mirror
}

public enum MirrorAxis
{
    Horizontal,
    Vertical,
    Both
}

public enum ListeningMode
{
    WordTracking,
    Classic,
    SilencePaused
}

public static class ListeningModeExtensions
{
    public static string GetLabel(this ListeningMode mode) => mode switch
    {
        ListeningMode.WordTracking => "Word Tracking",
        ListeningMode.Classic => "Classic",
        ListeningMode.SilencePaused => "Voice-Activated",
        _ => "Word Tracking"
    };

    public static string GetDescription(this ListeningMode mode) => mode switch
    {
        ListeningMode.WordTracking => "Tracks each word you say and highlights it in real time.",
        ListeningMode.Classic => "Auto-scrolls at a constant speed. No microphone needed.",
        ListeningMode.SilencePaused => "Scrolls while you speak, pauses when you're silent.",
        _ => ""
    };
}

public enum ReadingPosition
{
    Centered,
    NearTop
}

public class NotchSettings
{
    public double NotchWidth { get; set; } = 340.0;
    public double TextAreaHeight { get; set; } = 150.0;
    public string SpeechLocale { get; set; } = "en-US";
    public FontSizePreset FontSizePreset { get; set; } = FontSizePreset.Lg;
    public FontFamilyPreset FontFamilyPreset { get; set; } = FontFamilyPreset.Sans;
    public FontColorPreset FontColorPreset { get; set; } = FontColorPreset.White;
    public FontColorPreset CueColorPreset { get; set; } = FontColorPreset.White;
    public CueBrightness CueBrightness { get; set; } = CueBrightness.Dim;
    public OverlayMode OverlayMode { get; set; } = OverlayMode.Pinned;
    public NotchDisplayMode NotchDisplayMode { get; set; } = NotchDisplayMode.FollowMouse;
    public uint PinnedScreenID { get; set; } = 0;
    public bool FloatingGlassEffect { get; set; } = false;
    public double GlassOpacity { get; set; } = 0.15;
    public bool OverlayTransparency { get; set; } = false;
    public double OverlayTransparencyOpacity { get; set; } = 0.85;
    public bool FollowCursorWhenUndocked { get; set; } = false;
    public ExternalDisplayMode ExternalDisplayMode { get; set; } = ExternalDisplayMode.Off;
    public uint ExternalScreenID { get; set; } = 0;
    public MirrorAxis MirrorAxis { get; set; } = MirrorAxis.Horizontal;
    public ListeningMode ListeningMode { get; set; } = ListeningMode.WordTracking;
    public double ScrollSpeed { get; set; } = 3.0; // words per second
    public bool HideFromScreenShare { get; set; } = true;
    public bool ShowElapsedTime { get; set; } = true;
    public bool KeepScreenAwake { get; set; } = false;
    public ReadingPosition ReadingPosition { get; set; } = ReadingPosition.Centered;
    public bool ShowParagraphDividers { get; set; } = false;
    public bool ShowLastSpokenWords { get; set; } = true;
    public string SelectedMicUID { get; set; } = string.Empty;
    public bool AutoNextPage { get; set; } = false;
    public int AutoNextPageDelay { get; set; } = 3; // 0, 1, 3, or 5 seconds
    public uint FullscreenScreenID { get; set; } = 0;
    public bool BrowserServerEnabled { get; set; } = false;
    public ushort BrowserServerPort { get; set; } = 7373;
    public bool DirectorModeEnabled { get; set; } = false;
    public ushort DirectorServerPort { get; set; } = 7575;

    public const double DefaultWidth = 340.0;
    public const double DefaultHeight = 150.0;
    public const double MinWidth = 280.0;
    public const double MaxWidth = 500.0;
    public const double MinHeight = 100.0;
    public const double MaxHeight = 400.0;

    // Convenience accessors
    public double FontSize
    {
        get => FontSizePreset.GetPointSize();
        set
        {
            if (value <= 14) FontSizePreset = FontSizePreset.Xs;
            else if (value <= 18) FontSizePreset = FontSizePreset.Sm;
            else if (value <= 22) FontSizePreset = FontSizePreset.Lg;
            else FontSizePreset = FontSizePreset.Xl;
        }
    }

    public string FontColor => FontColorPreset.GetHexColor();
    public string CueColor => CueColorPreset.GetHexColor();

    public double PrompterOpacity
    {
        get => OverlayTransparency ? OverlayTransparencyOpacity : 0.95;
        set
        {
            OverlayTransparency = true;
            OverlayTransparencyOpacity = value;
        }
    }

    public double ReadingSpeedWpm
    {
        get => ScrollSpeed * 60.0;
        set => ScrollSpeed = Math.Max(0.5, value / 60.0);
    }

    public bool HighlightWords { get; set; } = true;
    public bool ShowReadPageBadge { get; set; } = true;
    public bool DirectorServerEnabled
    {
        get => DirectorModeEnabled;
        set => DirectorModeEnabled = value;
    }
}
