using System.Text.Json.Serialization;

namespace Textream.Windows.Models;

public class BrowserState
{
    [JsonPropertyName("words")]
    public List<string> Words { get; set; } = new();

    [JsonPropertyName("highlightedCharCount")]
    public int HighlightedCharCount { get; set; }

    [JsonPropertyName("totalCharCount")]
    public int TotalCharCount { get; set; }

    [JsonPropertyName("audioLevels")]
    public List<double> AudioLevels { get; set; } = new();

    [JsonPropertyName("isListening")]
    public bool IsListening { get; set; }

    [JsonPropertyName("isDone")]
    public bool IsDone { get; set; }

    [JsonPropertyName("fontColor")]
    public string FontColor { get; set; } = "#ffffff";

    [JsonPropertyName("cueColor")]
    public string CueColor { get; set; } = "#ffffff";

    [JsonPropertyName("hasNextPage")]
    public bool HasNextPage { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("highlightWords")]
    public bool HighlightWords { get; set; } = true;

    [JsonPropertyName("lastSpokenText")]
    public string LastSpokenText { get; set; } = string.Empty;
}

public class DirectorState
{
    [JsonPropertyName("words")]
    public List<string> Words { get; set; } = new();

    [JsonPropertyName("highlightedCharCount")]
    public int HighlightedCharCount { get; set; }

    [JsonPropertyName("totalCharCount")]
    public int TotalCharCount { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("isDone")]
    public bool IsDone { get; set; }

    [JsonPropertyName("isListening")]
    public bool IsListening { get; set; }

    [JsonPropertyName("fontColor")]
    public string FontColor { get; set; } = "#ffffff";

    [JsonPropertyName("cueColor")]
    public string CueColor { get; set; } = "#ffffff";

    [JsonPropertyName("lastSpokenText")]
    public string LastSpokenText { get; set; } = string.Empty;

    [JsonPropertyName("audioLevels")]
    public List<double> AudioLevels { get; set; } = new();
}

public class DirectorCommand
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // "auth", "setText", "updateText", "stop"

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("readCharCount")]
    public int? ReadCharCount { get; set; }
}
